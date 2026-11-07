use crate::error::ResticError;

/// Ergänzt bekannte Restic-Fehler um einen konkreten Handlungshinweis.
pub fn describe(error: &ResticError) -> String {
    let message = error.to_string();
    let hint = match error.exit_code() {
        Some(10) => {
            Some("Prüfe den Repository-Pfad und ob das Laufwerk oder die Netzfreigabe erreichbar ist.")
        }
        Some(11) => Some("Warte, bis der andere Vorgang beendet ist, und versuche es erneut."),
        Some(12) => Some("Prüfe das Passwort auf Tippfehler und die Umschalt- oder Feststelltaste."),
        _ => None,
    };
    let hint = hint.or_else(|| {
        let lower = message.to_lowercase();
        (lower.contains("restic") && lower.contains("nicht gefunden"))
            .then_some("Wähle im Verbindungsdialog unter „Erweitert“ eine Restic-Datei aus.")
    });
    match hint {
        Some(hint) => format!("{message}\n\n{hint}"),
        None => message,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adds_hints_for_known_exit_codes() {
        let wrong_pw = ResticError::with_exit_code("Das Repository-Passwort ist falsch.", 12);
        assert!(describe(&wrong_pw).contains("Tippfehler"));
        let missing = ResticError::with_exit_code("Das Repository wurde nicht gefunden.", 10);
        assert!(describe(&missing).contains("Repository-Pfad"));
        let locked = ResticError::with_exit_code("Gesperrt.", 11);
        assert!(describe(&locked).contains("andere Vorgang"));
    }

    #[test]
    fn leaves_unknown_errors_unchanged() {
        assert_eq!(
            describe(&ResticError::with_exit_code("Etwas anderes.", 1)),
            "Etwas anderes."
        );
    }

    #[test]
    fn hints_at_missing_restic_executable() {
        let error = ResticError::new("Restic wurde nicht gefunden.");
        assert!(describe(&error).contains("Erweitert"));
    }
}
