//! Lesender Repository-Zugriff über Restic (Gegenstück zu `ResticRepositoryService.cs`).
//!
//! Alle Befehle laufen mit `--no-lock`. Restore schreibt ausschließlich in das ausdrücklich gewählte
//! Ziel außerhalb des Repositorys. Mount gehört nicht zu diesem Stand (siehe `docs/rust-migration.md`).

use std::collections::HashMap;
use std::future::Future;
use std::path::Path;
use std::sync::Mutex as StdMutex;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::time::{Duration, Instant};

use serde::de::DeserializeOwned;
use serde_json::Value;
use tokio::sync::Mutex;
use tokio_util::sync::CancellationToken;

use crate::bounded_details::BoundedCheckDetails;
use crate::command::{self, normalize_snapshot_path};
use crate::credentials::SessionCredentials;
use crate::error::ResticError;
use crate::models::{
    BackupNode, CheckMode, FilePreviewData, FileSearchResult, FileVersion, FindSnapshotGroup,
    LatestFileMatch, RepositoryCheckResult, RepositoryStats, ResticVersion, RestorePreviewItem,
    RestorePreviewResult, RestoreProgress, RestoreRequest, RestoreResult, SnapshotInfo, format_bytes,
};
use crate::paths::validate_destination;
use crate::profile::RepositoryProfile;
use crate::result_batch::ResultBatch;
use crate::runner::{Flow, ProcessResult, ResticCommand, ResticProcessRunner};
use crate::storage::{StorageAnalysisProgress, StorageAnalysisResult, StorageAnalyzer, extension_of};
use crate::version_probe::{PROBE_TIMEOUT, ProcessVersionProbe, VersionProbe};

/// Obergrenze für Suchtreffer und Dateiversionen.
pub const MAXIMUM_SEARCH_MATCHES: usize = 10_000;
const MAXIMUM_PREVIEW_BYTES: usize = 5 * 1024 * 1024;
const MAXIMUM_REPORTED_RESTORE_ERRORS: usize = 100;
const PROGRESS_INTERVAL: Duration = Duration::from_millis(200);

#[derive(Debug, Clone, Copy, Default)]
pub struct ResticRepositoryService {
    runner: ResticProcessRunner,
}

impl ResticRepositoryService {
    pub fn new() -> Self {
        Self::default()
    }

    /// Prüft die Restic-Datei des Profils mit `restic version --json`.
    pub async fn validate(
        &self,
        profile: &RepositoryProfile,
        stop: &CancellationToken,
    ) -> Result<ResticVersion, ResticError> {
        let executable = require_executable(profile)?;
        ProcessVersionProbe::new()
            .probe(&executable, stop, PROBE_TIMEOUT)
            .await
    }

    pub async fn get_snapshots(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        stop: &CancellationToken,
    ) -> Result<Vec<SnapshotInfo>, ResticError> {
        let snapshots = StdMutex::new(Vec::new());
        let snapshots_ref = &snapshots;
        self.get_snapshots_batched(
            profile,
            credentials,
            move |batch| {
                snapshots_ref.lock().expect("Sperre").extend(batch);
                std::future::ready(Ok(()))
            },
            stop,
        )
        .await?;
        Ok(snapshots.into_inner().expect("Sperre"))
    }

    /// Liefert Snapshots in Batches, sobald sie gelesen werden (erster sofort, höchstens 256 je Batch).
    pub async fn get_snapshots_batched<F, Fut>(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        on_batch: F,
        stop: &CancellationToken,
    ) -> Result<(), ResticError>
    where
        F: FnMut(Vec<SnapshotInfo>) -> Fut,
        Fut: Future<Output = Result<(), ResticError>>,
    {
        let cmd = self.command(profile, credentials, |repo| {
            command::with_repository(repo, &["snapshots", "--json"])
        })?;
        let batches = Mutex::new(ResultBatch::new(on_batch, stop));
        let batches_ref = &batches;
        let result = self
            .runner
            .run_json_array(
                &cmd,
                move |item: SnapshotInfo| async move { batches_ref.lock().await.add(item).await },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        batches.lock().await.flush().await
    }

    /// Listet ein Verzeichnis zeilenweise. Ordner stehen zuerst, danach nach Namen (ohne Beachtung
    /// der Groß-/Kleinschreibung) sortiert.
    pub async fn get_directory_batched<F, Fut>(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        path: &str,
        on_batch: F,
        stop: &CancellationToken,
    ) -> Result<Vec<BackupNode>, ResticError>
    where
        F: FnMut(Vec<BackupNode>) -> Fut,
        Fut: Future<Output = Result<(), ResticError>>,
    {
        let cmd = self.command(profile, credentials, |repo| {
            command::with_repository(
                repo,
                &["ls", "--json", snapshot_id, &normalize_snapshot_path(path)],
            )
        })?;
        let nodes = StdMutex::new(Vec::<BackupNode>::new());
        let nodes_ref = &nodes;
        let batches = Mutex::new(ResultBatch::new(on_batch, stop));
        let batches_ref = &batches;
        let result = self
            .runner
            .run_lines(
                &cmd,
                move |line| async move {
                    if let Some(node) = try_json_line::<BackupNode>(&line)
                        && is_node(&node)
                        && !paths_equal(&node.path, path)
                    {
                        nodes_ref.lock().expect("Sperre").push(node.clone());
                        batches_ref.lock().await.add(node).await?;
                    }
                    Ok(())
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        batches.lock().await.flush().await?;
        let mut nodes = nodes.into_inner().expect("Sperre");
        nodes.sort_by(|a, b| {
            b.is_directory()
                .cmp(&a.is_directory())
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        });
        Ok(nodes)
    }

    /// Sucht in einem Snapshot; bei mehr als 10.000 Treffern wird Restic beendet und `true`
    /// (gekürzt) gemeldet.
    pub async fn find_batched<F, Fut>(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        pattern: &str,
        on_batch: F,
        stop: &CancellationToken,
    ) -> Result<bool, ResticError>
    where
        F: FnMut(Vec<BackupNode>) -> Fut,
        Fut: Future<Output = Result<(), ResticError>>,
    {
        let cmd = self.command(profile, credentials, |repo| {
            command::with_repository(repo, &["find", "--json", "--snapshot", snapshot_id, pattern])
        })?;
        let batches = Mutex::new(ResultBatch::new(on_batch, stop));
        let batches_ref = &batches;
        let count = AtomicUsize::new(0);
        let truncated = AtomicBool::new(false);
        let (count_ref, truncated_ref) = (&count, &truncated);
        let result = self
            .runner
            .run_find_matches_until(
                &cmd,
                move |node| async move {
                    if stop.is_cancelled() {
                        return Err(ResticError::Cancelled);
                    }
                    if count_ref.load(Ordering::Relaxed) < MAXIMUM_SEARCH_MATCHES {
                        count_ref.fetch_add(1, Ordering::Relaxed);
                        batches_ref.lock().await.add(node).await?;
                        Ok(Flow::Continue)
                    } else {
                        truncated_ref.store(true, Ordering::Relaxed);
                        Ok(Flow::Stop)
                    }
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        batches.lock().await.flush().await?;
        Ok(truncated.load(Ordering::Relaxed))
    }

    pub async fn find(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        pattern: &str,
        stop: &CancellationToken,
    ) -> Result<FileSearchResult, ResticError> {
        let matches = StdMutex::new(Vec::with_capacity(256));
        let matches_ref = &matches;
        let is_truncated = self
            .find_batched(
                profile,
                credentials,
                snapshot_id,
                pattern,
                move |batch| {
                    matches_ref.lock().expect("Sperre").extend(batch);
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        Ok(FileSearchResult {
            matches: matches.into_inner().expect("Sperre"),
            is_truncated,
        })
    }

    /// Neueste Version einer Datei über alle Snapshots mit genau einem Restic-Prozess; Restic wird
    /// nach dem ersten gültigen Treffer beendet.
    pub async fn find_newest(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        pattern: &str,
        stop: &CancellationToken,
    ) -> Result<Option<LatestFileMatch>, ResticError> {
        let cmd = self.command(profile, credentials, |repo| {
            command::with_repository(repo, &["find", "--json", pattern])
        })?;
        let newest = StdMutex::new(None);
        let newest_ref = &newest;
        let result = self
            .runner
            .run_json_array_until(
                &cmd,
                move |group: FindSnapshotGroup| async move {
                    if let Some(node) = group.matches.iter().find(|node| !node.is_directory())
                        && !group.snapshot.trim().is_empty()
                    {
                        *newest_ref.lock().expect("Sperre") = Some(LatestFileMatch {
                            snapshot_id: group.snapshot.clone(),
                            node: node.clone(),
                        });
                        return Ok(Flow::Stop);
                    }
                    Ok(Flow::Continue)
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        Ok(newest.into_inner().expect("Sperre"))
    }

    /// Alle Versionen einer Datei (exakter Pfad), neueste zuerst. Mit `known_snapshots` entfällt der
    /// zusätzliche `snapshots`-Aufruf.
    pub async fn get_file_versions(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        exact_path: &str,
        hostname: Option<&str>,
        known_snapshots: Option<&[SnapshotInfo]>,
        stop: &CancellationToken,
    ) -> Result<Vec<FileVersion>, ResticError> {
        let snapshots = match known_snapshots {
            Some(known) => known.to_vec(),
            None => self.get_snapshots(profile, credentials, stop).await?,
        };
        let index: HashMap<&str, &SnapshotInfo> = snapshots.iter().map(|s| (s.id.as_str(), s)).collect();
        let index_ref = &index;
        let cmd = self.command(profile, credentials, |repo| {
            command::with_repository(repo, &["find", "--json", exact_path])
        })?;
        let versions = StdMutex::new(Vec::<FileVersion>::new());
        let versions_ref = &versions;
        let wanted_host = hostname.filter(|h| !h.trim().is_empty()).map(str::to_lowercase);
        let wanted_host_ref = &wanted_host;
        let result = self
            .runner
            .run_json_array_until(
                &cmd,
                move |group: FindSnapshotGroup| async move {
                    let Some(snapshot) = index_ref.get(group.snapshot.as_str()) else {
                        return Ok(Flow::Continue);
                    };
                    if let Some(host) = wanted_host_ref
                        && snapshot.hostname.to_lowercase() != *host
                    {
                        return Ok(Flow::Continue);
                    }
                    for node in group
                        .matches
                        .iter()
                        .filter(|n| !n.is_directory() && n.path == exact_path)
                    {
                        let mut list = versions_ref.lock().expect("Sperre");
                        list.push(FileVersion {
                            snapshot: (*snapshot).clone(),
                            node: node.clone(),
                        });
                        if list.len() >= MAXIMUM_SEARCH_MATCHES {
                            return Ok(Flow::Stop);
                        }
                    }
                    Ok(Flow::Continue)
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        let mut versions = versions.into_inner().expect("Sperre");
        versions.sort_by_key(|version| std::cmp::Reverse(version.snapshot.time));
        Ok(versions)
    }

    /// Stellt die gewählten Pfade in das Ziel außerhalb des Repositorys wieder her.
    pub async fn restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        mut on_progress: impl FnMut(RestoreProgress),
        stop: &CancellationToken,
    ) -> Result<RestoreResult, ResticError> {
        validate_destination(profile, &request.target, "Wiederherstellungsziel")?;
        let cmd = self.command(profile, credentials, |repo| command::restore(repo, request))?;
        let (mut restored, mut skipped) = (0i64, 0i64);
        let mut reported_errors: Vec<String> = Vec::new();
        let mut reported_count = 0usize;
        let mut all_symlink_errors = true;
        let result = self
            .runner
            .run_lines(
                &cmd,
                |line| {
                    for error in json_error_messages(&line) {
                        reported_count += 1;
                        all_symlink_errors &= is_symlink_permission_error(&error);
                        if reported_errors.len() < MAXIMUM_REPORTED_RESTORE_ERRORS
                            && !reported_errors.contains(&error)
                        {
                            reported_errors.push(error);
                        }
                    }
                    if let Some(status) = try_json_line::<RestoreProgress>(&line)
                        && matches!(status.message_type.as_str(), "status" | "summary")
                    {
                        restored = status.files_restored;
                        skipped = status.files_skipped;
                        on_progress(status);
                    }
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        if result.exit_code != 0 {
            let symlink_errors_only =
                reported_count > 0 && all_symlink_errors && result.standard_error.trim().is_empty();
            if symlink_errors_only {
                return Ok(RestoreResult {
                    success: true,
                    exit_code: result.exit_code,
                    files_restored: restored,
                    files_skipped: skipped,
                    message: format!(
                        "Wiederherstellung mit Hinweis abgeschlossen. {} symbolische Verknüpfung(en) konnten unter Windows nicht erstellt werden; alle übrigen Elemente wurden wiederhergestellt.",
                        format_count(reported_count)
                    ),
                });
            }
            let mut lines = reported_errors.clone();
            if reported_count > reported_errors.len() {
                lines.push(format!(
                    "… {} weitere Fehlermeldung(en) wurden ausgeblendet.",
                    format_count(reported_count - reported_errors.len())
                ));
            }
            lines.push(result.standard_error.clone());
            let output = lines
                .into_iter()
                .filter(|l| !l.trim().is_empty())
                .collect::<Vec<_>>()
                .join("\n");
            return Err(create_exit_error(result.exit_code, &output));
        }
        Ok(RestoreResult {
            success: true,
            exit_code: 0,
            files_restored: restored,
            files_skipped: skipped,
            message: "Wiederherstellung erfolgreich abgeschlossen.".to_string(),
        })
    }

    /// Trockenlauf des Restores: zählt Aktionen, ohne etwas zu schreiben.
    pub async fn preview_restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        stop: &CancellationToken,
    ) -> Result<RestorePreviewResult, ResticError> {
        validate_destination(profile, &request.target, "Wiederherstellungsziel")?;
        let cmd = self.command(profile, credentials, |repo| {
            command::preview_restore(repo, request)
        })?;
        let mut preview = RestorePreviewResult::default();
        let result = self
            .runner
            .run_lines(
                &cmd,
                |line| {
                    if let Some(item) = try_json_line::<RestoreProgress>(&line)
                        && item.message_type == "verbose_status"
                    {
                        match item.action.as_str() {
                            "restored" => preview.restored += 1,
                            "updated" => preview.updated += 1,
                            "unchanged" => preview.unchanged += 1,
                            _ => return std::future::ready(Ok(())),
                        }
                        if preview.items.len() < RestorePreviewResult::MAXIMUM_VISIBLE_ITEMS {
                            preview.items.push(RestorePreviewItem {
                                action: item.action,
                                path: item.item,
                                size: item.size,
                            });
                        } else {
                            preview.is_truncated = true;
                        }
                    }
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        Ok(preview)
    }

    /// Integritätsprüfung; Ausgabe wird gestreamt und auf 64 KiB begrenzt.
    pub async fn check(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        mode: CheckMode,
        stop: &CancellationToken,
    ) -> Result<RepositoryCheckResult, ResticError> {
        let cmd = self.command(profile, credentials, |repo| command::check(repo, mode))?;
        let mut check = RepositoryCheckResult {
            mode,
            error_count: 0,
            suggest_repair_index: false,
            suggest_prune: false,
            details: String::new(),
            details_truncated: false,
        };
        let mut details = BoundedCheckDetails::new();
        let result = self
            .runner
            .run_lines(
                &cmd,
                |line| {
                    details.append_line(&line);
                    if let Ok(Value::Object(root)) = serde_json::from_str::<Value>(&line) {
                        if let Some(count) = root
                            .get("num_errors")
                            .and_then(Value::as_i64)
                            .and_then(|n| i32::try_from(n).ok())
                        {
                            check.error_count = check.error_count.max(count);
                        }
                        check.suggest_repair_index |=
                            root.get("suggest_repair_index") == Some(&Value::Bool(true));
                        check.suggest_prune |= root.get("suggest_prune") == Some(&Value::Bool(true));
                    }
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        details.append_line(&result.standard_error);
        check.details_truncated = details.is_truncated();
        check.details = details.into_text();
        if result.exit_code != 0 && check.error_count == 0 {
            return Err(create_exit_error(result.exit_code, &result.standard_error));
        }
        Ok(check)
    }

    /// Statistik des Repositorys. Wird nie automatisch beim Verbinden aufgerufen.
    pub async fn get_stats(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        stop: &CancellationToken,
    ) -> Result<RepositoryStats, ResticError> {
        let cmd = self.command(profile, credentials, command::stats)?;
        let result = self.runner.run_json::<RepositoryStats>(&cmd, stop).await?;
        if result.exit_code != 0 {
            return Err(create_exit_error(result.exit_code, &result.standard_error));
        }
        Ok(result.value.unwrap_or_default())
    }

    /// Lädt eine Vorschau (Text oder Bild) bis 5 MB. Fehler werden als Meldung im Ergebnis geführt.
    pub async fn get_file_preview(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        node: &BackupNode,
        snapshot_id: &str,
        stop: &CancellationToken,
    ) -> Result<FilePreviewData, ResticError> {
        let mut preview = FilePreviewData {
            node: node.clone(),
            path: node.path.clone(),
            ..FilePreviewData::default()
        };
        if node.is_directory() {
            preview.error_message = "Ordner können nicht in der Vorschau angezeigt werden.".to_string();
            return Ok(preview);
        }
        if node.size > MAXIMUM_PREVIEW_BYTES as i64 {
            preview.error_message = format!(
                "Die Datei ist mit {} zu groß für die Direktvorschau (Max 5 MB).",
                format_bytes(node.size)
            );
            return Ok(preview);
        }
        let outcome: Result<Vec<u8>, ResticError> = async {
            let cmd = self.command(profile, credentials, |repo| {
                command::dump(repo, snapshot_id, &node.path)
            })?;
            let result = self.runner.run_binary(&cmd, MAXIMUM_PREVIEW_BYTES, stop).await?;
            if result.exit_code != 0 {
                return Err(create_exit_error(result.exit_code, &result.standard_error));
            }
            Ok(result.standard_output)
        }
        .await;
        match outcome {
            Ok(bytes) => {
                if is_image_extension(&extension_of(&node.name)) {
                    preview.is_image = true;
                    preview.image_bytes = Some(bytes);
                } else {
                    preview.is_text = true;
                    preview.text_content = Some(String::from_utf8_lossy(&bytes).into_owned());
                }
            }
            Err(ResticError::Cancelled) => return Err(ResticError::Cancelled),
            Err(error) => preview.error_message = format!("Vorschau konnte nicht geladen werden: {error}"),
        }
        Ok(preview)
    }

    /// Analysiert Größen und Kategorien eines Snapshots (ein `ls`-Lauf, begrenzter Speicher).
    pub async fn analyze_snapshot_storage(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        mut on_progress: impl FnMut(StorageAnalysisProgress),
        stop: &CancellationToken,
    ) -> Result<StorageAnalysisResult, ResticError> {
        let cmd = self.command(profile, credentials, |repo| command::ls_json(repo, snapshot_id))?;
        let mut analyzer = StorageAnalyzer::new();
        let mut last_progress = Instant::now();
        let result = self
            .runner
            .run_lines(
                &cmd,
                |line| {
                    if let Some(node) = try_json_line::<BackupNode>(&line)
                        && is_node(&node)
                    {
                        analyzer.add(&node);
                        if last_progress.elapsed() >= PROGRESS_INTERVAL {
                            on_progress(analyzer.progress());
                            last_progress = Instant::now();
                        }
                    }
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        ensure_success(&result)?;
        on_progress(analyzer.progress());
        Ok(analyzer.finish(snapshot_id))
    }

    fn command(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        arguments: impl FnOnce(&str) -> Vec<String>,
    ) -> Result<ResticCommand, ResticError> {
        let executable = require_executable(profile)?;
        let repository = profile.build_repository_string()?;
        Ok(ResticCommand::new(executable, arguments(&repository))
            .with_environment(build_environment(credentials)))
    }
}

fn build_environment(credentials: &SessionCredentials) -> HashMap<String, String> {
    let mut environment = credentials.environment().clone();
    environment.insert("RESTIC_PASSWORD".to_string(), credentials.password().to_string());
    environment
}

fn require_executable(profile: &RepositoryProfile) -> Result<String, ResticError> {
    profile
        .resolved_restic_executable
        .as_deref()
        .or(profile.restic_executable.as_deref())
        .filter(|path| !path.trim().is_empty() && Path::new(path).is_file())
        .map(str::to_string)
        .ok_or_else(|| ResticError::new("Das ausgewählte Restic-Programm wurde nicht gefunden."))
}

fn ensure_success(result: &ProcessResult) -> Result<(), ResticError> {
    if result.exit_code != 0 && !result.stopped_early {
        return Err(create_exit_error(result.exit_code, &result.standard_error));
    }
    Ok(())
}

fn try_json_line<T: DeserializeOwned>(line: &str) -> Option<T> {
    serde_json::from_str(line).ok()
}

fn is_node(node: &BackupNode) -> bool {
    node.message_type == "node" || node.struct_type == "node"
}

fn paths_equal(left: &str, right: &str) -> bool {
    normalize_snapshot_path(left).trim_end_matches('/')
        == normalize_snapshot_path(right).trim_end_matches('/')
}

fn is_image_extension(extension: &str) -> bool {
    matches!(
        extension,
        ".png" | ".jpg" | ".jpeg" | ".bmp" | ".gif" | ".ico" | ".webp"
    )
}

/// Zahl mit deutschem Tausenderpunkt, z. B. `1.234`.
fn format_count(value: usize) -> String {
    let digits = value.to_string();
    let mut result = String::new();
    for (index, ch) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            result.push('.');
        }
        result.push(ch);
    }
    result
}

/// Übersetzt Exitcodes und Fehlertexte von Restic in verständliche deutsche Meldungen.
pub fn create_exit_error(exit_code: i32, standard_error: &str) -> ResticError {
    let detail = format_error_detail(standard_error);
    let lower = detail.to_lowercase();
    let message = match exit_code {
        10 => "Das Repository wurde nicht gefunden oder ist nicht initialisiert.".to_string(),
        11 => "Das Repository ist momentan durch einen anderen Vorgang gesperrt.".to_string(),
        12 => "Das Repository-Passwort ist falsch.".to_string(),
        130 => "Der Vorgang wurde abgebrochen.".to_string(),
        _ if lower.contains("no space") => "Auf dem Ziellaufwerk ist nicht genügend Speicherplatz.".to_string(),
        _ if contains_only_symlink_permission_errors(&detail) => "Symbolische Links konnten nicht wiederhergestellt werden. Aktiviere in Windows den Entwicklermodus oder starte die App mit dem Recht zum Erstellen symbolischer Links und wiederhole die Wiederherstellung.".to_string(),
        _ if lower.contains("access") || lower.contains("permission") => "Der Zugriff wurde verweigert.".to_string(),
        _ if detail.trim().is_empty() => format!("Restic wurde mit Fehlercode {exit_code} beendet."),
        _ => detail,
    };
    ResticError::with_exit_code(message, exit_code)
}

fn is_symlink_permission_error(detail: &str) -> bool {
    let lower = detail.to_lowercase();
    lower.contains("symlink")
        && (lower.contains("erforderliches recht")
            || lower.contains("required privilege")
            || lower.contains("privilege not held"))
}

fn contains_only_symlink_permission_errors(detail: &str) -> bool {
    let errors: Vec<&str> = detail.lines().map(str::trim).filter(|l| !l.is_empty()).collect();
    !errors.is_empty() && errors.iter().all(|e| is_symlink_permission_error(e))
}

fn format_error_detail(output: &str) -> String {
    let messages = json_error_messages(output);
    if messages.is_empty() {
        output.trim().to_string()
    } else {
        messages.join("\n")
    }
}

/// Meldungen aus Restic-Fehlerzeilen der Form `{"error":{"message":"…"}}` (ohne Duplikate).
fn json_error_messages(output: &str) -> Vec<String> {
    let mut messages: Vec<String> = Vec::new();
    for line in output.split(['\r', '\n']).filter(|l| !l.is_empty()) {
        if let Ok(Value::Object(root)) = serde_json::from_str::<Value>(line)
            && let Some(Value::Object(error)) = root.get("error")
            && let Some(Value::String(message)) = error.get("message")
            && !message.trim().is_empty()
            && !messages.contains(message)
        {
            messages.push(message.clone());
        }
    }
    messages
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn exit_codes_map_to_german_messages() {
        assert!(create_exit_error(10, "").to_string().contains("nicht gefunden"));
        assert!(create_exit_error(11, "").to_string().contains("gesperrt"));
        assert_eq!(
            create_exit_error(12, "irgendwas").to_string(),
            "Das Repository-Passwort ist falsch."
        );
        assert_eq!(
            create_exit_error(130, "").to_string(),
            "Der Vorgang wurde abgebrochen."
        );
        assert_eq!(
            create_exit_error(1, "").to_string(),
            "Restic wurde mit Fehlercode 1 beendet."
        );
        assert_eq!(
            create_exit_error(7, "Etwas ist schiefgelaufen\n").to_string(),
            "Etwas ist schiefgelaufen"
        );
        assert_eq!(create_exit_error(7, "x").exit_code(), Some(7));
    }

    #[test]
    fn detail_text_is_classified() {
        assert!(
            create_exit_error(1, "write: no space left on device")
                .to_string()
                .contains("Speicherplatz")
        );
        assert_eq!(
            create_exit_error(1, "Access is denied").to_string(),
            "Der Zugriff wurde verweigert."
        );
        assert_eq!(
            create_exit_error(1, "open x: permission denied").to_string(),
            "Der Zugriff wurde verweigert."
        );
    }

    #[test]
    fn json_errors_are_unwrapped_and_deduplicated() {
        let output = "{\"message_type\":\"error\",\"error\":{\"message\":\"A\"}}\nkein json\n{\"error\":{\"message\":\"A\"}}\n{\"error\":{\"message\":\"  \"}}\n{\"error\":{\"message\":\"B\"}}";
        assert_eq!(json_error_messages(output), ["A", "B"]);
        assert_eq!(create_exit_error(1, output).to_string(), "A\nB");
    }

    #[test]
    fn symlink_privilege_errors_are_recognised() {
        let error = "{\"message_type\":\"error\",\"error\":{\"message\":\"symlink \\\\usr\\\\bin\\\\mail: A required privilege is not held by the client.\"},\"during\":\"restore\"}";
        assert!(
            create_exit_error(1, error)
                .to_string()
                .contains("Symbolische Links")
        );
        let mixed = format!("{error}\n{{\"error\":{{\"message\":\"open x: some other error\"}}}}");
        assert!(
            !create_exit_error(1, &mixed)
                .to_string()
                .contains("Symbolische Links")
        );
    }

    #[test]
    fn counts_use_german_thousands_separator() {
        assert_eq!(format_count(0), "0");
        assert_eq!(format_count(999), "999");
        assert_eq!(format_count(1000), "1.000");
        assert_eq!(format_count(1234567), "1.234.567");
    }

    #[test]
    fn executable_must_exist() {
        let profile = RepositoryProfile::new("repo");
        assert!(
            require_executable(&profile)
                .unwrap_err()
                .to_string()
                .contains("nicht gefunden")
        );
        let profile = RepositoryProfile {
            restic_executable: Some("/gibt/es/nicht".into()),
            ..RepositoryProfile::new("repo")
        };
        assert!(require_executable(&profile).is_err());
        let current = std::env::current_exe().unwrap().to_string_lossy().into_owned();
        let profile = RepositoryProfile {
            resolved_restic_executable: Some(current.clone()),
            ..RepositoryProfile::new("repo")
        };
        assert_eq!(require_executable(&profile).unwrap(), current);
    }

    #[test]
    fn environment_adds_the_password_without_changing_credentials() {
        let credentials =
            SessionCredentials::new("pw", HashMap::from([("TOKEN".to_string(), "t".to_string())]));
        let environment = build_environment(&credentials);
        assert_eq!(environment["RESTIC_PASSWORD"], "pw");
        assert_eq!(environment["TOKEN"], "t");
        assert!(!credentials.environment().contains_key("RESTIC_PASSWORD"));
    }

    #[test]
    fn directory_path_comparison_ignores_trailing_slash_and_backslashes() {
        assert!(paths_equal("/a/b/", "/a/b"));
        assert!(paths_equal(r"a\b", "/a/b"));
        assert!(!paths_equal("/a/b", "/a/bc"));
    }
}
