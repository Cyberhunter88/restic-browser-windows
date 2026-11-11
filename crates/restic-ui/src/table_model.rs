//! Zustand der Dateitabelle ohne Zeichenlogik: Sortierung, Auswahl (Strg, Umschalt, Pfeiltasten,
//! Alles) und Zusammenfassung. Rein und damit testbar; die Zeichnung steht in `file_table.rs`.

use restic_core::models::{BackupNode, format_bytes};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SortColumn {
    Name,
    Kind,
    Size,
    Modified,
}

#[derive(Debug)]
pub struct TableModel {
    /// Anzeigereihenfolge: Index in die Knotenliste der Sitzung.
    order: Vec<usize>,
    selected: Vec<bool>,
    /// Anzahl gesetzter Einträge in `selected`; erspart das Zählen in jedem Bild.
    selected_count: usize,
    anchor: usize,
    cursor: usize,
    sort: SortColumn,
    ascending: bool,
    selection_version: u64,
}

impl Default for TableModel {
    fn default() -> Self {
        Self {
            order: Vec::new(),
            selected: Vec::new(),
            selected_count: 0,
            anchor: 0,
            cursor: 0,
            sort: SortColumn::Name,
            ascending: true,
            selection_version: 0,
        }
    }
}

impl TableModel {
    pub fn sort_column(&self) -> SortColumn {
        self.sort
    }

    pub fn is_ascending(&self) -> bool {
        self.ascending
    }

    pub fn len(&self) -> usize {
        self.order.len()
    }

    pub fn is_empty(&self) -> bool {
        self.order.is_empty()
    }

    /// Wird bei jeder Änderung der Auswahl größer; die Oberfläche cacht damit die Zusammenfassung.
    pub fn selection_version(&self) -> u64 {
        self.selection_version
    }

    /// Index in die Knotenliste für eine Anzeigezeile.
    pub fn node_index(&self, row: usize) -> usize {
        self.order[row]
    }

    pub fn is_row_selected(&self, row: usize) -> bool {
        self.selected[self.order[row]]
    }

    pub fn cursor_row(&self) -> usize {
        self.cursor
    }

    /// Übernimmt eine neue Knotenliste: die Auswahl wird zurückgesetzt, die Sortierung bleibt.
    pub fn rebuild(&mut self, nodes: &[BackupNode]) {
        self.order = (0..nodes.len()).collect();
        self.selected = vec![false; nodes.len()];
        self.selected_count = 0;
        self.anchor = 0;
        self.cursor = 0;
        self.selection_version += 1;
        self.resort(nodes);
    }

    /// Klick auf eine Kopfzeile: gleiche Spalte kehrt die Richtung um, neue Spalte sortiert aufsteigend.
    pub fn toggle_sort(&mut self, column: SortColumn, nodes: &[BackupNode]) {
        self.ascending = if self.sort == column {
            !self.ascending
        } else {
            true
        };
        self.sort = column;
        self.resort(nodes);
    }

    fn resort(&mut self, nodes: &[BackupNode]) {
        let (column, ascending) = (self.sort, self.ascending);
        // Ausgewählte Knoten bleiben beim Umsortieren ausgewählt (Auswahl hängt am Knoten, nicht an der Zeile).
        let direction = |ordering: std::cmp::Ordering| if ascending { ordering } else { ordering.reverse() };
        match column {
            SortColumn::Name => self.order.sort_by_cached_key(|&i| {
                (
                    std::cmp::Reverse(nodes[i].is_directory()),
                    NameKey(nodes[i].display_name().to_lowercase(), ascending),
                )
            }),
            SortColumn::Kind => self.order.sort_by(|&a, &b| {
                nodes[b]
                    .is_directory()
                    .cmp(&nodes[a].is_directory())
                    .then_with(|| direction(nodes[a].type_text().cmp(nodes[b].type_text())))
            }),
            SortColumn::Size => self.order.sort_by(|&a, &b| {
                nodes[b]
                    .is_directory()
                    .cmp(&nodes[a].is_directory())
                    .then_with(|| direction(nodes[a].size.cmp(&nodes[b].size)))
            }),
            SortColumn::Modified => self.order.sort_by(|&a, &b| {
                nodes[b]
                    .is_directory()
                    .cmp(&nodes[a].is_directory())
                    .then_with(|| direction(nodes[a].modified.cmp(&nodes[b].modified)))
            }),
        }
        self.anchor = 0;
        self.cursor = 0;
    }

    /// Klick auf eine Anzeigezeile mit Umschalttasten.
    pub fn click(&mut self, row: usize, ctrl: bool, shift: bool) {
        if row >= self.order.len() {
            return;
        }
        if shift {
            self.select_range(self.anchor, row, ctrl);
        } else if ctrl {
            let node = self.order[row];
            self.selected[node] = !self.selected[node];
            if self.selected[node] {
                self.selected_count += 1;
            } else {
                self.selected_count -= 1;
            }
            self.anchor = row;
        } else {
            self.select_range(row, row, false);
            self.anchor = row;
        }
        self.cursor = row;
        self.selection_version += 1;
    }

    /// Pfeiltasten: Cursor bewegen, mit Umschalttaste die Auswahl erweitern.
    pub fn move_cursor(&mut self, delta: i64, extend: bool) {
        if self.order.is_empty() {
            return;
        }
        let target = (self.cursor as i64 + delta).clamp(0, self.order.len() as i64 - 1) as usize;
        if extend {
            self.select_range(self.anchor, target, false);
        } else {
            self.select_range(target, target, false);
            self.anchor = target;
        }
        self.cursor = target;
        self.selection_version += 1;
    }

    /// Setzt den Cursor auf eine Zeile (Pos1, Ende, Sprung beim Tippen); zu große Werte meinen die letzte Zeile.
    pub fn move_cursor_to(&mut self, row: usize, extend: bool) {
        if self.order.is_empty() {
            return;
        }
        let target = row.min(self.order.len() - 1);
        self.move_cursor(target as i64 - self.cursor as i64, extend);
    }

    /// Erste Anzeigezeile ab `start` (umlaufend), deren Name mit `prefix` beginnt, ohne Beachtung der Schreibweise.
    pub fn find_prefix(&self, nodes: &[BackupNode], prefix: &str, start: usize) -> Option<usize> {
        let prefix = prefix.to_lowercase();
        let len = self.order.len();
        (0..len).map(|offset| (start + offset) % len).find(|&row| {
            nodes
                .get(self.order[row])
                .is_some_and(|node| node.display_name().to_lowercase().starts_with(&prefix))
        })
    }

    pub fn select_all(&mut self) {
        self.selected.iter_mut().for_each(|s| *s = true);
        self.selected_count = self.selected.len();
        self.selection_version += 1;
    }

    pub fn clear_selection(&mut self) {
        self.selected.iter_mut().for_each(|s| *s = false);
        self.selected_count = 0;
        self.selection_version += 1;
    }

    fn select_range(&mut self, from: usize, to: usize, keep_others: bool) {
        let (low, high) = (from.min(to), from.max(to));
        if !keep_others {
            self.selected.iter_mut().for_each(|s| *s = false);
            self.selected_count = 0;
        }
        for row in low..=high {
            let node = self.order[row];
            if !self.selected[node] {
                self.selected[node] = true;
                self.selected_count += 1;
            }
        }
    }

    /// Indizes der ausgewählten Knoten in Anzeigereihenfolge.
    pub fn selected_nodes(&self) -> Vec<usize> {
        self.order.iter().copied().filter(|&i| self.selected[i]).collect()
    }

    pub fn selected_count(&self) -> usize {
        self.selected_count
    }

    pub fn summary(&self, nodes: &[BackupNode]) -> String {
        let selected: Vec<&BackupNode> = self
            .selected
            .iter()
            .enumerate()
            .filter(|(_, s)| **s)
            .map(|(i, _)| &nodes[i])
            .collect();
        if selected.is_empty() {
            return "Nichts ausgewählt".to_string();
        }
        let folders = selected.iter().filter(|n| n.is_directory()).count();
        let bytes: i64 = selected
            .iter()
            .filter(|n| !n.is_directory())
            .map(|n| n.size)
            .sum();
        format!(
            "{} Element(e) ausgewählt ({folders} Ordner, {})",
            selected.len(),
            format_bytes(bytes)
        )
    }
}

/// Sortierschlüssel für Namen, der die Richtung berücksichtigt (cached-key-Sortierung kennt keine Umkehr).
#[derive(PartialEq, Eq)]
struct NameKey(String, bool);

impl PartialOrd for NameKey {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for NameKey {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        let ordering = self.0.cmp(&other.0);
        if self.1 { ordering } else { ordering.reverse() }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn node(name: &str, is_dir: bool, size: i64) -> BackupNode {
        BackupNode {
            name: name.into(),
            path: format!("/{name}"),
            node_type: if is_dir { "dir" } else { "file" }.into(),
            size,
            ..BackupNode::default()
        }
    }

    fn model(nodes: &[BackupNode]) -> TableModel {
        let mut model = TableModel::default();
        model.rebuild(nodes);
        model
    }

    fn names(model: &TableModel, nodes: &[BackupNode]) -> Vec<String> {
        (0..model.len())
            .map(|row| nodes[model.node_index(row)].name.clone())
            .collect()
    }

    fn sample() -> Vec<BackupNode> {
        vec![
            node("zeta.txt", false, 5),
            node("Alpha.txt", false, 50),
            node("docs", true, 0),
            node("beta", true, 0),
            node("mitte.bin", false, 10),
        ]
    }

    #[test]
    fn folders_come_first_and_names_sort_case_insensitively() {
        let nodes = sample();
        let model = model(&nodes);
        assert_eq!(
            names(&model, &nodes),
            ["beta", "docs", "Alpha.txt", "mitte.bin", "zeta.txt"]
        );
    }

    #[test]
    fn toggling_the_same_column_reverses_but_folders_stay_first() {
        let nodes = sample();
        let mut model = model(&nodes);
        model.toggle_sort(SortColumn::Name, &nodes);
        assert!(!model.is_ascending());
        assert_eq!(
            names(&model, &nodes),
            ["docs", "beta", "zeta.txt", "mitte.bin", "Alpha.txt"]
        );
        model.toggle_sort(SortColumn::Size, &nodes);
        assert!(model.is_ascending(), "neue Spalte sortiert aufsteigend");
        assert_eq!(names(&model, &nodes)[2..], ["zeta.txt", "mitte.bin", "Alpha.txt"]);
        model.toggle_sort(SortColumn::Size, &nodes);
        assert_eq!(names(&model, &nodes)[2..], ["Alpha.txt", "mitte.bin", "zeta.txt"]);
    }

    #[test]
    fn plain_click_selects_one_row_ctrl_toggles_shift_selects_ranges() {
        let nodes = sample();
        let mut model = model(&nodes);
        model.click(1, false, false);
        assert_eq!(model.selected_count(), 1);
        model.click(3, true, false);
        assert_eq!(model.selected_count(), 2);
        model.click(3, true, false);
        assert_eq!(model.selected_count(), 1, "Strg schaltet um");
        model.click(4, false, true);
        assert_eq!(
            model.selected_count(),
            2,
            "Bereich vom Anker (Zeile 3) bis Zeile 4"
        );
    }

    #[test]
    fn shift_range_with_ctrl_keeps_other_selections() {
        let nodes = sample();
        let mut model = model(&nodes);
        model.click(0, false, false);
        model.click(3, true, false);
        model.click(4, true, true);
        assert_eq!(model.selected_nodes().len(), 3);
    }

    #[test]
    fn cursor_keys_move_and_extend() {
        let nodes = sample();
        let mut model = model(&nodes);
        model.click(1, false, false);
        model.move_cursor(1, false);
        assert_eq!((model.selected_count(), model.cursor_row()), (1, 2));
        model.move_cursor(2, true);
        assert_eq!(model.selected_count(), 3, "Anker Zeile 2 bis Zeile 4");
        model.move_cursor(-100, false);
        assert_eq!((model.cursor_row(), model.selected_count()), (0, 1));
        model.move_cursor(100, false);
        assert_eq!(model.cursor_row(), 4);
    }

    #[test]
    fn cursor_jumps_to_rows_and_to_names_by_prefix() {
        let nodes = sample();
        let mut model = model(&nodes);
        // Anzeige: beta, docs, Alpha.txt, mitte.bin, zeta.txt
        model.move_cursor_to(usize::MAX, false);
        assert_eq!((model.cursor_row(), model.selected_count()), (4, 1));
        model.move_cursor_to(0, true);
        assert_eq!((model.cursor_row(), model.selected_count()), (0, 5));
        assert_eq!(model.find_prefix(&nodes, "ALP", 0), Some(2));
        assert_eq!(model.find_prefix(&nodes, "b", 1), Some(0), "Suche läuft um");
        assert_eq!(model.find_prefix(&nodes, "x", 0), None);
        assert_eq!(TableModel::default().find_prefix(&[], "a", 3), None);
    }

    #[test]
    fn selection_follows_the_node_when_sorting() {
        let nodes = sample();
        let mut model = model(&nodes);
        let row = (0..model.len())
            .find(|&r| nodes[model.node_index(r)].name == "mitte.bin")
            .unwrap();
        model.click(row, false, false);
        model.toggle_sort(SortColumn::Size, &nodes);
        let selected = model.selected_nodes();
        assert_eq!(selected.len(), 1);
        assert_eq!(nodes[selected[0]].name, "mitte.bin");
    }

    #[test]
    fn select_all_clear_and_summary() {
        let nodes = sample();
        let mut model = model(&nodes);
        assert_eq!(model.summary(&nodes), "Nichts ausgewählt");
        model.select_all();
        assert_eq!(model.selected_count(), 5);
        assert_eq!(model.summary(&nodes), "5 Element(e) ausgewählt (2 Ordner, 65 B)");
        let version = model.selection_version();
        model.clear_selection();
        assert!(model.selection_version() > version);
        assert_eq!(model.selected_count(), 0);
    }

    #[test]
    fn rebuild_resets_the_selection_but_keeps_the_sort() {
        let nodes = sample();
        let mut model = model(&nodes);
        model.toggle_sort(SortColumn::Size, &nodes);
        model.select_all();
        model.rebuild(&nodes);
        assert_eq!(model.selected_count(), 0);
        assert_eq!(model.sort_column(), SortColumn::Size);
    }

    #[test]
    fn out_of_range_clicks_and_empty_lists_are_safe() {
        let mut model = TableModel::default();
        model.click(3, false, false);
        model.move_cursor(1, true);
        model.select_all();
        assert!(model.is_empty() && model.selected_nodes().is_empty());
    }

    #[test]
    fn hundred_thousand_rows_select_and_sort_quickly() {
        let nodes: Vec<BackupNode> = (0..100_000)
            .map(|i| node(&format!("Datei {i}"), i % 25 == 0, i))
            .collect();
        let started = std::time::Instant::now();
        let mut model = model(&nodes);
        model.select_all();
        model.toggle_sort(SortColumn::Size, &nodes);
        model.click(99_999, false, true);
        println!("METRIK Tabellenmodell 100k: {} ms", started.elapsed().as_millis());
        assert_eq!(model.len(), 100_000);
    }
}
