//! Repository-Statistik. Wird nie automatisch geladen, sondern nur, wenn der Nutzer den Dialog öffnet
//! (`restic stats` liest bei großen Repositories viele Daten).

use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText};
use restic_core::guidance;
use restic_core::models::{RepositoryStats, format_bytes};
use restic_core::repository::format_count;
use tokio_util::sync::CancellationToken;

use crate::shared::Shared;
use crate::theme::{self, Palette};

type Loaded = Result<RepositoryStats, String>;

#[derive(Default)]
struct StatsState {
    result: Mutex<Option<Loaded>>,
}

pub struct StatsDialog {
    pub open: bool,
    state: Arc<StatsState>,
    stop: CancellationToken,
}

impl StatsDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            state: Arc::new(StatsState::default()),
            stop: CancellationToken::new(),
        }
    }

    pub fn is_ready(&self) -> bool {
        self.state.result.lock().expect("Sperre").is_some()
    }

    pub fn open_dialog(&mut self, shared: &Arc<Shared>) {
        self.stop.cancel();
        self.open = true;
        self.state = Arc::new(StatsState::default());
        self.stop = CancellationToken::new();
        let (state, stop, session) = (
            Arc::clone(&self.state),
            self.stop.clone(),
            shared.session.clone(),
        );
        shared.spawn(async move {
            let result = session.fetch_repository_stats(&stop).await;
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
        let mut close = false;
        egui::Modal::new(egui::Id::NULL.with("statistik")).frame(theme::dialog_frame(ctx)).show(ctx, |ui| {
            ui.set_width(520.0);
            ui.label(theme::title("Repository-Statistik"));
            ui.label(theme::secondary(
                palette,
                "Die Statistik liest das Repository und wird nur auf Anforderung berechnet. Bei großen Repositories kann das dauern.",
            ));
            ui.add_space(8.0);
            match &result {
                None => {
                    ui.horizontal(|ui| {
                        ui.spinner();
                        ui.label(theme::secondary(palette, "Statistik wird berechnet …"));
                    });
                    ui.add_space(60.0);
                }
                Some(Err(message)) => {
                    ui.label(RichText::new(message).color(palette.danger));
                }
                Some(Ok(stats)) => {
                    egui::Grid::new("statistik-werte").num_columns(2).spacing([24.0, 8.0]).show(ui, |ui| {
                        for (label, value) in stats_rows(stats) {
                            ui.label(theme::secondary(palette, label));
                            ui.label(RichText::new(value).strong());
                            ui.end_row();
                        }
                    });
                }
            }
            ui.add_space(8.0);
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

/// Beschriftete Werte der Statistik für die Anzeige.
pub fn stats_rows(stats: &RepositoryStats) -> Vec<(&'static str, String)> {
    let count = |value: i64| format_count(value.max(0) as usize);
    let mut rows = vec![
        ("Snapshots", count(i64::from(stats.snapshots_count))),
        ("Dateien", count(stats.total_file_count)),
    ];
    // Die Blob-Anzahl liefert restic nur bei bestimmten Statistik-Modi; ein "0" wäre irreführend.
    if stats.total_blob_count > 0 {
        rows.push(("Datenblöcke (Blobs)", count(stats.total_blob_count)));
    }
    rows.push((
        "Größe bei vollständiger Wiederherstellung",
        format_bytes(stats.total_size),
    ));
    rows
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rows_use_german_number_formats() {
        let stats = RepositoryStats {
            total_size: 1_610_612_736,
            total_file_count: 12_345,
            total_blob_count: 987,
            snapshots_count: 3,
        };
        let rows = stats_rows(&stats);
        assert_eq!(rows[0], ("Snapshots", "3".to_string()));
        assert_eq!(rows[1], ("Dateien", "12.345".to_string()));
        assert_eq!(rows[2], ("Datenblöcke (Blobs)", "987".to_string()));
        assert_eq!(
            rows[3],
            ("Größe bei vollständiger Wiederherstellung", "1,5 GB".to_string())
        );
    }

    #[test]
    fn empty_repository_shows_zeros() {
        let rows = stats_rows(&RepositoryStats::default());
        assert_eq!(rows[1].1, "0");
        assert_eq!(rows.len(), 3, "ohne Blob-Angabe entfällt die Zeile");
        assert_eq!(rows[2].1, "0 B");
    }
}
