use std::collections::HashMap;
use std::fmt;

use zeroize::Zeroize;

/// Passwort und Sitzungsumgebung. Wird nur im Arbeitsspeicher gehalten, beim Freigeben
/// überschrieben und niemals ausgegeben (`Debug` zeigt keine Werte).
pub struct SessionCredentials {
    password: String,
    environment: HashMap<String, String>,
}

impl SessionCredentials {
    pub fn new(password: impl Into<String>, environment: HashMap<String, String>) -> Self {
        let environment = environment
            .into_iter()
            .filter(|(key, _)| !key.trim().is_empty())
            .map(|(key, value)| (key.trim().to_string(), value))
            .collect();
        let pw = password.into();
        Self {
            password: pw,
            environment,
        }
    }

    pub fn password(&self) -> &str {
        &self.password
    }

    pub fn environment(&self) -> &HashMap<String, String> {
        &self.environment
    }

    /// Überschreibt alle Geheimnisse und leert die Umgebung (entspricht `Dispose` in 3.x).
    pub fn clear(&mut self) {
        self.password.zeroize();
        for value in self.environment.values_mut() {
            value.zeroize();
        }
        self.environment.clear();
    }
}

impl Drop for SessionCredentials {
    fn drop(&mut self) {
        self.clear();
    }
}

impl fmt::Debug for SessionCredentials {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("SessionCredentials")
            .field("password", &"<verborgen>")
            .field("environment_entries", &self.environment.len())
            .finish()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clear_removes_all_secrets() {
        let mut credentials =
            SessionCredentials::new("secret", HashMap::from([("TOKEN".into(), "hidden".into())]));
        assert_eq!(credentials.password(), "secret");
        credentials.clear();
        assert_eq!(credentials.password(), "");
        assert!(credentials.environment().is_empty());
    }

    #[test]
    fn debug_output_never_contains_secrets() {
        let credentials = SessionCredentials::new(
            "geheim",
            HashMap::from([("TOKEN".into(), "stop-wert-123".into())]),
        );
        let text = format!("{credentials:?}");
        assert!(!text.contains("geheim"));
        assert!(!text.contains("stop-wert-123"));
    }

    #[test]
    fn blank_names_are_dropped_and_names_trimmed() {
        let credentials = SessionCredentials::new(
            "x",
            HashMap::from([(" ".into(), "a".into()), (" NAME ".into(), "b".into())]),
        );
        assert_eq!(credentials.environment().len(), 1);
        assert_eq!(credentials.environment()["NAME"], "b");
    }
}
