//! Auflösung und Bereitstellung der Restic-Datei.
//!
//! Feste Reihenfolge: ausdrücklich ausgewählte Datei (nie ein Ersatz), danach die mitgelieferte
//! geprüfte Version, portable Dateien neben der Anwendung oder in `tools`, zuletzt `PATH`
//! (unter Windows zusätzlich WinGet). Es gibt keine Downloads zur Laufzeit.
//!
//! Die mitgelieferte Windows-Datei wird bei Bedarf atomar, gesperrt und SHA-256-geprüft im
//! Benutzerdatenordner bereitgestellt. Beschädigte oder parallele Provisionierungen dürfen keine
//! ausführbare Zieldatei hinterlassen.

use std::collections::HashSet;
use std::fs::{self, File, OpenOptions, TryLockError};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;

use sha2::{Digest, Sha256};
use tokio_util::sync::CancellationToken;

use crate::error::ResticError;
use crate::locator;
use crate::manifest::ResticManifest;
use crate::version_probe::{PROBE_TIMEOUT, ProcessVersionProbe, VersionProbe};

/// Die tatsächlich validierte Restic-Datei samt Herkunft und gemeldeter Version.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ResticExecutableInfo {
    pub path: PathBuf,
    pub version: String,
    pub source: String,
}

/// Eingebettete Restic-Datei (Windows) mit erwarteter SHA-256-Prüfsumme.
#[derive(Debug, Clone)]
pub struct EmbeddedRestic {
    pub bytes: Arc<[u8]>,
    pub sha256: String,
}

struct Candidate {
    path: String,
    source: String,
    hash: Option<String>,
}

pub struct ResticResolver<P: VersionProbe> {
    data_directory: PathBuf,
    base_directory: PathBuf,
    search_path: String,
    program_files: String,
    include_system_locations: bool,
    probe: P,
    probe_timeout: Duration,
    linux_sha256: String,
    embedded_windows: Option<EmbeddedRestic>,
    version: String,
}

impl ResticResolver<ProcessVersionProbe> {
    /// Resolver für die laufende Anwendung: `PATH` und `ProgramFiles` aus der Umgebung.
    pub fn for_application(data_directory: impl Into<PathBuf>, base_directory: impl Into<PathBuf>) -> Self {
        let search_path = std::env::var("PATH").unwrap_or_default();
        Self::new(
            data_directory,
            base_directory,
            search_path,
            ProcessVersionProbe::new(),
        )
    }
}

impl<P: VersionProbe> ResticResolver<P> {
    pub fn new(
        data_directory: impl Into<PathBuf>,
        base_directory: impl Into<PathBuf>,
        search_path: impl Into<String>,
        probe: P,
    ) -> Self {
        let manifest = ResticManifest::embedded();
        Self {
            data_directory: data_directory.into(),
            base_directory: base_directory.into(),
            search_path: search_path.into(),
            program_files: std::env::var("ProgramFiles").unwrap_or_default(),
            include_system_locations: true,
            probe,
            probe_timeout: PROBE_TIMEOUT,
            linux_sha256: manifest.linux.binary_sha256,
            embedded_windows: None,
            version: manifest.version,
        }
    }

    pub fn with_probe_timeout(mut self, timeout: Duration) -> Self {
        self.probe_timeout = timeout;
        self
    }

    pub fn with_linux_sha256(mut self, sha256: impl Into<String>) -> Self {
        self.linux_sha256 = sha256.into();
        self
    }

    /// Schließt Linux-Systempfade und WinGet aus (nur `PATH`, neben der Anwendung und `tools`).
    pub fn without_system_locations(mut self) -> Self {
        self.include_system_locations = false;
        self
    }

    pub fn with_program_files(mut self, program_files: impl Into<String>) -> Self {
        self.program_files = program_files.into();
        self
    }

    /// Hinterlegt die eingebettete Windows-Datei (nur unter Windows verwendet).
    pub fn with_embedded_windows(mut self, embedded: EmbeddedRestic) -> Self {
        self.embedded_windows = Some(embedded);
        self
    }

    pub async fn resolve(
        &self,
        configured_path: Option<&str>,
        stop: &CancellationToken,
    ) -> Result<ResticExecutableInfo, ResticError> {
        if stop.is_cancelled() {
            return Err(ResticError::Cancelled);
        }
        if let Some(configured) = configured_path.filter(|p| !p.trim().is_empty()) {
            return match self
                .validate(Path::new(configured), "Ausgewähltes Programm", None, stop)
                .await
            {
                Err(ResticError::Failed { message, exit_code }) => Err(ResticError::Failed {
                    message: format!(
                        "Das ausdrücklich ausgewählte Restic-Programm ist nicht verwendbar: {message}"
                    ),
                    exit_code,
                }),
                other => other,
            };
        }

        let is_windows = cfg!(windows);
        let base = self.base_directory.to_string_lossy().into_owned();
        let mut candidates = Vec::<Candidate>::new();
        if is_windows {
            if let Some(embedded) = &self.embedded_windows {
                let path = self.provision(embedded, stop).await?;
                candidates.push(Candidate {
                    path: path.to_string_lossy().into_owned(),
                    source: "In Restic Browser enthalten".into(),
                    hash: Some(embedded.sha256.clone()),
                });
            }
        } else {
            candidates.push(Candidate {
                path: self
                    .base_directory
                    .join("tools")
                    .join("restic")
                    .to_string_lossy()
                    .into_owned(),
                source: "Mit der Anwendung ausgeliefert".into(),
                hash: Some(self.linux_sha256.clone()),
            });
        }
        for path in locator::candidates_with(
            is_windows,
            &base,
            &self.search_path,
            &self.program_files,
            self.include_system_locations,
        ) {
            let mut source = locator::describe(&path, &base, is_windows).to_string();
            if !is_windows
                && source == "PATH"
                && (path == "/usr/local/bin/restic" || path == "/usr/bin/restic")
            {
                source = "Systempfad".into();
            }
            candidates.push(Candidate {
                path,
                source,
                hash: None,
            });
        }

        let mut seen = HashSet::<PathBuf>::new();
        let mut failures = Vec::<String>::new();
        for candidate in candidates {
            if stop.is_cancelled() {
                return Err(ResticError::Cancelled);
            }
            let path =
                std::path::absolute(&candidate.path).unwrap_or_else(|_| PathBuf::from(&candidate.path));
            // Eine abgelehnte mitgelieferte Datei darf nie als ungeprüfte portable Datei erneut verwendet werden.
            if !seen.insert(normalize_identity(&locator::identity(&path), is_windows)) || !path.is_file() {
                continue;
            }
            match self
                .validate(&path, &candidate.source, candidate.hash.as_deref(), stop)
                .await
            {
                Ok(info) => return Ok(info),
                Err(ResticError::Cancelled) => return Err(ResticError::Cancelled),
                Err(error) => failures.push(format!("{}: {error}", candidate.source)),
            }
        }
        let mut unique = Vec::<String>::new();
        for failure in failures {
            if !unique.contains(&failure) {
                unique.push(failure);
            }
        }
        let detail = if unique.is_empty() {
            "Keine Restic-Datei gefunden.".to_string()
        } else {
            unique.join("\n")
        };
        Err(ResticError::new(format!(
            "Restic konnte nicht automatisch aufgelöst werden. {detail}\nVerwende die vollständige portable Ausgabe oder wähle eine ausführbare Restic-Datei aus."
        )))
    }

    async fn validate(
        &self,
        path: &Path,
        source: &str,
        hash: Option<&str>,
        stop: &CancellationToken,
    ) -> Result<ResticExecutableInfo, ResticError> {
        if stop.is_cancelled() {
            return Err(ResticError::Cancelled);
        }
        if let Some(error) = locator::file_error(path) {
            return Err(ResticError::new(error));
        }
        if let Some(expected) = hash {
            let (path, expected) = (path.to_path_buf(), expected.to_string());
            let verified = tokio::task::spawn_blocking(move || is_verified(&path, &expected))
                .await
                .unwrap_or(false);
            if !verified {
                return Err(ResticError::new(
                    "Die mitgelieferte Restic-Datei hat nicht die erwartete SHA-256-Prüfsumme.",
                ));
            }
        }
        let full = std::path::absolute(path).unwrap_or_else(|_| path.to_path_buf());
        let version = self
            .probe
            .probe(&full.to_string_lossy(), stop, self.probe_timeout)
            .await?;
        Ok(ResticExecutableInfo {
            path: full,
            version: version.version,
            source: source.to_string(),
        })
    }

    /// Stellt die eingebettete Datei bereit (siehe [`provision_embedded`]).
    pub async fn provision(
        &self,
        embedded: &EmbeddedRestic,
        stop: &CancellationToken,
    ) -> Result<PathBuf, ResticError> {
        let directory = self
            .data_directory
            .join("ResticBrowser")
            .join("tools")
            .join("restic")
            .join(&self.version)
            .join("win-x64");
        provision_embedded(&directory, "restic.exe", embedded, stop).await
    }
}

fn normalize_identity(path: &Path, is_windows: bool) -> PathBuf {
    if is_windows {
        PathBuf::from(path.to_string_lossy().to_ascii_lowercase())
    } else {
        path.to_path_buf()
    }
}

/// Wahr, wenn die Datei existiert, nicht leer ist und die erwartete SHA-256-Prüfsumme hat.
pub fn is_verified(path: &Path, expected_sha256: &str) -> bool {
    let Ok(metadata) = fs::metadata(path) else {
        return false;
    };
    if !metadata.is_file() || metadata.len() == 0 {
        return false;
    }
    let Ok(mut file) = File::open(path) else {
        return false;
    };
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 64 * 1024];
    loop {
        match file.read(&mut buffer) {
            Ok(0) => break,
            Ok(count) => hasher.update(&buffer[..count]),
            Err(_) => return false,
        }
    }
    hex_upper(&hasher.finalize()).eq_ignore_ascii_case(expected_sha256.trim())
}

fn hex_upper(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02X}")).collect()
}

/// SHA-256 als Hexadezimalzeichenfolge (Großbuchstaben).
pub fn sha256_hex(bytes: &[u8]) -> String {
    hex_upper(&Sha256::digest(bytes))
}

static TEMPORARY_COUNTER: AtomicU64 = AtomicU64::new(0);

/// Schreibt die eingebettete Datei atomar nach `directory/file_name`.
///
/// - Eine bereits geprüfte Zieldatei wird unverändert verwendet.
/// - Andernfalls wird unter einer Sperrdatei (prozessübergreifend) in eine temporäre Datei
///   geschrieben, dort geprüft und erst dann über die Zieldatei verschoben.
/// - Schlägt die Prüfung fehl, bleibt keine ausführbare Zieldatei zurück.
pub async fn provision_embedded(
    directory: &Path,
    file_name: &str,
    embedded: &EmbeddedRestic,
    stop: &CancellationToken,
) -> Result<PathBuf, ResticError> {
    let destination = directory.join(file_name);
    if is_verified(&destination, &embedded.sha256) {
        return Ok(destination);
    }
    fs::create_dir_all(directory).map_err(|e| {
        ResticError::new(format!(
            "Das Verzeichnis für Restic konnte nicht angelegt werden: {e}"
        ))
    })?;
    let lock = acquire_lock(&directory.join(".provision.lock"), stop).await?;
    // Ein anderer Prozess kann die Datei inzwischen bereitgestellt haben.
    if is_verified(&destination, &embedded.sha256) {
        drop(lock);
        return Ok(destination);
    }

    let temporary = directory.join(format!(
        "restic-{}-{}-{}.tmp",
        std::process::id(),
        TEMPORARY_COUNTER.fetch_add(1, Ordering::Relaxed),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_or(0, |d| d.as_nanos())
    ));
    let (bytes, expected) = (Arc::clone(&embedded.bytes), embedded.sha256.clone());
    let (temp_for_task, destination_for_task) = (temporary.clone(), destination.clone());
    let result = tokio::task::spawn_blocking(move || {
        write_and_replace(&temp_for_task, &destination_for_task, &bytes, &expected)
    })
    .await
    .map_err(|e| ResticError::new(format!("Die Bereitstellung von Restic wurde unterbrochen: {e}")))?;
    let _ = fs::remove_file(&temporary);
    drop(lock);
    result.map(|()| destination)
}

fn write_and_replace(
    temporary: &Path,
    destination: &Path,
    bytes: &[u8],
    expected: &str,
) -> Result<(), ResticError> {
    let io_error = |e: std::io::Error| {
        ResticError::new(format!(
            "Die enthaltene Restic-Datei konnte nicht bereitgestellt werden: {e}"
        ))
    };
    {
        let mut file = File::create(temporary).map_err(io_error)?;
        file.write_all(bytes).map_err(io_error)?;
        file.sync_all().map_err(io_error)?;
    }
    if !is_verified(temporary, expected) {
        return Err(ResticError::new(
            "Die enthaltene Restic-Datei konnte nicht geprüft werden.",
        ));
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(temporary, fs::Permissions::from_mode(0o755)).map_err(io_error)?;
    }
    fs::rename(temporary, destination).map_err(io_error)
}

async fn acquire_lock(path: &Path, stop: &CancellationToken) -> Result<File, ResticError> {
    loop {
        if stop.is_cancelled() {
            return Err(ResticError::Cancelled);
        }
        let file = OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(path)
            .map_err(|e| {
                ResticError::new(format!(
                    "Die Sperrdatei für Restic konnte nicht geöffnet werden: {e}"
                ))
            })?;
        match file.try_lock() {
            Ok(()) => return Ok(file),
            Err(TryLockError::WouldBlock) => {
                tokio::select! {
                    biased;
                    () = stop.cancelled() => return Err(ResticError::Cancelled),
                    () = tokio::time::sleep(Duration::from_millis(100)) => {}
                }
            }
            Err(TryLockError::Error(e)) => {
                return Err(ResticError::new(format!(
                    "Die Sperrdatei für Restic konnte nicht gesperrt werden: {e}"
                )));
            }
        }
    }
}
