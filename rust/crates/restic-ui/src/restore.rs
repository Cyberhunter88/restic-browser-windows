//! Wiederherstellungsdialog: Ziel wählen, Überschreibverhalten, Vorschau und Fortschritt.
//! Geschrieben wird ausschließlich in das ausdrücklich gewählte Ziel außerhalb des Repositorys.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText};
use restic_core::guidance;
use restic_core::models::{BackupNode, OverwritePolicy, RestoreProgress};
use restic_core::repository::format_count;
use tokio_util::sync::CancellationToken;

use crate::shared::Shared;
use crate::theme::{self, Palette};

const OVERWRITE_OPTIONS: [(OverwritePolicy, &str); 4] = [
    (OverwritePolicy::Never, "Vorhandene Dateien nicht überschreiben"),
    (
        OverwritePolicy::IfNewer,
        "Nur überschreiben, wenn die Sicherung neuer ist",
    ),
    (
        OverwritePolicy::IfChanged,
        "Überschreiben, wenn die Datei geändert ist",
    ),
    (OverwritePolicy::Always, "Immer überschreiben"),
];

#[derive(Default)]
struct RestoreState {
    busy: AtomicBool,
    progress: Mutex<Option<RestoreProgress>>,
    outcome: Mutex<Option<Result<String, String>>>,
    preview: Mutex<Option<Result<String, String>>>,
    picked_folder: Mutex<Option<String>>,
}

pub struct RestoreDialog {
    pub open: bool,
    nodes: Vec<BackupNode>,
    summary: String,
    target: String,
    overwrite: usize,
    state: Arc<RestoreState>,
    stop: CancellationToken,
}

impl RestoreDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            nodes: Vec::new(),
            summary: String::new(),
            target: String::new(),
            overwrite: 0,
            state: Arc::new(RestoreState::default()),
            stop: CancellationToken::new(),
        }
    }

    pub fn open_with(&mut self, nodes: Vec<BackupNode>, summary: String, last_target: Option<&str>) {
        self.open = true;
        self.nodes = nodes;
        self.summary = summary;
        if self.target.is_empty() {
            self.target = last_target.unwrap_or_default().to_string();
        }
        self.state = Arc::new(RestoreState::default());
        self.stop = CancellationToken::new();
    }

    pub fn ui(&mut self, ctx: &egui::Context, shared: &Arc<Shared>, palette: &Palette) {
        if !self.open {
            return;
        }
        if let Some(folder) = self.state.picked_folder.lock().expect("Sperre").take() {
            self.target = folder;
            *self.state.preview.lock().expect("Sperre") = None;
        }
        let busy = self.state.busy.load(Ordering::SeqCst);
        let outcome = self.state.outcome.lock().expect("Sperre").clone();
        let finished = outcome.is_some();
        let mut start = false;
        let mut preview = false;
        let mut close = false;
        let mut cancel_run = false;
        egui::Modal::new(egui::Id::NULL.with("wiederherstellen"))
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(560.0);
                ui.label(theme::title("Wiederherstellen"));
                ui.label(theme::secondary(palette, &self.summary));
                ui.add_space(6.0);
                ui.add_enabled_ui(!busy && !finished, |ui| {
                    ui.label("Zielordner (außerhalb des Repositorys)");
                    ui.horizontal(|ui| {
                        ui.add(
                            egui::TextEdit::singleline(&mut self.target)
                                .desired_width(420.0)
                                .hint_text("z. B. D:\\Wiederhergestellt"),
                        );
                        if ui.button("Ordner wählen …").clicked() {
                            let state = Arc::clone(&self.state);
                            let ctx = shared.ctx.clone();
                            shared.handle.spawn_blocking(move || {
                                if let Some(folder) = rfd::FileDialog::new()
                                    .set_title("Wiederherstellungsziel wählen")
                                    .pick_folder()
                                {
                                    *state.picked_folder.lock().expect("Sperre") =
                                        Some(folder.to_string_lossy().into_owned());
                                }
                                ctx.request_repaint();
                            });
                        }
                    });
                    ui.label("Wenn eine Datei im Ziel schon existiert");
                    egui::ComboBox::from_id_salt("ueberschreiben")
                        .width(ui.available_width())
                        .selected_text(OVERWRITE_OPTIONS[self.overwrite].1)
                        .show_ui(ui, |ui| {
                            for (index, (_, label)) in OVERWRITE_OPTIONS.iter().enumerate() {
                                ui.selectable_value(&mut self.overwrite, index, *label);
                            }
                        });
                    ui.label(theme::small(
                        palette,
                        "Das Repository wird nie verändert; es wird nur in den Zielordner geschrieben.",
                    ));
                });
                if let Some(result) = self.state.preview.lock().expect("Sperre").clone() {
                    match result {
                        Ok(text) => ui.label(theme::secondary(palette, text)),
                        Err(error) => ui.label(RichText::new(error).color(palette.danger)),
                    };
                }
                if busy {
                    let progress = self.state.progress.lock().expect("Sperre").clone();
                    let fraction = progress
                        .as_ref()
                        .map_or(0.0, |p| p.percent_done as f32)
                        .clamp(0.0, 1.0);
                    let text = progress.map_or("Wiederherstellung läuft …".to_string(), |p| {
                        format!(
                            "{} von {} Dateien",
                            format_count(p.files_restored.max(0) as usize),
                            format_count(p.total_files.max(0) as usize)
                        )
                    });
                    ui.add(egui::ProgressBar::new(fraction).text(text).animate(true));
                }
                if let Some(result) = &outcome {
                    match result {
                        Ok(message) => ui.label(RichText::new(message).color(palette.accent)),
                        Err(error) => ui.label(RichText::new(error).color(palette.danger)),
                    };
                }
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if finished {
                        if ui.add(theme::primary_button(palette, "Schließen")).clicked() {
                            close = true;
                        }
                    } else if busy {
                        if ui.button("Abbrechen").clicked() {
                            cancel_run = true;
                        }
                    } else {
                        let ready = !self.target.trim().is_empty();
                        if ui
                            .add_enabled(ready, theme::primary_button(palette, "Wiederherstellen"))
                            .clicked()
                        {
                            start = true;
                        }
                        if ui
                            .add_enabled(ready, egui::Button::new("Vorschau"))
                            .on_hover_text("Zeigt, was wiederhergestellt würde, ohne etwas zu schreiben")
                            .clicked()
                        {
                            preview = true;
                        }
                        if ui.button("Abbrechen").clicked() {
                            close = true;
                        }
                    }
                });
            });
        if cancel_run {
            self.stop.cancel();
        }
        if close || (!busy && ctx.input(|i| i.key_pressed(egui::Key::Escape))) {
            self.open = false;
        } else if start {
            self.run(shared, false);
        } else if preview {
            self.run(shared, true);
        }
    }

    pub fn is_finished(&self) -> bool {
        self.state.outcome.lock().expect("Sperre").is_some()
    }

    /// Nur für Entwicklungstests: Ziel setzen und die Wiederherstellung sofort starten.
    pub fn dev_run(&mut self, target: String, shared: &Arc<Shared>) {
        self.target = target;
        self.run(shared, false);
    }

    fn run(&mut self, shared: &Arc<Shared>, dry_run: bool) {
        let state = Arc::clone(&self.state);
        state.busy.store(true, Ordering::SeqCst);
        *state.progress.lock().expect("Sperre") = None;
        *state.preview.lock().expect("Sperre") = None;
        self.stop = CancellationToken::new();
        let (stop, session, ctx) = (self.stop.clone(), shared.session.clone(), shared.ctx.clone());
        let (nodes, target, policy) = (
            self.nodes.clone(),
            self.target.trim().to_string(),
            OVERWRITE_OPTIONS[self.overwrite].0,
        );
        shared.spawn(async move {
            if dry_run {
                let result = session.preview_restore(&nodes, &target, policy, &stop).await;
                *state.preview.lock().expect("Sperre") = Some(match result {
                    Ok(preview) => Ok(format!(
                        "Vorschau: {} neu, {} aktualisiert, {} unverändert{}",
                        format_count(preview.restored.max(0) as usize),
                        format_count(preview.updated.max(0) as usize),
                        format_count(preview.unchanged.max(0) as usize),
                        if preview.is_truncated { " (Liste gekürzt)" } else { "" }
                    )),
                    Err(error) => Err(guidance::describe(&error)),
                });
            } else {
                let progress_state = Arc::clone(&state);
                let progress = move |p: RestoreProgress| {
                    *progress_state.progress.lock().expect("Sperre") = Some(p);
                    ctx.request_repaint();
                };
                let result = session.restore(&nodes, &target, policy, &progress, &stop).await;
                *state.outcome.lock().expect("Sperre") = Some(match result {
                    Ok(done) => Ok(format!(
                        "{} ({} Element(e) wiederhergestellt, {} übersprungen)",
                        done.message,
                        format_count(done.files_restored.max(0) as usize),
                        format_count(done.files_skipped.max(0) as usize)
                    )),
                    Err(error) if error.is_cancelled() => {
                        Err("Wiederherstellung abgebrochen. Bereits geschriebene Dateien im Ziel können unvollständig sein.".to_string())
                    }
                    Err(error) => Err(guidance::describe(&error)),
                });
            }
            state.busy.store(false, Ordering::SeqCst);
        });
    }
}
