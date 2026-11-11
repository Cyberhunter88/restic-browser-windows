/// Sammelt Prüfdetails und begrenzt sie auf höchstens 64 KiB UTF-8.
#[derive(Debug, Default)]
pub struct BoundedCheckDetails {
    text: String,
    bytes: usize,
    truncated: bool,
}

impl BoundedCheckDetails {
    pub const MAXIMUM_BYTES: usize = 64 * 1024;
    const TRUNCATED_MESSAGE: &'static str = "\n… Weitere Prüfdetails wurden aus Platzgründen gekürzt.";

    pub fn new() -> Self {
        Self::default()
    }

    pub fn is_truncated(&self) -> bool {
        self.truncated
    }

    pub fn append_line(&mut self, value: &str) {
        if self.truncated || value.trim().is_empty() {
            return;
        }
        let remaining = Self::MAXIMUM_BYTES - Self::TRUNCATED_MESSAGE.len() - self.bytes;
        let separator = if self.text.is_empty() { "" } else { "\n" };
        if separator.len() + value.len() <= remaining {
            self.text.push_str(separator);
            self.text.push_str(value);
            self.bytes += separator.len() + value.len();
            return;
        }
        let mut remaining = remaining;
        for ch in separator.chars().chain(value.chars()) {
            if ch.len_utf8() > remaining {
                break;
            }
            self.text.push(ch);
            remaining -= ch.len_utf8();
        }
        self.truncated = true;
    }

    pub fn into_text(self) -> String {
        let mut text = self.text;
        if self.truncated {
            text.push_str(Self::TRUNCATED_MESSAGE);
        }
        text
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keeps_short_output_and_skips_blank_lines() {
        let mut details = BoundedCheckDetails::new();
        details.append_line("erste");
        details.append_line("   ");
        details.append_line("zweite");
        assert!(!details.is_truncated());
        assert_eq!(details.into_text(), "erste\nzweite");
    }

    #[test]
    fn truncates_at_limit_without_splitting_characters() {
        let mut details = BoundedCheckDetails::new();
        let line = "ä".repeat(1000);
        for _ in 0..200 {
            details.append_line(&line);
        }
        assert!(details.is_truncated());
        let text = details.into_text();
        assert!(text.len() <= BoundedCheckDetails::MAXIMUM_BYTES);
        assert!(text.ends_with("gekürzt."));
    }

    #[test]
    fn ignores_lines_after_truncation() {
        let mut details = BoundedCheckDetails::new();
        details.append_line(&"x".repeat(BoundedCheckDetails::MAXIMUM_BYTES));
        assert!(details.is_truncated());
        let before = details.text.clone();
        details.append_line("später");
        assert_eq!(details.text, before);
    }
}
