//! Wiederherstellungsdialog: Ziel wählen, Überschreibverhalten, Vorschau und Fortschritt.
//! Geschrieben wird ausschließlich in das ausdrücklich gewählte Ziel außerhalb des Repositorys.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use eframe::egui::{self, RichText};
use restic_core::guidance;
use restic_core::models::{BackupNode, OverwritePolicy, RestorePreviewItem, RestoreProgress, format_bytes};
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
    preview: Mutex<Option<Result<PreviewSummary, String>>>,
    picked_folder: Mutex<Option<String>>,
}

/// Ergebnis des Trockenlaufs: Summenzeile und die (begrenzte) Liste der Einzelpfade.
#[derive(Clone)]
struct PreviewSummary {
    text: String,
    items: Arc<Vec<RestorePreviewItem>>,
}

pub struct RestoreDialog {
    pub open: bool,
    /// Startzeit der laufenden Wiederherstellung für die Restzeitschätzung.
    started: Option<Instant>,
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
            started: None,
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
        let mut open_target = false;
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
                        let edited = ui
                            .add(
                                egui::TextEdit::singleline(&mut self.target)
                                    .desired_width(420.0)
                                    .hint_text("z. B. D:\\Wiederhergestellt"),
                            )
                            .changed();
                        if edited {
                            // Eine Vorschau gilt nur für das Ziel, für das sie berechnet wurde.
                            *self.state.preview.lock().expect("Sperre") = None;
                        }
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
                                if ui.selectable_value(&mut self.overwrite, index, *label).changed() {
                                    *self.state.preview.lock().expect("Sperre") = None;
                                }
                            }
                        });
                    ui.label(theme::small(
                        palette,
                        "Das Repository wird nie verändert; es wird nur in den Zielordner geschrieben.",
                    ));
                });
                if let Some(result) = self.state.preview.lock().expect("Sperre").clone() {
                    match result {
                        Ok(summary) => {
                            ui.label(theme::secondary(palette, summary.text));
                            preview_list(ui, palette, &summary.items);
                        }
                        Err(error) => {
                            ui.label(RichText::new(error).color(palette.danger));
                        }
                    }
                }
                if busy {
                    let progress = self.state.progress.lock().expect("Sperre").clone();
                    let fraction = progress
                        .as_ref()
                        .map_or(0.0, |p| p.percent_done as f32)
                        .clamp(0.0, 1.0);
                    let elapsed = self.started.map(|started| started.elapsed());
                    let text = progress.map_or("Wiederherstellung läuft …".to_string(), |p| {
                        progress_text(&p, elapsed)
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
                        if matches!(outcome, Some(Ok(_)))
                            && ui
                                .button("Zielordner öffnen")
                                .on_hover_text("Den Zielordner im Dateimanager anzeigen")
                                .clicked()
                        {
                            open_target = true;
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
        if open_target && let Err(message) = open_folder(self.target.trim()) {
            shared.push_error(message);
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
        self.started = (!dry_run).then(Instant::now);
        let (stop, session, ctx) = (self.stop.clone(), shared.session.clone(), shared.ctx.clone());
        let (nodes, target, policy) = (
            self.nodes.clone(),
            self.target.trim().to_string(),
            OVERWRITE_OPTIONS[self.overwrite].0,
        );
        shared.spawn(async move {
            if dry_run {
                let result = session.preview_restore(&nodes, &target, policy, &stop).await;
                *state.preview.lock().expect("Sperre") = match result {
                    Ok(preview) => Some(Ok(PreviewSummary {
                        text: format!(
                            "Vorschau: {} neu, {} aktualisiert, {} unverändert{}",
                            format_count(preview.restored.max(0) as usize),
                            format_count(preview.updated.max(0) as usize),
                            format_count(preview.unchanged.max(0) as usize),
                            if preview.is_truncated { " (Liste gekürzt)" } else { "" }
                        ),
                        items: Arc::new(preview.items),
                    })),
                    // Eine abgebrochene Vorschau ist kein Fehler.
                    Err(error) if error.is_cancelled() => None,
                    Err(error) => Some(Err(guidance::describe(&error))),
                };
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

/// Liste der Einzelpfade des Trockenlaufs (virtualisiert, höchstens 10.000 Einträge).
fn preview_list(ui: &mut egui::Ui, palette: &Palette, items: &[RestorePreviewItem]) {
    if items.is_empty() {
        return;
    }
    egui::ScrollArea::vertical()
        .id_salt("restore-vorschau")
        .max_height(160.0)
        .auto_shrink([false, true])
        .show_rows(ui, 16.0, items.len(), |ui, range| {
            for item in &items[range] {
                ui.horizontal(|ui| {
                    ui.add_sized(
                        [92.0, 16.0],
                        egui::Label::new(theme::small(palette, action_text(&item.action))),
                    );
                    ui.add(egui::Label::new(theme::small(palette, item.path.as_str())).truncate());
                });
            }
        });
}

fn action_text(action: &str) -> &str {
    match action {
        "restored" => "neu",
        "updated" => "aktualisiert",
        "unchanged" => "unverändert",
        other => other,
    }
}

/// Fortschrittstext: Dateien, Datenmenge und – sobald schätzbar – die verbleibende Zeit.
fn progress_text(progress: &RestoreProgress, elapsed: Option<Duration>) -> String {
    let mut text = format!(
        "{} von {} Dateien",
        format_count(progress.files_restored.max(0) as usize),
        format_count(progress.total_files.max(0) as usize)
    );
    if progress.total_bytes > 0 {
        text.push_str(&format!(
            " · {} von {}",
            format_bytes(progress.bytes_restored.max(0)),
            format_bytes(progress.total_bytes)
        ));
    }
    // Erst ab 2 % und nach 3 Sekunden ist die Hochrechnung brauchbar.
    let done = progress.percent_done;
    if let Some(elapsed) = elapsed.filter(|e| e.as_secs() >= 3)
        && (0.02..1.0).contains(&done)
    {
        let remaining = elapsed.as_secs_f64() * (1.0 - done) / done;
        text.push_str(&format!(" · noch etwa {}", format_remaining(remaining)));
    }
    text
}

fn format_remaining(seconds: f64) -> String {
    let seconds = seconds.max(0.0).round() as u64;
    if seconds < 60 {
        format!("{} s", seconds.max(1))
    } else if seconds < 3600 {
        format!("{} min", seconds.div_ceil(60))
    } else {
        format!("{} h {} min", seconds / 3600, (seconds % 3600) / 60)
    }
}

/// Zeigt einen Ordner im Dateimanager. Der Start erfolgt ohne Shell mit dem Pfad als einzelnem Argument.
fn open_folder(path: &str) -> Result<(), String> {
    if !std::path::Path::new(path).is_dir() {
        return Err(format!("Der Zielordner „{path}“ wurde nicht gefunden."));
    }
    let program = if cfg!(windows) { "explorer.exe" } else { "xdg-open" };
    let mut child = std::process::Command::new(program)
        .arg(path)
        .spawn()
        .map_err(|error| format!("Der Dateimanager konnte nicht gestartet werden: {error}"))?;
    // Auf das Ende warten, damit unter Linux kein Zombie-Prozess zurückbleibt.
    std::thread::spawn(move || {
        let _ = child.wait();
    });
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn progress(percent: f64, files: (i64, i64), bytes: (i64, i64)) -> RestoreProgress {
        RestoreProgress {
            percent_done: percent,
            files_restored: files.0,
            total_files: files.1,
            bytes_restored: bytes.0,
            total_bytes: bytes.1,
            ..RestoreProgress::default()
        }
    }

    #[test]
    fn progress_shows_files_bytes_and_remaining_time() {
        let half = progress(0.5, (1500, 3000), (1024 * 1024, 2 * 1024 * 1024));
        assert_eq!(
            progress_text(&half, Some(Duration::from_secs(90))),
            "1.500 von 3.000 Dateien · 1 MB von 2 MB · noch etwa 2 min"
        );
        assert_eq!(
            progress_text(&half, None),
            "1.500 von 3.000 Dateien · 1 MB von 2 MB"
        );
    }

    #[test]
    fn remaining_time_needs_a_reliable_basis() {
        let early = progress(0.01, (1, 100), (0, 0));
        assert_eq!(
            progress_text(&early, Some(Duration::from_secs(60))),
            "1 von 100 Dateien"
        );
        let quick = progress(0.5, (1, 2), (0, 0));
        assert_eq!(
            progress_text(&quick, Some(Duration::from_secs(1))),
            "1 von 2 Dateien"
        );
        let done = progress(1.0, (2, 2), (0, 0));
        assert_eq!(
            progress_text(&done, Some(Duration::from_secs(60))),
            "2 von 2 Dateien"
        );
    }

    #[test]
    fn remaining_time_is_formatted_in_seconds_minutes_and_hours() {
        assert_eq!(format_remaining(0.2), "1 s");
        assert_eq!(format_remaining(45.0), "45 s");
        assert_eq!(format_remaining(61.0), "2 min");
        assert_eq!(format_remaining(3725.0), "1 h 2 min");
    }

    #[test]
    fn preview_actions_are_translated_and_missing_folders_are_reported() {
        assert_eq!(action_text("restored"), "neu");
        assert_eq!(action_text("etwas"), "etwas");
        assert!(
            open_folder("/gibt/es/wirklich/nicht")
                .unwrap_err()
                .contains("nicht gefunden")
        );
    }
}
