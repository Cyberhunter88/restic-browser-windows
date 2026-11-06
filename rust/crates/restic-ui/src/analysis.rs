//! Speicheranalyse des ausgewählten Snapshots: Kategorien, größte Ordner und größte Dateien.
//! Die Analyse liest nur die Dateiliste des Snapshots (ein `ls`-Lauf), nie Dateiinhalte.

use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText};
use egui_extras::{Column, TableBuilder};
use restic_core::guidance;
use restic_core::models::{SnapshotInfo, format_bytes};
use restic_core::repository::format_count;
use restic_core::storage::{FolderSizeNode, StorageAnalysisProgress, StorageAnalysisResult};
use tokio_util::sync::CancellationToken;

use crate::icons;
use crate::shared::Shared;
use crate::theme::{self, Palette};

type Loaded = Result<(SnapshotInfo, StorageAnalysisResult), String>;

#[derive(Default)]
struct AnalysisState {
    progress: Mutex<Option<StorageAnalysisProgress>>,
    result: Mutex<Option<Loaded>>,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Tab {
    Categories,
    Folders,
    Files,
}

pub struct AnalysisDialog {
    pub open: bool,
    tab: Tab,
    state: Arc<AnalysisState>,
    stop: CancellationToken,
}

impl AnalysisDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            tab: Tab::Categories,
            state: Arc::new(AnalysisState::default()),
            stop: CancellationToken::new(),
        }
    }

    pub fn is_ready(&self) -> bool {
        self.state.result.lock().expect("Sperre").is_some()
    }

    pub fn open_dialog(&mut self, shared: &Arc<Shared>) {
        self.stop.cancel();
        self.open = true;
        self.tab = Tab::Categories;
        self.state = Arc::new(AnalysisState::default());
        self.stop = CancellationToken::new();
        let (state, stop, session, ctx) = (
            Arc::clone(&self.state),
            self.stop.clone(),
            shared.session.clone(),
            shared.ctx.clone(),
        );
        shared.spawn(async move {
            let progress_state = Arc::clone(&state);
            let progress = move |p: StorageAnalysisProgress| {
                *progress_state.progress.lock().expect("Sperre") = Some(p);
                ctx.request_repaint();
            };
            let result = session.analyze_selected_snapshot(&progress, &stop).await;
            if stop.is_cancelled() {
                return;
            }
            *state.result.lock().expect("Sperre") = Some(result.map_err(|error| guidance::describe(&error)));
        });
    }

    pub fn ui(&mut self, ctx: &egui::Context, palette: &Palette, escape_allowed: bool) {
        if !self.open {
            return;
        }
        let result = self.state.result.lock().expect("Sperre").clone();
        let progress = *self.state.progress.lock().expect("Sperre");
        let mut close = false;
        egui::Modal::new(egui::Id::NULL.with("speicheranalyse"))
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(880.0);
                ui.label(theme::title("Speicheranalyse"));
                match &result {
                    Some(Ok((snapshot, analysis))) => {
                        ui.label(theme::secondary(
                            palette,
                            subtitle(snapshot, analysis.folder_analysis_is_truncated),
                        ));
                        ui.horizontal(|ui| {
                            ui.label(theme::secondary(palette, "Gesamtgröße:"));
                            ui.label(RichText::new(format_bytes(analysis.total_size)).strong());
                            ui.add_space(24.0);
                            ui.label(theme::secondary(palette, "Dateien:"));
                            ui.label(
                                RichText::new(format_count(analysis.total_file_count.max(0) as usize))
                                    .strong(),
                            );
                        });
                        ui.add_space(6.0);
                        ui.horizontal(|ui| {
                            ui.selectable_value(&mut self.tab, Tab::Categories, "Dateikategorien");
                            ui.selectable_value(&mut self.tab, Tab::Folders, "Größte Ordner");
                            ui.selectable_value(&mut self.tab, Tab::Files, "Größte Einzeldateien");
                        });
                        ui.separator();
                        match self.tab {
                            Tab::Categories => categories_table(ui, palette, analysis),
                            Tab::Folders => nodes_table(ui, palette, &analysis.top_folders, true),
                            Tab::Files => nodes_table(ui, palette, &analysis.top_files, false),
                        }
                    }
                    Some(Err(message)) => {
                        ui.label(RichText::new(message).color(palette.danger));
                        ui.add_space(60.0);
                    }
                    None => {
                        ui.label(theme::secondary(
                            palette,
                            "Aufschlüsselung der Dateigrößen und Kategorien im Snapshot",
                        ));
                        ui.add_space(6.0);
                        ui.horizontal(|ui| {
                            ui.label(theme::secondary(palette, "Gesamtgröße:"));
                            ui.label(
                                RichText::new(
                                    progress.map_or("0 B".to_string(), |p| format_bytes(p.bytes_processed)),
                                )
                                .strong(),
                            );
                            ui.add_space(24.0);
                            ui.label(theme::secondary(palette, "Dateien:"));
                            ui.label(
                                RichText::new(progress.map_or("0".to_string(), |p| {
                                    format_count(p.files_processed.max(0) as usize)
                                }))
                                .strong(),
                            );
                        });
                        ui.add_space(8.0);
                        ui.horizontal(|ui| {
                            ui.spinner();
                            ui.label(theme::secondary(
                                palette,
                                "Snapshot-Struktur wird analysiert und Ordnergrößen werden berechnet …",
                            ));
                        });
                        ui.add_space(200.0);
                    }
                }
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.add(theme::primary_button(palette, "Schließen")).clicked() {
                        close = true;
                    }
                });
            });
        if close || (escape_allowed && ctx.input(|i| i.key_pressed(egui::Key::Escape))) {
            self.stop.cancel();
            self.open = false;
        }
    }
}

/// Untertitel mit Snapshot-Angaben (und Hinweis, wenn die Ordnerauswertung begrenzt wurde).
pub fn subtitle(snapshot: &SnapshotInfo, folders_truncated: bool) -> String {
    let when = snapshot
        .time
        .map(|t| t.format("%d.%m.%Y %H:%M").to_string())
        .unwrap_or_default();
    let mut text = format!(
        "Snapshot {} ({}) vom {when}",
        snapshot.display_id(),
        snapshot.hostname
    );
    if folders_truncated {
        text.push_str(" – Ordnerauswertung auf 100.000 Ordner begrenzt");
    }
    text
}

/// Anteil als deutsche Prozentangabe mit einer Nachkommastelle, z. B. `12,3 %`.
pub fn format_percent(value: f64) -> String {
    format!("{value:.1}").replace('.', ",") + " %"
}

fn share_bar(ui: &mut egui::Ui, palette: &Palette, percentage: f64) {
    ui.horizontal(|ui| {
        ui.add(
            egui::ProgressBar::new((percentage / 100.0).clamp(0.0, 1.0) as f32)
                .desired_width(110.0)
                .desired_height(8.0),
        );
        ui.label(theme::small(palette, format_percent(percentage)));
    });
}

fn categories_table(ui: &mut egui::Ui, palette: &Palette, analysis: &StorageAnalysisResult) {
    TableBuilder::new(ui)
        .striped(true)
        .max_scroll_height(300.0)
        .column(Column::exact(36.0))
        .column(Column::initial(240.0))
        .column(Column::initial(140.0))
        .column(Column::initial(130.0))
        .column(Column::remainder())
        .header(26.0, |mut header| {
            for label in ["", "Kategorie", "Anzahl Dateien", "Gesamtgröße", "Anteil"] {
                header.col(|ui| {
                    ui.label(theme::small(palette, label).strong());
                });
            }
        })
        .body(|body| {
            body.rows(28.0, analysis.categories.len(), |mut row| {
                let category = &analysis.categories[row.index()];
                row.col(|ui| {
                    icons::icon(ui, icons::category_icon(&category.name), palette);
                });
                row.col(|ui| {
                    ui.label(&category.name);
                });
                row.col(|ui| {
                    ui.label(format_count(category.file_count.max(0) as usize));
                });
                row.col(|ui| {
                    ui.label(format_bytes(category.total_size));
                });
                row.col(|ui| share_bar(ui, palette, category.percentage));
            });
        });
}

fn nodes_table(ui: &mut egui::Ui, palette: &Palette, nodes: &[FolderSizeNode], folders: bool) {
    let mut table = TableBuilder::new(ui)
        .striped(true)
        .max_scroll_height(300.0)
        .column(Column::exact(36.0))
        .column(Column::initial(190.0).clip(true))
        .column(Column::initial(260.0).clip(true));
    if folders {
        table = table.column(Column::initial(80.0)).column(Column::initial(130.0));
    } else {
        table = table.column(Column::initial(120.0));
    }
    table
        .column(Column::remainder())
        .header(26.0, |mut header| {
            let labels: &[&str] = if folders {
                &[
                    "",
                    "Ordnername",
                    "Pfad",
                    "Dateien",
                    "Kumulierte Größe",
                    "Anteil am Snapshot",
                ]
            } else {
                &["", "Dateiname", "Pfad", "Größe", "Anteil am Snapshot"]
            };
            for label in labels {
                header.col(|ui| {
                    ui.label(theme::small(palette, *label).strong());
                });
            }
        })
        .body(|body| {
            body.rows(28.0, nodes.len(), |mut row| {
                let node = &nodes[row.index()];
                row.col(|ui| {
                    let kind = if folders {
                        icons::IconKind::Folder
                    } else {
                        icons::icon_kind("file", &node.name)
                    };
                    icons::icon(ui, kind, palette);
                });
                row.col(|ui| {
                    ui.add(egui::Label::new(&node.name).truncate());
                });
                row.col(|ui| {
                    ui.add(egui::Label::new(theme::secondary(palette, &node.path)).truncate());
                });
                if folders {
                    row.col(|ui| {
                        ui.label(format_count(node.file_count.max(0) as usize));
                    });
                }
                row.col(|ui| {
                    ui.label(format_bytes(node.total_size));
                });
                row.col(|ui| share_bar(ui, palette, node.percentage));
            });
        });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn percentages_use_a_decimal_comma() {
        assert_eq!(format_percent(0.0), "0,0 %");
        assert_eq!(format_percent(12.345), "12,3 %");
        assert_eq!(format_percent(100.0), "100,0 %");
    }

    #[test]
    fn subtitle_names_the_snapshot_and_the_folder_limit() {
        let snapshot: SnapshotInfo =
            serde_snapshot("abcdef1234567890", "arbeits-pc", "2026-11-06T22:43:07+01:00");
        assert_eq!(
            subtitle(&snapshot, false),
            "Snapshot abcdef12 (arbeits-pc) vom 06.11.2026 22:43"
        );
        assert!(subtitle(&snapshot, true).ends_with("Ordnerauswertung auf 100.000 Ordner begrenzt"));
    }

    fn serde_snapshot(id: &str, host: &str, time: &str) -> SnapshotInfo {
        SnapshotInfo {
            id: id.into(),
            hostname: host.into(),
            time: Some(chrono::DateTime::parse_from_rfc3339(time).unwrap()),
            ..SnapshotInfo::default()
        }
    }
}
