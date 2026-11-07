//! Verlauf der besuchten Verzeichnisse einer Sitzung (ohne Repository- oder UI-Abhängigkeit).

#[derive(Debug, Default)]
pub struct NavigationHistory {
    back: Vec<String>,
    forward: Vec<String>,
}

impl NavigationHistory {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn can_go_back(&self) -> bool {
        !self.back.is_empty()
    }

    pub fn can_go_forward(&self) -> bool {
        !self.forward.is_empty()
    }

    pub fn push_back(&mut self, path: impl Into<String>) {
        self.back.push(path.into());
    }

    pub fn push_forward(&mut self, path: impl Into<String>) {
        self.forward.push(path.into());
    }

    pub fn pop_back(&mut self) -> Option<String> {
        self.back.pop()
    }

    pub fn pop_forward(&mut self) -> Option<String> {
        self.forward.pop()
    }

    pub fn clear_forward(&mut self) {
        self.forward.clear();
    }

    pub fn clear(&mut self) {
        self.back.clear();
        self.forward.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn back_and_forward_are_independent_stacks() {
        let mut history = NavigationHistory::new();
        assert!(!history.can_go_back() && !history.can_go_forward());
        history.push_back("/a");
        history.push_back("/b");
        history.push_forward("/c");
        assert_eq!(history.pop_back().as_deref(), Some("/b"));
        assert_eq!(history.pop_forward().as_deref(), Some("/c"));
        assert!(history.can_go_back() && !history.can_go_forward());
        history.push_forward("/x");
        history.clear_forward();
        assert!(!history.can_go_forward());
        history.clear();
        assert!(!history.can_go_back());
        assert_eq!(history.pop_back(), None);
    }
}
