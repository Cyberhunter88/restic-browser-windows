//! Einstellungen und Profile in `settings.json` (Gegenstück zu `SettingsService.cs`).
//!
//! - Gleiches JSON-Format wie die C#-Linie (Eigenschaften in `PascalCase`, Typ als Zahl), damit
//!   Nutzer ohne Migration wechseln können. Beim Lesen werden auch camelCase-Namen akzeptiert.
//! - Schreiben ist atomar (temporäre Datei, `fsync`, Ersetzen) und läuft unter einer
//!   prozessübergreifenden Sperrdatei mit 5 s Zeitlimit.
//! - Eine beschädigte Datei wird nie überschrieben.
//! - Geheimnisse werden nie gespeichert: das Profil enthält keine Passwörter.

use std::fmt;
use std::fs::{self, File, OpenOptions, TryLockError};
use std::io::{ErrorKind, Write};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{Duration, Instant};

use restic_core::profile::{RepositoryProfile, RepositoryType, new_profile_id};
use serde::{Deserialize, Serialize};
use serde_json::Value;

const LOCK_TIMEOUT: Duration = Duration::from_secs(5);
const LOCK_POLL: Duration = Duration::from_millis(50);

/// Fenstergeometrie des letzten Sitzungsendes.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WindowSettings {
    #[serde(rename = "Width", alias = "width", default)]
    pub width: f64,
    #[serde(rename = "Height", alias = "height", default)]
    pub height: f64,
    #[serde(rename = "X", alias = "x", default)]
    pub x: Option<i32>,
    #[serde(rename = "Y", alias = "y", default)]
    pub y: Option<i32>,
    #[serde(rename = "IsMaximized", alias = "isMaximized", default)]
    pub is_maximized: bool,
}

#[derive(Debug, Clone, Default)]
pub struct AppSettings {
    pub profiles: Vec<RepositoryProfile>,
    /// Zuletzt verbundenes Profil; wird im Verbindungsdialog vorausgewählt.
    pub last_profile_id: Option<String>,
    /// Zuletzt gewählter Wiederherstellungsordner (nur Pfad).
    pub last_restore_target: Option<String>,
    pub window: Option<WindowSettings>,
    /// Zuletzt gewähltes Design (`None`: noch nie gewählt, die Anwendung startet hell).
    pub dark_theme: Option<bool>,
    /// Zeigt die Dateitabelle die Spalte „Berechtigungen“?
    pub show_permissions: bool,
    /// Anzahl der beim Laden entfernten, nicht mehr unterstützten Profile (wird nicht gespeichert).
    pub removed_profile_count: usize,
}

/// Fehler mit verständlicher deutscher Meldung.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SettingsError(pub String);

impl fmt::Display for SettingsError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for SettingsError {}

impl SettingsError {
    fn corrupt() -> Self {
        Self("Die Profildatei ist beschädigt oder ungültig. Sie bleibt unverändert. Bitte die Datei settings.json im Benutzerdatenordner prüfen oder aus einer Sicherung wiederherstellen.".into())
    }

    fn io() -> Self {
        Self("Die Profile konnten nicht geladen oder gespeichert werden. Bitte Zugriffsrechte und freien Speicherplatz im Benutzerdatenordner prüfen.".into())
    }

    fn busy() -> Self {
        Self("Die Profildatei wird gerade von einer anderen Aktion oder Instanz verwendet. Bitte nach Abschluss erneut versuchen.".into())
    }
}

#[derive(Debug)]
enum Failure {
    Corrupt,
    Io,
    Busy,
}

impl From<Failure> for SettingsError {
    fn from(failure: Failure) -> Self {
        match failure {
            Failure::Corrupt => Self::corrupt(),
            Failure::Io => Self::io(),
            Failure::Busy => Self::busy(),
        }
    }
}

// --- Dateiformat ---

#[derive(Serialize, Deserialize)]
struct ProfileRecord {
    #[serde(rename = "Id", alias = "id", default = "new_profile_id")]
    id: String,
    #[serde(rename = "Name", alias = "name", default)]
    name: String,
    // `null` ist ungültig (wie in der C#-Linie); fehlt das Feld, gilt ein leerer Text.
    #[serde(rename = "Repository", alias = "repository", default)]
    repository: String,
    #[serde(rename = "ResticExecutable", alias = "resticExecutable", default)]
    restic_executable: Option<String>,
    #[serde(rename = "Type", alias = "type", default)]
    profile_type: i64,
}

#[derive(Serialize, Deserialize)]
struct SettingsRecord {
    #[serde(rename = "Profiles", alias = "profiles", default)]
    profiles: Vec<ProfileRecord>,
    #[serde(rename = "LastProfileId", alias = "lastProfileId", default)]
    last_profile_id: Option<String>,
    #[serde(rename = "LastRestoreTarget", alias = "lastRestoreTarget", default)]
    last_restore_target: Option<String>,
    #[serde(rename = "Window", alias = "window", default)]
    window: Option<WindowSettings>,
    // Neuere Felder fehlen in der Datei, solange sie ihren Standardwert haben (Format bleibt zu 3.x kompatibel).
    #[serde(
        rename = "DarkTheme",
        alias = "darkTheme",
        default,
        skip_serializing_if = "Option::is_none"
    )]
    dark_theme: Option<bool>,
    #[serde(
        rename = "ShowPermissions",
        alias = "showPermissions",
        default,
        skip_serializing_if = "std::ops::Not::not"
    )]
    show_permissions: bool,
}

fn normalize_guid(text: &str) -> Result<String, Failure> {
    uuid::Uuid::parse_str(text)
        .map(|id| id.to_string())
        .map_err(|_| Failure::Corrupt)
}

impl ProfileRecord {
    fn into_profile(self) -> Result<RepositoryProfile, Failure> {
        Ok(RepositoryProfile {
            id: normalize_guid(&self.id)?,
            name: self.name,
            repository: self.repository,
            restic_executable: self.restic_executable,
            resolved_restic_executable: None,
            resolved_restic_source: None,
            repository_type: RepositoryType::from_number(self.profile_type),
        })
    }

    fn from_profile(profile: &RepositoryProfile) -> Self {
        Self {
            id: profile.id.clone(),
            name: profile.name.clone(),
            repository: profile.repository.clone(),
            restic_executable: profile.restic_executable.clone(),
            profile_type: i64::from(profile.repository_type.to_number()),
        }
    }
}

fn parse_settings(text: &str) -> Result<AppSettings, Failure> {
    let value: Value = serde_json::from_str(text).map_err(|_| Failure::Corrupt)?;
    let record = match value {
        // Älteste Dateien enthielten nur die Profilliste.
        Value::Array(_) => SettingsRecord {
            profiles: serde_json::from_value(value).map_err(|_| Failure::Corrupt)?,
            last_profile_id: None,
            last_restore_target: None,
            window: None,
            dark_theme: None,
            show_permissions: false,
        },
        other => serde_json::from_value(other).map_err(|_| Failure::Corrupt)?,
    };
    Ok(AppSettings {
        profiles: record
            .profiles
            .into_iter()
            .map(ProfileRecord::into_profile)
            .collect::<Result<_, _>>()?,
        last_profile_id: record
            .last_profile_id
            .as_deref()
            .map(normalize_guid)
            .transpose()?,
        last_restore_target: record.last_restore_target,
        window: record.window,
        dark_theme: record.dark_theme,
        show_permissions: record.show_permissions,
        removed_profile_count: 0,
    })
}

fn serialize_settings(settings: &AppSettings) -> Result<String, Failure> {
    let record = SettingsRecord {
        profiles: settings
            .profiles
            .iter()
            .map(ProfileRecord::from_profile)
            .collect(),
        last_profile_id: settings.last_profile_id.clone(),
        last_restore_target: settings.last_restore_target.clone(),
        window: settings.window.clone(),
        dark_theme: settings.dark_theme,
        show_permissions: settings.show_permissions,
    };
    serde_json::to_string_pretty(&record).map_err(|_| Failure::Io)
}

fn is_removed_backend(profile: &RepositoryProfile) -> bool {
    let repository = profile.repository.to_ascii_lowercase();
    profile.repository_type != RepositoryType::Local
        || repository.starts_with("sftp:")
        || repository.starts_with("s3:")
        || repository.starts_with("rest:")
}

// --- Datenordner ---

/// Datenordner der Anwendung: `%LOCALAPPDATA%` unter Windows, sonst `XDG_DATA_HOME` oder
/// `~/.local/share`.
pub fn data_directory_from(
    is_windows: bool,
    local_app_data: Option<&str>,
    xdg_data_home: Option<&str>,
    home: Option<&Path>,
) -> PathBuf {
    if is_windows {
        return PathBuf::from(local_app_data.unwrap_or_default());
    }
    match xdg_data_home.filter(|value| !value.trim().is_empty()) {
        Some(xdg) => PathBuf::from(xdg),
        None => home
            .map(Path::to_path_buf)
            .unwrap_or_default()
            .join(".local")
            .join("share"),
    }
}

pub fn data_directory() -> PathBuf {
    let home = std::env::var_os("HOME")
        .or_else(|| std::env::var_os("USERPROFILE"))
        .map(PathBuf::from);
    data_directory_from(
        cfg!(windows),
        std::env::var("LOCALAPPDATA").ok().as_deref(),
        std::env::var("XDG_DATA_HOME").ok().as_deref(),
        home.as_deref(),
    )
}

pub fn default_settings_path() -> PathBuf {
    data_directory().join("ResticBrowser").join("settings.json")
}

// --- Dienst ---

#[derive(Clone)]
pub struct SettingsService {
    path: PathBuf,
    gate: Arc<tokio::sync::Mutex<()>>,
}

impl SettingsService {
    pub fn new(settings_path: impl Into<PathBuf>) -> Self {
        let path = settings_path.into();
        let path = std::path::absolute(&path).unwrap_or(path);
        Self {
            path,
            gate: Arc::new(tokio::sync::Mutex::new(())),
        }
    }

    pub fn with_default_path() -> Self {
        Self::new(default_settings_path())
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    /// Lädt die Einstellungen. Nicht mehr unterstützte Profile (SFTP, S3, REST) werden vorher einmalig
    /// in `settings.json.bak` gesichert und dauerhaft entfernt.
    pub async fn load_settings(&self) -> Result<AppSettings, SettingsError> {
        self.with_lock(|path| {
            let mut settings = read_settings(path)?;
            let removed = settings.profiles.iter().filter(|p| is_removed_backend(p)).count();
            if removed > 0 {
                let backup = backup_path(path);
                if path.exists() && !backup.exists() {
                    fs::copy(path, &backup).map_err(|_| Failure::Io)?;
                }
                settings.profiles.retain(|p| !is_removed_backend(p));
                write_settings(path, &settings)?;
                settings.removed_profile_count = removed;
            }
            Ok(settings)
        })
        .await
    }

    /// Speichert die Einstellungen. Eine beschädigte vorhandene Datei bleibt unverändert.
    pub async fn save_settings(&self, settings: &AppSettings) -> Result<(), SettingsError> {
        let settings = AppSettings {
            removed_profile_count: 0,
            ..settings.clone()
        };
        self.with_lock(move |path| {
            read_settings(path)?;
            write_settings(path, &settings)
        })
        .await
    }

    pub async fn load_profiles(&self) -> Result<Vec<RepositoryProfile>, SettingsError> {
        Ok(self.load_settings().await?.profiles)
    }

    pub async fn save_profiles(&self, profiles: Vec<RepositoryProfile>) -> Result<(), SettingsError> {
        self.save_settings(&AppSettings {
            profiles,
            ..AppSettings::default()
        })
        .await
    }

    async fn with_lock<T: Send + 'static>(
        &self,
        action: impl FnOnce(&Path) -> Result<T, Failure> + Send + 'static,
    ) -> Result<T, SettingsError> {
        let started = Instant::now();
        let _gate = tokio::time::timeout(LOCK_TIMEOUT, self.gate.lock())
            .await
            .map_err(|_| SettingsError::busy())?;
        let path = self.path.clone();
        tokio::task::spawn_blocking(move || {
            if let Some(parent) = path.parent() {
                fs::create_dir_all(parent).map_err(|_| Failure::Io)?;
            }
            let _file_lock = acquire_file_lock(&path, started)?;
            action(&path)
        })
        .await
        .map_err(|_| SettingsError::io())?
        .map_err(SettingsError::from)
    }
}

fn backup_path(path: &Path) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(".bak");
    PathBuf::from(name)
}

fn sibling(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(suffix);
    PathBuf::from(name)
}

/// Die Sperrdatei bleibt bestehen: Löschen würde parallele Sperren auf verschiedene Dateien erlauben.
fn acquire_file_lock(settings_path: &Path, started: Instant) -> Result<File, Failure> {
    let lock_path = sibling(settings_path, ".lock");
    loop {
        match OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(&lock_path)
        {
            Ok(file) => match file.try_lock() {
                Ok(()) => return Ok(file),
                Err(TryLockError::WouldBlock) => {}
                Err(TryLockError::Error(_)) => return Err(Failure::Io),
            },
            // Freigabeverletzung (Windows 32/33) bzw. gesperrt: eine andere Instanz hält die Datei.
            Err(error)
                if matches!(error.raw_os_error(), Some(32 | 33 | 11))
                    || error.kind() == ErrorKind::WouldBlock => {}
            Err(_) => return Err(Failure::Io),
        }
        let remaining = LOCK_TIMEOUT.saturating_sub(started.elapsed());
        if remaining.is_zero() {
            return Err(Failure::Busy);
        }
        std::thread::sleep(remaining.min(LOCK_POLL));
    }
}

fn read_settings(path: &Path) -> Result<AppSettings, Failure> {
    let text = match fs::read(path) {
        Ok(bytes) => String::from_utf8(bytes).map_err(|_| Failure::Corrupt)?,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(AppSettings::default()),
        Err(_) => return Err(Failure::Io),
    };
    // Ein UTF-8-BOM (von manchen Editoren) ist erlaubt.
    parse_settings(text.trim_start_matches('\u{feff}'))
}

fn write_settings(path: &Path, settings: &AppSettings) -> Result<(), Failure> {
    let json = serialize_settings(settings)?;
    let temporary = sibling(path, &format!(".{}.tmp", uuid::Uuid::new_v4().simple()));
    let result = (|| -> std::io::Result<()> {
        let mut file = OpenOptions::new().write(true).create_new(true).open(&temporary)?;
        file.write_all(json.as_bytes())?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temporary, path)
    })();
    // Eine fehlgeschlagene Bereinigung darf den ursprünglichen Fehler nicht verdecken.
    let _ = fs::remove_file(&temporary);
    result.map_err(|_| Failure::Io)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn data_directory_follows_platform_rules() {
        assert_eq!(
            data_directory_from(true, Some(r"C:\Users\x\AppData\Local"), None, None),
            PathBuf::from(r"C:\Users\x\AppData\Local")
        );
        assert_eq!(
            data_directory_from(
                false,
                None,
                Some("/tmp/restic-browser-xdg"),
                Some(Path::new("/home/x"))
            ),
            PathBuf::from("/tmp/restic-browser-xdg")
        );
        assert_eq!(
            data_directory_from(false, None, Some("  "), Some(Path::new("/home/x"))),
            PathBuf::from("/home/x/.local/share")
        );
        assert_eq!(
            data_directory_from(false, None, None, Some(Path::new("/home/x"))),
            PathBuf::from("/home/x/.local/share")
        );
    }

    #[test]
    fn removed_backends_are_recognised_by_type_and_prefix() {
        let mut profile = RepositoryProfile::new("lokal");
        assert!(!is_removed_backend(&profile));
        for repository in ["sftp:u@h:/r", "S3:https://h/b", "REST:https://h/r"] {
            assert!(
                is_removed_backend(&RepositoryProfile::new(repository)),
                "{repository}"
            );
        }
        profile.repository_type = RepositoryType::Rest;
        assert!(is_removed_backend(&profile));
    }

    #[test]
    fn csharp_files_are_readable_and_written_in_the_same_format() {
        let csharp = r#"{
  "Profiles": [
    { "Id": "0f8fad5b-d9cb-469f-a165-70867728950e", "Name": "Lokal", "Repository": "D:\\Backup",
      "ResticExecutable": null, "Type": 0, "RestServerUrl": "ignoriert" }
  ],
  "LastProfileId": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "LastRestoreTarget": "D:\\Ziel",
  "Window": { "Width": 1200, "Height": 700, "X": 10, "Y": null, "IsMaximized": true }
}"#;
        let settings = parse_settings(csharp).unwrap();
        assert_eq!(settings.profiles[0].repository, r"D:\Backup");
        assert_eq!(
            settings.last_profile_id.as_deref(),
            Some("0f8fad5b-d9cb-469f-a165-70867728950e")
        );
        let window = settings.window.clone().unwrap();
        assert_eq!(
            (window.width, window.x, window.y, window.is_maximized),
            (1200.0, Some(10), None, true)
        );

        let written = serialize_settings(&settings).unwrap();
        for key in [
            "\"Profiles\"",
            "\"Id\"",
            "\"Name\"",
            "\"Repository\"",
            "\"ResticExecutable\"",
            "\"Type\"",
            "\"LastProfileId\"",
            "\"LastRestoreTarget\"",
            "\"Window\"",
            "\"IsMaximized\"",
        ] {
            assert!(written.contains(key), "{key} fehlt in {written}");
        }
        assert!(
            !written.contains("RestServerUrl"),
            "unbekannte Felder werden nicht übernommen"
        );
        assert!(
            !written.contains("Resolved"),
            "Laufzeitwerte werden nie gespeichert"
        );
    }

    #[test]
    fn camel_case_legacy_array_and_bom_are_tolerated() {
        let camel = r#"{"profiles":[{"name":"A","repository":"r","type":0}],"lastRestoreTarget":"x"}"#;
        let settings = parse_settings(camel).unwrap();
        assert_eq!(settings.profiles[0].name, "A");
        assert_eq!(settings.last_restore_target.as_deref(), Some("x"));
        let legacy = parse_settings(r#"[{"Name":"Alt","Repository":"r"}]"#).unwrap();
        assert_eq!(legacy.profiles.len(), 1);
        assert!(legacy.last_profile_id.is_none() && legacy.window.is_none());
    }

    #[test]
    fn invalid_content_is_rejected() {
        for invalid in [
            "{broken",
            "null",
            r#"{"Profiles":[null]}"#,
            r#"{"Profiles":[{"Repository":null}]}"#,
            r#"{"Profiles":[{"Id":"kein-guid"}]}"#,
            r#"{"LastProfileId":"x"}"#,
        ] {
            assert!(parse_settings(invalid).is_err(), "{invalid}");
        }
    }

    #[test]
    fn display_options_are_only_written_when_set_and_survive_a_roundtrip() {
        let plain = serialize_settings(&AppSettings::default()).unwrap();
        assert!(!plain.contains("DarkTheme") && !plain.contains("ShowPermissions"));
        let settings = AppSettings {
            dark_theme: Some(true),
            show_permissions: true,
            ..AppSettings::default()
        };
        let loaded = parse_settings(&serialize_settings(&settings).unwrap()).unwrap();
        assert_eq!((loaded.dark_theme, loaded.show_permissions), (Some(true), true));
        let old = parse_settings(r#"{"Profiles":[]}"#).unwrap();
        assert_eq!((old.dark_theme, old.show_permissions), (None, false));
    }

    #[test]
    fn profile_ids_survive_a_roundtrip_in_lowercase() {
        let profile = RepositoryProfile::new("repo");
        let settings = AppSettings {
            profiles: vec![profile.clone()],
            last_profile_id: Some(profile.id.clone()),
            ..AppSettings::default()
        };
        let loaded = parse_settings(&serialize_settings(&settings).unwrap()).unwrap();
        assert_eq!(loaded.profiles[0].id, profile.id);
        let upper = serialize_settings(&settings)
            .unwrap()
            .replace(&profile.id, &profile.id.to_uppercase());
        assert_eq!(parse_settings(&upper).unwrap().profiles[0].id, profile.id);
    }
}
