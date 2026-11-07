//! Begrenzter LRU-Cache für Verzeichnislisten (nur Sitzungsdauer, nie dauerhaft).
//!
//! Höchstens 24 Verzeichnisse und höchstens 50.000 Knoten insgesamt. Listen, die allein größer
//! als das Knotenbudget sind, werden nicht gecacht.

use std::collections::{HashMap, VecDeque};
use std::sync::Arc;

use restic_core::models::BackupNode;

pub const DIRECTORY_CACHE_CAPACITY: usize = 24;
pub const DIRECTORY_CACHE_NODE_CAPACITY: usize = 50_000;

#[derive(Debug, Default)]
pub struct DirectoryCache {
    entries: HashMap<String, Arc<Vec<BackupNode>>>,
    /// Ältester Eintrag vorn, zuletzt verwendeter hinten.
    order: VecDeque<String>,
    node_count: usize,
}

impl DirectoryCache {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn len(&self) -> usize {
        self.entries.len()
    }

    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    pub fn node_count(&self) -> usize {
        self.node_count
    }

    pub fn insert(&mut self, key: String, nodes: Arc<Vec<BackupNode>>) {
        if nodes.len() > DIRECTORY_CACHE_NODE_CAPACITY {
            return;
        }
        if let Some(existing) = self.entries.remove(&key) {
            self.order.retain(|k| *k != key);
            self.node_count -= existing.len();
        }
        while self.entries.len() >= DIRECTORY_CACHE_CAPACITY
            || self.node_count + nodes.len() > DIRECTORY_CACHE_NODE_CAPACITY
        {
            let Some(oldest) = self.order.pop_front() else {
                break;
            };
            if let Some(removed) = self.entries.remove(&oldest) {
                self.node_count -= removed.len();
            }
        }
        self.node_count += nodes.len();
        self.order.push_back(key.clone());
        self.entries.insert(key, nodes);
    }

    /// Liefert den Eintrag und markiert ihn als zuletzt verwendet.
    pub fn get(&mut self, key: &str) -> Option<Arc<Vec<BackupNode>>> {
        let nodes = self.entries.get(key)?.clone();
        self.order.retain(|k| k != key);
        self.order.push_back(key.to_string());
        Some(nodes)
    }

    pub fn clear(&mut self) {
        self.entries.clear();
        self.order.clear();
        self.node_count = 0;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn nodes(count: usize) -> Arc<Vec<BackupNode>> {
        Arc::new(
            (0..count)
                .map(|i| BackupNode {
                    name: i.to_string(),
                    ..BackupNode::default()
                })
                .collect(),
        )
    }

    #[test]
    fn entry_count_is_bounded_and_oldest_entries_are_evicted() {
        let mut cache = DirectoryCache::new();
        for i in 0..30 {
            cache.insert(format!("snapshot\n/{i}"), nodes(0));
        }
        assert_eq!(cache.len(), DIRECTORY_CACHE_CAPACITY);
        assert!(cache.get("snapshot\n/0").is_none());
        assert!(cache.get("snapshot\n/29").is_some());
    }

    #[test]
    fn node_budget_is_bounded() {
        let mut cache = DirectoryCache::new();
        for i in 0..24 {
            cache.insert(format!("snapshot\n/{i}"), nodes(3_000));
        }
        assert!(cache.node_count() <= DIRECTORY_CACHE_NODE_CAPACITY);
        assert!(cache.len() < 24, "das Knotenbudget verdrängt ältere Einträge");
    }

    #[test]
    fn recently_used_entries_survive_eviction() {
        let mut cache = DirectoryCache::new();
        for i in 0..DIRECTORY_CACHE_CAPACITY {
            cache.insert(format!("k{i}"), nodes(1));
        }
        assert!(cache.get("k0").is_some(), "k0 wird zuletzt verwendet");
        cache.insert("neu".into(), nodes(1));
        assert!(cache.get("k0").is_some());
        assert!(cache.get("k1").is_none(), "k1 war der älteste ungenutzte Eintrag");
    }

    #[test]
    fn oversized_lists_are_not_cached_and_replacing_updates_the_count() {
        let mut cache = DirectoryCache::new();
        cache.insert("riesig".into(), nodes(DIRECTORY_CACHE_NODE_CAPACITY + 1));
        assert!(cache.is_empty());
        cache.insert("a".into(), nodes(10));
        cache.insert("a".into(), nodes(4));
        assert_eq!((cache.len(), cache.node_count()), (1, 4));
        cache.clear();
        assert_eq!((cache.len(), cache.node_count()), (0, 0));
    }
}
