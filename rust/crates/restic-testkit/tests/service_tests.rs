//! Service-Tests (Phase 3). Gegenstücke zu `StreamingTests`, `PerformanceTests`, `RestoreTests`,
//! `ReliabilityTests` und `IntegrationTests` der C#-Linie. Die meisten Tests starten das
//! skriptgesteuerte `scripted-restic` als echten Prozess; der End-to-End-Test nutzt echtes Restic.

use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use restic_core::models::{BackupNode, CheckMode, OverwritePolicy, RestoreRequest, SnapshotInfo};
use restic_core::{RepositoryProfile, ResticError, ResticRepositoryService, SessionCredentials};
use serde_json::{Value, json};
use tokio_util::sync::CancellationToken;

static COUNTER: AtomicUsize = AtomicUsize::new(0);
const EXE: &str = if cfg!(windows) { "restic.exe" } else { "restic" };

struct TempDir(PathBuf);

impl TempDir {
    fn new(name: &str) -> Self {
        let id = COUNTER.fetch_add(1, Ordering::Relaxed);
        let path = std::env::temp_dir().join(format!("rb-svc-{name}-{}-{id}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

struct Setup {
    root: TempDir,
    exe: PathBuf,
    profile: RepositoryProfile,
}

fn setup(script: Value) -> Setup {
    let root = TempDir::new("script");
    let exe = root.0.join(EXE);
    fs::copy(env!("CARGO_BIN_EXE_scripted-restic"), &exe).unwrap();
    fs::write(format!("{}.script", exe.display()), script.to_string()).unwrap();
    let profile = RepositoryProfile {
        repository: root.0.join("repo").to_string_lossy().into_owned(),
        restic_executable: Some(exe.to_string_lossy().into_owned()),
        ..RepositoryProfile::default()
    };
    Setup { root, exe, profile }
}

impl Setup {
    fn side(&self, extension: &str) -> String {
        format!("{}.{extension}", self.exe.display())
    }

    /// Befehle aller bisherigen Starts.
    fn starts(&self) -> Vec<String> {
        fs::read_to_string(self.side("log"))
            .map(|t| t.lines().map(str::to_string).collect())
            .unwrap_or_default()
    }

    fn args(&self, command: &str) -> Vec<String> {
        let text =
            fs::read_to_string(self.side(&format!("args.{command}"))).expect("Argumente protokolliert");
        serde_json::from_str(&text).unwrap()
    }

    async fn assert_process_gone(&self) {
        let alive = self.side("alive");
        let read = || {
            fs::read_to_string(&alive)
                .ok()
                .and_then(|t| t.trim().parse::<u64>().ok())
        };
        for _ in 0..50 {
            let before = read();
            tokio::time::sleep(Duration::from_millis(400)).await;
            if before == read() {
                return;
            }
        }
        panic!("Der Prozess läuft noch");
    }
}

fn creds() -> SessionCredentials {
    SessionCredentials::new("secret", HashMap::new())
}

fn stop() -> CancellationToken {
    CancellationToken::new()
}

fn service() -> ResticRepositoryService {
    ResticRepositoryService::new()
}

async fn within<T>(future: impl std::future::Future<Output = T>) -> T {
    tokio::time::timeout(Duration::from_secs(60), future)
        .await
        .expect("Zeitüberschreitung: Prozess wurde nicht beendet")
}

fn error_text<T: std::fmt::Debug>(result: Result<T, ResticError>) -> String {
    result.expect_err("ein Fehler wurde erwartet").to_string()
}

fn node(name: &str, path: &str, size: i64) -> BackupNode {
    BackupNode {
        name: name.into(),
        path: path.into(),
        node_type: "file".into(),
        size,
        ..BackupNode::default()
    }
}

// --- Snapshots und Fehlerabbildung ---

#[tokio::test]
async fn snapshots_are_listed_read_only_and_the_password_stays_in_the_environment() {
    let snapshots = r#"[{"id":"aaaa1111bbbb2222","time":"2026-01-02T10:00:00Z","hostname":"h","paths":["/x"],"unknown":1},{"id":"cccc3333dddd4444","time":"2026-01-01T10:00:00Z","hostname":"h"}]"#;
    let s = setup(json!({ "snapshots": { "stdout": snapshots } }));
    let result = service()
        .get_snapshots(&s.profile, &creds(), &stop())
        .await
        .unwrap();
    assert_eq!(result.len(), 2);
    assert_eq!(result[0].display_id(), "aaaa1111");
    let args = s.args("snapshots");
    assert_eq!(args.iter().filter(|a| *a == "--no-lock").count(), 1);
    assert!(args.contains(&"--json".to_string()));
    assert!(
        !args.iter().any(|a| a.contains("secret")),
        "das Passwort darf nie in den Argumenten stehen"
    );
    assert_eq!(fs::read_to_string(s.side("password")).unwrap(), "secret");
}

#[tokio::test]
async fn restic_errors_become_german_messages_with_exit_codes() {
    let cases = [
        (1, "open x: permission denied", "Der Zugriff wurde verweigert."),
        (
            10,
            "",
            "Das Repository wurde nicht gefunden oder ist nicht initialisiert.",
        ),
        (
            11,
            "",
            "Das Repository ist momentan durch einen anderen Vorgang gesperrt.",
        ),
        (12, "", "Das Repository-Passwort ist falsch."),
    ];
    for (code, stderr, expected) in cases {
        let s = setup(json!({ "*": { "exit": code, "stderr": stderr } }));
        let error = service()
            .get_snapshots(&s.profile, &creds(), &stop())
            .await
            .unwrap_err();
        assert_eq!(error.to_string(), expected);
        assert_eq!(error.exit_code(), Some(code));
    }
}

#[tokio::test]
async fn missing_executable_is_reported_before_any_process_starts() {
    let profile = RepositoryProfile {
        restic_executable: Some("/gibt/es/nicht".into()),
        ..RepositoryProfile::new("repo")
    };
    let error = error_text(service().get_snapshots(&profile, &creds(), &stop()).await);
    assert!(error.contains("nicht gefunden"), "{error}");
}

#[tokio::test]
async fn remote_backend_addresses_are_refused_before_starting_restic() {
    let s = setup(json!({}));
    let profile = RepositoryProfile {
        repository: "sftp:user@host:/repo".into(),
        ..s.profile.clone()
    };
    let error = error_text(service().get_snapshots(&profile, &creds(), &stop()).await);
    assert!(error.contains("Nur lokale"), "{error}");
    assert!(s.starts().is_empty());
}

// --- Verzeichnis, Suche, Dateiversionen ---

#[tokio::test]
async fn directory_listing_streams_in_bounded_batches() {
    let s = setup(json!({ "ls": { "gen": "ls:1000" } }));
    let sizes = Arc::new(Mutex::new(Vec::new()));
    let sizes_in = Arc::clone(&sizes);
    let nodes = service()
        .get_directory_batched(
            &s.profile,
            &creds(),
            "snapshot",
            "/",
            move |batch| {
                sizes_in.lock().unwrap().push(batch.len());
                std::future::ready(Ok(()))
            },
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(nodes.len(), 1000);
    let sizes = sizes.lock().unwrap();
    assert_eq!(sizes[0], 1, "der erste Treffer wird sofort geliefert");
    assert!(sizes.iter().all(|&n| n <= 256));
    assert_eq!(sizes.iter().sum::<usize>(), 1000);
    assert!(sizes.len() >= 5);
    assert_eq!(
        s.args("ls")[s.args("ls").iter().position(|a| a == "snapshot").unwrap() + 1],
        "/"
    );
}

#[tokio::test]
async fn directory_listing_skips_the_directory_itself_and_sorts_folders_first() {
    let lines = [
        r#"{"message_type":"snapshot","id":"x"}"#,
        r#"{"message_type":"node","name":"docs","path":"/docs","type":"dir"}"#,
        r#"{"message_type":"node","name":"zeta.txt","path":"/docs/zeta.txt","type":"file","size":1}"#,
        r#"{"message_type":"node","name":"Alpha.txt","path":"/docs/Alpha.txt","type":"file","size":1}"#,
        r#"{"message_type":"node","name":"beta","path":"/docs/beta","type":"dir"}"#,
        "kein json",
    ]
    .join("\n");
    let s = setup(json!({ "ls": { "stdout": lines } }));
    let nodes = service()
        .get_directory_batched(
            &s.profile,
            &creds(),
            "snap",
            "/docs/",
            |_| std::future::ready(Ok(())),
            &stop(),
        )
        .await
        .unwrap();
    let names: Vec<_> = nodes.iter().map(|n| n.name.as_str()).collect();
    assert_eq!(names, ["beta", "Alpha.txt", "zeta.txt"]);
}

#[tokio::test]
async fn search_stops_restic_at_the_limit_and_reports_truncation() {
    for generator in ["find-groups-search:10001", "find-matches:10001"] {
        let s = setup(json!({ "find": { "gen": generator, "hang_after": true } }));
        let batch_sizes = Arc::new(Mutex::new(Vec::new()));
        let batch_sizes_in = Arc::clone(&batch_sizes);
        let count = Arc::new(AtomicUsize::new(0));
        let count_in = Arc::clone(&count);
        let truncated = within(service().find_batched(
            &s.profile,
            &creds(),
            "s",
            "*",
            move |batch| {
                assert!(batch.len() <= 256);
                count_in.fetch_add(batch.len(), Ordering::Relaxed);
                batch_sizes_in.lock().unwrap().push(batch.len());
                std::future::ready(Ok(()))
            },
            &stop(),
        ))
        .await
        .unwrap();
        assert_eq!(count.load(Ordering::Relaxed), 10_000, "{generator}");
        assert!(truncated, "{generator}");
        assert!(batch_sizes.lock().unwrap().len() > 1);
        let args = s.args("find");
        assert!(args.windows(2).any(|w| w == ["--snapshot", "s"]));
        s.assert_process_gone().await;
    }
}

#[tokio::test]
async fn find_collects_matches_without_truncation_for_small_results() {
    let s = setup(
        json!({ "find": { "stdout": r#"[{"snapshot":"s","matches":[{"name":"a.txt","path":"/a.txt","type":"file"}]}]"# } }),
    );
    let result = service()
        .find(&s.profile, &creds(), "s", "a.txt", &stop())
        .await
        .unwrap();
    assert_eq!(result.matches.len(), 1);
    assert!(!result.is_truncated);
}

#[tokio::test]
async fn newest_search_needs_one_process_and_stops_after_the_first_file() {
    let json_output = r#"[{"snapshot":"newest-snapshot","matches":[{"name":"dir","path":"/dir","type":"dir"},{"name":"probe.txt","path":"/probe.txt","type":"file","size":12}]},
 {"snapshot":"older-snapshot","matches":[{"name":"probe.txt","path":"/probe.txt","type":"file","size":10}]}]"#;
    let s = setup(json!({ "find": { "stdout": json_output, "hang_after": true } }));
    let result = within(service().find_newest(&s.profile, &creds(), "probe.txt", &stop()))
        .await
        .unwrap()
        .unwrap();
    assert_eq!(result.snapshot_id, "newest-snapshot");
    assert_eq!(result.node.name, "probe.txt");
    assert_eq!(s.starts(), ["find"], "genau ein Restic-Prozess");
    assert!(!s.args("find").contains(&"--snapshot".to_string()));
    s.assert_process_gone().await;
}

#[tokio::test]
async fn newest_search_without_file_hit_returns_none() {
    let s = setup(
        json!({ "find": { "stdout": r#"[{"snapshot":"s","matches":[{"name":"d","path":"/d","type":"dir"}]}]"# } }),
    );
    assert!(
        service()
            .find_newest(&s.profile, &creds(), "d", &stop())
            .await
            .unwrap()
            .is_none()
    );
}

fn numbered_snapshots(count: usize) -> Vec<SnapshotInfo> {
    (0..count)
        .map(|i| {
            let time = chrono_time(i);
            serde_json::from_value(
                json!({ "id": format!("snapshot-{i:05}"), "hostname": "host", "paths": ["/"], "time": time }),
            )
            .unwrap()
        })
        .collect()
}

fn chrono_time(minutes: usize) -> String {
    format!("2026-01-01T{:02}:{:02}:00Z", (minutes / 60) % 24, minutes % 60)
}

#[tokio::test]
async fn file_versions_stop_restic_at_the_limit() {
    for (count, stops) in [(9_999usize, false), (10_000, true), (10_001, true)] {
        let s = setup(json!({ "find": { "gen": format!("find-groups:{count}"), "hang_after": stops } }));
        let snapshots = numbered_snapshots(count);
        let versions = within(service().get_file_versions(
            &s.profile,
            &creds(),
            "/file.txt",
            None,
            Some(&snapshots),
            &stop(),
        ))
        .await
        .unwrap();
        assert_eq!(versions.len(), count.min(10_000), "{count}");
        assert!(
            versions
                .windows(2)
                .all(|w| w[0].snapshot.time >= w[1].snapshot.time),
            "neueste zuerst"
        );
        if stops {
            s.assert_process_gone().await;
        }
    }
}

#[tokio::test]
async fn file_versions_reuse_known_snapshots_and_filter_by_host() {
    let snapshot = r#"[{"id":"snapshot-1","hostname":"Host","paths":["/"],"time":"2026-01-01T00:00:00Z"}]"#;
    let group = r#"[{"snapshot":"snapshot-1","matches":[{"name":"file.txt","path":"/file.txt","type":"file"},{"name":"file.txt","path":"/other/file.txt","type":"file"}]}]"#;
    let s = setup(json!({ "snapshots": { "stdout": snapshot }, "find": { "stdout": group } }));
    let known: Vec<SnapshotInfo> = serde_json::from_str(snapshot).unwrap();

    let versions = service()
        .get_file_versions(&s.profile, &creds(), "/file.txt", None, Some(&known), &stop())
        .await
        .unwrap();
    assert_eq!(versions.len(), 1, "nur der exakte Pfad zählt");
    assert_eq!(s.starts(), ["find"], "keine zusätzliche Snapshot-Liste");

    let listed = service()
        .get_file_versions(&s.profile, &creds(), "/file.txt", None, None, &stop())
        .await
        .unwrap();
    assert_eq!(listed.len(), 1);
    assert_eq!(s.starts(), ["find", "snapshots", "find"]);

    let same_host = service()
        .get_file_versions(
            &s.profile,
            &creds(),
            "/file.txt",
            Some("HOST"),
            Some(&known),
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(same_host.len(), 1, "Rechnername ohne Beachtung der Schreibweise");
    let other_host = service()
        .get_file_versions(
            &s.profile,
            &creds(),
            "/file.txt",
            Some("anderer"),
            Some(&known),
            &stop(),
        )
        .await
        .unwrap();
    assert!(other_host.is_empty());
}

// --- Restore ---

fn restore_request(target: &Path) -> RestoreRequest {
    RestoreRequest {
        snapshot_id: "snap".into(),
        target: target.to_string_lossy().into_owned(),
        includes: vec!["/etc".into()],
        overwrite: OverwritePolicy::Never,
    }
}

#[tokio::test]
async fn restore_reports_progress_and_final_counts() {
    let lines = [
        r#"{"message_type":"status","percent_done":0.5,"files_restored":1,"files_skipped":0}"#,
        r#"{"message_type":"error","error":{"message":"ignoriert, weil Exit 0"}}"#,
        r#"{"message_type":"summary","files_restored":3,"files_skipped":2}"#,
    ]
    .join("\n");
    let s = setup(json!({ "restore": { "stdout": lines } }));
    let target = s.root.0.join("ziel");
    let mut seen = Vec::new();
    let result = service()
        .restore(
            &s.profile,
            &creds(),
            &restore_request(&target),
            |p| seen.push((p.message_type, p.files_restored)),
            &stop(),
        )
        .await
        .unwrap();
    assert!(result.success);
    assert_eq!((result.files_restored, result.files_skipped), (3, 2));
    assert_eq!(result.message, "Wiederherstellung erfolgreich abgeschlossen.");
    assert_eq!(seen, [("status".to_string(), 1), ("summary".to_string(), 3)]);
    let args = s.args("restore");
    assert_eq!(
        args[args.iter().position(|a| a == "--target").unwrap() + 1],
        target.to_string_lossy()
    );
    assert_eq!(
        args[args.iter().position(|a| a == "--overwrite").unwrap() + 1],
        "never"
    );
}

#[tokio::test]
async fn restore_with_only_symlink_privilege_errors_is_a_success_with_a_note() {
    let error = "{\"message_type\":\"error\",\"error\":{\"message\":\"symlink \\\\usr\\\\bin\\\\mail: A required privilege is not held by the client.\"},\"during\":\"restore\"}";
    let s = setup(json!({ "restore": { "stdout": error, "exit": 1 } }));
    let result = service()
        .restore(
            &s.profile,
            &creds(),
            &restore_request(&s.root.0.join("ziel")),
            |_| {},
            &stop(),
        )
        .await
        .unwrap();
    assert!(result.success);
    assert!(
        result.message.contains("symbolische Verknüpfung"),
        "{}",
        result.message
    );
    assert!(!result.message.contains("message_type"));
}

#[tokio::test]
async fn restore_with_additional_errors_fails() {
    let errors = [
        "{\"message_type\":\"error\",\"error\":{\"message\":\"symlink \\\\usr\\\\bin\\\\mail: A required privilege is not held by the client.\"},\"during\":\"restore\"}",
        "{\"message_type\":\"error\",\"error\":{\"message\":\"open \\\\etc\\\\secret: permission denied\"},\"during\":\"restore\"}",
    ]
    .join("\n");
    let s = setup(json!({ "restore": { "stdout": errors, "exit": 1 } }));
    let error = error_text(
        service()
            .restore(
                &s.profile,
                &creds(),
                &restore_request(&s.root.0.join("ziel")),
                |_| {},
                &stop(),
            )
            .await,
    );
    assert_eq!(error, "Der Zugriff wurde verweigert.");
}

#[tokio::test]
async fn restore_error_output_stays_bounded() {
    let s = setup(json!({ "restore": { "gen": "restore-errors:150", "exit": 1 } }));
    let error = error_text(
        service()
            .restore(
                &s.profile,
                &creds(),
                &restore_request(&s.root.0.join("ziel")),
                |_| {},
                &stop(),
            )
            .await,
    );
    assert!(error.contains("weitere Fehlermeldung"), "{error}");
    assert!(error.contains("50 weitere"), "{error}");
    assert!(error.len() < 4_000, "{}", error.len());
}

#[tokio::test]
async fn restore_and_preview_refuse_targets_that_overlap_the_repository() {
    let s = setup(json!({}));
    let repository = PathBuf::from(&s.profile.repository);
    fs::create_dir_all(&repository).unwrap();
    for target in [repository.clone(), repository.join("child"), s.root.0.clone()] {
        let request = restore_request(&target);
        let error = error_text(
            service()
                .restore(&s.profile, &creds(), &request, |_| {}, &stop())
                .await,
        );
        assert!(error.contains("überlappen"), "{target:?}: {error}");
        let error = error_text(
            service()
                .preview_restore(&s.profile, &creds(), &request, &stop())
                .await,
        );
        assert!(error.contains("überlappen"), "{target:?}: {error}");
    }
    assert!(
        s.starts().is_empty(),
        "bei unsicherem Ziel wird Restic nie gestartet"
    );
}

#[tokio::test]
async fn restore_preview_counts_actions_without_writing() {
    let lines = [
        r#"{"message_type":"verbose_status","action":"restored","item":"/a","size":10}"#,
        r#"{"message_type":"verbose_status","action":"updated","item":"/b","size":20}"#,
        r#"{"message_type":"verbose_status","action":"unchanged","item":"/c","size":30}"#,
        r#"{"message_type":"verbose_status","action":"deleted","item":"/d"}"#,
        r#"{"message_type":"status","percent_done":1}"#,
    ]
    .join("\n");
    let s = setup(json!({ "restore": { "stdout": lines } }));
    let preview = service()
        .preview_restore(
            &s.profile,
            &creds(),
            &restore_request(&s.root.0.join("ziel")),
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!((preview.restored, preview.updated, preview.unchanged), (1, 1, 1));
    assert_eq!(preview.items.len(), 3);
    assert!(!preview.is_truncated);
    let args = s.args("restore");
    assert!(args.contains(&"--dry-run".to_string()) && args.contains(&"--verbose=2".to_string()));
}

// --- Check, Stats, Vorschau ---

#[tokio::test]
async fn check_streams_and_bounds_details() {
    let s = setup(
        json!({ "check": { "gen": "check-big:1000", "exit": 1, "stderr": "Zusätzliche Fehlerausgabe" } }),
    );
    let result = service()
        .check(&s.profile, &creds(), CheckMode::Full, &stop())
        .await
        .unwrap();
    assert_eq!(result.error_count, 3);
    assert!(result.suggest_repair_index && result.suggest_prune && !result.is_healthy());
    assert!(result.details.len() <= 64 * 1024);
    assert!(result.details.contains("gekürzt"));
    assert!(result.details_truncated);
    assert!(s.args("check").contains(&"--read-data".to_string()));
}

#[tokio::test]
async fn healthy_check_and_failing_check_are_distinguished() {
    let healthy = setup(json!({ "check": { "stdout": "{\"num_errors\":0}\n" } }));
    let result = service()
        .check(&healthy.profile, &creds(), CheckMode::Quick, &stop())
        .await
        .unwrap();
    assert!(result.is_healthy() && !result.details.contains("gekürzt") && !result.details_truncated);
    assert!(!healthy.args("check").contains(&"--read-data".to_string()));

    let failing = setup(
        json!({ "check": { "stdout": "Fehler bei Prüfung\n", "exit": 2, "stderr": "Zugriff verweigert" } }),
    );
    let error = service()
        .check(&failing.profile, &creds(), CheckMode::Quick, &stop())
        .await
        .unwrap_err();
    assert_eq!(error.exit_code(), Some(2));
    assert_eq!(error.to_string(), "Zugriff verweigert");
}

#[tokio::test]
async fn stats_are_read_on_demand() {
    let s = setup(
        json!({ "stats": { "stdout": r#"{"total_size":2048,"total_file_count":7,"snapshots_count":2,"extra":true}"# } }),
    );
    let stats = service().get_stats(&s.profile, &creds(), &stop()).await.unwrap();
    assert_eq!(
        (stats.total_size, stats.total_file_count, stats.snapshots_count),
        (2048, 7, 2)
    );
    let broken = setup(json!({ "stats": { "exit": 12 } }));
    assert!(
        service()
            .get_stats(&broken.profile, &creds(), &stop())
            .await
            .is_err()
    );
}

#[tokio::test]
async fn previews_cover_text_images_directories_size_limit_and_failures() {
    let s = setup(json!({ "dump": { "stdout": "restic-browser-e2e\n" } }));
    let text = service()
        .get_file_preview(
            &s.profile,
            &creds(),
            &node("a.txt", "/dir/a.txt", 19),
            "snap",
            &stop(),
        )
        .await
        .unwrap();
    assert!(text.is_text && !text.is_image);
    assert_eq!(
        text.text_content.as_deref().map(str::trim),
        Some("restic-browser-e2e")
    );
    assert!(s.args("dump").contains(&"/dir/a.txt".to_string()));

    let image = setup(json!({ "dump": { "stdout_hex": "89504e470d0a1a0a" } }));
    let preview = service()
        .get_file_preview(
            &image.profile,
            &creds(),
            &node("Foto.PNG", "/Foto.PNG", 8),
            "snap",
            &stop(),
        )
        .await
        .unwrap();
    assert!(preview.is_image && !preview.is_text);
    assert_eq!(
        preview.image_bytes.unwrap(),
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]
    );

    let untouched = setup(json!({}));
    let folder = BackupNode {
        node_type: "dir".into(),
        ..node("d", "/d", 0)
    };
    let result = service()
        .get_file_preview(&untouched.profile, &creds(), &folder, "snap", &stop())
        .await
        .unwrap();
    assert!(result.error_message.contains("Ordner"));
    let large = node("big.bin", "/big.bin", 6 * 1024 * 1024);
    let result = service()
        .get_file_preview(&untouched.profile, &creds(), &large, "snap", &stop())
        .await
        .unwrap();
    assert!(
        result.error_message.contains("zu groß") && result.error_message.contains("6 MB"),
        "{}",
        result.error_message
    );
    assert!(
        untouched.starts().is_empty(),
        "Ordner und zu große Dateien starten Restic nicht"
    );

    let failing = setup(json!({ "dump": { "exit": 1, "stderr": "ctx: permission denied" } }));
    let result = service()
        .get_file_preview(
            &failing.profile,
            &creds(),
            &node("a.txt", "/a.txt", 1),
            "snap",
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(
        result.error_message,
        "Vorschau konnte nicht geladen werden: Der Zugriff wurde verweigert."
    );

    let oversized = setup(json!({ "dump": { "gen": "ls:200000" } }));
    let result = service()
        .get_file_preview(
            &oversized.profile,
            &creds(),
            &node("a.txt", "/a.txt", 1),
            "snap",
            &stop(),
        )
        .await
        .unwrap();
    assert!(
        result.error_message.contains("erlaubte Größe"),
        "{}",
        result.error_message
    );
}

// --- Speicheranalyse ---

#[tokio::test]
async fn storage_analysis_reports_progress() {
    let s = setup(json!({ "ls": { "gen": "ls-sized:1000" } }));
    let mut latest = None;
    let result = service()
        .analyze_snapshot_storage(&s.profile, &creds(), "snapshot", |p| latest = Some(p), &stop())
        .await
        .unwrap();
    let latest = latest.expect("Fortschritt");
    assert_eq!((latest.files_processed, latest.bytes_processed), (1000, 10_000));
    assert_eq!(result.total_file_count, 1000);
    assert_eq!(result.snapshot_id, "snapshot");
}

#[tokio::test]
async fn storage_analysis_bounds_folder_aggregation() {
    let s = setup(json!({ "ls": { "gen": "ls-folders:100001" } }));
    let result =
        within(service().analyze_snapshot_storage(&s.profile, &creds(), "snapshot", |_| {}, &stop()))
            .await
            .unwrap();
    assert_eq!(result.total_file_count, 100_001);
    assert!(result.folder_analysis_is_truncated);
    assert!(result.top_folders.len() <= 15);
}

#[tokio::test]
async fn large_snapshot_analysis_is_fast_enough() {
    let s = setup(json!({ "ls": { "gen": "ls-deep:100000" } }));
    let started = std::time::Instant::now();
    let result =
        within(service().analyze_snapshot_storage(&s.profile, &creds(), "snapshot", |_| {}, &stop()))
            .await
            .unwrap();
    println!("METRIK Analyse-100k={} ms", started.elapsed().as_millis());
    assert_eq!(result.total_file_count, 100_000);
    assert_eq!(result.categories[0].name, "Sonstiges");
}

// --- Abbruch und Validierung ---

#[tokio::test]
async fn user_cancel_ends_a_running_listing_and_stops_the_process() {
    let s = setup(json!({ "ls": { "gen": "ls:500", "hang_after": true } }));
    let cancel = stop();
    let canceller = cancel.clone();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(500)).await;
        canceller.cancel();
    });
    let result = within(service().get_directory_batched(
        &s.profile,
        &creds(),
        "snap",
        "/",
        |_| std::future::ready(Ok(())),
        &cancel,
    ))
    .await;
    assert!(matches!(result, Err(ResticError::Cancelled)), "{result:?}");
    s.assert_process_gone().await;
}

#[tokio::test]
async fn validate_checks_the_version_of_the_started_file() {
    let root = TempDir::new("validate");
    let exe = root.0.join(EXE);
    fs::copy(env!("CARGO_BIN_EXE_fake-restic"), &exe).unwrap();
    let profile = RepositoryProfile {
        restic_executable: Some(exe.to_string_lossy().into_owned()),
        ..RepositoryProfile::new("repo")
    };
    assert_eq!(
        service().validate(&profile, &stop()).await.unwrap().version,
        "0.19.1"
    );
    fs::write(format!("{}.mode", exe.display()), "old").unwrap();
    assert!(error_text(service().validate(&profile, &stop()).await).contains("zu alt"));
}

// --- End-to-End mit echtem Restic ---

async fn find_real_restic() -> Option<String> {
    if let Ok(path) = std::env::var("RESTIC_BROWSER_TEST_RESTIC") {
        return Some(path);
    }
    let temp = TempDir::new("locate");
    let resolver = restic_core::ResticResolver::for_application(&temp.0, &temp.0);
    resolver
        .resolve(None, &stop())
        .await
        .ok()
        .map(|info| info.path.to_string_lossy().into_owned())
}

#[cfg(unix)]
fn set_read_only(root: &Path, restore: &mut Vec<(PathBuf, u32)>) {
    use std::os::unix::fs::PermissionsExt;
    let mut stack = vec![root.to_path_buf()];
    while let Some(path) = stack.pop() {
        let mode = fs::metadata(&path).unwrap().permissions().mode();
        restore.push((path.clone(), mode));
        if path.is_dir() {
            stack.extend(fs::read_dir(&path).unwrap().map(|e| e.unwrap().path()));
        }
        fs::set_permissions(&path, fs::Permissions::from_mode(mode & !0o222)).unwrap();
    }
}

#[tokio::test]
async fn end_to_end_with_real_restic_search_preview_and_restore() {
    let Some(executable) = find_real_restic().await else {
        assert!(
            std::env::var("RESTIC_BROWSER_REQUIRE_RESTIC").is_err(),
            "Restic ist erforderlich, wurde aber nicht gefunden"
        );
        eprintln!("SKIP: kein Restic gefunden (RESTIC_BROWSER_TEST_RESTIC setzen)");
        return;
    };
    let root = TempDir::new("e2e");
    let source = root.0.join("Quelle mit Umlaut");
    let repository = root.0.join("repository");
    let target = root.0.join("Wiederhergestellt");
    fs::create_dir_all(&source).unwrap();
    fs::write(source.join("prüfung.txt"), "restic-browser-e2e").unwrap();
    if cfg!(unix) {
        for (name, content) in [
            ("a*.txt", "ausgewählt"),
            ("abc.txt", "nicht ausgewählt"),
            ("b?.txt", "ausgewählt"),
            ("bx.txt", "nicht ausgewählt"),
            ("c[1].txt", "ausgewählt"),
            ("c1.txt", "nicht ausgewählt"),
        ] {
            fs::write(source.join(name), content).unwrap();
        }
    }
    let environment = HashMap::from([
        ("RESTIC_PASSWORD".to_string(), "test-password-only".to_string()),
        (
            "RESTIC_CACHE_DIR".to_string(),
            root.0.join("cache").to_string_lossy().into_owned(),
        ),
    ]);
    let runner = restic_core::ResticProcessRunner::new();
    let repo = repository.to_string_lossy().into_owned();
    // Das temporäre Test-Repository wird hier – nur im Test – direkt mit Restic angelegt.
    for arguments in [
        vec!["--repo", &repo, "init", "--json"],
        vec!["--repo", &repo, "backup", "--json", "."],
    ] {
        let command =
            restic_core::ResticCommand::new(&executable, arguments.iter().map(|s| s.to_string()).collect())
                .with_environment(environment.clone())
                .with_working_directory(&source);
        let result = runner.run(&command, &stop()).await.unwrap();
        assert_eq!(
            result.exit_code, 0,
            "Restic {:?}: {}",
            arguments[2], result.standard_error
        );
    }

    #[cfg(unix)]
    let mut modes = Vec::new();
    #[cfg(unix)]
    set_read_only(&repository, &mut modes);

    let profile = RepositoryProfile {
        name: "Integrationstest".into(),
        repository: repo.clone(),
        restic_executable: Some(executable.clone()),
        ..RepositoryProfile::default()
    };
    let credentials = SessionCredentials::new(
        "test-password-only",
        HashMap::from([(
            "RESTIC_CACHE_DIR".to_string(),
            root.0.join("cache").to_string_lossy().into_owned(),
        )]),
    );
    let svc = service();
    let outcome = async {
        let snapshots = svc.get_snapshots(&profile, &credentials, &stop()).await.unwrap();
        assert_eq!(snapshots.len(), 1);
        let matches = svc
            .find(&profile, &credentials, &snapshots[0].id, "prüfung.txt", &stop())
            .await
            .unwrap();
        assert_eq!(matches.matches.len(), 1);
        let preview = svc
            .get_file_preview(
                &profile,
                &credentials,
                &matches.matches[0],
                &snapshots[0].id,
                &stop(),
            )
            .await
            .unwrap();
        assert_eq!(
            preview.text_content.as_deref().map(str::trim),
            Some("restic-browser-e2e"),
            "{}",
            preview.error_message
        );

        let request = RestoreRequest {
            snapshot_id: snapshots[0].id.clone(),
            target: target.to_string_lossy().into_owned(),
            includes: vec![matches.matches[0].path.clone()],
            overwrite: OverwritePolicy::Never,
        };
        let restore = svc
            .restore(&profile, &credentials, &request, |_| {}, &stop())
            .await
            .unwrap();
        assert!(restore.success);
        assert_eq!(
            find_file(&target, "prüfung.txt")
                .map(|p| fs::read_to_string(p).unwrap())
                .as_deref(),
            Some("restic-browser-e2e")
        );

        if cfg!(unix) {
            let parent = restic_core::command::parent_path(&matches.matches[0].path);
            for name in ["a*.txt", "b?.txt", "c[1].txt"] {
                let literal_target = root
                    .0
                    .join(format!("literal-{}", COUNTER.fetch_add(1, Ordering::Relaxed)));
                let request = RestoreRequest {
                    snapshot_id: snapshots[0].id.clone(),
                    target: literal_target.to_string_lossy().into_owned(),
                    includes: vec![format!("{parent}/{name}")],
                    overwrite: OverwritePolicy::Never,
                };
                svc.restore(&profile, &credentials, &request, |_| {}, &stop())
                    .await
                    .unwrap();
                let restored = find_file(&literal_target, name).unwrap_or_else(|| panic!("{name} fehlt"));
                assert_eq!(fs::read_to_string(restored).unwrap(), "ausgewählt");
                assert_eq!(
                    count_files(&literal_target),
                    1,
                    "nur die gewählte Datei, keine Glob-Treffer"
                );
            }
        }
    }
    .await;

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        for (path, mode) in modes {
            let _ = fs::set_permissions(path, fs::Permissions::from_mode(mode));
        }
    }
    outcome
}

fn find_file(root: &Path, name: &str) -> Option<PathBuf> {
    for entry in fs::read_dir(root).ok()?.flatten() {
        let path = entry.path();
        if path.is_dir() {
            if let Some(found) = find_file(&path, name) {
                return Some(found);
            }
        } else if path.file_name().is_some_and(|n| n == name) {
            return Some(path);
        }
    }
    None
}

fn count_files(root: &Path) -> usize {
    fs::read_dir(root)
        .map(|entries| {
            entries
                .flatten()
                .map(|e| {
                    if e.path().is_dir() {
                        count_files(&e.path())
                    } else {
                        1
                    }
                })
                .sum()
        })
        .unwrap_or(0)
}
