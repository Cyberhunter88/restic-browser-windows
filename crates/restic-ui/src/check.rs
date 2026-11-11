//! Integritätsprüfung: Schnellprüfung (Struktur und Metadaten) oder vollständige Datenprüfung.
//! Die Prüfung liest das Repository und führt nie eine Reparatur oder Änderung aus.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText, TextWrapMode};
use restic_core::guidance;
use restic_core::models::{CheckMode, RepositoryCheckResult};
use tokio_util::sync::CancellationToken;

use crate::preview::prepare_lines;
use crate::shared::Shared;
use crate::theme::{self, Palette};

#[derive(Default)]
struct CheckState {
    busy: AtomicBool,
    report: Mutex<Option<String>>,
}

pub struct CheckDialog {
    pub open: bool,
    full: bool,
    state: Arc<CheckState>,
    stop: CancellationToken,
}

impl CheckDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            full: false,
            state: Arc::new(CheckState::default()),
            stop: CancellationToken::new(),
        }
    }

    pub fn open_dialog(&mut self) {
        self.open = true;
        self.full = false;
        self.state = Arc::new(CheckState::default());
        self.stop = CancellationToken::new();
    }

    /// Ist ein Ergebnis da und die Prüfung beendet?
    pub fn is_ready(&self) -> bool {
        !self.state.busy.load(Ordering::SeqCst) && self.state.report.lock().expect("Sperre").is_some()
    }

    /// Nur für Entwicklungstests: die Schnellprüfung sofort starten.
    pub fn dev_start(&mut self, shared: &Arc<Shared>) {
        self.start(shared);
    }

    fn start(&mut self, shared: &Arc<Shared>) {
        let state = Arc::clone(&self.state);
        state.busy.store(true, Ordering::SeqCst);
        *state.report.lock().expect("Sperre") = Some("Prüfung läuft …".to_string());
        self.stop = CancellationToken::new();
        let (stop, session) = (self.stop.clone(), shared.session.clone());
        let mode = if self.full {
            CheckMode::Full
        } else {
            CheckMode::Quick
        };
        shared.spawn(async move {
            let report = match session.check_repository(mode, &stop).await {
                Ok(result) => report_text(mode, &result),
                Err(error) if error.is_cancelled() => "Prüfung abgebrochen.".to_string(),
                Err(error) => format!("Prüfung fehlgeschlagen:\n{}", guidance::describe(&error)),
            };
            *state.report.lock().expect("Sperre") = Some(report);
            state.busy.store(false, Ordering::SeqCst);
        });
    }

    pub fn ui(&mut self, ctx: &egui::Context, shared: &Arc<Shared>, palette: &Palette, escape_allowed: bool) {
        if !self.open {
            return;
        }
        let busy = self.state.busy.load(Ordering::SeqCst);
        let report = self.state.report.lock().expect("Sperre").clone();
        let mut start = false;
        let mut close = false;
        egui::Modal::new(egui::Id::NULL.with("pruefung"))
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(700.0);
                ui.label(theme::title("Repository-Integrität prüfen"));
                ui.label(theme::secondary(
                    palette,
                    "Die Prüfung liest das Repository, führt aber keine Reparatur oder Änderung aus.",
                ));
                ui.add_space(6.0);
                ui.add_enabled_ui(!busy, |ui| {
                    ui.radio_value(
                        &mut self.full,
                        false,
                        "Schnellprüfung – Struktur und Metadaten prüfen",
                    );
                    ui.radio_value(
                        &mut self.full,
                        true,
                        "Vollständige Datenprüfung – liest alle gesicherten Daten (kann lange dauern)",
                    );
                });
                ui.separator();
                if busy {
                    ui.horizontal(|ui| {
                        ui.spinner();
                        ui.label(theme::secondary(palette, "Prüfung läuft …"));
                    });
                }
                if let Some(text) = &report {
                    let lines = prepare_lines(text);
                    egui::ScrollArea::both()
                        .max_height(300.0)
                        .auto_shrink([false, false])
                        .show_rows(ui, 16.0, lines.len(), |ui, range| {
                            for line in &lines[range] {
                                ui.add(
                                    egui::Label::new(RichText::new(line).monospace())
                                        .wrap_mode(TextWrapMode::Extend),
                                );
                            }
                        });
                } else {
                    ui.add_space(300.0);
                }
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.button(if busy { "Abbrechen" } else { "Schließen" }).clicked() {
                        close = true;
                    }
                    if ui
                        .add_enabled(!busy, theme::primary_button(palette, "Prüfung starten"))
                        .clicked()
                    {
                        start = true;
                    }
                });
            });
        if close || (escape_allowed && ctx.input(|i| i.key_pressed(egui::Key::Escape))) {
            self.stop.cancel();
            self.open = false;
        } else if start {
            self.start(shared);
        }
    }
}

/// Ergebnistext der Prüfung (Aufbau wie in der C#-Linie).
pub fn report_text(mode: CheckMode, result: &RepositoryCheckResult) -> String {
    let state = if result.is_healthy() {
        "Keine Fehler gefunden.".to_string()
    } else {
        format!("{} Fehler gefunden.", result.error_count)
    };
    let notice = if result.details_truncated {
        "\nPrüfdetails wurden gekürzt; alle Meldungen wurden ausgewertet."
    } else {
        ""
    };
    let mode_text = if mode == CheckMode::Full {
        "vollständige Datenprüfung"
    } else {
        "Schnellprüfung"
    };
    format!("{state}{notice}\nModus: {mode_text}\n\n{}", result.details)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn result(error_count: i32, truncated: bool) -> RepositoryCheckResult {
        RepositoryCheckResult {
            mode: CheckMode::Quick,
            error_count,
            suggest_repair_index: false,
            suggest_prune: false,
            details: "no errors were found".into(),
            details_truncated: truncated,
        }
    }

    #[test]
    fn healthy_report_names_mode_and_details() {
        let text = report_text(CheckMode::Quick, &result(0, false));
        assert!(text.starts_with("Keine Fehler gefunden.\nModus: Schnellprüfung\n\n"));
        assert!(text.ends_with("no errors were found"));
    }

    #[test]
    fn errors_and_truncation_are_reported() {
        let text = report_text(CheckMode::Full, &result(3, true));
        assert!(text.starts_with("3 Fehler gefunden.\nPrüfdetails wurden gekürzt"));
        assert!(text.contains("Modus: vollständige Datenprüfung"));
    }

    #[test]
    fn a_suggested_index_repair_is_not_healthy() {
        let mut broken = result(0, false);
        broken.suggest_repair_index = true;
        assert!(report_text(CheckMode::Quick, &broken).starts_with("0 Fehler gefunden."));
    }
}
