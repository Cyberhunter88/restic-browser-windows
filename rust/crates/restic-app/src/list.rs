//! Liste mit gebündelten Änderungsmeldungen (Gegenstück zu `BatchObservableCollection.cs`).
//!
//! Mehrere Elemente werden mit genau einer Meldung angehängt oder ersetzt. Die Oberfläche kann
//! daraus ein einzelnes Anhängen oder Zurücksetzen ableiten, statt je Element zu aktualisieren.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ListChange {
    /// Der Inhalt wurde ersetzt oder verkleinert; die Ansicht lädt neu.
    Reset,
    /// `count` Elemente wurden ab `start` angehängt; Auswahl und Scrollposition bleiben erhalten.
    Appended { start: usize, count: usize },
}

#[derive(Debug, Clone)]
pub struct BatchList<T> {
    items: Vec<T>,
}

impl<T> Default for BatchList<T> {
    fn default() -> Self {
        Self { items: Vec::new() }
    }
}

impl<T> BatchList<T> {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn len(&self) -> usize {
        self.items.len()
    }

    pub fn is_empty(&self) -> bool {
        self.items.is_empty()
    }

    pub fn as_slice(&self) -> &[T] {
        &self.items
    }

    pub fn iter(&self) -> std::slice::Iter<'_, T> {
        self.items.iter()
    }

    pub fn get(&self, index: usize) -> Option<&T> {
        self.items.get(index)
    }

    pub fn push(&mut self, item: T) -> ListChange {
        let start = self.items.len();
        self.items.push(item);
        ListChange::Appended { start, count: 1 }
    }

    /// Hängt alle Elemente mit einer Meldung an; bei leerer Eingabe gibt es keine Änderung.
    pub fn add_range(&mut self, items: impl IntoIterator<Item = T>) -> Option<ListChange> {
        let start = self.items.len();
        self.items.extend(items);
        let count = self.items.len() - start;
        (count > 0).then_some(ListChange::Appended { start, count })
    }

    /// Ersetzt den Inhalt mit genau einer Meldung.
    pub fn replace_with(&mut self, items: impl IntoIterator<Item = T>) -> ListChange {
        self.items.clear();
        self.items.extend(items);
        ListChange::Reset
    }

    pub fn clear(&mut self) -> Option<ListChange> {
        let had_items = !self.items.is_empty();
        self.items.clear();
        had_items.then_some(ListChange::Reset)
    }

    pub fn remove_where(&mut self, mut predicate: impl FnMut(&T) -> bool) -> Option<ListChange> {
        let before = self.items.len();
        self.items.retain(|item| !predicate(item));
        (self.items.len() != before).then_some(ListChange::Reset)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn range_is_appended_with_one_change_that_keeps_the_start_index() {
        let mut list = BatchList::new();
        list.push(1);
        let change = list.add_range([2, 3]);
        assert_eq!(change, Some(ListChange::Appended { start: 1, count: 2 }));
        assert_eq!(list.as_slice(), [1, 2, 3]);
        assert_eq!(list.add_range(Vec::<i32>::new()), None);
    }

    #[test]
    fn replacing_100k_items_is_one_reset() {
        let mut list = BatchList::new();
        list.push(0);
        assert_eq!(list.replace_with(0..100_000), ListChange::Reset);
        assert_eq!(list.len(), 100_000);
    }

    #[test]
    fn clear_and_remove_report_only_real_changes() {
        let mut list = BatchList::new();
        assert_eq!(list.clear(), None);
        list.add_range([1, 2, 3, 4]);
        assert_eq!(list.remove_where(|n| n % 2 == 0), Some(ListChange::Reset));
        assert_eq!(list.as_slice(), [1, 3]);
        assert_eq!(list.remove_where(|n| *n > 100), None);
        assert_eq!(list.clear(), Some(ListChange::Reset));
    }
}
