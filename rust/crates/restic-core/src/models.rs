//! JSON-Modelle der Restic-Ausgaben. Alle Felder sind tolerant: fehlende Felder und `null`
//! ergeben Standardwerte, unbekannte Felder werden ignoriert.

use chrono::{DateTime, FixedOffset};
use serde::{Deserialize, Deserializer};

/// Liest `null` wie ein fehlendes Feld.
fn null_default<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: Deserialize<'de> + Default,
{
    Ok(Option::<T>::deserialize(deserializer)?.unwrap_or_default())
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct SnapshotSummary {
    #[serde(default, deserialize_with = "null_default")]
    pub total_bytes_processed: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub total_files_processed: i64,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct SnapshotInfo {
    #[serde(default)]
    pub time: Option<DateTime<FixedOffset>>,
    #[serde(default, deserialize_with = "null_default")]
    pub hostname: String,
    #[serde(default, deserialize_with = "null_default")]
    pub username: String,
    #[serde(default, deserialize_with = "null_default")]
    pub paths: Vec<String>,
    #[serde(default, deserialize_with = "null_default")]
    pub tags: Vec<String>,
    #[serde(default, deserialize_with = "null_default")]
    pub id: String,
    #[serde(default, deserialize_with = "null_default")]
    pub short_id: String,
    #[serde(default)]
    pub summary: Option<SnapshotSummary>,
}

impl SnapshotInfo {
    /// Kurze ID für die Anzeige: `short_id`, sonst die ersten 8 Zeichen der ID.
    pub fn display_id(&self) -> String {
        if self.short_id.trim().is_empty() {
            self.id.chars().take(8).collect()
        } else {
            self.short_id.clone()
        }
    }

    pub fn path_text(&self) -> String {
        self.paths.join(", ")
    }

    pub fn tag_text(&self) -> String {
        if self.tags.is_empty() {
            "Keine Tags".to_string()
        } else {
            self.tags.join(", ")
        }
    }

    pub fn size_text(&self) -> String {
        format_bytes(self.summary.as_ref().map_or(0, |s| s.total_bytes_processed))
    }
}

/// Knoten aus `ls` oder `find` (Datei, Ordner oder Verknüpfung).
#[derive(Debug, Clone, Default, Deserialize)]
pub struct BackupNode {
    #[serde(default, deserialize_with = "null_default")]
    pub message_type: String,
    #[serde(default, deserialize_with = "null_default")]
    pub struct_type: String,
    #[serde(default, deserialize_with = "null_default")]
    pub name: String,
    #[serde(default, rename = "type", deserialize_with = "null_default")]
    pub node_type: String,
    #[serde(default, deserialize_with = "null_default")]
    pub path: String,
    #[serde(default, deserialize_with = "null_default")]
    pub size: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub permissions: String,
    #[serde(default, rename = "mtime")]
    pub modified: Option<DateTime<FixedOffset>>,
}

impl BackupNode {
    pub fn is_directory(&self) -> bool {
        self.node_type.eq_ignore_ascii_case("dir")
    }

    pub fn type_text(&self) -> &str {
        match self.node_type.as_str() {
            "dir" => "Ordner",
            "file" => "Datei",
            "symlink" => "Verknüpfung",
            other => other,
        }
    }

    pub fn size_text(&self) -> String {
        if self.is_directory() {
            "—".to_string()
        } else {
            format_bytes(self.size)
        }
    }
}

/// Eine Gruppe aus `restic find --json`: Treffer eines Snapshots.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct FindSnapshotGroup {
    #[serde(default, deserialize_with = "null_default")]
    pub matches: Vec<BackupNode>,
    #[serde(default, deserialize_with = "null_default")]
    pub snapshot: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RestoreProgress {
    #[serde(default, deserialize_with = "null_default")]
    pub message_type: String,
    #[serde(default, deserialize_with = "null_default")]
    pub percent_done: f64,
    #[serde(default, deserialize_with = "null_default")]
    pub total_files: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub files_restored: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub files_skipped: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub total_bytes: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub bytes_restored: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub action: String,
    #[serde(default, deserialize_with = "null_default")]
    pub item: String,
    #[serde(default, deserialize_with = "null_default")]
    pub size: i64,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct ResticVersion {
    #[serde(default, deserialize_with = "null_default")]
    pub version: String,
    #[serde(default, rename = "go_arch", deserialize_with = "null_default")]
    pub architecture: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RepositoryStats {
    #[serde(default, deserialize_with = "null_default")]
    pub total_size: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub total_file_count: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub total_blob_count: i64,
    #[serde(default, deserialize_with = "null_default")]
    pub snapshots_count: i32,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct EnvironmentEntry {
    pub name: String,
    pub value: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OverwritePolicy {
    Never,
    IfNewer,
    IfChanged,
    Always,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CheckMode {
    Quick,
    Full,
}

#[derive(Debug, Clone)]
pub struct RestoreRequest {
    pub snapshot_id: String,
    pub target: String,
    pub includes: Vec<String>,
    pub overwrite: OverwritePolicy,
}

#[derive(Debug, Clone)]
pub struct MountRequest {
    pub snapshot_id: Option<String>,
    pub mount_point: String,
}

/// Ein Abschnitt der Pfadleiste.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PathSegment {
    pub name: String,
    pub path: String,
    pub display: String,
}

impl PathSegment {
    pub fn build(path: &str) -> Vec<PathSegment> {
        if path.starts_with("Suchergebnisse:") {
            return vec![PathSegment {
                name: path.into(),
                path: path.into(),
                display: path.into(),
            }];
        }
        let mut result = vec![PathSegment {
            name: "Snapshot".into(),
            path: "/".into(),
            display: "Snapshot".into(),
        }];
        let mut current = String::new();
        for part in path.split('/').filter(|part| !part.is_empty()) {
            current.push('/');
            current.push_str(part);
            result.push(PathSegment {
                name: part.into(),
                path: current.clone(),
                display: format!("› {part}"),
            });
        }
        result
    }
}

/// Größenangabe mit deutschem Dezimalkomma, z. B. `2 KB` oder `1,5 MB`.
pub fn format_bytes(bytes: i64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut value = bytes as f64;
    let mut index = 0;
    while value >= 1024.0 && index < UNITS.len() - 1 {
        value /= 1024.0;
        index += 1;
    }
    let text = format!("{value:.1}");
    let text = text.strip_suffix(".0").unwrap_or(&text).replace('.', ",");
    format!("{text} {}", UNITS[index])
}

/// Relative Zeitangabe wie „vor 3 Tagen“.
pub fn format_relative_time(time: DateTime<FixedOffset>, now: DateTime<FixedOffset>) -> String {
    let span = now - time;
    if span < chrono::Duration::minutes(1) {
        return "gerade eben".to_string();
    }
    let plural = |count: i64, one: &str, many: &str| {
        if count == 1 {
            format!("vor 1 {one}")
        } else {
            format!("vor {count} {many}")
        }
    };
    if span < chrono::Duration::hours(1) {
        return plural(span.num_minutes(), "Minute", "Minuten");
    }
    if span < chrono::Duration::days(1) {
        return plural(span.num_hours(), "Stunde", "Stunden");
    }
    if span < chrono::Duration::days(30) {
        return plural(span.num_days(), "Tag", "Tagen");
    }
    if span < chrono::Duration::days(365) {
        return plural(span.num_days() / 30, "Monat", "Monaten");
    }
    plural(span.num_days() / 365, "Jahr", "Jahren")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(text: &str) -> DateTime<FixedOffset> {
        DateTime::parse_from_rfc3339(text).unwrap()
    }

    #[test]
    fn snapshot_json_tolerates_additional_fields() {
        let json = r#"{"time":"2026-01-01T12:00:00Z","id":"abcdef123456","hostname":"pc","future_field":42,"summary":{"total_bytes_processed":2048}}"#;
        let value: SnapshotInfo = serde_json::from_str(json).unwrap();
        assert_eq!(value.display_id(), "abcdef12");
        assert_eq!(value.size_text(), "2 KB");
    }

    #[test]
    fn null_and_missing_fields_become_defaults() {
        let value: SnapshotInfo =
            serde_json::from_str(r#"{"paths":null,"tags":null,"hostname":null}"#).unwrap();
        assert!(value.paths.is_empty());
        assert_eq!(value.tag_text(), "Keine Tags");
        assert_eq!(value.hostname, "");
    }

    #[test]
    fn backup_node_maps_type_and_text() {
        let node: BackupNode = serde_json::from_str(
            r#"{"message_type":"node","name":"Datei.txt","path":"/Datei.txt","type":"file","size":15,"extra":true}"#,
        )
        .unwrap();
        assert_eq!(node.type_text(), "Datei");
        assert!(!node.is_directory());
        assert_eq!(node.size_text(), "15 B");
        let dir: BackupNode = serde_json::from_str(r#"{"type":"dir","name":"x"}"#).unwrap();
        assert_eq!(dir.size_text(), "—");
        assert_eq!(dir.type_text(), "Ordner");
    }

    #[test]
    fn formats_bytes_with_decimal_comma() {
        assert_eq!(format_bytes(0), "0 B");
        assert_eq!(format_bytes(1536), "1,5 KB");
        assert_eq!(format_bytes(2048), "2 KB");
        assert_eq!(format_bytes(5 * 1024 * 1024 * 1024), "5 GB");
    }

    #[test]
    fn relative_time_matches_csharp_behaviour() {
        let now = at("2026-11-04T12:00:00Z");
        let ago = |duration: chrono::Duration| format_relative_time(now - duration, now);
        assert_eq!(ago(chrono::Duration::seconds(20)), "gerade eben");
        assert_eq!(ago(chrono::Duration::minutes(5)), "vor 5 Minuten");
        assert_eq!(ago(chrono::Duration::hours(1)), "vor 1 Stunde");
        assert_eq!(ago(chrono::Duration::days(3)), "vor 3 Tagen");
        assert_eq!(ago(chrono::Duration::days(65)), "vor 2 Monaten");
        assert_eq!(ago(chrono::Duration::days(400)), "vor 1 Jahr");
        assert_eq!(ago(-chrono::Duration::minutes(10)), "gerade eben");
    }

    #[test]
    fn path_segments_are_split() {
        assert_eq!(PathSegment::build("/").len(), 1);
        let deep = PathSegment::build("/home/user/docs");
        assert_eq!(deep.len(), 4);
        assert_eq!(deep[1].path, "/home");
        assert_eq!(deep[3].path, "/home/user/docs");
        assert_eq!(deep[3].display, "› docs");
        let search = PathSegment::build("Suchergebnisse: *.txt");
        assert_eq!(search.len(), 1);
        assert_eq!(search[0].path, "Suchergebnisse: *.txt");
    }
}
