use std::collections::HashMap;

use crate::error::ResticError;
use crate::models::EnvironmentEntry;

const RESERVED: [&str; 5] = [
    "RESTIC_PASSWORD",
    "RESTIC_PASSWORD_FILE",
    "RESTIC_PASSWORD_COMMAND",
    "RESTIC_REPOSITORY",
    "RESTIC_REPOSITORY_FILE",
];

fn is_valid_name(name: &str) -> bool {
    let mut chars = name.chars();
    matches!(chars.next(), Some(c) if c.is_ascii_alphabetic() || c == '_')
        && chars.all(|c| c.is_ascii_alphanumeric() || c == '_')
}

/// Prüft zusätzliche Umgebungsvariablen für Restic. Leere Namen werden übersprungen,
/// verwaltete oder doppelte Variablen (ohne Beachtung der Groß-/Kleinschreibung) abgelehnt.
pub fn normalize(entries: &[EnvironmentEntry]) -> Result<HashMap<String, String>, ResticError> {
    let mut result = HashMap::new();
    let mut seen = Vec::<String>::new();
    for entry in entries {
        let name = entry.name.trim();
        if name.is_empty() {
            continue;
        }
        if !is_valid_name(name) {
            return Err(ResticError::new(format!(
                "Der Variablenname '{name}' ist ungültig."
            )));
        }
        if RESERVED
            .iter()
            .any(|reserved| reserved.eq_ignore_ascii_case(name))
        {
            return Err(ResticError::new(format!(
                "Die Variable '{name}' wird von Restic Browser selbst verwaltet."
            )));
        }
        let key = name.to_ascii_lowercase();
        if seen.contains(&key) {
            return Err(ResticError::new(format!(
                "Die Variable '{name}' wurde mehrfach angegeben."
            )));
        }
        seen.push(key);
        result.insert(name.to_string(), entry.value.clone());
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn entry(name: &str, value: &str) -> EnvironmentEntry {
        EnvironmentEntry {
            name: name.into(),
            value: value.into(),
        }
    }

    #[test]
    fn accepts_and_trims_valid_names() {
        let values = normalize(&[entry(" MY_BACKEND_TOKEN ", "key"), entry("  ", "ignoriert")]).unwrap();
        assert_eq!(values.len(), 1);
        assert_eq!(values["MY_BACKEND_TOKEN"], "key");
    }

    #[test]
    fn rejects_managed_variables_in_any_case() {
        assert!(normalize(&[entry("RESTIC_PASSWORD", "secret")]).is_err());
        assert!(normalize(&[entry("restic_repository", "x")]).is_err());
    }

    #[test]
    fn rejects_duplicates_ignoring_case() {
        assert!(normalize(&[entry("A", "1"), entry(" a ", "2")]).is_err());
    }

    #[test]
    fn rejects_invalid_names() {
        assert!(normalize(&[entry("1ABC", "x")]).is_err());
        assert!(normalize(&[entry("A-B", "x")]).is_err());
        assert!(normalize(&[entry("A B", "x")]).is_err());
    }
}
