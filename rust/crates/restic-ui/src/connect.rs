//! Verbindungsdialog: Repository-Ordner, Passwort und optional eine ausdrücklich gewählte Restic-Datei.
//! Das Passwort lebt nur im Arbeitsspeicher, wird nie protokolliert oder gespeichert und nach Erfolg
//! oder Schließen überschrieben.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText};
use restic_core::guidance;
use restic_core::{RepositoryProfile, SessionCredentials};
use tokio_util::sync::CancellationToken;
use zeroize::Zeroize;

use crate::shared::Shared;
use crate::theme::{self, Palette};

/// Entfernt Zeilenumbrüche am Ende eingefügter Texte (nur dort; innere Umbrüche behandelt das Feld selbst).
fn strip_trailing_line_breaks(events: &mut [egui::Event]) {
    for event in events {
        if let egui::Event::Paste(text) = event {
            let kept = text.trim_end_matches(['\r', '\n']).len();
            text.truncate(kept);
        }
    }
}

#[derive(Default)]
struct ConnectState {
    busy: AtomicBool,
    done: AtomicBool,
    error: Mutex<Option<String>>,
    picked_folder: Mutex<Option<String>>,
    picked_restic: Mutex<Option<String>>,
}

pub struct ConnectDialog {
    pub open: bool,
    repository: String,
    password: String,
    restic_path: String,
    advanced: bool,
    selected_profile: Option<String>,
    focus_password: bool,
    state: Arc<ConnectState>,
}

impl ConnectDialog {
    pub fn new() -> Self {
        let pw = String::new();
        Self {
            open: false,
            repository: String::new(),
            password: pw,
            restic_path: String::new(),
            advanced: false,
            selected_profile: None,
            focus_password: false,
            state: Arc::new(ConnectState::default()),
        }
    }

    /// Öffnet den Dialog; das zuletzt verbundene Profil ist vorgewählt, der Fokus liegt im Passwortfeld.
    pub fn open_with(&mut self, profiles: &[RepositoryProfile], last_profile_id: Option<&str>) {
        self.open = true;
        self.focus_password = true;
        *self.state.error.lock().expect("Sperre") = None;
        self.state.done.store(false, Ordering::SeqCst);
        if let Some(profile) = last_profile_id.and_then(|id| profiles.iter().find(|p| p.id == id)) {
            self.select_profile(profile);
        }
    }

    fn select_profile(&mut self, profile: &RepositoryProfile) {
        self.selected_profile = Some(profile.id.clone());
        self.repository = profile.repository.clone();
        self.restic_path = profile.restic_executable.clone().unwrap_or_default();
        self.advanced = !self.restic_path.is_empty();
    }

    fn close(&mut self) {
        self.password.zeroize();
        self.open = false;
    }

    pub fn ui(
        &mut self,
        ctx: &egui::Context,
        shared: &Arc<Shared>,
        palette: &Palette,
        profiles: &[RepositoryProfile],
    ) {
        if !self.open {
            return;
        }
        if self.state.done.swap(false, Ordering::SeqCst) {
            self.close();
            return;
        }
        if let Some(folder) = self.state.picked_folder.lock().expect("Sperre").take() {
            self.repository = folder;
            self.selected_profile = None;
        }
        if let Some(file) = self.state.picked_restic.lock().expect("Sperre").take() {
            self.restic_path = file;
        }
        let busy = self.state.busy.load(Ordering::SeqCst);
        let mut submit = false;
        let mut cancel = false;
        let modal_id = egui::Id::NULL.with("verbinden");
        egui::Modal::new(modal_id)
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(520.0);
                ui.label(theme::title("Mit Repository verbinden"));
                ui.add_space(4.0);
                ui.label(theme::secondary(
                    palette,
                    "Wähle den Ordner eines vorhandenen Restic-Repositorys und gib sein Passwort ein.",
                ));
                ui.add_space(6.0);
                ui.add_enabled_ui(!busy, |ui| {
                    if !profiles.is_empty() {
                        let current = profiles
                            .iter()
                            .find(|p| Some(&p.id) == self.selected_profile.as_ref())
                            .map_or("Neues Repository", |p| p.display_name());
                        egui::ComboBox::from_label("Gespeichertes Profil")
                            .width(280.0)
                            .selected_text(current)
                            .show_ui(ui, |ui| {
                                for profile in profiles {
                                    if ui
                                        .selectable_label(
                                            Some(&profile.id) == self.selected_profile.as_ref(),
                                            profile.display_name(),
                                        )
                                        .clicked()
                                    {
                                        self.select_profile(profile);
                                    }
                                }
                            });
                    }
                    ui.label("Repository-Ordner");
                    ui.horizontal(|ui| {
                        ui.add(
                            egui::TextEdit::singleline(&mut self.repository)
                                .desired_width(380.0)
                                .hint_text("z. B. D:\\Backups\\Dokumente"),
                        );
                        if ui.button("Ordner wählen …").clicked() {
                            let state = Arc::clone(&self.state);
                            let ctx = shared.ctx.clone();
                            shared.handle.spawn_blocking(move || {
                                if let Some(folder) = rfd::FileDialog::new()
                                    .set_title("Repository-Ordner wählen")
                                    .pick_folder()
                                {
                                    *state.picked_folder.lock().expect("Sperre") =
                                        Some(folder.to_string_lossy().into_owned());
                                }
                                ctx.request_repaint();
                            });
                        }
                    });
                    ui.label("Passwort");
                    // Einfügen per Strg+V: Ein angehängter Zeilenumbruch (Passwortmanager, Terminal) würde im
                    // einzeiligen Feld zu einem Leerzeichen und damit zu einem falschen Passwort.
                    let password_id = ui.make_persistent_id("passwort");
                    if ui.memory(|m| m.has_focus(password_id)) {
                        ui.input_mut(|i| strip_trailing_line_breaks(&mut i.events));
                    }
                    let response = ui
                        .add(
                            egui::TextEdit::singleline(&mut self.password)
                                .id(password_id)
                                .password(true)
                                .desired_width(f32::INFINITY),
                        )
                        .on_hover_text("Strg+V fügt das Passwort aus der Zwischenablage ein");
                    if std::mem::take(&mut self.focus_password) {
                        response.request_focus();
                    }
                    if response.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                        submit = true;
                    }
                    ui.collapsing("Erweitert", |ui| {
                        ui.label("Restic-Datei (leer: automatisch, sonst genau diese Datei)");
                        ui.horizontal(|ui| {
                            ui.add(egui::TextEdit::singleline(&mut self.restic_path).desired_width(380.0));
                            if ui.button("Datei wählen …").clicked() {
                                let state = Arc::clone(&self.state);
                                let ctx = shared.ctx.clone();
                                shared.handle.spawn_blocking(move || {
                                    if let Some(file) = rfd::FileDialog::new()
                                        .set_title("Restic-Datei wählen")
                                        .pick_file()
                                    {
                                        *state.picked_restic.lock().expect("Sperre") =
                                            Some(file.to_string_lossy().into_owned());
                                    }
                                    ctx.request_repaint();
                                });
                            }
                        });
                    });
                });
                if let Some(error) = self.state.error.lock().expect("Sperre").clone() {
                    ui.add_space(4.0);
                    ui.label(RichText::new(error).color(palette.danger));
                }
                if busy {
                    ui.horizontal(|ui| {
                        ui.spinner();
                        ui.label(theme::secondary(palette, "Verbindung wird aufgebaut …"));
                    });
                }
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let can_submit = !busy && !self.repository.trim().is_empty() && !self.password.is_empty();
                    if ui
                        .add_enabled(can_submit, theme::primary_button(palette, "Verbinden"))
                        .clicked()
                    {
                        submit = true;
                    }
                    if ui.button("Abbrechen").clicked() {
                        cancel = true;
                    }
                });
            });
        if cancel || (!busy && ctx.input(|i| i.key_pressed(egui::Key::Escape))) {
            self.close();
        } else if submit && !busy && !self.repository.trim().is_empty() && !self.password.is_empty() {
            self.submit(shared, profiles);
        }
    }

    fn submit(&mut self, shared: &Arc<Shared>, profiles: &[RepositoryProfile]) {
        let repository = self.repository.trim().to_string();
        let configured = Some(self.restic_path.trim().to_string()).filter(|path| !path.is_empty());
        let mut profile = self
            .selected_profile
            .as_ref()
            .and_then(|id| profiles.iter().find(|p| &p.id == id).cloned())
            .unwrap_or_else(|| RepositoryProfile::new(&repository));
        profile.repository = repository.clone();
        profile.restic_executable = configured.clone();
        if profile.name.trim().is_empty() {
            profile.name = repository
                .rsplit(['\\', '/'])
                .find(|part| !part.is_empty())
                .unwrap_or(&repository)
                .to_string();
        }
        let credentials = SessionCredentials::new(self.password.clone(), HashMap::new());
        let state = Arc::clone(&self.state);
        state.busy.store(true, Ordering::SeqCst);
        *state.error.lock().expect("Sperre") = None;
        let session = shared.session.clone();
        shared.spawn(async move {
            let outcome = async {
                let resolver = crate::embedded::resolver();
                let info = resolver
                    .resolve(configured.as_deref(), &CancellationToken::new())
                    .await?;
                profile.resolved_restic_executable = Some(info.path.to_string_lossy().into_owned());
                profile.resolved_restic_source = Some(info.source);
                session.connect(profile, credentials).await
            }
            .await;
            match outcome {
                Ok(()) => state.done.store(true, Ordering::SeqCst),
                Err(error) => *state.error.lock().expect("Sperre") = Some(guidance::describe(&error)),
            }
            state.busy.store(false, Ordering::SeqCst);
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn paste(text: &str) -> egui::Event {
        egui::Event::Paste(text.to_string())
    }

    #[test]
    fn trailing_line_breaks_are_removed_from_pasted_text() {
        let mut events = [
            paste("geheim\n"),
            paste("a b\r\n\r\n"),
            paste("ohne"),
            egui::Event::Copy,
        ];
        strip_trailing_line_breaks(&mut events);
        let texts: Vec<&str> = events
            .iter()
            .filter_map(|e| {
                if let egui::Event::Paste(t) = e {
                    Some(t.as_str())
                } else {
                    None
                }
            })
            .collect();
        assert_eq!(texts, ["geheim", "a b", "ohne"]);
    }

    #[test]
    fn inner_spaces_and_edges_of_a_password_are_kept() {
        let mut events = [paste("  Pa ss  wort \n")];
        strip_trailing_line_breaks(&mut events);
        assert!(matches!(&events[0], egui::Event::Paste(t) if t == "  Pa ss  wort "));
    }

    /// Ein Paste-Ereignis erreicht das fokussierte Passwortfeld und wird ohne Zeilenumbruch übernommen.
    #[test]
    fn paste_event_fills_a_focused_password_field() {
        let ctx = egui::Context::default();
        let mut pw = String::new();
        let frame = |events: Vec<egui::Event>, pw_field: &mut String| {
            let input = egui::RawInput {
                events,
                ..Default::default()
            };
            let mut output = ctx.run_ui(input, |ui| {
                let id = ui.make_persistent_id("passwort");
                if ui.memory(|m| m.has_focus(id)) {
                    ui.input_mut(|i| strip_trailing_line_breaks(&mut i.events));
                }
                let response = ui.add(egui::TextEdit::singleline(pw_field).id(id).password(true));
                if !ui.memory(|m| m.has_focus(id)) {
                    response.request_focus();
                }
            });
            output.textures_delta.clear();
        };
        frame(vec![], &mut pw);
        frame(vec![paste("geheim 123\n")], &mut pw);
        assert_eq!(pw, "geheim 123");
    }
}
