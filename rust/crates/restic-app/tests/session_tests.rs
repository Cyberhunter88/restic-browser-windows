//! Tests der Sitzung mit gesteuertem Fake-Backend. Gegenstücke zu `StreamingTests`,
//! `PerformanceTests` (OperationRace, ConnectState, StaleStats) und `UxTests` der C#-Linie.

use std::collections::{HashMap, HashSet};
use std::future::Future;
use std::path::PathBuf;
use std::pin::Pin;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::{DateTime, FixedOffset};
use restic_app::{BatchSink, BrowserSession, Change, ListChange, RepositoryBackend, SettingsService};
use restic_core::models::{
    BackupNode, CheckMode, FilePreviewData, FileVersion, LatestFileMatch, OverwritePolicy,
    RepositoryCheckResult, RepositoryStats, ResticVersion, RestorePreviewResult, RestoreProgress,
    RestoreRequest, RestoreResult, SnapshotInfo,
};
use restic_core::storage::{StorageAnalysisProgress, StorageAnalysisResult};
use restic_core::{RepositoryProfile, ResticError, SessionCredentials};
use tokio::sync::{Notify, oneshot};
use tokio_util::sync::CancellationToken;

type BoxFuture<T> = Pin<Box<dyn Future<Output = T> + Send>>;
type SnapshotScript =
    Arc<dyn Fn(BatchSink<SnapshotInfo>) -> BoxFuture<Result<(), ResticError>> + Send + Sync>;
type SearchScript =
    Arc<dyn Fn(String, BatchSink<BackupNode>) -> BoxFuture<Result<bool, ResticError>> + Send + Sync>;

static COUNTER: AtomicUsize = AtomicUsize::new(0);

// --- Fake-Backend ---

#[derive(Default)]
struct Controlled {
    snapshots: Mutex<Vec<SnapshotInfo>>,
    snapshot_script: Mutex<Option<SnapshotScript>>,
    search_script: Mutex<Option<SearchScript>>,
    manual_dirs: Mutex<HashSet<String>>,
    pending_dirs: Mutex<HashMap<String, oneshot::Sender<Vec<BackupNode>>>>,
    directory_listings: Mutex<HashMap<String, Vec<BackupNode>>>,
    directory_calls: Mutex<Vec<String>>,
    stats_waiter: Mutex<Option<oneshot::Sender<RepositoryStats>>>,
    stats_calls: AtomicUsize,
    newest: Mutex<Option<LatestFileMatch>>,
    restore_requests: Mutex<Vec<RestoreRequest>>,
    fail_validate: Mutex<bool>,
    check_modes: Mutex<Vec<CheckMode>>,
    check_outcome: Mutex<Option<String>>,
}

#[derive(Clone, Default)]
struct Fake(Arc<Controlled>);

impl std::ops::Deref for Fake {
    type Target = Controlled;
    fn deref(&self) -> &Controlled {
        &self.0
    }
}

impl Fake {
    /// Verzeichnisanfragen für `path` warten, bis der Test sie mit `complete_directory` abschließt.
    fn hold_directory(&self, path: &str) {
        self.manual_dirs.lock().unwrap().insert(path.to_string());
    }

    async fn complete_directory(&self, path: &str, nodes: Vec<BackupNode>) {
        wait_until("Verzeichnisanfrage", || {
            self.pending_dirs.lock().unwrap().contains_key(path)
        })
        .await;
        let sender = self.pending_dirs.lock().unwrap().remove(path).unwrap();
        let _ = sender.send(nodes);
    }

    async fn complete_stats(&self, stats: RepositoryStats) {
        wait_until("Statistikanfrage", || self.stats_waiter.lock().unwrap().is_some()).await;
        let sender = self.stats_waiter.lock().unwrap().take().unwrap();
        let _ = sender.send(stats);
    }
}

impl RepositoryBackend for Fake {
    async fn validate(
        &self,
        _: &RepositoryProfile,
        _: &CancellationToken,
    ) -> Result<ResticVersion, ResticError> {
        if *self.fail_validate.lock().unwrap() {
            return Err(ResticError::new("Restic ist nicht verwendbar."));
        }
        Ok(ResticVersion {
            version: "0.19.1".into(),
            architecture: "amd64".into(),
        })
    }

    async fn get_snapshots_batched(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        sink: BatchSink<SnapshotInfo>,
        _: &CancellationToken,
    ) -> Result<(), ResticError> {
        let script = self.snapshot_script.lock().unwrap().clone();
        match script {
            Some(script) => script(sink).await,
            None => {
                let snapshots = self.snapshots.lock().unwrap().clone();
                if !snapshots.is_empty() {
                    sink.send(snapshots).await?;
                }
                Ok(())
            }
        }
    }

    async fn get_directory_batched(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        snapshot_id: &str,
        path: &str,
        sink: BatchSink<BackupNode>,
        _: &CancellationToken,
    ) -> Result<Vec<BackupNode>, ResticError> {
        self.directory_calls
            .lock()
            .unwrap()
            .push(format!("{snapshot_id}:{path}"));
        let manual = self.manual_dirs.lock().unwrap().contains(path);
        let nodes = if manual {
            let (sender, receiver) = oneshot::channel();
            self.pending_dirs.lock().unwrap().insert(path.to_string(), sender);
            // Wie ein verspätetes Backend ignoriert der Fake den Abbruch.
            receiver.await.map_err(|_| ResticError::Cancelled)?
        } else {
            self.directory_listings
                .lock()
                .unwrap()
                .get(path)
                .cloned()
                .unwrap_or_default()
        };
        if !nodes.is_empty() {
            sink.send(nodes.clone()).await?;
        }
        Ok(nodes)
    }

    async fn find_batched(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        _: &str,
        pattern: &str,
        sink: BatchSink<BackupNode>,
        _: &CancellationToken,
    ) -> Result<bool, ResticError> {
        let script = self.search_script.lock().unwrap().clone();
        match script {
            Some(script) => script(pattern.to_string(), sink).await,
            None => Ok(false),
        }
    }

    async fn find_newest(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        _: &str,
        _: &CancellationToken,
    ) -> Result<Option<LatestFileMatch>, ResticError> {
        Ok(self.newest.lock().unwrap().clone())
    }

    async fn get_file_versions(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        _: &str,
        _: Option<&str>,
        known: Option<&[SnapshotInfo]>,
        _: &CancellationToken,
    ) -> Result<Vec<FileVersion>, ResticError> {
        Ok(known
            .unwrap_or_default()
            .iter()
            .map(|snapshot| FileVersion {
                snapshot: snapshot.clone(),
                node: BackupNode::default(),
            })
            .collect())
    }

    async fn get_stats(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        _: &CancellationToken,
    ) -> Result<RepositoryStats, ResticError> {
        self.stats_calls.fetch_add(1, Ordering::Relaxed);
        let (sender, receiver) = oneshot::channel();
        *self.stats_waiter.lock().unwrap() = Some(sender);
        receiver.await.map_err(|_| ResticError::Cancelled)
    }

    async fn get_file_preview(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        node: &BackupNode,
        snapshot_id: &str,
        _: &CancellationToken,
    ) -> Result<FilePreviewData, ResticError> {
        Ok(FilePreviewData {
            node: node.clone(),
            path: format!("{snapshot_id}:{}", node.path),
            ..FilePreviewData::default()
        })
    }

    async fn restore(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        request: &RestoreRequest,
        progress: &(dyn Fn(RestoreProgress) + Send + Sync),
        _: &CancellationToken,
    ) -> Result<RestoreResult, ResticError> {
        self.restore_requests.lock().unwrap().push(request.clone());
        progress(RestoreProgress {
            message_type: "status".into(),
            files_restored: 1,
            ..RestoreProgress::default()
        });
        Ok(RestoreResult {
            success: true,
            exit_code: 0,
            files_restored: 1,
            files_skipped: 0,
            message: "ok".into(),
        })
    }

    async fn preview_restore(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        request: &RestoreRequest,
        _: &CancellationToken,
    ) -> Result<RestorePreviewResult, ResticError> {
        self.restore_requests.lock().unwrap().push(request.clone());
        Ok(RestorePreviewResult::default())
    }

    async fn check(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        mode: CheckMode,
        _: &CancellationToken,
    ) -> Result<RepositoryCheckResult, ResticError> {
        self.check_modes.lock().unwrap().push(mode);
        match self.check_outcome.lock().unwrap().clone() {
            Some(message) => Err(ResticError::new(message)),
            None => Ok(RepositoryCheckResult {
                mode,
                error_count: 0,
                suggest_repair_index: false,
                suggest_prune: false,
                details: "no errors were found".into(),
                details_truncated: false,
            }),
        }
    }

    async fn analyze_snapshot_storage(
        &self,
        _: &RepositoryProfile,
        _: &SessionCredentials,
        snapshot_id: &str,
        progress: &(dyn Fn(StorageAnalysisProgress) + Send + Sync),
        _: &CancellationToken,
    ) -> Result<StorageAnalysisResult, ResticError> {
        progress(StorageAnalysisProgress {
            files_processed: 3,
            directories_processed: 1,
            bytes_processed: 30,
        });
        Ok(StorageAnalysisResult {
            snapshot_id: snapshot_id.into(),
            total_file_count: 3,
            total_size: 30,
            ..StorageAnalysisResult::default()
        })
    }
}

// --- Hilfen ---

async fn wait_until(what: &str, condition: impl Fn() -> bool) {
    for _ in 0..1000 {
        if condition() {
            return;
        }
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    panic!("Zeitüberschreitung beim Warten auf: {what}");
}

fn time(minute: i64) -> DateTime<FixedOffset> {
    DateTime::from_timestamp(minute * 60, 0).unwrap().fixed_offset()
}

fn snapshot(id: &str, host: &str, path: &str, tags: &[&str], minute: i64) -> SnapshotInfo {
    SnapshotInfo {
        id: id.into(),
        hostname: host.into(),
        paths: vec![path.into()],
        tags: tags.iter().map(|t| t.to_string()).collect(),
        time: Some(time(minute)),
        ..SnapshotInfo::default()
    }
}

fn file(name: &str, path: &str) -> BackupNode {
    BackupNode {
        name: name.into(),
        path: path.into(),
        node_type: "file".into(),
        message_type: "node".into(),
        ..BackupNode::default()
    }
}

fn directory(name: &str, path: &str) -> BackupNode {
    BackupNode {
        node_type: "dir".into(),
        ..file(name, path)
    }
}

struct TestSession {
    session: BrowserSession<Fake>,
    fake: Fake,
    settings_path: PathBuf,
}

impl Drop for TestSession {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(self.settings_path.parent().unwrap());
    }
}

fn new_session() -> TestSession {
    let id = COUNTER.fetch_add(1, Ordering::Relaxed);
    let directory = std::env::temp_dir().join(format!("rb-session-{}-{id}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    let settings_path = directory.join("settings.json");
    let fake = Fake::default();
    let session = BrowserSession::new(fake.clone(), SettingsService::new(&settings_path));
    TestSession {
        session,
        fake,
        settings_path,
    }
}

fn credentials() -> SessionCredentials {
    SessionCredentials::new("secret", HashMap::new())
}

/// Verbindet und wartet, bis das Wurzelverzeichnis des ersten Snapshots geladen ist.
async fn connected(snapshots: Vec<SnapshotInfo>) -> TestSession {
    let test = new_session();
    *test.fake.snapshots.lock().unwrap() = snapshots;
    test.session
        .connect(RepositoryProfile::new("repo"), credentials())
        .await
        .unwrap();
    settle(&test.session).await;
    test
}

async fn settle(session: &BrowserSession<Fake>) {
    tokio::time::sleep(Duration::from_millis(20)).await;
    wait_until("Ruhezustand", || session.view(|v| !v.is_busy())).await;
    tokio::time::sleep(Duration::from_millis(20)).await;
}

fn recorded_changes(session: &BrowserSession<Fake>) -> Arc<Mutex<Vec<Change>>> {
    let changes = Arc::new(Mutex::new(Vec::new()));
    let sink = Arc::clone(&changes);
    session.subscribe(move |change| sink.lock().unwrap().push(*change));
    changes
}

// --- Verbindung und Snapshots ---

#[tokio::test]
async fn connect_reports_connection_once_selects_the_newest_snapshot_and_starts_no_stats() {
    let test = new_session();
    *test.fake.snapshots.lock().unwrap() = vec![snapshot("snapshot", "host", "/", &[], 5)];
    let changes = recorded_changes(&test.session);

    test.session
        .connect(RepositoryProfile::new("repo"), credentials())
        .await
        .unwrap();
    settle(&test.session).await;

    assert_eq!(
        changes
            .lock()
            .unwrap()
            .iter()
            .filter(|c| **c == Change::Connection)
            .count(),
        1
    );
    test.session.view(|v| {
        assert!(v.is_connected());
        assert_eq!(v.selected_snapshot().unwrap().id, "snapshot");
        assert_eq!(v.status(), "0 Element(e)");
        assert_eq!(v.validated_restic_version().unwrap().version, "0.19.1");
        assert_eq!(
            v.validated_restic_source(),
            Some("Automatisch aufgelöstes Programm")
        );
        assert_eq!(v.available_hosts(), ["Alle Hosts", "host"]);
    });
    assert_eq!(
        test.fake.stats_calls.load(Ordering::Relaxed),
        0,
        "Statistik wird nie automatisch geladen"
    );
}

#[tokio::test]
async fn connect_persists_the_profile_and_last_profile_id_but_no_secrets() {
    let test = connected(vec![snapshot("s", "h", "/", &[], 1)]).await;
    let text = std::fs::read_to_string(&test.settings_path).unwrap();
    assert!(text.contains("\"Repository\": \"repo\""), "{text}");
    let id = test.session.view(|v| v.active_profile().unwrap().id.clone());
    assert!(text.contains(&format!("\"LastProfileId\": \"{id}\"")));
    assert!(!text.contains("secret"));

    // Ein zweiter Start findet Profil, letztes Profil und gemerktes Restore-Ziel wieder.
    test.session.save_last_restore_target("D:\\Ziel").await.unwrap();
    let restarted = BrowserSession::new(Fake::default(), SettingsService::new(&test.settings_path));
    restarted.initialize().await.unwrap();
    restarted.view(|v| {
        assert_eq!(v.profiles().len(), 1);
        assert_eq!(v.last_profile_id(), Some(id.as_str()));
        assert_eq!(v.last_restore_target(), Some("D:\\Ziel"));
    });
}

#[tokio::test]
async fn failed_validation_leaves_the_session_disconnected() {
    let test = new_session();
    *test.fake.fail_validate.lock().unwrap() = true;
    let error = test
        .session
        .connect(RepositoryProfile::new("repo"), credentials())
        .await
        .unwrap_err();
    assert_eq!(error.to_string(), "Restic ist nicht verwendbar.");
    test.session
        .view(|v| assert!(!v.is_connected() && !v.is_busy() && v.profiles().is_empty()));
}

#[tokio::test]
async fn snapshots_are_shown_before_loading_completes_and_sorted_at_the_end() {
    let test = connected(vec![]).await;
    let delivered = Arc::new(Notify::new());
    let gate = Arc::new(Notify::new());
    {
        let (delivered, gate) = (Arc::clone(&delivered), Arc::clone(&gate));
        *test.fake.snapshot_script.lock().unwrap() = Some(Arc::new(move |sink| {
            let (delivered, gate) = (Arc::clone(&delivered), Arc::clone(&gate));
            Box::pin(async move {
                sink.send(vec![snapshot("old", "h", "/", &[], 0)]).await?;
                delivered.notify_one();
                gate.notified().await;
                sink.send(vec![snapshot("new", "h", "/", &[], 100)]).await
            })
        }));
    }
    let session = test.session.clone();
    let load = tokio::spawn(async move { session.refresh_snapshots().await });

    tokio::time::timeout(Duration::from_secs(5), delivered.notified())
        .await
        .unwrap();
    wait_until("erste Zeile sichtbar", || {
        test.session.view(|v| v.visible_snapshots().len() == 1)
    })
    .await;
    test.session.view(|v| {
        assert_eq!(v.visible_snapshots()[0].id, "old");
        assert_eq!(v.status(), "1 Snapshot(s) werden geladen …");
    });
    assert!(!load.is_finished());

    gate.notify_one();
    load.await.unwrap().unwrap();
    test.session.view(|v| {
        assert_eq!(
            v.snapshots()[0].id,
            "new",
            "am Ende absteigend nach Zeit sortiert"
        );
        assert_eq!(v.snapshots()[1].id, "old");
    });
}

#[tokio::test]
async fn latest_only_projection_follows_streamed_batches() {
    let test = connected(vec![]).await;
    test.session.set_filter_only_latest(true);
    test.session.set_filter_tag("keep");
    let (first_done, release_first, second_done, release_second) = (
        Arc::new(Notify::new()),
        Arc::new(Notify::new()),
        Arc::new(Notify::new()),
        Arc::new(Notify::new()),
    );
    {
        let (a, b, c, d) = (
            first_done.clone(),
            release_first.clone(),
            second_done.clone(),
            release_second.clone(),
        );
        *test.fake.snapshot_script.lock().unwrap() = Some(Arc::new(move |sink| {
            let (a, b, c, d) = (a.clone(), b.clone(), c.clone(), d.clone());
            Box::pin(async move {
                sink.send(vec![
                    snapshot("a-old", "host-a", "/a", &["keep"], 0),
                    snapshot("b-old", "host-b", "/b", &["keep"], 1),
                ])
                .await?;
                a.notify_one();
                b.notified().await;
                sink.send(vec![
                    snapshot("a-new", "host-a", "/a", &["skip"], 4),
                    snapshot("b-new", "host-b", "/b", &["keep"], 3),
                ])
                .await?;
                c.notify_one();
                d.notified().await;
                sink.send(vec![snapshot("b-middle", "host-b", "/b", &["keep"], 2)])
                    .await
            })
        }));
    }
    let session = test.session.clone();
    let load = tokio::spawn(async move { session.refresh_snapshots().await });
    let ids = |session: &BrowserSession<Fake>| {
        session.view(|v| {
            v.visible_snapshots()
                .iter()
                .map(|s| s.id.clone())
                .collect::<HashSet<_>>()
        })
    };

    tokio::time::timeout(Duration::from_secs(5), first_done.notified())
        .await
        .unwrap();
    wait_until("beide sichtbar", || ids(&test.session).len() == 2).await;
    assert_eq!(
        ids(&test.session),
        HashSet::from(["a-old".to_string(), "b-old".to_string()])
    );

    release_first.notify_one();
    tokio::time::timeout(Duration::from_secs(5), second_done.notified())
        .await
        .unwrap();
    wait_until("nur b-new", || {
        ids(&test.session) == HashSet::from(["b-new".to_string()])
    })
    .await;

    release_second.notify_one();
    load.await.unwrap().unwrap();
    assert_eq!(ids(&test.session), HashSet::from(["b-new".to_string()]));
    test.session.view(|v| {
        assert_eq!(v.snapshots()[0].id, "a-new");
        assert_eq!(v.snapshots()[1].id, "b-new");
    });
}

#[tokio::test]
async fn snapshot_load_failure_clears_partial_results_and_reports_german_status() {
    let test = connected(vec![]).await;
    *test.fake.snapshot_script.lock().unwrap() = Some(Arc::new(|sink| {
        Box::pin(async move {
            sink.send(vec![snapshot("partial", "h", "/", &[], 0)]).await?;
            Err(ResticError::new("Lesefehler"))
        })
    }));
    let error = test.session.refresh_snapshots().await.unwrap_err();
    assert_eq!(error.to_string(), "Lesefehler");
    test.session.view(|v| {
        assert!(v.snapshots().is_empty() && v.visible_snapshots().is_empty() && !v.is_busy());
        assert_eq!(v.status(), "Snapshots konnten nicht vollständig geladen werden");
    });
}

#[tokio::test]
async fn disconnect_during_loading_discards_late_batches() {
    let test = connected(vec![]).await;
    let (delivered, gate) = (Arc::new(Notify::new()), Arc::new(Notify::new()));
    {
        let (delivered, gate) = (delivered.clone(), gate.clone());
        *test.fake.snapshot_script.lock().unwrap() = Some(Arc::new(move |sink| {
            let (delivered, gate) = (delivered.clone(), gate.clone());
            Box::pin(async move {
                sink.send(vec![snapshot("first", "h", "/", &[], 0)]).await?;
                delivered.notify_one();
                gate.notified().await;
                // Ein verspätetes Backend liefert weiter, obwohl die Verbindung getrennt wurde.
                let _ = sink.send(vec![snapshot("late", "h", "/", &[], 1)]).await;
                Ok(())
            })
        }));
    }
    let session = test.session.clone();
    let loading = tokio::spawn(async move { session.refresh_snapshots().await });
    tokio::time::timeout(Duration::from_secs(5), delivered.notified())
        .await
        .unwrap();

    test.session.disconnect();
    gate.notify_one();
    loading.await.unwrap().unwrap();

    test.session.view(|v| {
        assert!(v.snapshots().is_empty() && v.visible_snapshots().is_empty());
        assert_eq!(v.status(), "Verbindung getrennt");
        assert!(!v.is_connected() && !v.is_busy());
    });
}

#[tokio::test]
async fn disconnect_resets_everything_and_notifies() {
    let test = connected(vec![snapshot("s", "h", "/", &["t"], 1)]).await;
    test.session.set_selection_summary("3 ausgewählt");
    let changes = recorded_changes(&test.session);
    test.session.disconnect();
    test.session.view(|v| {
        assert!(!v.is_connected() && v.selected_snapshot().is_none());
        assert!(v.nodes().is_empty() && v.available_hosts().is_empty() && v.available_tags().is_empty());
        assert_eq!(
            (v.current_path(), v.selection_summary(), v.directory_cache_len()),
            ("/", "", 0)
        );
        assert!(!v.can_go_back() && !v.can_go_forward() && v.validated_restic_version().is_none());
    });
    assert!(changes.lock().unwrap().contains(&Change::Connection));
}

// --- Verzeichnisse, Cache, Navigation ---

#[tokio::test]
async fn a_newer_directory_load_wins_over_a_slower_older_one() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    test.fake.hold_directory("/alt");
    test.fake.hold_directory("/neu");
    let (s1, s2) = (test.session.clone(), test.session.clone());
    let old = tokio::spawn(async move { s1.load_directory("/alt").await });
    wait_until("alte Anfrage", || {
        test.fake.pending_dirs.lock().unwrap().contains_key("/alt")
    })
    .await;
    let new = tokio::spawn(async move { s2.load_directory("/neu").await });

    test.fake
        .complete_directory("/neu", vec![file("neu.txt", "/neu/neu.txt")])
        .await;
    new.await.unwrap().unwrap();
    test.fake
        .complete_directory("/alt", vec![file("alt.txt", "/alt/alt.txt")])
        .await;
    old.await.unwrap().unwrap();

    test.session.view(|v| {
        assert_eq!(v.current_path(), "/neu");
        assert_eq!(v.nodes().len(), 1);
        assert_eq!(v.nodes()[0].name, "neu.txt");
        assert!(!v.is_busy());
    });
}

#[tokio::test]
async fn directories_are_cached_per_snapshot_and_path() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    test.fake
        .directory_listings
        .lock()
        .unwrap()
        .insert("/docs".into(), vec![file("a.txt", "/docs/a.txt")]);
    let calls = || {
        test.fake
            .directory_calls
            .lock()
            .unwrap()
            .iter()
            .filter(|c| c.ends_with(":/docs"))
            .count()
    };

    test.session.load_directory("/docs").await.unwrap();
    test.session.load_directory("/").await.unwrap();
    test.session.load_directory("/docs").await.unwrap();
    assert_eq!(calls(), 1, "der zweite Besuch kommt aus dem Cache");
    test.session.view(|v| assert_eq!(v.nodes()[0].name, "a.txt"));
}

#[tokio::test]
async fn cache_stays_bounded_over_many_directories() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    for i in 0..40 {
        test.session.load_directory(&format!("/d{i}")).await.unwrap();
    }
    test.session.view(|v| {
        assert!(v.directory_cache_len() <= 24);
        assert!(v.directory_cache_node_count() <= 50_000);
    });
}

#[tokio::test]
async fn navigation_history_back_forward_and_up() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    test.session.load_directory("/a").await.unwrap();
    test.session.load_directory("/a/b").await.unwrap();
    test.session
        .view(|v| assert!(v.can_go_back() && !v.can_go_forward() && v.can_go_up()));

    test.session.go_back().await.unwrap();
    test.session
        .view(|v| assert_eq!((v.current_path(), v.can_go_forward()), ("/a", true)));
    test.session.go_forward().await.unwrap();
    test.session.view(|v| assert_eq!(v.current_path(), "/a/b"));
    test.session.go_up().await.unwrap();
    test.session.view(|v| assert_eq!(v.current_path(), "/a"));
    // Ein neuer Besuch verwirft die Vorwärts-Liste.
    test.session.go_back().await.unwrap();
    test.session.load_directory("/x").await.unwrap();
    test.session.view(|v| assert!(!v.can_go_forward()));
}

#[tokio::test]
async fn opening_nodes_navigates_into_folders_only() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    test.session.open_node(&file("a.txt", "/a.txt")).await.unwrap();
    test.session.view(|v| assert_eq!(v.current_path(), "/"));
    test.session.open_node(&directory("docs", "/docs")).await.unwrap();
    test.session.view(|v| assert_eq!(v.current_path(), "/docs"));
}

#[tokio::test]
async fn load_failure_clears_nodes_and_reports_german_status() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    test.fake.hold_directory("/kaputt");
    let session = test.session.clone();
    let load = tokio::spawn(async move { session.load_directory("/kaputt").await });
    wait_until("Anfrage", || {
        test.fake.pending_dirs.lock().unwrap().contains_key("/kaputt")
    })
    .await;
    drop(test.fake.pending_dirs.lock().unwrap().remove("/kaputt")); // Sender verwerfen = Fehler im Fake
    assert!(load.await.unwrap().is_err());
    test.session.view(|v| {
        assert!(v.nodes().is_empty() && !v.is_busy());
        assert_eq!(v.status(), "Verzeichnis konnte nicht geladen werden");
    });
}

// --- Suche ---

#[tokio::test]
async fn search_replaces_nodes_and_ignores_late_batches_of_older_searches() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    let (delivered, gate) = (Arc::new(Notify::new()), Arc::new(Notify::new()));
    {
        let (delivered, gate) = (delivered.clone(), gate.clone());
        *test.fake.search_script.lock().unwrap() = Some(Arc::new(move |pattern, sink| {
            let (delivered, gate) = (delivered.clone(), gate.clone());
            Box::pin(async move {
                sink.send(vec![file(&pattern, &format!("/{pattern}"))]).await?;
                if pattern == "old" {
                    delivered.notify_one();
                    gate.notified().await;
                    let _ = sink.send(vec![file("late", "/late")]).await;
                }
                Ok(false)
            })
        }));
    }
    let session = test.session.clone();
    let old = tokio::spawn(async move { session.search("old").await });
    tokio::time::timeout(Duration::from_secs(5), delivered.notified())
        .await
        .unwrap();
    wait_until("erster Treffer", || test.session.view(|v| v.nodes().len() == 1)).await;
    test.session.view(|v| assert_eq!(v.nodes()[0].name, "old"));

    test.session.search("new").await.unwrap();
    gate.notify_one();
    old.await.unwrap().unwrap();

    test.session.view(|v| {
        assert_eq!(v.nodes().len(), 1);
        assert_eq!(v.nodes()[0].name, "new");
        assert!(v.is_search_result() && !v.can_go_up());
        assert_eq!(v.status(), "1 Treffer");
    });
}

#[tokio::test]
async fn cancelling_a_search_clears_results_and_reports_cancellation() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    let session = test.session.clone();
    *test.fake.search_script.lock().unwrap() = Some(Arc::new(move |_, sink| {
        let session = session.clone();
        Box::pin(async move {
            sink.send(vec![file("partial", "/partial")]).await?;
            session.cancel();
            let _ = sink.send(vec![file("too-late", "/too-late")]).await;
            Ok(false)
        })
    }));
    let error = test.session.search("abbrechen").await.unwrap_err();
    assert!(error.is_cancelled());
    test.session.view(|v| {
        assert!(v.nodes().is_empty() && !v.is_busy());
        assert_eq!(v.status(), "Suche abgebrochen");
    });
}

#[tokio::test]
async fn search_results_open_their_parent_folder_and_report_truncation() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    *test.fake.search_script.lock().unwrap() = Some(Arc::new(|_, sink| {
        Box::pin(async move {
            sink.send(vec![file("a.txt", "/docs/a.txt")]).await?;
            Ok(true)
        })
    }));
    test.session.search("*.txt").await.unwrap();
    test.session.view(|v| {
        assert!(
            v.status()
                .contains("weitere Treffer werden aus Leistungsgründen nicht angezeigt")
        )
    });
    test.session.go_back().await.unwrap();
    test.session.view(|v| assert_eq!(v.current_path(), "/"));
    test.session.search("*.txt").await.unwrap();
    test.session
        .open_node(&file("a.txt", "/docs/a.txt"))
        .await
        .unwrap();
    test.session
        .view(|v| assert_eq!((v.current_path(), v.can_go_back()), ("/docs", false)));
}

// --- Statistik, Neueste Datei, Restore ---

#[tokio::test]
async fn late_statistics_of_a_disconnected_session_are_ignored() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    let session = test.session.clone();
    let load = tokio::spawn(async move { session.load_repository_stats().await });
    wait_until("Statistikanfrage", || {
        test.fake.stats_waiter.lock().unwrap().is_some()
    })
    .await;
    test.session.disconnect();
    test.fake
        .complete_stats(RepositoryStats {
            total_file_count: 123,
            ..RepositoryStats::default()
        })
        .await;
    load.await.unwrap().unwrap();
    test.session.view(|v| assert!(v.repo_stats().is_none()));
}

#[tokio::test]
async fn statistics_are_applied_when_still_connected() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    let session = test.session.clone();
    let load = tokio::spawn(async move { session.load_repository_stats().await });
    test.fake
        .complete_stats(RepositoryStats {
            total_file_count: 7,
            ..RepositoryStats::default()
        })
        .await;
    load.await.unwrap().unwrap();
    test.session
        .view(|v| assert_eq!(v.repo_stats().unwrap().total_file_count, 7));
}

#[tokio::test]
async fn newest_version_selects_the_matching_snapshot() {
    let test = connected(vec![
        snapshot("abcdef123456", "h", "/", &[], 1),
        snapshot("zzzz", "h", "/", &[], 0),
    ])
    .await;
    *test.fake.newest.lock().unwrap() = Some(LatestFileMatch {
        snapshot_id: "ABCDEF12".into(),
        node: file("probe.txt", "/probe.txt"),
    });
    let node = test
        .session
        .find_newest("probe.txt")
        .await
        .unwrap()
        .expect("Treffer");
    assert_eq!(node.name, "probe.txt");
    test.session.view(|v| {
        assert_eq!(v.selected_snapshot().unwrap().id, "abcdef123456");
        assert!(v.status().starts_with("Neueste Version aus "), "{}", v.status());
    });

    *test.fake.newest.lock().unwrap() = None;
    assert!(test.session.find_newest("nichts").await.unwrap().is_none());
    test.session
        .view(|v| assert_eq!(v.status(), "Keine passende Datei in den Snapshots gefunden"));
    assert!(test.session.find_newest("   ").await.unwrap().is_none());
}

#[tokio::test]
async fn restore_sends_unique_paths_reports_progress_and_remembers_the_target() {
    let test = connected(vec![snapshot("snapshot", "h", "/", &[], 1)]).await;
    let progress = Arc::new(Mutex::new(Vec::new()));
    let progress_in = Arc::clone(&progress);
    let nodes = [file("a", "/a"), file("b", "/b"), file("a", "/a")];
    let result = test
        .session
        .restore(
            &nodes,
            "D:\\Ziel",
            OverwritePolicy::IfNewer,
            &move |p| progress_in.lock().unwrap().push(p.files_restored),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert!(result.success);
    assert_eq!(*progress.lock().unwrap(), [1]);
    let request = test.fake.restore_requests.lock().unwrap()[0].clone();
    assert_eq!(
        (request.snapshot_id.as_str(), request.includes.clone()),
        ("snapshot", vec!["/a".to_string(), "/b".to_string()])
    );
    test.session
        .view(|v| assert_eq!(v.last_restore_target(), Some("D:\\Ziel")));

    test.session
        .preview_restore(
            &nodes,
            "D:\\Ziel",
            OverwritePolicy::Never,
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert_eq!(test.fake.restore_requests.lock().unwrap().len(), 2);
}

#[tokio::test]
async fn restore_without_snapshot_fails_with_a_german_message() {
    let test = new_session();
    let error = test
        .session
        .restore(
            &[],
            "x",
            OverwritePolicy::Never,
            &|_| {},
            &CancellationToken::new(),
        )
        .await
        .unwrap_err();
    assert_eq!(error.to_string(), "Es ist kein Snapshot ausgewählt.");
    assert_eq!(
        test.session
            .get_file_preview(&file("a", "/a"), &CancellationToken::new())
            .await
            .unwrap()
            .error_message,
        "Kein Snapshot ausgewählt."
    );
}

#[tokio::test]
async fn file_versions_use_the_known_snapshot_list() {
    let test = connected(vec![
        snapshot("s1", "h", "/", &[], 1),
        snapshot("s2", "h", "/", &[], 2),
    ])
    .await;
    let versions = test
        .session
        .get_file_versions(&file("a", "/a"), true, &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(versions.len(), 2);
}

// --- Filter ---

#[tokio::test]
async fn filter_changes_apply_after_the_debounce_delay() {
    let test = connected(vec![
        snapshot("s1", "alpha", "/a", &[], 2),
        snapshot("s2", "beta", "/b", &[], 1),
    ])
    .await;
    test.session.view(|v| assert_eq!(v.visible_snapshots().len(), 2));
    test.session.set_snapshot_filter("alpha");
    test.session
        .view(|v| assert_eq!(v.visible_snapshots().len(), 2, "noch nicht angewendet"));
    wait_until("Filter angewendet", || {
        test.session.view(|v| v.visible_snapshots().len() == 1)
    })
    .await;
    test.session
        .view(|v| assert_eq!(v.visible_snapshots()[0].id, "s1"));

    test.session.set_snapshot_filter("");
    test.session.apply_filter_now(true);
    test.session.view(|v| assert_eq!(v.visible_snapshots().len(), 2));
}

#[tokio::test]
async fn visible_snapshot_appends_are_reported_without_reset_while_streaming() {
    let test = connected(vec![]).await;
    let changes = recorded_changes(&test.session);
    *test.fake.snapshot_script.lock().unwrap() = Some(Arc::new(|sink| {
        Box::pin(async move {
            sink.send(vec![snapshot("a", "h", "/a", &[], 1)]).await?;
            sink.send(vec![snapshot("b", "h", "/b", &[], 2)]).await
        })
    }));
    test.session.refresh_snapshots().await.unwrap();
    let recorded = changes.lock().unwrap().clone();
    assert!(recorded.contains(&Change::VisibleSnapshots(ListChange::Appended {
        start: 0,
        count: 1
    })));
    assert!(recorded.contains(&Change::VisibleSnapshots(ListChange::Appended {
        start: 1,
        count: 1
    })));
    // Der Zwischenstand wird nie zurückgesetzt, nur am Ende einmal sortiert ersetzt.
    let resets = recorded
        .iter()
        .filter(|c| matches!(c, Change::VisibleSnapshots(ListChange::Reset)))
        .count();
    assert!(resets <= 3, "{recorded:?}");
}

// --- Prüfung, Analyse, Statistik ---

#[tokio::test]
async fn check_uses_the_requested_mode_and_reports_errors() {
    let test = connected(vec![snapshot("s", "h", "/", &[], 1)]).await;
    let quick = test
        .session
        .check_repository(CheckMode::Quick, &CancellationToken::new())
        .await
        .unwrap();
    assert!(quick.is_healthy());
    test.session
        .check_repository(CheckMode::Full, &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(
        *test.fake.check_modes.lock().unwrap(),
        [CheckMode::Quick, CheckMode::Full]
    );

    *test.fake.check_outcome.lock().unwrap() = Some("Zugriff verweigert".into());
    let error = test
        .session
        .check_repository(CheckMode::Quick, &CancellationToken::new())
        .await
        .unwrap_err();
    assert_eq!(error.to_string(), "Zugriff verweigert");
}

#[tokio::test]
async fn analysis_runs_for_the_selected_snapshot_and_reports_progress() {
    let test = connected(vec![snapshot("abc123", "host", "/", &[], 1)]).await;
    let seen = Arc::new(Mutex::new(Vec::new()));
    let seen_in = Arc::clone(&seen);
    let (snapshot, result) = test
        .session
        .analyze_selected_snapshot(
            &move |p| seen_in.lock().unwrap().push(p.files_processed),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert_eq!(
        (
            snapshot.id.as_str(),
            result.snapshot_id.as_str(),
            result.total_file_count
        ),
        ("abc123", "abc123", 3)
    );
    assert_eq!(*seen.lock().unwrap(), [3]);
}

#[tokio::test]
async fn tools_report_german_errors_without_connection_or_snapshot() {
    let test = new_session();
    let stop = CancellationToken::new();
    assert_eq!(
        test.session
            .check_repository(CheckMode::Quick, &stop)
            .await
            .unwrap_err()
            .to_string(),
        "Es besteht keine Repository-Verbindung."
    );
    assert_eq!(
        test.session
            .fetch_repository_stats(&stop)
            .await
            .unwrap_err()
            .to_string(),
        "Es besteht keine Repository-Verbindung."
    );
    assert!(
        test.session
            .analyze_selected_snapshot(&|_| {}, &stop)
            .await
            .is_err()
    );

    let connected_without_snapshot = connected(vec![]).await;
    let error = connected_without_snapshot
        .session
        .analyze_selected_snapshot(&|_| {}, &stop)
        .await
        .unwrap_err();
    assert_eq!(error.to_string(), "Es ist kein Snapshot ausgewählt.");
}

#[tokio::test]
async fn explicit_statistics_are_returned_and_stored() {
    let test = connected(vec![snapshot("s", "h", "/", &[], 1)]).await;
    let session = test.session.clone();
    let task = tokio::spawn(async move { session.fetch_repository_stats(&CancellationToken::new()).await });
    test.fake
        .complete_stats(RepositoryStats {
            total_size: 2048,
            snapshots_count: 2,
            ..RepositoryStats::default()
        })
        .await;
    let stats = task.await.unwrap().unwrap();
    assert_eq!((stats.total_size, stats.snapshots_count), (2048, 2));
    test.session
        .view(|v| assert_eq!(v.repo_stats().unwrap().total_size, 2048));
    assert_eq!(test.fake.stats_calls.load(Ordering::Relaxed), 1);
}
