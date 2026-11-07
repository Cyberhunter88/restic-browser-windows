//! Repository-Profil. Die Persistenz (JSON-Format der 3.x-Linie) liegt in `restic-app`.

use crate::error::ResticError;

/// Entfernte Typen bleiben nur zur Erkennung alter Profile erhalten. Die Zahlenwerte entsprechen
/// der Aufzählung in der C#-Linie und stehen so in `settings.json`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum RepositoryType {
    #[default]
    Local,
    Sftp,
    S3,
    Rest,
    Other,
}

impl RepositoryType {
    pub fn to_number(self) -> i32 {
        match self {
            Self::Local => 0,
            Self::Sftp => 1,
            Self::S3 => 2,
            Self::Rest => 3,
            Self::Other => 4,
        }
    }

    /// Unbekannte Zahlen gelten als entfernter Typ (`Other`).
    pub fn from_number(number: i64) -> Self {
        match number {
            0 => Self::Local,
            1 => Self::Sftp,
            2 => Self::S3,
            3 => Self::Rest,
            _ => Self::Other,
        }
    }
}

/// Neue zufällige Kennung im Format der C#-Linie (`Guid`, kleingeschrieben mit Bindestrichen).
pub fn new_profile_id() -> String {
    uuid::Uuid::new_v4().to_string()
}

#[derive(Debug, Clone)]
pub struct RepositoryProfile {
    pub id: String,
    pub name: String,
    pub repository: String,
    /// Ausdrücklich ausgewählte Restic-Datei.
    pub restic_executable: Option<String>,
    /// Zur Laufzeit validierte Datei (wird nie gespeichert).
    pub resolved_restic_executable: Option<String>,
    /// Herkunft der validierten Datei (nur Laufzeit, wird nie gespeichert).
    pub resolved_restic_source: Option<String>,
    pub repository_type: RepositoryType,
}

impl Default for RepositoryProfile {
    fn default() -> Self {
        Self {
            id: new_profile_id(),
            name: String::new(),
            repository: String::new(),
            restic_executable: None,
            resolved_restic_executable: None,
            resolved_restic_source: None,
            repository_type: RepositoryType::Local,
        }
    }
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

    #[test]
    fn type_numbers_match_the_csharp_enumeration() {
        for (number, expected) in [
            (0, RepositoryType::Local),
            (1, RepositoryType::Sftp),
            (2, RepositoryType::S3),
            (3, RepositoryType::Rest),
            (4, RepositoryType::Other),
            (99, RepositoryType::Other),
        ] {
            assert_eq!(RepositoryType::from_number(number), expected);
        }
        assert_eq!(RepositoryType::Rest.to_number(), 3);
    }

    #[test]
    fn every_profile_gets_a_unique_lowercase_guid() {
        let (a, b) = (RepositoryProfile::new("a"), RepositoryProfile::new("b"));
        assert_ne!(a.id, b.id);
        assert_eq!(a.id.len(), 36);
        assert_eq!(a.id, a.id.to_lowercase());
    }
}
