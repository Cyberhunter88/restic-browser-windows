//! Dateiversionen: alle Snapshots, in denen eine Datei (exakter Pfad) vorkommt, neueste zuerst.
//! Aus einer Version lassen sich Vorschau und Wiederherstellung öffnen.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText, Sense};
use egui_extras::{Column, TableBuilder};
use restic_core::guidance;
use restic_core::models::{BackupNode, FileVersion};
use tokio_util::sync::CancellationToken;

use crate::shared::Shared;
use crate::theme::{self, Palette};

#[derive(Default)]
struct VersionsState {
    loading: AtomicBool,
    generation: AtomicU64,
    versions: Mutex<Vec<FileVersion>>,
    error: Mutex<Option<String>>,
    loaded: AtomicBool,
}

/// Was der Nutzer im Dialog ausgelöst hat; die Anwendung führt es nach dem Zeichnen aus.
pub enum VersionsAction {
    None,
    Preview(FileVersion),
    Restore(FileVersion),
}

pub struct VersionsDialog {
    pub open: bool,
    node: BackupNode,
    all_hosts: bool,
    selected: Option<usize>,
    state: Arc<VersionsState>,
    stop: CancellationToken,
}

impl VersionsDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            node: BackupNode::default(),
            all_hosts: false,
            selected: None,
            state: Arc::new(VersionsState::default()),
            stop: CancellationToken::new(),
        }
    }

    pub fn is_ready(&self) -> bool {
        self.state.loaded.load(Ordering::SeqCst)
    }

    /// Nur für Entwicklungstests: die Version an Position `index`.
    pub fn version_at(&self, index: usize) -> Option<FileVersion> {
        self.state.versions.lock().expect("Sperre").get(index).cloned()
    }

    pub fn open_with(&mut self, shared: &Arc<Shared>, node: BackupNode) {
        self.open = true;
        self.node = node;
        self.all_hosts = false;
        self.state = Arc::new(VersionsState::default());
        self.load(shared);
    }

    fn load(&mut self, shared: &Arc<Shared>) {
        self.stop.cancel();
        self.stop = CancellationToken::new();
        self.selected = None;
        let state = Arc::clone(&self.state);
        let generation = state.generation.fetch_add(1, Ordering::SeqCst) + 1;
        state.loading.store(true, Ordering::SeqCst);
        state.loaded.store(false, Ordering::SeqCst);
        *state.error.lock().expect("Sperre") = None;
        let (session, node, all_hosts, stop) = (
            shared.session.clone(),
            self.node.clone(),
            self.all_hosts,
            self.stop.clone(),
        );
        shared.spawn(async move {
            let result = session.get_file_versions(&node, all_hosts, &stop).await;
            // Ein späteres Neuladen hat Vorrang vor diesem Ergebnis.
            if state.generation.load(Ordering::SeqCst) != generation {
                return;
            }
            match result {
                Ok(versions) => *state.versions.lock().expect("Sperre") = versions,
                Err(error) if error.is_cancelled() => return,
                Err(error) => {
                    state.versions.lock().expect("Sperre").clear();
                    *state.error.lock().expect("Sperre") = Some(guidance::describe(&error));
                }
            }
            state.loading.store(false, Ordering::SeqCst);
            state.loaded.store(true, Ordering::SeqCst);
        });
    }

    pub fn close(&mut self) {
        self.stop.cancel();
        self.open = false;
    }

    pub fn ui(
        &mut self,
        ctx: &egui::Context,
        shared: &Arc<Shared>,
        palette: &Palette,
        escape_allowed: bool,
    ) -> VersionsAction {
        if !self.open {
            return VersionsAction::None;
        }
        let loading = self.state.loading.load(Ordering::SeqCst);
        let versions = self.state.versions.lock().expect("Sperre").clone();
        let error = self.state.error.lock().expect("Sperre").clone();
        let mut action = VersionsAction::None;
        let mut close = false;
        let mut reload = false;
        egui::Modal::new(egui::Id::NULL.with("versionen"))
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(780.0);
                ui.label(theme::title("Dateiversionen"));
                ui.add(egui::Label::new(theme::secondary(palette, &self.node.path)).truncate());
                if ui
                    .checkbox(&mut self.all_hosts, "Alle Hosts")
                    .on_hover_text("Auch Snapshots anderer Rechner einbeziehen")
                    .changed()
                {
                    reload = true;
                }
                ui.separator();
                if let Some(message) = &error {
                    ui.label(RichText::new(message).color(palette.danger));
                }
                if loading {
                    ui.horizontal(|ui| {
                        ui.spinner();
                        ui.label(theme::secondary(palette, "Versionen werden gesucht …"));
                    });
                } else if versions.is_empty() && error.is_none() {
                    ui.label(theme::secondary(
                        palette,
                        "Für diese Datei wurden keine Versionen gefunden.",
                    ));
                }
                let mut clicked = None;
                ui.scope(|ui| {
                    // Unsichtbare, aber ziehbare Spaltenanfasser wie in der Dateitabelle.
                    let widgets = &mut ui.visuals_mut().widgets;
                    widgets.noninteractive.bg_stroke = egui::Stroke::NONE;
                    widgets.hovered.bg_stroke = egui::Stroke::NONE;
                    widgets.active.bg_stroke = egui::Stroke::NONE;
                    TableBuilder::new(ui)
                        .striped(true)
                        .resizable(true)
                        .sense(Sense::click())
                        .max_scroll_height(280.0)
                        .column(Column::initial(150.0).at_least(140.0).clip(true))
                        .column(Column::initial(160.0).at_least(80.0).clip(true))
                        .column(Column::initial(110.0).at_least(100.0).clip(true))
                        .column(Column::initial(90.0).at_least(70.0).clip(true))
                        .column(Column::remainder().at_least(110.0).clip(true))
                        .header(28.0, |mut header| {
                            for label in ["Zeitpunkt", "Host", "Snapshot-ID", "Größe", "Geändert"] {
                                header.col(|ui| {
                                    ui.label(theme::small(palette, label).strong());
                                });
                            }
                        })
                        .body(|body| {
                            body.rows(26.0, versions.len(), |mut row| {
                                let index = row.index();
                                let version = &versions[index];
                                row.set_selected(self.selected == Some(index));
                                row.col(|ui| {
                                    ui.label(format_time(version.snapshot.time));
                                });
                                row.col(|ui| {
                                    ui.label(&version.snapshot.hostname);
                                });
                                row.col(|ui| {
                                    ui.label(theme::secondary(palette, version.snapshot.display_id()));
                                });
                                row.col(|ui| {
                                    ui.label(theme::secondary(palette, version.node.size_text()));
                                });
                                row.col(|ui| {
                                    ui.label(theme::secondary(palette, format_time(version.node.modified)));
                                });
                                if row.response().clicked() {
                                    clicked = Some(index);
                                }
                            });
                        });
                });
                if let Some(index) = clicked {
                    self.selected = Some(index);
                }
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let chosen = self.selected.and_then(|index| versions.get(index));
                    if ui
                        .add_enabled(
                            chosen.is_some(),
                            theme::primary_button(palette, "Diese Version wiederherstellen"),
                        )
                        .clicked()
                    {
                        action = VersionsAction::Restore(chosen.cloned().expect("ausgewählt"));
                    }
                    if ui
                        .add_enabled(chosen.is_some(), egui::Button::new("Vorschau"))
                        .clicked()
                    {
                        action = VersionsAction::Preview(chosen.cloned().expect("ausgewählt"));
                    }
                    if ui.button("Schließen").clicked() {
                        close = true;
                    }
                });
            });
        if reload {
            self.load(shared);
        }
        if close || (escape_allowed && ctx.input(|i| i.key_pressed(egui::Key::Escape))) {
            self.close();
        }
        action
    }
}

fn format_time(time: Option<chrono::DateTime<chrono::FixedOffset>>) -> String {
    time.map(|t| t.format("%d.%m.%Y %H:%M").to_string())
        .unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn times_are_formatted_in_german_style_or_empty() {
        let time = chrono::DateTime::parse_from_rfc3339("2026-11-06T22:43:07+01:00").unwrap();
        assert_eq!(format_time(Some(time)), "06.11.2026 22:43");
        assert_eq!(format_time(None), "");
    }
}
