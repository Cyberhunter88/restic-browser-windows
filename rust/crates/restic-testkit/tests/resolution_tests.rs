//! Tests für Auflösung, Versionsprüfung und Provisionierung (Phase 2). Gegenstücke zu
//! `ResticResolutionTests.cs`. Es werden echte Prozesse gestartet: `fake-restic` verhält sich je
//! nach Sidecar-Datei `<pfad>.mode` wie ein gültiges oder fehlerhaftes Restic.

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use restic_core::provisioning::{EmbeddedRestic, is_verified, provision_embedded, sha256_hex};
use restic_core::{ProcessVersionProbe, ResticError, ResticResolver};
use tokio_util::sync::CancellationToken;

static COUNTER: AtomicUsize = AtomicUsize::new(0);
const EXE: &str = if cfg!(windows) { "restic.exe" } else { "restic" };

struct TempDir(PathBuf);

impl TempDir {
    fn new(name: &str) -> Self {
        let id = COUNTER.fetch_add(1, Ordering::Relaxed);
        let path = std::env::temp_dir().join(format!("rb-{name}-{}-{id}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }

    fn path(&self) -> &Path {
        &self.0
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn fake_restic() -> &'static str {
    env!("CARGO_BIN_EXE_fake-restic")
}

/// Legt `fake-restic` als `<dir>/<name>` ab und setzt den Modus.
fn install(directory: &Path, name: &str, mode: &str) -> PathBuf {
    fs::create_dir_all(directory).unwrap();
    let path = directory.join(name);
    fs::copy(fake_restic(), &path).unwrap();
    fs::write(format!("{}.mode", path.display()), mode).unwrap();
    path
}

fn starts(path: &Path) -> usize {
    fs::read_to_string(format!("{}.log", path.display())).map_or(0, |log| log.lines().count())
}

fn resolver(root: &Path, search: &str) -> ResticResolver<ProcessVersionProbe> {
    ResticResolver::new(root, root, search, ProcessVersionProbe::new()).without_system_locations()
}

fn stop() -> CancellationToken {
    CancellationToken::new()
}

async fn error_of(resolver: &ResticResolver<ProcessVersionProbe>, configured: Option<&str>) -> String {
    match resolver.resolve(configured, &stop()).await {
        Err(error) => error.to_string(),
        Ok(info) => panic!("Eine unbrauchbare Restic-Datei wurde akzeptiert: {info:?}"),
    }
}

fn search_path(directory: &Path) -> String {
    let separator = if cfg!(windows) { ";" } else { ":" };
    format!("{0}{separator}{0}", directory.display())
}

#[tokio::test]
async fn automatic_resolution_skips_broken_candidates_and_never_falls_back_from_explicit_choice() {
    let root = TempDir::new("resolve");
    let bundled = install(&root.path().join("tools"), EXE, "badjson");
    let portable = install(root.path(), EXE, "badjson");
    let system = install(&root.path().join("path with spaces"), EXE, "ok");
    let resolver = resolver(root.path(), &search_path(system.parent().unwrap()));

    let info = resolver.resolve(None, &stop()).await.unwrap();
    assert_eq!(info.path, std::path::absolute(&system).unwrap());
    assert_eq!(info.source, "PATH");
    assert_eq!(info.version, "0.19.1");
    assert_eq!(starts(&portable), 1);
    assert_eq!(starts(&system), 1);
    if !cfg!(windows) {
        assert_eq!(
            starts(&bundled),
            0,
            "die mitgelieferte Datei mit falschem Hash wird nicht gestartet"
        );
    }

    // Eine ausdrücklich gewählte, defekte Datei fällt nie auf eine andere zurück.
    let error = error_of(&resolver, Some(&portable.to_string_lossy())).await;
    assert!(error.contains("ausdrücklich ausgewählte"), "{error}");

    fs::remove_file(&portable).unwrap();
    fs::remove_file(&bundled).unwrap();
    let info = resolver.resolve(None, &stop()).await.unwrap();
    assert_eq!(info.path, std::path::absolute(&system).unwrap());
}

#[tokio::test]
async fn invalid_version_outputs_are_rejected_for_explicit_and_automatic_resolution() {
    let root = TempDir::new("probe");
    for mode in ["null", "empty-object", "brace", "old", "null-version", "badjson"] {
        let file = install(root.path(), EXE, mode);
        let resolver = resolver(root.path(), "");
        let explicit = error_of(&resolver, Some(&file.to_string_lossy())).await;
        assert!(explicit.contains("Restic"), "{mode}: {explicit}");
        let automatic = error_of(&resolver, None).await;
        assert!(automatic.contains("automatisch"), "{mode}: {automatic}");
    }
}

#[tokio::test]
async fn failing_probe_does_not_leak_raw_output() {
    let root = TempDir::new("fail");
    let file = install(root.path(), EXE, "fail");
    let error = error_of(&resolver(root.path(), ""), Some(&file.to_string_lossy())).await;
    assert!(error.contains("Versionsprüfung"), "{error}");
    assert!(!error.contains("private raw output"), "{error}");
}

#[tokio::test]
async fn probe_timeout_kills_the_process_and_reports_the_limit() {
    let root = TempDir::new("timeout");
    let file = install(root.path(), EXE, "hang");
    let resolver = resolver(root.path(), "").with_probe_timeout(Duration::from_millis(300));
    let error = error_of(&resolver, Some(&file.to_string_lossy())).await;
    assert!(error.contains("Zeitlimit"), "{error}");
    assert_process_stopped(&file).await;
}

#[tokio::test]
async fn user_cancel_during_probe_stays_a_cancel() {
    let root = TempDir::new("cancel");
    let file = install(root.path(), EXE, "hang");
    let resolver = resolver(root.path(), "");
    let cancel = stop();
    let canceller = cancel.clone();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(300)).await;
        canceller.cancel();
    });
    let result = resolver.resolve(Some(&file.to_string_lossy()), &cancel).await;
    assert!(matches!(result, Err(ResticError::Cancelled)), "{result:?}");
    assert_process_stopped(&file).await;
}

async fn assert_process_stopped(file: &Path) {
    let alive = format!("{}.alive", file.display());
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

#[tokio::test]
async fn nothing_found_gives_helpful_message() {
    let root = TempDir::new("none");
    let error = error_of(&resolver(root.path(), ""), None).await;
    assert!(
        error.contains("Keine Restic-Datei gefunden") || error.contains("automatisch"),
        "{error}"
    );
    assert!(
        error.contains("wähle eine ausführbare Restic-Datei aus"),
        "{error}"
    );
}

#[tokio::test]
async fn already_cancelled_token_is_reported_before_any_work() {
    let root = TempDir::new("pre-cancel");
    let cancel = stop();
    cancel.cancel();
    let result = resolver(root.path(), "").resolve(None, &cancel).await;
    assert!(matches!(result, Err(ResticError::Cancelled)));
}

#[cfg(unix)]
#[tokio::test]
async fn linux_checks_permissions_symlinks_and_bundle_hash() {
    use std::os::unix::fs::{PermissionsExt, symlink};

    let root = TempDir::new("linux");
    let file = install(root.path(), "restic", "ok");
    let resolver = resolver(root.path(), "");

    fs::set_permissions(&file, fs::Permissions::from_mode(0o600)).unwrap();
    assert!(
        error_of(&resolver, Some(&file.to_string_lossy()))
            .await
            .contains("Ausführungsrechte")
    );
    fs::set_permissions(&file, fs::Permissions::from_mode(0o755)).unwrap();

    let link = root.path().join("relative link");
    symlink("restic", &link).unwrap();
    let info = resolver
        .resolve(Some(&link.to_string_lossy()), &stop())
        .await
        .unwrap();
    assert_eq!(info.path, std::path::absolute(&link).unwrap());

    // Mitgelieferte Datei: nur mit passender Prüfsumme; sonst nie als ungeprüfte portable Datei.
    let tools = install(&root.path().join("tools"), "restic", "ok");
    let hash = sha256_hex(&fs::read(&tools).unwrap());
    let bundle = ResticResolver::new(root.path(), root.path(), "", ProcessVersionProbe::new())
        .without_system_locations()
        .with_linux_sha256(hash);
    let info = bundle.resolve(None, &stop()).await.unwrap();
    assert_eq!(info.source, "Mit der Anwendung ausgeliefert");
    assert_eq!(info.path, std::path::absolute(&tools).unwrap());
    assert_eq!(starts(&tools), 1);

    let mismatch = ResticResolver::new(root.path(), root.path(), "", ProcessVersionProbe::new())
        .without_system_locations();
    fs::remove_file(&link).unwrap();
    fs::remove_file(&file).unwrap();
    let before = starts(&tools);
    let error = mismatch.resolve(None, &stop()).await.unwrap_err().to_string();
    assert!(error.contains("SHA-256"), "{error}");
    assert_eq!(
        starts(&tools),
        before,
        "eine Datei mit falscher Prüfsumme wird nie gestartet"
    );

    fs::write(&file, b"").unwrap();
    assert!(
        error_of(&resolver, Some(&file.to_string_lossy()))
            .await
            .contains("leer")
    );
    fs::remove_file(&file).unwrap();
    assert!(
        error_of(&resolver, Some(&file.to_string_lossy()))
            .await
            .contains("fehlt")
    );
}

// --- Provisionierung (plattformunabhängig getestet) ---

fn embedded(bytes: &[u8]) -> EmbeddedRestic {
    EmbeddedRestic {
        bytes: bytes.to_vec().into(),
        sha256: sha256_hex(bytes),
    }
}

fn directory_listing(directory: &Path) -> Vec<String> {
    let mut names: Vec<String> = fs::read_dir(directory)
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .collect();
    names.sort();
    names
}

#[tokio::test]
async fn provisioning_writes_verified_file_and_leaves_no_temporary_files() {
    let root = TempDir::new("prov");
    let directory = root.path().join("a").join("b");
    let payload = embedded(b"echte Restic-Daten");
    let path = provision_embedded(&directory, "restic.exe", &payload, &stop())
        .await
        .unwrap();
    assert!(is_verified(&path, &payload.sha256));
    assert_eq!(directory_listing(&directory), [".provision.lock", "restic.exe"]);

    // Zweiter Aufruf nutzt die geprüfte Datei unverändert.
    let modified = fs::metadata(&path).unwrap().modified().unwrap();
    provision_embedded(&directory, "restic.exe", &payload, &stop())
        .await
        .unwrap();
    assert_eq!(fs::metadata(&path).unwrap().modified().unwrap(), modified);
}

#[tokio::test]
async fn provisioning_replaces_a_corrupt_destination() {
    let root = TempDir::new("prov-corrupt");
    fs::write(root.path().join("restic.exe"), b"manipuliert").unwrap();
    let payload = embedded(b"echte Restic-Daten");
    let path = provision_embedded(root.path(), "restic.exe", &payload, &stop())
        .await
        .unwrap();
    assert!(is_verified(&path, &payload.sha256));
    assert_eq!(fs::read(&path).unwrap(), b"echte Restic-Daten");
}

#[tokio::test]
async fn provisioning_with_wrong_checksum_leaves_no_executable_and_no_temporary_file() {
    let root = TempDir::new("prov-bad");
    let bad = EmbeddedRestic {
        bytes: b"daten".to_vec().into(),
        sha256: sha256_hex(b"etwas anderes"),
    };
    let error = provision_embedded(root.path(), "restic.exe", &bad, &stop())
        .await
        .unwrap_err();
    assert!(error.to_string().contains("nicht geprüft"), "{error}");
    assert_eq!(directory_listing(root.path()), [".provision.lock"]);
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn parallel_provisioning_yields_one_verified_file() {
    let root = TempDir::new("prov-parallel");
    let payload = embedded(&vec![7u8; 2 * 1024 * 1024]);
    let directory = root.path().to_path_buf();
    let mut tasks = Vec::new();
    for _ in 0..8 {
        let (directory, payload) = (directory.clone(), payload.clone());
        tasks.push(tokio::spawn(async move {
            provision_embedded(&directory, "restic.exe", &payload, &CancellationToken::new()).await
        }));
    }
    for task in tasks {
        let path = task.await.unwrap().unwrap();
        assert!(is_verified(&path, &payload.sha256));
    }
    assert_eq!(directory_listing(&directory), [".provision.lock", "restic.exe"]);
}

#[tokio::test]
async fn waiting_for_a_held_lock_can_be_cancelled() {
    let root = TempDir::new("prov-lock");
    let lock = fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(root.path().join(".provision.lock"))
        .unwrap();
    lock.lock().unwrap();
    let cancel = stop();
    let canceller = cancel.clone();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(300)).await;
        canceller.cancel();
    });
    let result = provision_embedded(root.path(), "restic.exe", &embedded(b"x"), &cancel).await;
    assert!(matches!(result, Err(ResticError::Cancelled)), "{result:?}");
    assert!(!root.path().join("restic.exe").exists());
}

#[cfg(windows)]
#[tokio::test]
async fn windows_uses_the_embedded_file_first_after_hash_verification() {
    let root = TempDir::new("embedded");
    let bytes = fs::read(fake_restic()).unwrap();
    let resolver = resolver(root.path(), "").with_embedded_windows(embedded(&bytes));
    let info = resolver.resolve(None, &stop()).await.unwrap();
    assert_eq!(info.source, "In Restic Browser enthalten");
    assert!(
        info.path
            .starts_with(root.path().join("ResticBrowser").join("tools").join("restic"))
    );
    assert!(
        info.path
            .ends_with(Path::new("0.19.1").join("win-x64").join("restic.exe"))
    );
    assert_eq!(info.version, "0.19.1");

    // Eine nachträglich manipulierte Datei wird ersetzt statt gestartet.
    fs::write(&info.path, b"manipuliert").unwrap();
    let again = resolver.resolve(None, &stop()).await.unwrap();
    assert!(is_verified(&again.path, &sha256_hex(&bytes)));
}
