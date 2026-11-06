//! Sitzung des Hauptfensters (Gegenstück zu `MainViewModel`): Verbindung, Snapshots, Verzeichnisse,
//! Suche, Filter, Cache und Navigation – ohne Oberfläche.
//!
//! Jede Operation erhält eine Versionsnummer und ein eigenes Abbruchsignal. Verspätete Ergebnisse
//! ersetzter oder getrennter Operationen werden verworfen und überschreiben nie den aktuellen
//! Zustand. Änderungen werden als [`Change`] an Beobachter gemeldet; Listen melden gebündelt.

use std::collections::HashSet;
use std::future::Future;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use restic_core::command::{normalize_snapshot_path, parent_path};
use restic_core::models::{
    BackupNode, FilePreviewData, FileVersion, OverwritePolicy, PathSegment, RepositoryStats, ResticVersion,
    RestorePreviewResult, RestoreProgress, RestoreRequest, RestoreResult, SnapshotInfo,
};
use restic_core::repository::format_count;
use restic_core::{RepositoryProfile, ResticError, SessionCredentials};
use tokio_util::sync::CancellationToken;

use crate::backend::{BatchSink, RepositoryBackend, batch_channel};
use crate::cache::DirectoryCache;
use crate::filter::{ALL_HOSTS, ALL_TAGS, FilterCriteria, LatestUpdate, SnapshotFilter};
use crate::list::{BatchList, ListChange};
use crate::navigation::NavigationHistory;
use crate::settings::{SettingsError, SettingsService, WindowSettings};

const SEARCH_PREFIX: &str = "Suchergebnisse:";
const FILTER_DELAY: Duration = Duration::from_millis(200);

/// Beobachtbare Änderung der Sitzung. Die Oberfläche liest danach den neuen Zustand über
/// [`BrowserSession::view`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Change {
    Status,
    Busy,
    /// Verbindung hergestellt oder getrennt (`is_connected` hat sich geändert).
    Connection,
    Profiles,
    Snapshots(ListChange),
    VisibleSnapshots(ListChange),
    Nodes(ListChange),
    Hosts,
    Tags,
    SelectedSnapshot,
    CurrentPath,
    Navigation,
    RepoStats,
    SelectionSummary,
}

type Observer = Arc<dyn Fn(&Change) + Send + Sync>;

fn settings_error(error: SettingsError) -> ResticError {
    ResticError::new(error.0)
}

fn is_search_path(path: &str) -> bool {
    path.starts_with(SEARCH_PREFIX)
}

#[derive(Clone)]
struct OperationState {
    version: u64,
    stop: CancellationToken,
}

struct State {
    profiles: Vec<RepositoryProfile>,
    snapshots: BatchList<SnapshotInfo>,
    visible_snapshots: BatchList<SnapshotInfo>,
    nodes: BatchList<BackupNode>,
    hosts: Vec<String>,
    tags: Vec<String>,
    active_profile: Option<Arc<RepositoryProfile>>,
    credentials: Option<Arc<SessionCredentials>>,
    selected: Option<SnapshotInfo>,
    repo_stats: Option<RepositoryStats>,
    validated_version: Option<ResticVersion>,
    validated_source: Option<String>,
    current_path: String,
    selection_summary: String,
    status: String,
    filter_text: String,
    filter_host: String,
    filter_tag: String,
    only_latest: bool,
    busy: bool,
    operation: Option<CancellationToken>,
    stats_stop: Option<CancellationToken>,
    stats_version: u64,
    filter_stop: Option<CancellationToken>,
    operation_version: u64,
    connection_version: u64,
    snapshot_load_version: Option<u64>,
    history: NavigationHistory,
    cache: DirectoryCache,
    filter_index: SnapshotFilter,
    last_profile_id: Option<String>,
    last_restore_target: Option<String>,
    saved_window: Option<WindowSettings>,
    pending: Vec<Change>,
}

impl State {
    fn new() -> Self {
        Self {
            profiles: Vec::new(),
            snapshots: BatchList::new(),
            visible_snapshots: BatchList::new(),
            nodes: BatchList::new(),
            hosts: Vec::new(),
            tags: Vec::new(),
            active_profile: None,
            credentials: None,
            selected: None,
            repo_stats: None,
            validated_version: None,
            validated_source: None,
            current_path: "/".to_string(),
            selection_summary: String::new(),
            status: "Noch mit keinem Repository verbunden".to_string(),
            filter_text: String::new(),
            filter_host: String::new(),
            filter_tag: String::new(),
            only_latest: false,
            busy: false,
            operation: None,
            stats_stop: None,
            stats_version: 0,
            filter_stop: None,
            operation_version: 0,
            connection_version: 0,
            snapshot_load_version: None,
            history: NavigationHistory::new(),
            cache: DirectoryCache::new(),
            filter_index: SnapshotFilter::new(),
            last_profile_id: None,
            last_restore_target: None,
            saved_window: None,
            pending: Vec::new(),
        }
    }

    fn set_status(&mut self, text: impl Into<String>) {
        let text = text.into();
        if self.status != text {
            self.status = text;
            self.pending.push(Change::Status);
        }
    }

    fn set_busy(&mut self, busy: bool) {
        if self.busy != busy {
            self.busy = busy;
            self.pending.push(Change::Busy);
        }
    }

    fn set_path(&mut self, path: String) {
        if self.current_path != path {
            self.current_path = path;
            self.pending.push(Change::CurrentPath);
            self.pending.push(Change::Navigation);
        }
    }

    fn clear_nodes(&mut self) {
        if let Some(change) = self.nodes.clear() {
            self.pending.push(Change::Nodes(change));
        }
    }

    fn clear_snapshots(&mut self) {
        if let Some(change) = self.snapshots.clear() {
            self.pending.push(Change::Snapshots(change));
        }
        if let Some(change) = self.visible_snapshots.clear() {
            self.pending.push(Change::VisibleSnapshots(change));
        }
        self.filter_index.clear();
    }

    fn set_selected(&mut self, snapshot: Option<SnapshotInfo>) -> bool {
        let same = match (&self.selected, &snapshot) {
            (Some(a), Some(b)) => a.id == b.id,
            (None, None) => true,
            _ => false,
        };
        if !same {
            self.selected = snapshot;
            self.pending.push(Change::SelectedSnapshot);
        }
        !same
    }

    fn criteria(&self) -> FilterCriteria<'_> {
        FilterCriteria {
            text: &self.filter_text,
            host: &self.filter_host,
            tag: &self.filter_tag,
            only_latest: self.only_latest,
        }
    }

    fn apply_filter(&mut self, snapshots_are_sorted: bool) {
        let visible =
            self.filter_index
                .apply(self.snapshots.as_slice(), &self.criteria(), snapshots_are_sorted);
        let change = self.visible_snapshots.replace_with(visible);
        self.pending.push(Change::VisibleSnapshots(change));
    }
}

/// Lesender Blick auf den Zustand der Sitzung.
pub struct SessionView<'a>(&'a State);

impl SessionView<'_> {
    pub fn status(&self) -> &str {
        &self.0.status
    }
    pub fn is_busy(&self) -> bool {
        self.0.busy
    }
    pub fn is_connected(&self) -> bool {
        self.0.active_profile.is_some() && self.0.credentials.is_some()
    }
    pub fn profiles(&self) -> &[RepositoryProfile] {
        &self.0.profiles
    }
    pub fn active_profile(&self) -> Option<&RepositoryProfile> {
        self.0.active_profile.as_deref()
    }
    pub fn snapshots(&self) -> &[SnapshotInfo] {
        self.0.snapshots.as_slice()
    }
    pub fn visible_snapshots(&self) -> &[SnapshotInfo] {
        self.0.visible_snapshots.as_slice()
    }
    pub fn nodes(&self) -> &[BackupNode] {
        self.0.nodes.as_slice()
    }
    pub fn available_hosts(&self) -> &[String] {
        &self.0.hosts
    }
    pub fn available_tags(&self) -> &[String] {
        &self.0.tags
    }
    pub fn selected_snapshot(&self) -> Option<&SnapshotInfo> {
        self.0.selected.as_ref()
    }
    pub fn current_path(&self) -> &str {
        &self.0.current_path
    }
    pub fn path_segments(&self) -> Vec<PathSegment> {
        PathSegment::build(&self.0.current_path)
    }
    pub fn is_search_result(&self) -> bool {
        is_search_path(&self.0.current_path)
    }
    pub fn can_go_up(&self) -> bool {
        self.0.current_path != "/" && !is_search_path(&self.0.current_path)
    }
    pub fn can_go_back(&self) -> bool {
        self.0.history.can_go_back()
    }
    pub fn can_go_forward(&self) -> bool {
        self.0.history.can_go_forward()
    }
    pub fn repo_stats(&self) -> Option<&RepositoryStats> {
        self.0.repo_stats.as_ref()
    }
    pub fn validated_restic_version(&self) -> Option<&ResticVersion> {
        self.0.validated_version.as_ref()
    }
    pub fn validated_restic_source(&self) -> Option<&str> {
        self.0.validated_source.as_deref()
    }
    pub fn selection_summary(&self) -> &str {
        &self.0.selection_summary
    }
    pub fn last_profile_id(&self) -> Option<&str> {
        self.0.last_profile_id.as_deref()
    }
    pub fn last_restore_target(&self) -> Option<&str> {
        self.0.last_restore_target.as_deref()
    }
    pub fn saved_window(&self) -> Option<&WindowSettings> {
        self.0.saved_window.as_ref()
    }
    pub fn directory_cache_len(&self) -> usize {
        self.0.cache.len()
    }
    pub fn directory_cache_node_count(&self) -> usize {
        self.0.cache.node_count()
    }
}

struct Inner<B> {
    backend: B,
    settings: SettingsService,
    state: Mutex<State>,
    observers: Mutex<Vec<Observer>>,
}

impl<B> Drop for Inner<B> {
    fn drop(&mut self) {
        if let Ok(state) = self.state.get_mut() {
            state.operation_version += 1;
            for stop in [
                state.operation.take(),
                state.stats_stop.take(),
                state.filter_stop.take(),
            ]
            .into_iter()
            .flatten()
            {
                stop.cancel();
            }
        }
    }
}

pub struct BrowserSession<B: RepositoryBackend> {
    inner: Arc<Inner<B>>,
}

impl<B: RepositoryBackend> Clone for BrowserSession<B> {
    fn clone(&self) -> Self {
        Self {
            inner: Arc::clone(&self.inner),
        }
    }
}

impl<B: RepositoryBackend> BrowserSession<B> {
    pub fn new(backend: B, settings: SettingsService) -> Self {
        Self {
            inner: Arc::new(Inner {
                backend,
                settings,
                state: Mutex::new(State::new()),
                observers: Mutex::new(Vec::new()),
            }),
        }
    }

    /// Registriert einen Beobachter. Er wird synchron aufgerufen und darf die Sitzung nicht blockieren.
    pub fn subscribe(&self, observer: impl Fn(&Change) + Send + Sync + 'static) {
        self.inner
            .observers
            .lock()
            .expect("Sperre")
            .push(Arc::new(observer));
    }

    pub fn view<R>(&self, f: impl FnOnce(SessionView<'_>) -> R) -> R {
        let state = self.inner.state.lock().expect("Sperre");
        f(SessionView(&state))
    }

    fn update<R>(&self, f: impl FnOnce(&mut State) -> R) -> R {
        let (result, changes) = {
            let mut state = self.inner.state.lock().expect("Sperre");
            let result = f(&mut state);
            (result, std::mem::take(&mut state.pending))
        };
        if !changes.is_empty() {
            let observers: Vec<Observer> = self.inner.observers.lock().expect("Sperre").clone();
            for change in &changes {
                for observer in &observers {
                    observer(change);
                }
            }
        }
        result
    }

    // --- Operationen ---

    fn begin_operation(&self) -> OperationState {
        self.update(|s| {
            if let Some(previous) = s.operation.take() {
                previous.cancel();
            }
            let stop = CancellationToken::new();
            s.operation = Some(stop.clone());
            s.operation_version += 1;
            s.set_busy(true);
            OperationState {
                version: s.operation_version,
                stop,
            }
        })
    }

    fn is_current(&self, operation: &OperationState) -> bool {
        self.inner.state.lock().expect("Sperre").operation_version == operation.version
    }

    fn complete_operation(&self, operation: &OperationState) {
        self.update(|s| {
            if s.operation_version == operation.version {
                s.set_busy(false);
            }
        });
    }

    /// Bricht die laufende Operation ab.
    pub fn cancel(&self) {
        if let Some(stop) = self.inner.state.lock().expect("Sperre").operation.clone() {
            stop.cancel();
        }
    }

    pub fn report_status(&self, message: impl Into<String>) {
        self.update(|s| s.set_status(message));
    }

    pub fn set_selection_summary(&self, summary: impl Into<String>) {
        self.update(|s| {
            let summary = summary.into();
            if s.selection_summary != summary {
                s.selection_summary = summary;
                s.pending.push(Change::SelectionSummary);
            }
        });
    }

    /// Liest Batches aus dem Kanal, während der Erzeuger läuft. Verspätete Batches ersetzter oder
    /// abgebrochener Operationen werden verworfen.
    async fn stream<T, P, Fut>(
        &self,
        operation: &OperationState,
        producer: impl FnOnce(BatchSink<T>) -> Fut,
        mut apply: impl FnMut(&Self, Vec<T>),
    ) -> Result<P, ResticError>
    where
        Fut: Future<Output = Result<P, ResticError>>,
    {
        let (sink, mut receiver) = batch_channel::<T>();
        let producer = producer(sink);
        let consumer = async {
            while let Some(batch) = receiver.recv().await {
                if !self.is_current(operation) || operation.stop.is_cancelled() {
                    continue;
                }
                apply(self, batch);
                tokio::task::yield_now().await;
            }
        };
        let (result, ()) = tokio::join!(producer, consumer);
        result
    }

    // --- Einstellungen und Verbindung ---

    pub async fn initialize(&self) -> Result<(), ResticError> {
        let settings = self
            .inner
            .settings
            .load_settings()
            .await
            .map_err(settings_error)?;
        self.update(|s| {
            s.profiles = settings.profiles;
            s.pending.push(Change::Profiles);
            s.last_profile_id = settings.last_profile_id;
            s.last_restore_target = settings.last_restore_target;
            s.saved_window = settings.window;
            if settings.removed_profile_count > 0 {
                s.set_status(format!(
                    "{} nicht mehr unterstützte Profil(e) entfernt; Sicherung: settings.json.bak",
                    settings.removed_profile_count
                ));
            }
        });
        Ok(())
    }

    async fn save_settings_state(&self) -> Result<(), ResticError> {
        let mut settings = self
            .inner
            .settings
            .load_settings()
            .await
            .map_err(settings_error)?;
        let (profiles, last) = self.view(|v| (v.0.profiles.clone(), v.0.last_profile_id.clone()));
        settings.profiles = profiles;
        settings.last_profile_id = last;
        self.inner
            .settings
            .save_settings(&settings)
            .await
            .map_err(settings_error)
    }

    pub async fn save_last_restore_target(&self, target: &str) -> Result<(), ResticError> {
        if target.trim().is_empty() {
            return Ok(());
        }
        let mut settings = self
            .inner
            .settings
            .load_settings()
            .await
            .map_err(settings_error)?;
        settings.last_restore_target = Some(target.to_string());
        self.update(|s| s.last_restore_target = Some(target.to_string()));
        self.inner
            .settings
            .save_settings(&settings)
            .await
            .map_err(settings_error)
    }

    pub async fn save_window_settings(&self, window: WindowSettings) -> Result<(), ResticError> {
        let mut settings = self
            .inner
            .settings
            .load_settings()
            .await
            .map_err(settings_error)?;
        settings.window = Some(window);
        self.inner
            .settings
            .save_settings(&settings)
            .await
            .map_err(settings_error)
    }

    /// Verbindet mit dem Repository. Die Zugangsdaten gehen in den Besitz der Sitzung über und werden
    /// bei Fehlern, Trennung oder Verwerfen der Sitzung überschrieben.
    pub async fn connect(
        &self,
        profile: RepositoryProfile,
        credentials: SessionCredentials,
    ) -> Result<(), ResticError> {
        let operation = self.begin_operation();
        self.report_status("Restic wird geprüft …");
        let version = match self.inner.backend.validate(&profile, &operation.stop).await {
            Ok(version) => version,
            Err(error) => {
                self.complete_operation(&operation);
                return Err(error);
            }
        };
        if !self.is_current(&operation) {
            self.complete_operation(&operation);
            return Ok(());
        }

        let profile = Arc::new(profile);
        let source = profile.resolved_restic_source.clone().unwrap_or_else(|| {
            if profile
                .restic_executable
                .as_deref()
                .is_none_or(|e| e.trim().is_empty())
            {
                "Automatisch aufgelöstes Programm".to_string()
            } else {
                "Ausgewähltes Programm".to_string()
            }
        });
        self.update(|s| {
            s.credentials = Some(Arc::new(credentials));
            s.active_profile = Some(Arc::clone(&profile));
            s.validated_version = Some(version);
            s.validated_source = Some(source);
            s.connection_version += 1;
            s.last_profile_id = Some(profile.id.clone());
            match s.profiles.iter_mut().find(|p| p.id == profile.id) {
                Some(existing) => {
                    existing.name = profile.name.clone();
                    existing.repository = profile.repository.clone();
                    existing.restic_executable = profile.restic_executable.clone();
                    existing.resolved_restic_executable = profile.resolved_restic_executable.clone();
                    existing.resolved_restic_source = profile.resolved_restic_source.clone();
                    existing.repository_type = profile.repository_type;
                }
                None => s.profiles.push((*profile).clone()),
            }
            s.pending.push(Change::Profiles);
        });

        let result = self.connect_after_adoption(&operation).await;
        if result.is_err() && self.is_current(&operation) {
            self.disconnect();
        }
        self.complete_operation(&operation);
        result
    }

    async fn connect_after_adoption(&self, operation: &OperationState) -> Result<(), ResticError> {
        self.save_settings_state().await?;
        if !self.is_current(operation) {
            return Ok(());
        }
        let selected = self.refresh_snapshots_core(operation).await?;
        if !self.is_current(operation) {
            return Ok(());
        }
        self.update(|s| s.pending.push(Change::Connection));
        if let Some(snapshot) = selected {
            self.activate_snapshot(snapshot);
        }
        Ok(())
    }

    /// Trennt die Verbindung, bricht Operationen ab und überschreibt die Zugangsdaten.
    pub fn disconnect(&self) {
        self.update(|s| {
            for stop in [s.operation.take(), s.stats_stop.take()].into_iter().flatten() {
                stop.cancel();
            }
            s.operation_version += 1;
            s.set_busy(false);
            s.connection_version += 1;
            s.credentials = None;
            s.active_profile = None;
            s.validated_version = None;
            s.validated_source = None;
            if s.repo_stats.take().is_some() {
                s.pending.push(Change::RepoStats);
            }
            s.clear_snapshots();
            if !s.hosts.is_empty() {
                s.hosts.clear();
                s.pending.push(Change::Hosts);
            }
            if !s.tags.is_empty() {
                s.tags.clear();
                s.pending.push(Change::Tags);
            }
            s.clear_nodes();
            s.cache.clear();
            s.history.clear();
            s.set_selected(None);
            s.set_path("/".to_string());
            s.set_status("Verbindung getrennt");
            s.pending.push(Change::Connection);
            if !s.selection_summary.is_empty() {
                s.selection_summary.clear();
                s.pending.push(Change::SelectionSummary);
            }
            s.pending.push(Change::Navigation);
        });
    }

    // --- Snapshots ---

    pub async fn refresh_snapshots(&self) -> Result<(), ResticError> {
        if !self.view(|v| v.is_connected()) {
            return Ok(());
        }
        let operation = self.begin_operation();
        let result = self.refresh_snapshots_core(&operation).await;
        if let Ok(Some(snapshot)) = &result
            && self.is_current(&operation)
        {
            self.activate_snapshot(snapshot.clone());
        }
        self.complete_operation(&operation);
        result.map(|_| ())
    }

    fn connection(&self) -> Option<(Arc<RepositoryProfile>, Arc<SessionCredentials>)> {
        let state = self.inner.state.lock().expect("Sperre");
        Some((state.active_profile.clone()?, state.credentials.clone()?))
    }

    async fn refresh_snapshots_core(
        &self,
        operation: &OperationState,
    ) -> Result<Option<SnapshotInfo>, ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(None);
        };
        let previous_id = self.update(|s| {
            let previous = s.selected.as_ref().map(|snapshot| snapshot.id.clone());
            s.snapshot_load_version = Some(operation.version);
            s.set_selected(None);
            s.clear_nodes();
            s.set_path("/".to_string());
            s.set_status("Snapshots werden geladen …");
            s.clear_snapshots();
            s.cache.clear();
            previous
        });

        let result = self
            .load_snapshots(operation, &profile, &credentials, previous_id)
            .await;
        self.update(|s| {
            if s.snapshot_load_version == Some(operation.version) {
                s.snapshot_load_version = None;
            }
        });
        match result {
            Ok(selected) => Ok(selected),
            Err(error) => {
                if self.is_current(operation) {
                    let cancelled = operation.stop.is_cancelled();
                    self.update(|s| {
                        s.clear_snapshots();
                        s.set_status(if cancelled {
                            "Laden der Snapshots abgebrochen"
                        } else {
                            "Snapshots konnten nicht vollständig geladen werden"
                        });
                    });
                }
                Err(error)
            }
        }
    }

    async fn load_snapshots(
        &self,
        operation: &OperationState,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        previous_id: Option<String>,
    ) -> Result<Option<SnapshotInfo>, ResticError> {
        self.stream(
            operation,
            |sink| {
                self.inner
                    .backend
                    .get_snapshots_batched(profile, credentials, sink, &operation.stop)
            },
            |session, batch| session.apply_snapshot_batch(batch),
        )
        .await?;
        if !self.is_current(operation) {
            return Ok(None);
        }
        if operation.stop.is_cancelled() {
            return Err(ResticError::Cancelled);
        }
        Ok(self.update(|s| {
            let selected_id = s
                .selected
                .as_ref()
                .map(|snapshot| snapshot.id.clone())
                .or(previous_id);
            let mut ordered: Vec<SnapshotInfo> = s.snapshots.iter().cloned().collect();
            ordered.sort_by_key(|snapshot| std::cmp::Reverse(snapshot.time));
            let hosts = distinct_sorted(
                ordered.iter().map(|snapshot| snapshot.hostname.clone()),
                ALL_HOSTS,
            );
            let tags = distinct_sorted(
                ordered.iter().flat_map(|snapshot| snapshot.tags.clone()),
                ALL_TAGS,
            );
            let change = s.snapshots.replace_with(ordered.clone());
            s.pending.push(Change::Snapshots(change));
            s.hosts = hosts;
            s.pending.push(Change::Hosts);
            s.tags = tags;
            s.pending.push(Change::Tags);
            s.cancel_filter_timer();
            s.apply_filter(true);
            let count = s.snapshots.len();
            s.set_status(format!("{} Snapshot(s) geladen", format_count(count)));
            selected_id
                .and_then(|id| ordered.iter().find(|snapshot| snapshot.id == id).cloned())
                .or_else(|| s.visible_snapshots.get(0).cloned())
        }))
    }

    fn apply_snapshot_batch(&self, batch: Vec<SnapshotInfo>) {
        self.update(|s| {
            if let Some(change) = s.snapshots.add_range(batch.iter().cloned()) {
                s.pending.push(Change::Snapshots(change));
            }
            // Die „nur neueste“-Ansicht wird inkrementell geführt, ohne jedes geladene Präfix zu sortieren.
            let mut pending_visible: Vec<SnapshotInfo> = Vec::new();
            let mut pending_ids: HashSet<String> = HashSet::new();
            let mut superseded: HashSet<String> = HashSet::new();
            let only_latest = s.only_latest;
            for snapshot in &batch {
                match s.filter_index.track_latest(snapshot) {
                    LatestUpdate::Unchanged => continue,
                    LatestUpdate::New => {}
                    LatestUpdate::Replaced(old) => {
                        if pending_ids.remove(&old.id) {
                            pending_visible.retain(|candidate| candidate.id != old.id);
                        } else {
                            superseded.insert(old.id);
                        }
                    }
                }
                if only_latest && SnapshotFilter::matches(snapshot, &s.criteria()) {
                    pending_ids.insert(snapshot.id.clone());
                    pending_visible.push(snapshot.clone());
                }
            }
            if only_latest {
                if let Some(change) = s
                    .visible_snapshots
                    .remove_where(|snapshot| superseded.contains(&snapshot.id))
                {
                    s.pending.push(Change::VisibleSnapshots(change));
                }
                if let Some(change) = s.visible_snapshots.add_range(pending_visible) {
                    s.pending.push(Change::VisibleSnapshots(change));
                }
            } else {
                let matching: Vec<SnapshotInfo> = batch
                    .iter()
                    .filter(|snapshot| SnapshotFilter::matches(snapshot, &s.criteria()))
                    .cloned()
                    .collect();
                if let Some(change) = s.visible_snapshots.add_range(matching) {
                    s.pending.push(Change::VisibleSnapshots(change));
                }
            }
            let count = s.snapshots.len();
            s.set_status(format!("{} Snapshot(s) werden geladen …", format_count(count)));
        });
    }

    /// Wählt einen Snapshot. Außerhalb eines laufenden Ladens wird sein Wurzelverzeichnis geladen.
    pub fn select_snapshot(&self, snapshot: Option<SnapshotInfo>) {
        let reload = self.update(|s| {
            let is_some = snapshot.is_some();
            let changed = s.set_selected(snapshot);
            if changed && is_some && s.snapshot_load_version != Some(s.operation_version) {
                s.cache.clear();
                s.history.clear();
                s.pending.push(Change::Navigation);
                true
            } else {
                false
            }
        });
        if reload {
            self.spawn_select_task();
        }
    }

    fn activate_snapshot(&self, snapshot: SnapshotInfo) {
        let same = self.view(|v| {
            v.0.selected
                .as_ref()
                .is_some_and(|selected| selected.id == snapshot.id)
        });
        if same {
            self.spawn_select_task();
        } else {
            self.select_snapshot(Some(snapshot));
        }
    }

    fn spawn_select_task(&self) {
        let session = self.clone();
        tokio::spawn(async move {
            match session.load_directory_core("/", false).await {
                Ok(()) | Err(ResticError::Cancelled) => {}
                Err(error) => session.report_status(error.to_string()),
            }
        });
    }

    pub async fn load_repository_stats(&self) -> Result<(), ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(());
        };
        let (stop, connection_version, stats_version) = self.update(|s| {
            if let Some(previous) = s.stats_stop.take() {
                previous.cancel();
            }
            let stop = CancellationToken::new();
            s.stats_stop = Some(stop.clone());
            s.stats_version += 1;
            (stop, s.connection_version, s.stats_version)
        });
        let result = self.inner.backend.get_stats(&profile, &credentials, &stop).await;
        self.update(|s| {
            if let Ok(stats) = result
                && !stop.is_cancelled()
                && s.connection_version == connection_version
                && s.active_profile
                    .as_ref()
                    .is_some_and(|p| Arc::ptr_eq(p, &profile))
                && s.credentials
                    .as_ref()
                    .is_some_and(|c| Arc::ptr_eq(c, &credentials))
            {
                s.repo_stats = Some(stats);
                s.pending.push(Change::RepoStats);
            }
            // Statistik schlägt still fehl, wenn sie nicht verfügbar ist.
            if s.stats_version == stats_version {
                s.stats_stop = None;
            }
        });
        Ok(())
    }

    // --- Verzeichnisse und Suche ---

    pub async fn load_directory(&self, path: &str) -> Result<(), ResticError> {
        self.load_directory_core(path, true).await
    }

    async fn load_directory_core(&self, path: &str, record_history: bool) -> Result<(), ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(());
        };
        let Some(snapshot) = self.view(|v| v.0.selected.clone()) else {
            return Ok(());
        };
        let operation = self.begin_operation();
        let result = self
            .load_directory_inner(
                &operation,
                &profile,
                &credentials,
                &snapshot,
                path,
                record_history,
            )
            .await;
        if let Err(error) = &result {
            if self.is_current(&operation) {
                let cancelled = operation.stop.is_cancelled();
                self.update(|s| {
                    s.clear_nodes();
                    s.set_status(if cancelled {
                        "Verzeichnis laden abgebrochen"
                    } else {
                        "Verzeichnis konnte nicht geladen werden"
                    });
                });
            } else {
                // Eine ersetzte Operation meldet ihren Fehler nicht mehr.
                self.complete_operation(&operation);
                let _ = error;
                return Ok(());
            }
        }
        self.complete_operation(&operation);
        result
    }

    async fn load_directory_inner(
        &self,
        operation: &OperationState,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot: &SnapshotInfo,
        path: &str,
        record_history: bool,
    ) -> Result<(), ResticError> {
        let normalized = normalize_snapshot_path(path);
        let key = format!("{}\n{}", snapshot.id, normalized);
        let cached = self.update(|s| {
            s.set_status(format!("{normalized} wird geladen …"));
            s.cache.get(&key)
        });
        let nodes = match cached {
            Some(nodes) => nodes,
            None => {
                self.update(|s| s.clear_nodes());
                let listing = self
                    .stream(
                        operation,
                        |sink| {
                            self.inner.backend.get_directory_batched(
                                profile,
                                credentials,
                                &snapshot.id,
                                &normalized,
                                sink,
                                &operation.stop,
                            )
                        },
                        |session, batch| {
                            session.update(|s| {
                                if let Some(change) = s.nodes.add_range(batch) {
                                    s.pending.push(Change::Nodes(change));
                                }
                                let count = s.nodes.len();
                                s.set_status(format!("{} Element(e) werden geladen …", format_count(count)));
                            });
                        },
                    )
                    .await?;
                if !self.is_current(operation) {
                    return Ok(());
                }
                let nodes = Arc::new(listing);
                self.update(|s| s.cache.insert(key, Arc::clone(&nodes)));
                nodes
            }
        };
        if !self.is_current(operation) {
            return Ok(());
        }
        self.update(|s| {
            if record_history && s.current_path != normalized && !is_search_path(&s.current_path) {
                let previous = s.current_path.clone();
                s.history.push_back(previous);
                s.history.clear_forward();
            }
            let change = s.nodes.replace_with(nodes.iter().cloned());
            s.pending.push(Change::Nodes(change));
            s.set_path(normalized);
            let count = s.nodes.len();
            s.set_status(format!("{count} Element(e)"));
            s.pending.push(Change::Navigation);
        });
        Ok(())
    }

    pub async fn open_node(&self, node: &BackupNode) -> Result<(), ResticError> {
        let in_search = self.view(|v| v.is_search_result());
        if in_search {
            self.update(|s| s.history.clear());
            let target = if node.is_directory() {
                node.path.clone()
            } else {
                parent_path(&node.path)
            };
            return self.load_directory_core(&target, false).await;
        }
        if node.is_directory() {
            return self.load_directory(&node.path).await;
        }
        Ok(())
    }

    pub async fn go_up(&self) -> Result<(), ResticError> {
        let path = self.view(|v| v.current_path().to_string());
        self.load_directory(&parent_path(&path)).await
    }

    pub async fn go_back(&self) -> Result<(), ResticError> {
        let target = self.update(|s| {
            let target = s.history.pop_back()?;
            if is_search_path(&s.current_path) {
                s.history.clear_forward();
            } else {
                let current = s.current_path.clone();
                s.history.push_forward(current);
            }
            s.pending.push(Change::Navigation);
            Some(target)
        });
        match target {
            Some(target) => self.load_directory_core(&target, false).await,
            None => Ok(()),
        }
    }

    pub async fn go_forward(&self) -> Result<(), ResticError> {
        let target = self.update(|s| {
            let target = s.history.pop_forward()?;
            let current = s.current_path.clone();
            s.history.push_back(current);
            s.pending.push(Change::Navigation);
            Some(target)
        });
        match target {
            Some(target) => self.load_directory_core(&target, false).await,
            None => Ok(()),
        }
    }

    pub async fn search(&self, pattern: &str) -> Result<(), ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(());
        };
        let Some(snapshot) = self.view(|v| v.0.selected.clone()) else {
            return Ok(());
        };
        let pattern = pattern.trim().to_string();
        if pattern.is_empty() {
            return Ok(());
        }
        let operation = self.begin_operation();
        self.update(|s| {
            if !is_search_path(&s.current_path) {
                let current = s.current_path.clone();
                s.history.push_back(current);
            }
            s.history.clear_forward();
            s.clear_nodes();
            s.set_path(format!("{SEARCH_PREFIX} {pattern}"));
            s.pending.push(Change::Navigation);
            s.set_status("Backup wird durchsucht …");
        });
        let result: Result<bool, ResticError> = async {
            let truncated = self
                .stream(
                    &operation,
                    |sink| {
                        self.inner.backend.find_batched(
                            &profile,
                            &credentials,
                            &snapshot.id,
                            &pattern,
                            sink,
                            &operation.stop,
                        )
                    },
                    |session, batch| {
                        session.update(|s| {
                            if let Some(change) = s.nodes.add_range(batch) {
                                s.pending.push(Change::Nodes(change));
                            }
                            let count = s.nodes.len();
                            s.set_status(format!("{} Treffer werden geladen …", format_count(count)));
                        });
                    },
                )
                .await?;
            if self.is_current(&operation) && operation.stop.is_cancelled() {
                return Err(ResticError::Cancelled);
            }
            Ok(truncated)
        }
        .await;
        match &result {
            Ok(truncated) => {
                if self.is_current(&operation) {
                    let truncated = *truncated;
                    self.update(|s| {
                        let count = format_count(s.nodes.len());
                        s.set_status(if truncated {
                            format!("{count} Treffer angezeigt (weitere Treffer werden aus Leistungsgründen nicht angezeigt)")
                        } else {
                            format!("{count} Treffer")
                        });
                    });
                }
            }
            Err(_) => {
                if self.is_current(&operation) {
                    let cancelled = operation.stop.is_cancelled();
                    self.update(|s| {
                        s.clear_nodes();
                        s.set_status(if cancelled {
                            "Suche abgebrochen"
                        } else {
                            "Suche fehlgeschlagen"
                        });
                    });
                }
            }
        }
        self.complete_operation(&operation);
        result.map(|_| ())
    }

    /// Sucht die neueste Version über alle Snapshots und wählt deren Snapshot aus.
    pub async fn find_newest(&self, pattern: &str) -> Result<Option<BackupNode>, ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(None);
        };
        let pattern = pattern.trim().to_string();
        if pattern.is_empty() {
            return Ok(None);
        }
        let operation = self.begin_operation();
        self.report_status("Neueste Version wird gesucht …");
        let result = self
            .inner
            .backend
            .find_newest(&profile, &credentials, &pattern, &operation.stop)
            .await;
        let outcome = match result {
            Ok(found) if self.is_current(&operation) => match found {
                Some(found) => {
                    let wanted = found.snapshot_id.to_lowercase();
                    let snapshot = self.view(|v| {
                        v.snapshots()
                            .iter()
                            .find(|item| {
                                let id = item.id.to_lowercase();
                                id == wanted || id.starts_with(&wanted) || wanted.starts_with(&id)
                            })
                            .cloned()
                    });
                    match snapshot {
                        None => Ok(None),
                        Some(snapshot) => {
                            let when = snapshot
                                .time
                                .map(|t| t.format("%d.%m.%Y %H:%M").to_string())
                                .unwrap_or_default();
                            self.report_status(format!("Neueste Version aus {when} gefunden"));
                            self.select_snapshot(Some(snapshot));
                            Ok(Some(found.node))
                        }
                    }
                }
                None => {
                    self.report_status("Keine passende Datei in den Snapshots gefunden");
                    Ok(None)
                }
            },
            Ok(_) => Ok(None),
            Err(error) => Err(error),
        };
        self.complete_operation(&operation);
        outcome
    }

    // --- Filter ---

    pub fn set_snapshot_filter(&self, text: impl Into<String>) {
        let text = text.into();
        self.set_filter(move |s| {
            let changed = s.filter_text != text;
            s.filter_text = text;
            changed
        });
    }

    pub fn set_filter_host(&self, host: impl Into<String>) {
        let host = host.into();
        self.set_filter(move |s| {
            let changed = s.filter_host != host;
            s.filter_host = host;
            changed
        });
    }

    pub fn set_filter_tag(&self, tag: impl Into<String>) {
        let tag = tag.into();
        self.set_filter(move |s| {
            let changed = s.filter_tag != tag;
            s.filter_tag = tag;
            changed
        });
    }

    pub fn set_filter_only_latest(&self, only_latest: bool) {
        self.set_filter(move |s| {
            let changed = s.only_latest != only_latest;
            s.only_latest = only_latest;
            changed
        });
    }

    fn set_filter(&self, change: impl FnOnce(&mut State) -> bool) {
        let schedule = self.update(|s| {
            let changed = change(s);
            if changed {
                s.cancel_filter_timer();
            }
            changed
        });
        if schedule {
            self.schedule_filter();
        }
    }

    fn schedule_filter(&self) {
        let Ok(handle) = tokio::runtime::Handle::try_current() else {
            self.apply_filter_now(false);
            return;
        };
        let stop = CancellationToken::new();
        self.update(|s| s.filter_stop = Some(stop.clone()));
        let session = self.clone();
        handle.spawn(async move {
            tokio::select! {
                () = stop.cancelled() => {}
                () = tokio::time::sleep(FILTER_DELAY) => session.apply_filter_now(false),
            }
        });
    }

    /// Wendet den Filter sofort an (sonst nach 200 ms Ruhe, damit Tippen nicht jedes Mal filtert).
    pub fn apply_filter_now(&self, snapshots_are_sorted: bool) {
        self.update(|s| {
            s.cancel_filter_timer();
            s.apply_filter(snapshots_are_sorted);
        });
    }

    // --- Dateien und Wiederherstellung ---

    pub async fn get_file_preview(
        &self,
        node: &BackupNode,
        stop: &CancellationToken,
    ) -> Result<FilePreviewData, ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(error_preview("Kein Snapshot ausgewählt."));
        };
        let Some(snapshot) = self.view(|v| v.0.selected.clone()) else {
            return Ok(error_preview("Kein Snapshot ausgewählt."));
        };
        self.inner
            .backend
            .get_file_preview(&profile, &credentials, node, &snapshot.id, stop)
            .await
    }

    pub async fn get_file_preview_of_version(
        &self,
        version: &FileVersion,
        stop: &CancellationToken,
    ) -> Result<FilePreviewData, ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(error_preview("Es besteht keine Repository-Verbindung."));
        };
        self.inner
            .backend
            .get_file_preview(&profile, &credentials, &version.node, &version.snapshot.id, stop)
            .await
    }

    pub async fn get_file_versions(
        &self,
        node: &BackupNode,
        all_hosts: bool,
        stop: &CancellationToken,
    ) -> Result<Vec<FileVersion>, ResticError> {
        let Some((profile, credentials)) = self.connection() else {
            return Ok(Vec::new());
        };
        let Some((snapshot, known)) = self.view(|v| {
            v.0.selected
                .clone()
                .map(|s| (s, v.0.snapshots.as_slice().to_vec()))
        }) else {
            return Ok(Vec::new());
        };
        let hostname = (!all_hosts).then_some(snapshot.hostname.as_str());
        self.inner
            .backend
            .get_file_versions(&profile, &credentials, &node.path, hostname, Some(&known), stop)
            .await
    }

    pub async fn restore(
        &self,
        nodes: &[BackupNode],
        target: &str,
        overwrite: OverwritePolicy,
        progress: &(dyn Fn(RestoreProgress) + Send + Sync),
        stop: &CancellationToken,
    ) -> Result<RestoreResult, ResticError> {
        let (profile, credentials, snapshot) = self.restore_context()?;
        let request = restore_request(&snapshot.id, nodes, target, overwrite);
        let result = self
            .inner
            .backend
            .restore(&profile, &credentials, &request, progress, stop)
            .await?;
        // Das gemerkte Ziel ist optional; die Wiederherstellung war erfolgreich.
        let _ = self.save_last_restore_target(target).await;
        Ok(result)
    }

    pub async fn preview_restore(
        &self,
        nodes: &[BackupNode],
        target: &str,
        overwrite: OverwritePolicy,
        stop: &CancellationToken,
    ) -> Result<RestorePreviewResult, ResticError> {
        let (profile, credentials, snapshot) = self.restore_context()?;
        let request = restore_request(&snapshot.id, nodes, target, overwrite);
        self.inner
            .backend
            .preview_restore(&profile, &credentials, &request, stop)
            .await
    }

    fn restore_context(
        &self,
    ) -> Result<(Arc<RepositoryProfile>, Arc<SessionCredentials>, SnapshotInfo), ResticError> {
        let (profile, credentials) = self
            .connection()
            .ok_or_else(|| ResticError::new("Es ist kein Snapshot ausgewählt."))?;
        let snapshot = self
            .view(|v| v.0.selected.clone())
            .ok_or_else(|| ResticError::new("Es ist kein Snapshot ausgewählt."))?;
        Ok((profile, credentials, snapshot))
    }
}

impl State {
    fn cancel_filter_timer(&mut self) {
        if let Some(stop) = self.filter_stop.take() {
            stop.cancel();
        }
    }
}

fn error_preview(message: &str) -> FilePreviewData {
    FilePreviewData {
        error_message: message.to_string(),
        ..FilePreviewData::default()
    }
}

/// Baut die Restore-Anfrage: ausgewählte Pfade ohne Duplikate in der Reihenfolge der Auswahl.
pub fn restore_request(
    snapshot_id: &str,
    nodes: &[BackupNode],
    target: &str,
    overwrite: OverwritePolicy,
) -> RestoreRequest {
    let mut includes: Vec<String> = Vec::new();
    for node in nodes {
        if !includes.contains(&node.path) {
            includes.push(node.path.clone());
        }
    }
    RestoreRequest {
        snapshot_id: snapshot_id.to_string(),
        target: target.to_string(),
        includes,
        overwrite,
    }
}

/// „Alle …“-Eintrag gefolgt von den eindeutigen, nicht leeren Werten (ohne Beachtung der
/// Schreibweise), alphabetisch sortiert.
fn distinct_sorted(values: impl Iterator<Item = String>, all_label: &str) -> Vec<String> {
    let mut seen = HashSet::new();
    let mut unique: Vec<String> = values
        .filter(|v| !v.trim().is_empty())
        .filter(|v| seen.insert(v.to_lowercase()))
        .collect();
    unique.sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()).then_with(|| a.cmp(b)));
    std::iter::once(all_label.to_string()).chain(unique).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn restore_request_keeps_unique_paths_in_selection_order() {
        let node = |path: &str| BackupNode {
            path: path.into(),
            ..BackupNode::default()
        };
        let request = restore_request(
            "snap",
            &[node("/b"), node("/a"), node("/b")],
            "ziel",
            OverwritePolicy::IfNewer,
        );
        assert_eq!(request.includes, ["/b", "/a"]);
        assert_eq!(
            (request.snapshot_id.as_str(), request.target.as_str()),
            ("snap", "ziel")
        );
    }

    #[test]
    fn host_and_tag_lists_are_distinct_sorted_and_start_with_the_all_entry() {
        let values = ["zeta", "Alpha", "alpha", "", "  ", "beta"]
            .into_iter()
            .map(String::from);
        assert_eq!(
            distinct_sorted(values, ALL_HOSTS),
            ["Alle Hosts", "Alpha", "beta", "zeta"]
        );
    }
}
