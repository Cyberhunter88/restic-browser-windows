//! Repository-Profil (Phase 3: nur die für den Zugriff nötigen Felder; Persistenz folgt in Phase 4).

use crate::error::ResticError;

/// Entfernte Typen bleiben nur zur Erkennung alter Profile erhalten.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum RepositoryType {
    #[default]
    Local,
    Sftp,
    S3,
    Rest,
    Other,
}

#[derive(Debug, Clone, Default)]
pub struct RepositoryProfile {
    pub name: String,
    pub repository: String,
    /// Ausdrücklich ausgewählte Restic-Datei.
    pub restic_executable: Option<String>,
    /// Zur Laufzeit validierte Datei (wird nie gespeichert).
    pub resolved_restic_executable: Option<String>,
    pub repository_type: RepositoryType,
}

impl RepositoryProfile {
    pub fn new(repository: impl Into<String>) -> Self {
        Self {
            repository: repository.into(),
            ..Self::default()
        }
    }

    pub fn display_name(&self) -> &str {
        if self.name.trim().is_empty() {
            &self.repository
        } else {
            &self.name
        }
    }

    /// Nur lokale Ordner und Netzlaufwerke sind erlaubt; Backend-Adressen wie `sftp:` oder `s3:` nicht.
    pub fn build_repository_string(&self) -> Result<String, ResticError> {
        let repository = &self.repository;
        let colon = repository.find(':');
        let first_is_letter = repository.chars().next().is_some_and(|c| c.is_ascii_alphabetic());
        let windows_drive = cfg!(windows)
            && colon == Some(1)
            && first_is_letter
            && matches!(repository.as_bytes().get(2), Some(b'\\' | b'/'));
        let backend_prefix = colon.is_some_and(|index| index > 0)
            && first_is_letter
            && repository[..colon.unwrap_or(0)]
                .chars()
                .all(|c| c.is_ascii_alphanumeric() || matches!(c, '+' | '-' | '.'));
        if self.repository_type != RepositoryType::Local || (backend_prefix && !windows_drive) {
            return Err(ResticError::new(
                "Nur lokale Repository-Ordner und Netzlaufwerke werden unterstützt.",
            ));
        }
        Ok(repository.clone())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn removed_backend_types_are_rejected() {
        for repository_type in [
            RepositoryType::Sftp,
            RepositoryType::S3,
            RepositoryType::Rest,
            RepositoryType::Other,
        ] {
            let profile = RepositoryProfile {
                repository_type,
                ..RepositoryProfile::default()
            };
            assert!(profile.build_repository_string().is_err(), "{repository_type:?}");
        }
    }

    #[test]
    fn backend_addresses_are_rejected() {
        for repository in [
            "sftp:user@host:/repo",
            "S3:https://host/bucket",
            "REST:https://host/repo",
            "rclone:remote:repo",
            "b2:bucket:repo",
            "azure:container:repo",
            "gs:bucket:repo",
            "swift:container:repo",
        ] {
            assert!(
                RepositoryProfile::new(repository)
                    .build_repository_string()
                    .is_err(),
                "{repository}"
            );
        }
    }

    #[test]
    fn local_paths_are_accepted() {
        assert_eq!(
            RepositoryProfile::new("relative/repo")
                .build_repository_string()
                .unwrap(),
            "relative/repo"
        );
        assert_eq!(
            RepositoryProfile::new("/mnt/backup")
                .build_repository_string()
                .unwrap(),
            "/mnt/backup"
        );
        if cfg!(windows) {
            assert_eq!(
                RepositoryProfile::new(r"C:\repo")
                    .build_repository_string()
                    .unwrap(),
                r"C:\repo"
            );
            assert_eq!(
                RepositoryProfile::new("D:/repo")
                    .build_repository_string()
                    .unwrap(),
                "D:/repo"
            );
        }
    }

    #[test]
    fn display_name_falls_back_to_repository() {
        assert_eq!(RepositoryProfile::new("/r").display_name(), "/r");
        let named = RepositoryProfile {
            name: "Backup".into(),
            ..RepositoryProfile::new("/r")
        };
        assert_eq!(named.display_name(), "Backup");
    }
}
