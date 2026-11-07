//! Speicheranalyse eines Snapshots: Kategorien, größte Ordner und Dateien.
//!
//! Der Analysator verarbeitet `ls`-Knoten einzeln, hält höchstens 100.000 Ordnerpfade und je 15
//! Spitzenwerte im Speicher und ist unabhängig von Prozessen testbar.

use std::collections::HashMap;

use crate::command::normalize_snapshot_path;
use crate::models::BackupNode;

pub const MAXIMUM_TRACKED_FOLDERS: usize = 100_000;
const TOP_COUNT: usize = 15;

#[derive(Debug, Clone, PartialEq)]
pub struct StorageCategory {
    pub name: String,
    pub icon: String,
    pub total_size: i64,
    pub file_count: i64,
    pub percentage: f64,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FolderSizeNode {
    pub path: String,
    pub name: String,
    pub total_size: i64,
    pub file_count: i64,
    pub is_directory: bool,
    pub percentage: f64,
}

#[derive(Debug, Clone, Default)]
pub struct StorageAnalysisResult {
    pub snapshot_id: String,
    pub total_size: i64,
    pub total_file_count: i64,
    pub total_directory_count: i64,
    pub categories: Vec<StorageCategory>,
    pub top_folders: Vec<FolderSizeNode>,
    pub top_files: Vec<FolderSizeNode>,
    pub folder_analysis_is_truncated: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StorageAnalysisProgress {
    pub files_processed: i64,
    pub directories_processed: i64,
    pub bytes_processed: i64,
}

const CATEGORIES: [(&str, &str); 7] = [
    ("Dokumente", "📕"),
    ("Bilder", "🖼️"),
    ("Medien (Audio/Video)", "🎬"),
    ("Archive & ISOs", "📦"),
    ("Code & Skripte", "📝"),
    ("Ausführbar & System", "⚙️"),
    ("Sonstiges", "📄"),
];

/// Dateiendung wie `Path.GetExtension` in .NET: ab dem letzten Punkt, in Kleinbuchstaben.
/// Ein abschließender Punkt ergibt eine leere Endung; Punktdateien (`.bashrc`) behalten ihren Namen.
pub fn extension_of(name: &str) -> String {
    match name.rfind('.') {
        Some(index) if index + 1 < name.len() => name[index..].to_lowercase(),
        _ => String::new(),
    }
}

fn category_index(extension: &str) -> usize {
    match extension {
        ".pdf" | ".doc" | ".docx" | ".xls" | ".xlsx" | ".ppt" | ".pptx" | ".txt" | ".md" | ".odt"
        | ".ods" | ".rtf" | ".csv" | ".json" | ".xml" => 0,
        ".png" | ".jpg" | ".jpeg" | ".bmp" | ".gif" | ".ico" | ".webp" | ".svg" | ".tif" | ".tiff"
        | ".heic" => 1,
        ".mp3" | ".wav" | ".flac" | ".ogg" | ".aac" | ".mp4" | ".mkv" | ".avi" | ".mov" | ".wmv" => 2,
        ".zip" | ".tar" | ".gz" | ".7z" | ".rar" | ".bz2" | ".xz" | ".iso" => 3,
        ".cs" | ".py" | ".js" | ".ts" | ".cpp" | ".c" | ".h" | ".java" | ".html" | ".css" | ".sh"
        | ".ps1" | ".yaml" | ".yml" => 4,
        ".exe" | ".msi" | ".dll" | ".so" | ".dylib" | ".sys" => 5,
        _ => 6,
    }
}

struct TopFile {
    path: String,
    name: String,
    size: i64,
}

pub struct StorageAnalyzer {
    total_size: i64,
    total_files: i64,
    total_directories: i64,
    categories: [(i64, i64); 7],
    /// Reihenfolge des ersten Auftretens bleibt erhalten, damit Gleichstände deterministisch sind.
    folder_index: HashMap<String, usize>,
    folders: Vec<(String, i64, i64)>,
    top_files: Vec<TopFile>,
    folder_analysis_is_truncated: bool,
}

impl Default for StorageAnalyzer {
    fn default() -> Self {
        Self::new()
    }
}

impl StorageAnalyzer {
    pub fn new() -> Self {
        Self {
            total_size: 0,
            total_files: 0,
            total_directories: 0,
            categories: [(0, 0); 7],
            folder_index: HashMap::new(),
            folders: Vec::new(),
            top_files: Vec::with_capacity(TOP_COUNT + 1),
            folder_analysis_is_truncated: false,
        }
    }

    pub fn progress(&self) -> StorageAnalysisProgress {
        StorageAnalysisProgress {
            files_processed: self.total_files,
            directories_processed: self.total_directories,
            bytes_processed: self.total_size,
        }
    }

    pub fn add(&mut self, node: &BackupNode) {
        if node.is_directory() {
            self.total_directories += 1;
            return;
        }
        self.total_files += 1;
        self.total_size += node.size;
        let category = &mut self.categories[category_index(&extension_of(&node.name))];
        category.0 += node.size;
        category.1 += 1;
        add_top_file(&mut self.top_files, node);

        let normalized = normalize_snapshot_path(&node.path);
        let normalized = normalized.trim_end_matches('/');
        let mut end = normalized.len();
        while let Some(separator) = normalized[..end].rfind('/') {
            if separator == 0 {
                break;
            }
            let parent = &normalized[..separator];
            end = separator;
            match self.folder_index.get(parent) {
                Some(&index) => {
                    self.folders[index].1 += node.size;
                    self.folders[index].2 += 1;
                }
                None if self.folders.len() >= MAXIMUM_TRACKED_FOLDERS => {
                    self.folder_analysis_is_truncated = true
                }
                None => {
                    self.folder_index.insert(parent.to_string(), self.folders.len());
                    self.folders.push((parent.to_string(), node.size, 1));
                }
            }
        }
    }

    pub fn finish(self, snapshot_id: &str) -> StorageAnalysisResult {
        let total = self.total_size;
        let percentage = |size: i64| {
            if total > 0 {
                size as f64 * 100.0 / total as f64
            } else {
                0.0
            }
        };

        let mut top_folders: Vec<FolderSizeNode> = Vec::with_capacity(TOP_COUNT + 1);
        for (path, size, count) in &self.folders {
            let name = path.trim_end_matches('/').rsplit('/').next().unwrap_or_default();
            if name.is_empty() {
                continue;
            }
            insert_top(
                &mut top_folders,
                FolderSizeNode {
                    path: path.clone(),
                    name: name.to_string(),
                    total_size: *size,
                    file_count: *count,
                    is_directory: true,
                    percentage: percentage(*size),
                },
                |node| node.total_size,
            );
        }

        let mut categories: Vec<StorageCategory> = CATEGORIES
            .iter()
            .zip(self.categories)
            .filter(|(_, (_, count))| *count > 0)
            .map(|((name, icon), (size, count))| StorageCategory {
                name: (*name).to_string(),
                icon: (*icon).to_string(),
                total_size: size,
                file_count: count,
                percentage: percentage(size),
            })
            .collect();
        categories.sort_by_key(|category| std::cmp::Reverse(category.total_size));

        StorageAnalysisResult {
            snapshot_id: snapshot_id.to_string(),
            total_size: total,
            total_file_count: self.total_files,
            total_directory_count: self.total_directories,
            categories,
            top_folders,
            top_files: self
                .top_files
                .into_iter()
                .map(|file| FolderSizeNode {
                    percentage: percentage(file.size),
                    path: file.path,
                    name: file.name,
                    total_size: file.size,
                    file_count: 1,
                    is_directory: false,
                })
                .collect(),
            folder_analysis_is_truncated: self.folder_analysis_is_truncated,
        }
    }
}

fn add_top_file(files: &mut Vec<TopFile>, node: &BackupNode) {
    let entry = TopFile {
        path: node.path.clone(),
        name: node.name.clone(),
        size: node.size,
    };
    match files.iter().position(|existing| existing.size < entry.size) {
        Some(index) => files.insert(index, entry),
        None if files.len() < TOP_COUNT => files.push(entry),
        None => {}
    }
    files.truncate(TOP_COUNT);
}

fn insert_top<T>(list: &mut Vec<T>, item: T, size: impl Fn(&T) -> i64) {
    match list.iter().position(|existing| size(existing) < size(&item)) {
        Some(index) => list.insert(index, item),
        None if list.len() < TOP_COUNT => list.push(item),
        None => {}
    }
    list.truncate(TOP_COUNT);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(path: &str, size: i64) -> BackupNode {
        BackupNode {
            message_type: "node".into(),
            name: path.rsplit('/').next().unwrap().into(),
            node_type: "file".into(),
            path: path.into(),
            size,
            ..BackupNode::default()
        }
    }

    #[test]
    fn extensions_follow_dotnet_semantics() {
        assert_eq!(extension_of("Foto.JPG"), ".jpg");
        assert_eq!(extension_of(".bashrc"), ".bashrc");
        assert_eq!(extension_of("ende."), "");
        assert_eq!(extension_of("ohne"), "");
        assert_eq!(extension_of("a.tar.gz"), ".gz");
    }

    #[test]
    fn categorizes_sizes_and_counts_directories() {
        let mut analyzer = StorageAnalyzer::new();
        analyzer.add(&BackupNode {
            node_type: "dir".into(),
            path: "/a".into(),
            ..BackupNode::default()
        });
        analyzer.add(&file("/a/doc.pdf", 100));
        analyzer.add(&file("/a/bild.png", 300));
        analyzer.add(&file("/a/rest", 50));
        let result = analyzer.finish("snap");
        assert_eq!(result.total_file_count, 3);
        assert_eq!(result.total_directory_count, 1);
        assert_eq!(result.total_size, 450);
        let names: Vec<_> = result.categories.iter().map(|c| c.name.as_str()).collect();
        assert_eq!(
            names,
            ["Bilder", "Dokumente", "Sonstiges"],
            "nach Größe absteigend"
        );
        assert!((result.categories[0].percentage - 66.666).abs() < 0.01);
        assert_eq!(result.top_files[0].name, "bild.png");
        assert_eq!(result.snapshot_id, "snap");
    }

    #[test]
    fn folder_sizes_aggregate_over_all_parents_but_not_the_root() {
        let mut analyzer = StorageAnalyzer::new();
        analyzer.add(&file("/a/b/c.txt", 10));
        analyzer.add(&file("/a/d.txt", 5));
        let result = analyzer.finish("s");
        let by_path: HashMap<_, _> = result.top_folders.iter().map(|f| (f.path.as_str(), f)).collect();
        assert_eq!(by_path["/a"].total_size, 15);
        assert_eq!(by_path["/a"].file_count, 2);
        assert_eq!(by_path["/a/b"].total_size, 10);
        assert!(!by_path.contains_key("/"));
        assert_eq!(result.top_folders[0].path, "/a");
    }

    #[test]
    fn top_lists_are_limited_to_fifteen_and_sorted() {
        let mut analyzer = StorageAnalyzer::new();
        for i in 0..40 {
            analyzer.add(&file(&format!("/ordner{i}/datei{i}.bin"), i));
        }
        let result = analyzer.finish("s");
        assert_eq!(result.top_files.len(), 15);
        assert_eq!(result.top_folders.len(), 15);
        assert!(
            result
                .top_files
                .windows(2)
                .all(|w| w[0].total_size >= w[1].total_size)
        );
        assert_eq!(result.top_files[0].total_size, 39);
    }

    #[test]
    fn folder_tracking_is_bounded_and_marked_truncated() {
        let mut analyzer = StorageAnalyzer::new();
        for i in 0..=MAXIMUM_TRACKED_FOLDERS {
            analyzer.add(&file(&format!("/folder-{i}/file.txt"), 1));
        }
        let result = analyzer.finish("s");
        assert_eq!(result.total_file_count, MAXIMUM_TRACKED_FOLDERS as i64 + 1);
        assert!(result.folder_analysis_is_truncated);
        assert!(result.top_folders.len() <= 15);
    }

    #[test]
    fn empty_snapshot_has_zero_percentages() {
        let result = StorageAnalyzer::new().finish("s");
        assert_eq!(result.total_size, 0);
        assert!(result.categories.is_empty() && result.top_files.is_empty());
    }
}
