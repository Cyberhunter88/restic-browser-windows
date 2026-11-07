//! Inkrementelles Zerlegen von Restic-JSON-Ausgaben.
//!
//! Der Scanner arbeitet byteweise und hält nur den jeweils aktuellen Eintrag im Speicher.
//! Er prüft ausschließlich die Struktur; den Inhalt eines Eintrags deserialisiert der Aufrufer
//! mit `serde_json`. Weil nur ASCII-Strukturzeichen ausgewertet werden, spielen Chunk-Grenzen
//! mitten in UTF-8-Zeichen keine Rolle.

use serde::de::DeserializeOwned;

/// Strukturfehler: Die Ausgabe ist kein JSON-Array.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InvalidJson;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScanMode {
    /// Elemente eines JSON-Arrays auf oberster Ebene (`snapshots --json`).
    TopLevelArray,
    /// Einzelne Treffer aus `matches` in einem `find --json`-Array, auch innerhalb sehr großer Gruppen.
    FindMatches,
}

pub struct JsonScanner {
    mode: ScanMode,
    depth: usize,
    in_string: bool,
    escaped: bool,
    started: bool,
    finished: bool,
    track_string: bool,
    string_buf: Vec<u8>,
    last_key: Vec<u8>,
    pending_matches: bool,
    in_matches: bool,
    capture: Vec<u8>,
    capturing: bool,
}

impl JsonScanner {
    pub fn new(mode: ScanMode) -> Self {
        Self {
            mode,
            depth: 0,
            in_string: false,
            escaped: false,
            started: false,
            finished: false,
            track_string: false,
            string_buf: Vec::new(),
            last_key: Vec::new(),
            pending_matches: false,
            in_matches: false,
            capture: Vec::new(),
            capturing: false,
        }
    }

    /// Das Array wurde vollständig gelesen (schließende Klammer gesehen).
    pub fn is_complete(&self) -> bool {
        self.finished
    }

    /// Verarbeitet einen Chunk und hängt alle vollständig gelesenen Einträge an `out`.
    pub fn push(&mut self, bytes: &[u8], out: &mut Vec<Vec<u8>>) -> Result<(), InvalidJson> {
        for &byte in bytes {
            if self.in_string {
                self.string_byte(byte);
                continue;
            }
            match byte {
                b' ' | b'\t' | b'\r' | b'\n' => self.capture_byte(byte),
                b'"' => {
                    if self.depth == 0 {
                        return Err(InvalidJson);
                    }
                    self.in_string = true;
                    self.escaped = false;
                    self.track_string = self.mode == ScanMode::FindMatches && self.depth == 2;
                    self.string_buf.clear();
                    self.capture_byte(byte);
                }
                b'{' | b'[' => self.open(byte)?,
                b'}' | b']' => self.close(byte, out)?,
                b':' => {
                    if self.mode == ScanMode::FindMatches && self.depth == 2 {
                        self.pending_matches = self.last_key == b"matches";
                    }
                    self.capture_byte(byte);
                }
                _ => {
                    if self.depth == 0 {
                        return Err(InvalidJson);
                    }
                    self.capture_byte(byte);
                }
            }
        }
        Ok(())
    }

    fn capture_byte(&mut self, byte: u8) {
        if self.capturing {
            self.capture.push(byte);
        }
    }

    fn string_byte(&mut self, byte: u8) {
        self.capture_byte(byte);
        if self.escaped {
            self.escaped = false;
            if self.track_string {
                self.string_buf.push(byte);
            }
        } else if byte == b'\\' {
            self.escaped = true;
            if self.track_string {
                self.string_buf.push(byte);
            }
        } else if byte == b'"' {
            self.in_string = false;
            if self.track_string {
                self.last_key = std::mem::take(&mut self.string_buf);
            }
        } else if self.track_string {
            self.string_buf.push(byte);
        }
    }

    fn open(&mut self, byte: u8) -> Result<(), InvalidJson> {
        if self.depth == 0 {
            if byte != b'[' || self.finished {
                return Err(InvalidJson);
            }
            self.started = true;
        }
        self.depth += 1;
        match self.mode {
            ScanMode::TopLevelArray => {
                if self.depth == 2 {
                    self.start_capture();
                }
            }
            ScanMode::FindMatches => {
                if self.depth == 3 && byte == b'[' {
                    self.in_matches = self.pending_matches;
                    self.pending_matches = false;
                } else if self.depth == 4 && self.in_matches && byte == b'{' {
                    self.start_capture();
                }
            }
        }
        self.capture_byte(byte);
        Ok(())
    }

    fn close(&mut self, byte: u8, out: &mut Vec<Vec<u8>>) -> Result<(), InvalidJson> {
        if self.depth == 0 {
            return Err(InvalidJson);
        }
        self.capture_byte(byte);
        self.depth -= 1;
        let element_depth = match self.mode {
            ScanMode::TopLevelArray => 1,
            ScanMode::FindMatches => 3,
        };
        if self.capturing && self.depth == element_depth {
            self.capturing = false;
            out.push(std::mem::take(&mut self.capture));
        }
        if self.mode == ScanMode::FindMatches && byte == b']' && self.depth == 2 {
            self.in_matches = false;
        }
        if self.depth == 0 {
            self.finished = true;
        }
        Ok(())
    }

    fn start_capture(&mut self) {
        self.capturing = true;
        self.capture.clear();
    }
}

/// Liest JSON Lines tolerant: leere und ungültige Zeilen werden übersprungen.
pub fn parse_json_lines<T: DeserializeOwned>(text: &str) -> Vec<T> {
    text.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .filter_map(|line| serde_json::from_str(line).ok())
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::{BackupNode, FindSnapshotGroup, SnapshotInfo};

    fn scan(mode: ScanMode, input: &[u8], chunk: usize) -> (Vec<Vec<u8>>, bool) {
        let mut scanner = JsonScanner::new(mode);
        let mut out = Vec::new();
        for part in input.chunks(chunk) {
            scanner.push(part, &mut out).expect("gültige Struktur");
        }
        (out, scanner.is_complete())
    }

    #[test]
    fn splits_top_level_array_at_every_chunk_size() {
        let input = r#" [ {"a":"x}]\"y"} , {"b":[1,2,{"c":3}]}, [4] ] "#.as_bytes();
        for chunk in [1, 2, 3, 7, input.len()] {
            let (items, complete) = scan(ScanMode::TopLevelArray, input, chunk);
            assert!(complete, "chunk {chunk}");
            assert_eq!(items.len(), 3, "chunk {chunk}");
            assert_eq!(items[0], br#"{"a":"x}]\"y"}"#);
            assert_eq!(items[1], br#"{"b":[1,2,{"c":3}]}"#);
            assert_eq!(items[2], b"[4]");
        }
    }

    #[test]
    fn find_matches_are_emitted_before_group_ends() {
        let mut scanner = JsonScanner::new(ScanMode::FindMatches);
        let mut out = Vec::new();
        scanner
            .push(br#"[{"matches":[{"name":"first.txt"},"#, &mut out)
            .unwrap();
        assert_eq!(out.len(), 1);
        assert_eq!(out[0], br#"{"name":"first.txt"}"#);
        assert!(!scanner.is_complete());
    }

    #[test]
    fn find_ignores_other_arrays_and_handles_unicode_split_across_chunks() {
        let input = r#"[{"tags":[{"name":"nein"}],"matches":[{"name":"Übung/ä.txt","extra":{"x":[1]}}],"snapshot":"abc"},{"matches":[],"snapshot":"def"},{"matches":[{"name":"b"}]}]"#
            .as_bytes();
        for chunk in [1, 2, 5, input.len()] {
            let (items, complete) = scan(ScanMode::FindMatches, input, chunk);
            assert!(complete);
            assert_eq!(items.len(), 2, "chunk {chunk}");
            let first: BackupNode = serde_json::from_slice(&items[0]).unwrap();
            assert_eq!(first.name, "Übung/ä.txt");
        }
    }

    #[test]
    fn incomplete_output_is_not_complete() {
        let (_, complete) = scan(ScanMode::TopLevelArray, br#"[{"value":1},"#, 4);
        assert!(!complete);
        let (items, complete) = scan(ScanMode::TopLevelArray, b"", 4);
        assert!(!complete && items.is_empty());
    }

    #[test]
    fn rejects_non_array_output() {
        for input in [&b"!"[..], b"{\"a\":1}", b"Fatal: Fehler", b"[1] x", b"]"] {
            let mut scanner = JsonScanner::new(ScanMode::TopLevelArray);
            assert_eq!(
                scanner.push(input, &mut Vec::new()),
                Err(InvalidJson),
                "{input:?}"
            );
        }
    }

    #[test]
    fn json_lines_skip_unknown_and_invalid_lines() {
        let text = "{\"message_type\":\"snapshot\",\"id\":\"abc\"}\nnot-json\n\n{\"message_type\":\"node\",\"name\":\"Datei.txt\",\"path\":\"/Datei.txt\",\"type\":\"file\",\"size\":15,\"extra\":true}";
        let nodes: Vec<BackupNode> = parse_json_lines(text);
        assert_eq!(nodes.len(), 2);
        assert_eq!(nodes[1].name, "Datei.txt");
    }

    // --- Fixtures von restic 0.19.1 ---

    fn fixture(name: &str) -> Vec<u8> {
        let path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../fixtures/restic-0.19.1/");
        std::fs::read(format!("{path}{name}")).unwrap_or_else(|e| panic!("Fixture {name}: {e}"))
    }

    #[test]
    fn fixture_snapshots_parse() {
        let (items, complete) = scan(ScanMode::TopLevelArray, &fixture("snapshots.json"), 7);
        assert!(complete);
        assert_eq!(items.len(), 2);
        let snapshot: SnapshotInfo = serde_json::from_slice(&items[1]).unwrap();
        assert_eq!(snapshot.hostname, "fixture-host");
        assert_eq!(snapshot.tags, ["fixture"]);
        assert_eq!(snapshot.paths, ["/fixture/src"]);
        assert!(snapshot.time.is_some());
        assert_eq!(snapshot.display_id().len(), 8);
        assert!(snapshot.summary.is_some());
    }

    #[test]
    fn fixture_find_parses_matches_per_snapshot() {
        let (items, complete) = scan(ScanMode::FindMatches, &fixture("find-a.json"), 3);
        assert!(complete);
        assert_eq!(items.len(), 2, "ein Treffer je Snapshot");
        let node: BackupNode = serde_json::from_slice(&items[0]).unwrap();
        assert_eq!(node.node_type, "file");
        assert!(node.path.ends_with("docs/a.txt"));
        let groups: Vec<FindSnapshotGroup> = serde_json::from_slice(&fixture("find-a.json")).unwrap();
        assert_eq!(groups.len(), 2);
        assert_eq!(groups[0].matches.len(), 1);
    }

    #[test]
    fn fixture_find_without_hits_is_complete_and_empty() {
        let (items, complete) = scan(ScanMode::FindMatches, &fixture("find-empty.json"), 1);
        assert!(complete);
        assert!(items.is_empty());
    }

    #[test]
    fn fixture_ls_lines_parse_with_snapshot_header_and_nodes() {
        let text = String::from_utf8(fixture("ls-snapshot.jsonl")).unwrap();
        let nodes: Vec<BackupNode> = parse_json_lines(&text);
        assert!(nodes.iter().any(|n| n.is_directory() && n.path.ends_with("docs")));
        assert!(
            nodes
                .iter()
                .any(|n| n.node_type == "file" && n.name == "über.txt")
        );
    }

    #[test]
    fn fixture_restore_progress_and_preview_parse() {
        use crate::models::RestoreProgress;
        let restore = String::from_utf8(fixture("restore.jsonl")).unwrap();
        let items: Vec<RestoreProgress> = parse_json_lines(&restore);
        let summary = items
            .iter()
            .find(|i| i.message_type == "summary")
            .expect("Zusammenfassung");
        assert_eq!(summary.total_files, 1);
        assert!(summary.files_restored >= 1);

        let preview = String::from_utf8(fixture("restore-preview.jsonl")).unwrap();
        let items: Vec<RestoreProgress> = parse_json_lines(&preview);
        let verbose: Vec<_> = items
            .iter()
            .filter(|i| i.message_type == "verbose_status")
            .collect();
        assert!(
            verbose
                .iter()
                .any(|i| i.action == "restored" && i.item.ends_with("a.txt") && i.size == 15)
        );
    }

    #[test]
    fn fixture_errors_match_exit_code_mapping() {
        let wrong_pw = String::from_utf8(fixture("error-wrongpassword.txt")).unwrap();
        assert!(wrong_pw.contains("wrong password"));
        assert_eq!(
            crate::repository::create_exit_error(12, &wrong_pw).to_string(),
            "Das Repository-Passwort ist falsch."
        );
        let no_repo = String::from_utf8(fixture("error-norepo.txt")).unwrap();
        assert!(no_repo.contains("repository does not exist"));
        assert!(
            crate::repository::create_exit_error(10, &no_repo)
                .to_string()
                .contains("nicht gefunden")
        );
        assert!(
            !no_repo.contains("admin"),
            "Fixtures dürfen keine echten Namen enthalten"
        );
    }
}
