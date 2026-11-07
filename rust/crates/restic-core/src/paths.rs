//! Schutz des Repositorys: Wiederherstellungsziele dürfen sich nicht mit dem lokalen Repository
//! überlappen. Verknüpfungen werden aufgelöst, damit Alias-Pfade das Repository nicht umgehen.

use std::fs;
use std::path::{Component, MAIN_SEPARATOR, Path, PathBuf};

use crate::error::ResticError;
use crate::profile::RepositoryProfile;

/// Prüft, dass `destination` und das lokale Repository weder identisch sind noch ineinander liegen.
pub fn validate_destination(
    profile: &RepositoryProfile,
    destination: &str,
    label: &str,
) -> Result<(), ResticError> {
    if destination.trim().is_empty() {
        return Err(ResticError::new(format!("Bitte gib einen gültigen {label} an.")));
    }
    let unsafe_check = |message: String| {
        ResticError::new(format!(
            "{label} oder Repository-Pfad konnte nicht sicher geprüft werden: {message}"
        ))
    };
    let repository = resolve_directory_path(&profile.build_repository_string()?).map_err(unsafe_check)?;
    let target = resolve_directory_path(destination).map_err(unsafe_check)?;
    if is_same_or_child(&repository, &target) || is_same_or_child(&target, &repository) {
        return Err(ResticError::new(format!(
            "{label} und lokales Repository dürfen sich nicht überlappen."
        )));
    }
    Ok(())
}

fn normalize_case(text: &str) -> String {
    if cfg!(windows) {
        text.to_lowercase()
    } else {
        text.to_string()
    }
}

fn is_same_or_child(path: &str, parent: &str) -> bool {
    let (path, parent) = (normalize_case(path), normalize_case(parent));
    if path == parent {
        return true;
    }
    let prefix = if parent.ends_with(MAIN_SEPARATOR) {
        parent
    } else {
        format!("{parent}{MAIN_SEPARATOR}")
    };
    path.starts_with(&prefix)
}

/// Absoluter Pfad ohne Verknüpfungen. Pfade mit `..` werden abgelehnt, weil sie sich nicht sicher
/// prüfen lassen.
fn resolve_directory_path(path: &str) -> Result<String, String> {
    if Path::new(path)
        .components()
        .any(|c| matches!(c, Component::ParentDir))
    {
        return Err("Pfade mit '..' können nicht sicher geprüft werden.".to_string());
    }
    let full = std::path::absolute(path).map_err(|e| e.to_string())?;
    let mut current = PathBuf::new();
    for component in full.components() {
        match component {
            Component::Prefix(_) | Component::RootDir => current.push(component.as_os_str()),
            Component::Normal(part) => {
                current.push(part);
                if fs::symlink_metadata(&current).is_ok_and(|m| m.file_type().is_symlink()) {
                    current = fs::canonicalize(&current)
                        .map(strip_verbatim)
                        .map_err(|_| "Symbolische Verknüpfung konnte nicht aufgelöst werden.".to_string())?;
                }
            }
            Component::CurDir | Component::ParentDir => {}
        }
    }
    let text = current.to_string_lossy().into_owned();
    let trimmed = text.trim_end_matches(MAIN_SEPARATOR);
    Ok(if trimmed.is_empty() || trimmed.ends_with(':') {
        text
    } else {
        trimmed.to_string()
    })
}

/// Entfernt das Windows-Präfix `\\?\` aus kanonisierten Pfaden, damit Vergleiche stimmen.
fn strip_verbatim(path: PathBuf) -> PathBuf {
    let text = path.to_string_lossy();
    if let Some(rest) = text.strip_prefix(r"\\?\UNC\") {
        PathBuf::from(format!(r"\\{rest}"))
    } else if let Some(rest) = text.strip_prefix(r"\\?\") {
        PathBuf::from(rest)
    } else {
        path
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Temp(PathBuf);
    impl Temp {
        fn new(name: &str) -> Self {
            let path = std::env::temp_dir().join(format!("rb-paths-{name}-{}", std::process::id()));
            let _ = fs::remove_dir_all(&path);
            fs::create_dir_all(path.join("repository")).unwrap();
            Self(path)
        }
    }
    impl Drop for Temp {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn profile(root: &Temp) -> RepositoryProfile {
        RepositoryProfile::new(root.0.join("repository").to_string_lossy())
    }

    #[test]
    fn rejects_repository_children_parents_and_itself() {
        let root = Temp::new("overlap");
        let profile = profile(&root);
        let repository = root.0.join("repository");
        for target in [repository.clone(), repository.join("child"), root.0.clone()] {
            let error = validate_destination(&profile, &target.to_string_lossy(), "Wiederherstellungsziel")
                .unwrap_err();
            assert!(error.to_string().contains("überlappen"), "{target:?}: {error}");
        }
    }

    #[test]
    fn accepts_sibling_with_common_prefix() {
        let root = Temp::new("sibling");
        let profile = profile(&root);
        let sibling = root.0.join("repository-other");
        validate_destination(&profile, &sibling.to_string_lossy(), "Wiederherstellungsziel").unwrap();
    }

    #[test]
    fn ambiguous_parent_segments_cannot_be_checked_safely() {
        let root = Temp::new("dotdot");
        let profile = profile(&root);
        let tricky = root.0.join("other").join("..").join("repository");
        let error =
            validate_destination(&profile, &tricky.to_string_lossy(), "Wiederherstellungsziel").unwrap_err();
        assert!(error.to_string().contains("nicht sicher geprüft"), "{error}");
    }

    #[test]
    fn empty_destination_is_rejected() {
        let root = Temp::new("empty");
        let profile = profile(&root);
        assert!(
            validate_destination(&profile, "  ", "Wiederherstellungsziel")
                .unwrap_err()
                .to_string()
                .contains("gültigen")
        );
    }

    #[cfg(unix)]
    #[test]
    fn symlink_alias_to_the_repository_is_detected() {
        let root = Temp::new("alias");
        let profile = profile(&root);
        let alias = root.0.join("alias");
        std::os::unix::fs::symlink(root.0.join("repository"), &alias).unwrap();
        let target = alias.join("child");
        let error =
            validate_destination(&profile, &target.to_string_lossy(), "Wiederherstellungsziel").unwrap_err();
        assert!(error.to_string().contains("überlappen"), "{error}");
    }

    #[test]
    fn ancestor_comparison_respects_path_boundaries() {
        let sep = MAIN_SEPARATOR;
        assert!(is_same_or_child(
            &format!("{sep}a{sep}b{sep}c"),
            &format!("{sep}a{sep}b")
        ));
        assert!(!is_same_or_child(
            &format!("{sep}a{sep}bc"),
            &format!("{sep}a{sep}b")
        ));
        assert!(is_same_or_child(
            &format!("{sep}a{sep}b"),
            &format!("{sep}a{sep}b")
        ));
        assert!(is_same_or_child(&format!("{sep}a"), &sep.to_string()));
    }
}
