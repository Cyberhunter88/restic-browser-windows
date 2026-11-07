//! Dateitests für `SettingsService` (Gegenstücke zu `ReliabilityTests`, `CoreTests` und `UxTests`).

use std::fs::{self, OpenOptions};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::{Duration, Instant};

use restic_app::{AppSettings, SettingsService, WindowSettings};
use restic_core::profile::{RepositoryProfile, RepositoryType};

static COUNTER: AtomicUsize = AtomicUsize::new(0);

struct TempDir(PathBuf);

impl TempDir {
    fn new(name: &str) -> Self {
        let id = COUNTER.fetch_add(1, Ordering::Relaxed);
        let path = std::env::temp_dir().join(format!("rb-settings-{name}-{}-{id}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }

    fn settings(&self) -> PathBuf {
        self.0.join("settings.json")
    }

    fn temporary_files(&self) -> usize {
        fs::read_dir(&self.0)
            .unwrap()
            .flatten()
            .filter(|e| e.file_name().to_string_lossy().ends_with(".tmp"))
            .count()
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn profile(name: &str, repository: &str) -> RepositoryProfile {
    RepositoryProfile {
        name: name.into(),
        ..RepositoryProfile::new(repository)
    }
}

fn backup(path: &Path) -> PathBuf {
    PathBuf::from(format!("{}.bak", path.display()))
}

#[tokio::test]
async fn missing_file_gives_empty_settings_without_creating_it() {
    let dir = TempDir::new("missing");
    let service = SettingsService::new(dir.settings());
    let settings = service.load_settings().await.unwrap();
    assert!(settings.profiles.is_empty() && settings.window.is_none() && settings.last_profile_id.is_none());
    assert!(!dir.settings().exists());
}

#[tokio::test]
async fn removed_profiles_are_backed_up_once_before_deletion() {
    let dir = TempDir::new("removed");
    let original = r#"{"Profiles":[{"Name":"Lokal","Repository":"local-repo","Type":0},{"Name":"Alt","Repository":"sftp:user@host:/repo","Type":0}]}"#;
    fs::write(dir.settings(), original).unwrap();
    let service = SettingsService::new(dir.settings());

    let settings = service.load_settings().await.unwrap();
    assert_eq!(settings.profiles.len(), 1);
    assert_eq!(settings.removed_profile_count, 1);
    assert_eq!(fs::read_to_string(backup(&dir.settings())).unwrap(), original);

    // Eine zweite Bereinigung darf die erste Sicherung nicht überschreiben.
    fs::write(
        dir.settings(),
        r#"{"Profiles":[{"Name":"Alt2","Repository":"s3:https://host/bucket"}]}"#,
    )
    .unwrap();
    assert_eq!(service.load_settings().await.unwrap().removed_profile_count, 1);
    assert_eq!(fs::read_to_string(backup(&dir.settings())).unwrap(), original);
    assert_eq!(service.load_settings().await.unwrap().removed_profile_count, 0);
}

#[tokio::test]
async fn legacy_backend_profiles_are_removed_from_object_and_array_files() {
    for legacy_array in [false, true] {
        let dir = TempDir::new("legacy");
        let local = profile("Local", "local-repo");
        let profiles = format!(
            r#"[{{"Id":"{}","Name":"Local","Repository":"local-repo","Type":0}},
               {{"Name":"SFTP","Repository":"sftp:user@host:/repo","Type":1,"SftpHost":"host","SftpPort":22}},
               {{"Name":"S3","Repository":"s3:https://host/bucket","Type":2}},
               {{"Name":"REST","Repository":"rest:https://rest.example/repo","Type":3,"RestServerUrl":"https://rest.example"}},
               {{"Name":"REST-Adresse","Repository":"REST:https://rest.example/legacy","Type":0}}]"#,
            local.id
        );
        let text = if legacy_array {
            profiles
        } else {
            format!(r#"{{"Profiles":{profiles}}}"#)
        };
        fs::write(dir.settings(), text).unwrap();
        let service = SettingsService::new(dir.settings());
        let settings = service.load_settings().await.unwrap();
        assert_eq!(settings.profiles.len(), 1);
        assert_eq!(settings.profiles[0].id, local.id);
        assert_eq!(
            settings.profiles[0].build_repository_string().unwrap(),
            "local-repo"
        );
        assert_eq!(settings.removed_profile_count, 4);
        let reloaded = service.load_settings().await.unwrap();
        assert_eq!(reloaded.profiles.len(), 1);
        assert_eq!(reloaded.removed_profile_count, 0);
        let saved = fs::read_to_string(dir.settings()).unwrap();
        assert!(!saved.contains("sftp:") && !saved.contains("rest.example"));
    }
}

#[tokio::test]
async fn damaged_files_are_never_overwritten() {
    let dir = TempDir::new("damaged");
    let service = SettingsService::new(dir.settings());
    for invalid in [
        "{broken",
        "null",
        r#"{"Profiles":[null]}"#,
        r#"{"Profiles":[{"Repository":null}]}"#,
    ] {
        fs::write(dir.settings(), invalid).unwrap();
        let load = service.load_settings().await.unwrap_err();
        assert!(load.to_string().contains("beschädigt"), "{invalid}: {load}");
        let save = service.save_settings(&AppSettings::default()).await.unwrap_err();
        assert!(save.to_string().contains("beschädigt"), "{invalid}: {save}");
        assert_eq!(fs::read_to_string(dir.settings()).unwrap(), invalid);
        assert_eq!(dir.temporary_files(), 0);
    }
}

#[tokio::test]
async fn unreadable_target_is_an_error_and_leaves_no_temporary_file() {
    let dir = TempDir::new("directory");
    let service = SettingsService::new(dir.settings());
    fs::create_dir(dir.settings()).unwrap();
    assert!(
        service
            .load_settings()
            .await
            .unwrap_err()
            .to_string()
            .contains("konnten nicht geladen")
    );
    assert!(service.save_settings(&AppSettings::default()).await.is_err());
    assert_eq!(dir.temporary_files(), 0);
}

#[tokio::test]
async fn a_held_file_lock_times_out_after_about_five_seconds_without_changing_the_file() {
    let dir = TempDir::new("lock");
    let service = SettingsService::new(dir.settings());
    service
        .save_settings(&AppSettings {
            profiles: vec![profile("Erhalten", "repo")],
            ..AppSettings::default()
        })
        .await
        .unwrap();
    let original = fs::read_to_string(dir.settings()).unwrap();

    let holder = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(format!("{}.lock", dir.settings().display()))
        .unwrap();
    holder.lock().unwrap();
    let started = Instant::now();
    let error = service.save_settings(&AppSettings::default()).await.unwrap_err();
    let elapsed = started.elapsed();
    assert!(error.to_string().contains("gerade von einer anderen"), "{error}");
    assert!(
        elapsed >= Duration::from_secs(4) && elapsed < Duration::from_secs(8),
        "{elapsed:?}"
    );
    assert_eq!(fs::read_to_string(dir.settings()).unwrap(), original);

    // Nach Freigabe funktioniert das Speichern wieder.
    drop(holder);
    service.save_settings(&AppSettings::default()).await.unwrap();
}

#[tokio::test]
async fn a_lock_held_for_a_short_time_only_delays_the_save() {
    let dir = TempDir::new("short-lock");
    let service = SettingsService::new(dir.settings());
    let holder = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(format!("{}.lock", dir.settings().display()))
        .unwrap();
    holder.lock().unwrap();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(400)).await;
        drop(holder);
    });
    service
        .save_settings(&AppSettings {
            profiles: vec![profile("Spät", "repo")],
            ..AppSettings::default()
        })
        .await
        .unwrap();
    assert_eq!(service.load_settings().await.unwrap().profiles[0].name, "Spät");
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn parallel_saves_from_two_instances_keep_the_file_complete() {
    let dir = TempDir::new("concurrent");
    let first = SettingsService::new(dir.settings());
    let second = SettingsService::new(dir.settings());
    let mut tasks = Vec::new();
    for i in 0..40 {
        let service = if i % 2 == 0 { first.clone() } else { second.clone() };
        tasks.push(tokio::spawn(async move {
            let settings = AppSettings {
                profiles: vec![profile(&format!("Profil {i}"), &"x".repeat(4096))],
                ..AppSettings::default()
            };
            service.save_settings(&settings).await
        }));
    }
    for task in tasks {
        task.await.unwrap().unwrap();
    }
    let saved = SettingsService::new(dir.settings())
        .load_settings()
        .await
        .unwrap();
    assert_eq!(saved.profiles.len(), 1);
    assert_eq!(saved.profiles[0].repository.len(), 4096);
    assert_eq!(dir.temporary_files(), 0);
}

#[tokio::test]
async fn ux_state_roundtrips_and_older_files_without_it_stay_readable() {
    let dir = TempDir::new("ux");
    let service = SettingsService::new(dir.settings());
    let a = profile("A", "repo");
    service
        .save_settings(&AppSettings {
            profiles: vec![a.clone()],
            last_profile_id: Some(a.id.clone()),
            last_restore_target: Some("D:\\Ziel".into()),
            window: Some(WindowSettings {
                width: 1200.0,
                height: 700.0,
                x: Some(10),
                y: Some(20),
                is_maximized: true,
            }),
            removed_profile_count: 7,
        })
        .await
        .unwrap();
    let loaded = service.load_settings().await.unwrap();
    assert_eq!(loaded.last_profile_id.as_deref(), Some(a.id.as_str()));
    assert_eq!(loaded.last_restore_target.as_deref(), Some("D:\\Ziel"));
    let window = loaded.window.unwrap();
    assert_eq!(
        (
            window.width,
            window.height,
            window.x,
            window.y,
            window.is_maximized
        ),
        (1200.0, 700.0, Some(10), Some(20), true)
    );
    assert_eq!(loaded.removed_profile_count, 0, "der Zähler wird nie gespeichert");

    fs::write(dir.settings(), r#"{"Profiles":[]}"#).unwrap();
    let legacy = service.load_settings().await.unwrap();
    assert!(
        legacy.last_profile_id.is_none() && legacy.window.is_none() && legacy.last_restore_target.is_none()
    );
}

#[tokio::test]
async fn secrets_and_runtime_values_are_never_stored() {
    let dir = TempDir::new("secrets");
    let service = SettingsService::new(dir.settings());
    let mut p = profile("Mit Laufzeitwerten", "repo");
    p.restic_executable = Some("C:\\Werkzeuge\\restic.exe".into());
    p.resolved_restic_executable = Some("/laufzeit/restic".into());
    p.resolved_restic_source = Some("Systempfad".into());
    p.repository_type = RepositoryType::Local;
    service.save_profiles(vec![p]).await.unwrap();
    let text = fs::read_to_string(dir.settings()).unwrap();
    assert!(
        text.contains("restic.exe"),
        "die ausdrücklich gewählte Datei wird gespeichert"
    );
    assert!(
        !text.contains("/laufzeit") && !text.contains("Systempfad"),
        "{text}"
    );
    assert!(!text.to_lowercase().contains("password"));
}

#[tokio::test]
async fn parent_directory_is_created_on_save() {
    let dir = TempDir::new("nested");
    let path = dir.0.join("a").join("b").join("settings.json");
    SettingsService::new(&path)
        .save_profiles(vec![profile("X", "repo")])
        .await
        .unwrap();
    assert!(path.exists());
}
