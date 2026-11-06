use thiserror::Error;

/// Fehler des Restic-Zugriffs. Alle Meldungen sind für Benutzer auf Deutsch formuliert.
#[derive(Debug, Error)]
pub enum ResticError {
    /// Regulärer Fehler, optional mit dem Exitcode von Restic.
    #[error("{message}")]
    Failed { message: String, exit_code: Option<i32> },
    /// Der Benutzer hat den Vorgang abgebrochen. Kein Fehler im eigentlichen Sinn.
    #[error("Der Vorgang wurde abgebrochen.")]
    Cancelled,
}

impl ResticError {
    pub fn new(message: impl Into<String>) -> Self {
        Self::Failed {
            message: message.into(),
            exit_code: None,
        }
    }

    pub fn with_exit_code(message: impl Into<String>, exit_code: i32) -> Self {
        Self::Failed {
            message: message.into(),
            exit_code: Some(exit_code),
        }
    }

    pub fn exit_code(&self) -> Option<i32> {
        match self {
            Self::Failed { exit_code, .. } => *exit_code,
            Self::Cancelled => None,
        }
    }

    pub fn is_cancelled(&self) -> bool {
        matches!(self, Self::Cancelled)
    }
}
