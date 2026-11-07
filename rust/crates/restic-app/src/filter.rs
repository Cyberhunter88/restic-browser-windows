//! Filter der Snapshot-Liste (Gegenstück zu `SnapshotFilter.cs`): Text, Host, Tag und
//! „nur die neuesten je Host und Pfad“, auch inkrementell während des Ladens.

use std::collections::HashMap;

use restic_core::models::SnapshotInfo;

pub const ALL_HOSTS: &str = "Alle Hosts";
pub const ALL_TAGS: &str = "Alle Tags";

/// Aktuelle Filtereinstellungen (Texte unverändert; die Auswertung normalisiert selbst).
#[derive(Debug, Clone, Copy, Default)]
pub struct FilterCriteria<'a> {
    pub text: &'a str,
    pub host: &'a str,
    pub tag: &'a str,
    pub only_latest: bool,
}

/// Ergebnis von [`SnapshotFilter::track_latest`].
#[derive(Debug, Clone)]
pub enum LatestUpdate {
    /// Es gibt bereits einen gleich neuen oder neueren Snapshot in der Gruppe.
    Unchanged,
    /// Erster Snapshot der Gruppe.
    New,
    /// Neuer Snapshot, der den bisher neuesten der Gruppe ablöst.
    Replaced(SnapshotInfo),
}

#[derive(Debug, Default)]
pub struct SnapshotFilter {
    latest_by_group: HashMap<String, SnapshotInfo>,
}

fn group_key(snapshot: &SnapshotInfo) -> String {
    format!("{}\n{}", snapshot.hostname, snapshot.path_text()).to_lowercase()
}

impl SnapshotFilter {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn clear(&mut self) {
        self.latest_by_group.clear();
    }

    /// Merkt sich je Host und Pfade den neuesten Snapshot.
    pub fn track_latest(&mut self, snapshot: &SnapshotInfo) -> LatestUpdate {
        let key = group_key(snapshot);
        match self.latest_by_group.get(&key) {
            Some(current) if current.time >= snapshot.time => LatestUpdate::Unchanged,
            Some(_) => {
                let replaced = self
                    .latest_by_group
                    .insert(key, snapshot.clone())
                    .expect("vorhanden");
                LatestUpdate::Replaced(replaced)
            }
            None => {
                self.latest_by_group.insert(key, snapshot.clone());
                LatestUpdate::New
            }
        }
    }

    pub fn matches(snapshot: &SnapshotInfo, criteria: &FilterCriteria<'_>) -> bool {
        let text = criteria.text.trim();
        let text_matches = text.is_empty() || {
            let haystack = [
                snapshot.hostname.as_str(),
                &snapshot.path_text(),
                &snapshot.tag_text(),
                &snapshot.display_id(),
            ]
            .join("\n")
            .to_lowercase();
            haystack.contains(&text.to_lowercase())
        };
        let host = criteria.host;
        let host_matches = host.trim().is_empty()
            || host == ALL_HOSTS
            || snapshot.hostname.eq_ignore_ascii_case(host)
            || snapshot.hostname.to_lowercase() == host.to_lowercase();
        let tag = criteria.tag;
        let tag_matches = tag.trim().is_empty()
            || tag == ALL_TAGS
            || snapshot
                .tags
                .iter()
                .any(|t| t.to_lowercase() == tag.to_lowercase());
        text_matches && host_matches && tag_matches
    }

    /// Wendet den Filter an. Ist `snapshots_are_sorted` gesetzt, müssen die Snapshots absteigend nach
    /// Zeit vorliegen; dann genügt für „nur die neuesten“ das erste Auftreten je Gruppe.
    pub fn apply(
        &self,
        snapshots: &[SnapshotInfo],
        criteria: &FilterCriteria<'_>,
        snapshots_are_sorted: bool,
    ) -> Vec<SnapshotInfo> {
        let latest: Vec<&SnapshotInfo> = if !criteria.only_latest {
            snapshots.iter().collect()
        } else if snapshots_are_sorted {
            let mut seen = std::collections::HashSet::new();
            snapshots.iter().filter(|s| seen.insert(group_key(s))).collect()
        } else {
            let mut order: Vec<String> = Vec::new();
            let mut by_group: HashMap<String, &SnapshotInfo> = HashMap::new();
            for snapshot in snapshots {
                let key = group_key(snapshot);
                match by_group.get(&key) {
                    Some(current) if snapshot.time <= current.time => {}
                    Some(_) => {
                        by_group.insert(key, snapshot);
                    }
                    None => {
                        order.push(key.clone());
                        by_group.insert(key, snapshot);
                    }
                }
            }
            let mut values: Vec<&SnapshotInfo> = order.iter().map(|key| by_group[key]).collect();
            values.sort_by_key(|snapshot| std::cmp::Reverse(snapshot.time));
            values
        };
        latest
            .into_iter()
            .filter(|s| Self::matches(s, criteria))
            .cloned()
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::DateTime;

    fn snapshot(id: &str, host: &str, path: &str, tags: &[&str], minute: i64) -> SnapshotInfo {
        SnapshotInfo {
            id: id.into(),
            hostname: host.into(),
            paths: vec![path.into()],
            tags: tags.iter().map(|t| t.to_string()).collect(),
            time: Some(DateTime::from_timestamp(minute * 60, 0).unwrap().fixed_offset()),
            ..SnapshotInfo::default()
        }
    }

    fn criteria<'a>(text: &'a str, host: &'a str, tag: &'a str, only_latest: bool) -> FilterCriteria<'a> {
        FilterCriteria {
            text,
            host,
            tag,
            only_latest,
        }
    }

    #[test]
    fn latest_is_chosen_even_for_unsorted_input() {
        let snapshots = [
            snapshot("alt", "h", "/", &[], 0),
            snapshot("neu", "h", "/", &[], 10),
        ];
        let result = SnapshotFilter::new().apply(&snapshots, &criteria("", "", "", true), false);
        assert_eq!(result.len(), 1);
        assert_eq!(result[0].id, "neu");
    }

    #[test]
    fn sorted_input_takes_the_first_per_group_and_groups_ignore_case() {
        let snapshots = [
            snapshot("neu", "Host", "/a", &[], 10),
            snapshot("alt", "host", "/a", &[], 0),
            snapshot("b", "host", "/b", &[], 5),
        ];
        let result = SnapshotFilter::new().apply(&snapshots, &criteria("", "", "", true), true);
        let ids: Vec<_> = result.iter().map(|s| s.id.as_str()).collect();
        assert_eq!(ids, ["neu", "b"]);
    }

    #[test]
    fn text_host_and_tag_filters_ignore_case() {
        let s = snapshot("abcdef123456", "Arbeits-PC", "/home/anna", &["Wichtig"], 1);
        assert!(SnapshotFilter::matches(&s, &criteria("ANNA", "", "", false)));
        assert!(
            SnapshotFilter::matches(&s, &criteria("abcdef12", "", "", false)),
            "Kurz-ID"
        );
        assert!(
            SnapshotFilter::matches(&s, &criteria("  wichtig ", "", "", false)),
            "Tags sind durchsuchbar"
        );
        assert!(!SnapshotFilter::matches(&s, &criteria("nicht da", "", "", false)));
        assert!(SnapshotFilter::matches(
            &s,
            &criteria("", "arbeits-pc", "", false)
        ));
        assert!(SnapshotFilter::matches(
            &s,
            &criteria("", ALL_HOSTS, ALL_TAGS, false)
        ));
        assert!(!SnapshotFilter::matches(&s, &criteria("", "anderer", "", false)));
        assert!(SnapshotFilter::matches(&s, &criteria("", "", "WICHTIG", false)));
        assert!(!SnapshotFilter::matches(&s, &criteria("", "", "anderes", false)));
    }

    #[test]
    fn tracking_reports_replaced_snapshots_incrementally() {
        let mut filter = SnapshotFilter::new();
        assert!(matches!(
            filter.track_latest(&snapshot("a-old", "h", "/a", &[], 0)),
            LatestUpdate::New
        ));
        assert!(matches!(
            filter.track_latest(&snapshot("a-older", "h", "/a", &[], -5)),
            LatestUpdate::Unchanged
        ));
        match filter.track_latest(&snapshot("a-new", "h", "/a", &[], 4)) {
            LatestUpdate::Replaced(old) => assert_eq!(old.id, "a-old"),
            other => panic!("{other:?}"),
        }
        assert!(matches!(
            filter.track_latest(&snapshot("a-same", "h", "/a", &[], 4)),
            LatestUpdate::Unchanged
        ));
        filter.clear();
        assert!(matches!(
            filter.track_latest(&snapshot("x", "h", "/a", &[], 0)),
            LatestUpdate::New
        ));
    }

    #[test]
    fn filtering_ten_thousand_snapshots_is_fast() {
        let snapshots: Vec<_> = (0..10_000)
            .map(|i| {
                snapshot(
                    &format!("{i:064}"),
                    &format!("host-{}", i % 100),
                    &format!("/daten/{}", i % 50),
                    &[&format!("tag-{}", i % 20)],
                    10_000 - i,
                )
            })
            .collect();
        let started = std::time::Instant::now();
        let result = SnapshotFilter::new().apply(&snapshots, &criteria("host-99", "", "", false), false);
        println!("METRIK Filter-10k={} ms", started.elapsed().as_millis());
        assert_eq!(result.len(), 100);
    }
}
