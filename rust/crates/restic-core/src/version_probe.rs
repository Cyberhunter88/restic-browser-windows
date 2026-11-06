//! Validierung einer Restic-Datei mit `restic version --json`.
//!
//! Eine Herkunfts- oder Versionsanzeige darf nie allein aus einer Konstanten stammen:
//! maßgeblich ist die Ausgabe der tatsächlich gestarteten Datei.

use std::future::Future;
use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::error::ResticError;
use crate::models::ResticVersion;
use crate::runner::{ResticCommand, ResticProcessRunner};

/// Zeitlimit der Versionsprüfung.
pub const PROBE_TIMEOUT: Duration = Duration::from_secs(5);

/// Älteste unterstützte Restic-Version.
pub const MINIMUM_VERSION: (u32, u32, u32) = (0, 17, 1);

/// Prüft eine ausführbare Datei. Austauschbar, damit Tests ohne echte Prozesse auskommen.
pub trait VersionProbe: Send + Sync {
    fn probe(
        &self,
        executable: &str,
        stop: &CancellationToken,
        timeout: Duration,
    ) -> impl Future<Output = Result<ResticVersion, ResticError>> + Send;
}

/// Startet die Datei wirklich und wertet die JSON-Ausgabe aus.
#[derive(Debug, Clone, Copy, Default)]
pub struct ProcessVersionProbe {
    runner: ResticProcessRunner,
}

impl ProcessVersionProbe {
    pub fn new() -> Self {
        Self::default()
    }
}

impl VersionProbe for ProcessVersionProbe {
    async fn probe(
        &self,
        executable: &str,
        stop: &CancellationToken,
        timeout: Duration,
    ) -> Result<ResticVersion, ResticError> {
        if stop.is_cancelled() {
            return Err(ResticError::Cancelled);
        }
        let deadline = stop.child_token();
        let timer = {
            let deadline = deadline.clone();
            tokio::spawn(async move {
                tokio::time::sleep(timeout).await;
                deadline.cancel();
            })
        };
        let command = ResticCommand::new(executable, vec!["version".into(), "--json".into()]);
        let result = self.runner.run(&command, &deadline).await;
        timer.abort();
        let result = match result {
            Err(ResticError::Cancelled) if !stop.is_cancelled() => {
                return Err(ResticError::new(format!(
                    "Die Restic-Versionsprüfung hat das Zeitlimit überschritten ({} Sekunden).",
                    timeout.as_secs().max(1)
                )));
            }
            other => other?,
        };
        if result.exit_code != 0 {
            return Err(ResticError::new("Die Restic-Versionsprüfung ist fehlgeschlagen."));
        }
        parse_version_output(&result.standard_output)
    }
}

/// Wertet die Ausgabe von `restic version --json` aus und prüft die Mindestversion.
pub fn parse_version_output(output: &str) -> Result<ResticVersion, ResticError> {
    let version = match serde_json::from_str::<Option<ResticVersion>>(output) {
        Ok(Some(version)) => version,
        Ok(None) => return Err(ResticError::new("Die Restic-Versionsausgabe ist ungültig.")),
        Err(_) => {
            return Err(ResticError::new(
                "Die Restic-Versionsausgabe ist kein gültiges JSON.",
            ));
        }
    };
    let Some(parsed) = parse_version(&version.version) else {
        return Err(ResticError::new(
            "Die Restic-Versionsausgabe enthält keine gültige Versionsnummer.",
        ));
    };
    if parsed < MINIMUM_VERSION {
        return Err(ResticError::new(
            "Die Restic-Version ist zu alt. Benötigt wird mindestens 0.17.1.",
        ));
    }
    Ok(version)
}

/// Liest `x.y`, `x.y.z` oder `x.y.z.w` (mit optionalem führendem `v`); fehlende Teile zählen als 0.
pub fn parse_version(text: &str) -> Option<(u32, u32, u32)> {
    let parts: Vec<&str> = text.trim().trim_start_matches('v').split('.').collect();
    if !(2..=4).contains(&parts.len()) {
        return None;
    }
    let mut numbers = [0u32; 4];
    for (slot, part) in numbers.iter_mut().zip(&parts) {
        if part.is_empty() || !part.chars().all(|c| c.is_ascii_digit()) {
            return None;
        }
        *slot = part.parse().ok()?;
    }
    Some((numbers[0], numbers[1], numbers[2]))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_current_and_minimum_versions() {
        assert!(parse_version_output(r#"{"version":"0.19.1","unknown":true}"#).is_ok());
        assert!(parse_version_output(r#"{"version":"v0.17.1"}"#).is_ok());
        assert!(parse_version_output(r#"{"version":"1.0"}"#).is_ok());
    }

    #[test]
    fn rejects_invalid_outputs_with_german_messages() {
        let message = |text: &str| parse_version_output(text).unwrap_err().to_string();
        assert!(message("null").contains("ungültig"));
        assert!(message("{").contains("kein gültiges JSON"));
        assert!(message("{}").contains("Versionsnummer"));
        assert!(message(r#"{"version":null}"#).contains("Versionsnummer"));
        assert!(message(r#"{"version":"abc"}"#).contains("Versionsnummer"));
        assert!(message(r#"{"version":"0.16.0"}"#).contains("zu alt"));
        assert!(message(r#"{"version":"0.17.0"}"#).contains("zu alt"));
    }

    #[test]
    fn version_parsing_handles_edge_cases() {
        assert_eq!(parse_version("0.19.1"), Some((0, 19, 1)));
        assert_eq!(parse_version("0.19"), Some((0, 19, 0)));
        assert_eq!(parse_version("0.19.1.5"), Some((0, 19, 1)));
        assert_eq!(parse_version("1"), None);
        assert_eq!(parse_version("0.1.2.3.4"), None);
        assert_eq!(parse_version("0..1"), None);
        assert_eq!(parse_version("0.19.1-beta"), None);
        assert_eq!(parse_version(""), None);
    }
}
