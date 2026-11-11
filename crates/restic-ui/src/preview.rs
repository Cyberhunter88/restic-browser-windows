//! Dateivorschau: Text, Bilder oder eine verständliche Fehlermeldung. Der Inhalt wird nur gelesen
//! (`restic dump`, bis 5 MB) und nie geschrieben.

use std::sync::{Arc, Mutex};

use eframe::egui::{self, RichText, TextWrapMode};
use restic_core::guidance;
use restic_core::models::{BackupNode, FilePreviewData, FileVersion};
use tokio_util::sync::CancellationToken;

use crate::shared::Shared;
use crate::theme::{self, Palette};

const MAXIMUM_LINE_CHARS: usize = 2_000;
const BODY_HEIGHT: f32 = 420.0;

type Loaded = Result<FilePreviewData, String>;

#[derive(Default)]
struct PreviewState {
    result: Mutex<Option<Loaded>>,
}

enum Content {
    Loading,
    Error(String),
    Text(Vec<String>),
    Image { uri: String, bytes: Arc<[u8]> },
}

pub struct PreviewDialog {
    pub open: bool,
    node: BackupNode,
    path: String,
    state: Arc<PreviewState>,
    stop: CancellationToken,
    content: Content,
    generation: u64,
}

impl PreviewDialog {
    pub fn new() -> Self {
        Self {
            open: false,
            node: BackupNode::default(),
            path: String::new(),
            state: Arc::new(PreviewState::default()),
            stop: CancellationToken::new(),
            content: Content::Loading,
            generation: 0,
        }
    }

    /// Ist der Inhalt geladen (oder der Ladefehler bekannt)?
    pub fn is_ready(&self) -> bool {
        !matches!(self.content, Content::Loading)
    }

    pub fn open_node(&mut self, shared: &Arc<Shared>, node: BackupNode) {
        self.start(&node, node.path.clone());
        let (session, state, stop) = (shared.session.clone(), Arc::clone(&self.state), self.stop.clone());
        shared.spawn(async move {
            let result = session.get_file_preview(&node, &stop).await;
            *state.result.lock().expect("Sperre") = Some(result.map_err(|error| guidance::describe(&error)));
        });
    }

    pub fn open_version(&mut self, shared: &Arc<Shared>, version: FileVersion) {
        self.start(&version.node, version.node.path.clone());
        let (session, state, stop) = (shared.session.clone(), Arc::clone(&self.state), self.stop.clone());
        shared.spawn(async move {
            let result = session.get_file_preview_of_version(&version, &stop).await;
            *state.result.lock().expect("Sperre") = Some(result.map_err(|error| guidance::describe(&error)));
        });
    }

    fn start(&mut self, node: &BackupNode, path: String) {
        self.stop.cancel();
        self.open = true;
        self.node = node.clone();
        self.path = path;
        self.content = Content::Loading;
        self.generation += 1;
        self.stop = CancellationToken::new();
        self.state = Arc::new(PreviewState::default());
    }

    fn adopt_result(&mut self) {
        if !matches!(self.content, Content::Loading) {
            return;
        }
        let Some(result) = self.state.result.lock().expect("Sperre").take() else {
            return;
        };
        self.content = match result {
            Err(message) => Content::Error(message),
            Ok(data) => content_from(&data, self.generation),
        };
    }

    pub fn ui(&mut self, ctx: &egui::Context, palette: &Palette, escape_allowed: bool) {
        if !self.open {
            return;
        }
        self.adopt_result();
        let mut close = false;
        egui::Modal::new(egui::Id::NULL.with("vorschau"))
            .frame(theme::dialog_frame(ctx))
            .show(ctx, |ui| {
                ui.set_width(760.0);
                ui.label(theme::title(self.node.display_name()));
                ui.add(egui::Label::new(theme::secondary(palette, &self.path)).truncate());
                ui.horizontal(|ui| {
                    ui.label(RichText::new("Größe:").strong());
                    ui.label(self.node.size_text());
                    ui.add_space(16.0);
                    ui.label(RichText::new("Geändert:").strong());
                    ui.label(modified_text(&self.node));
                });
                ui.separator();
                match &self.content {
                    Content::Loading => {
                        ui.horizontal(|ui| {
                            ui.spinner();
                            ui.label(theme::secondary(palette, "Vorschau wird geladen …"));
                        });
                        ui.add_space(BODY_HEIGHT - 30.0);
                    }
                    Content::Error(message) => {
                        ui.label(RichText::new(message).color(palette.danger));
                        ui.add_space(BODY_HEIGHT - 30.0);
                    }
                    Content::Text(lines) => {
                        let row_height = 16.0;
                        egui::ScrollArea::both()
                            .max_height(BODY_HEIGHT)
                            .auto_shrink([false, false])
                            .show_rows(ui, row_height, lines.len(), |ui, range| {
                                for line in &lines[range] {
                                    ui.add(
                                        egui::Label::new(RichText::new(line).monospace())
                                            .wrap_mode(TextWrapMode::Extend),
                                    );
                                }
                            });
                    }
                    Content::Image { uri, bytes } => {
                        let source = egui::ImageSource::Bytes {
                            uri: uri.clone().into(),
                            bytes: egui::load::Bytes::from(Arc::clone(bytes)),
                        };
                        let image = egui::Image::new(source)
                            .max_size(egui::vec2(720.0, BODY_HEIGHT))
                            .fit_to_original_size(1.0);
                        match image.load_for_size(ui.ctx(), egui::vec2(720.0, BODY_HEIGHT)) {
                            Err(error) => {
                                ui.label(
                                    RichText::new(format!("Bild konnte nicht gerendert werden: {error}"))
                                        .color(palette.danger),
                                );
                            }
                            Ok(_) => {
                                ui.centered_and_justified(|ui| {
                                    ui.set_height(BODY_HEIGHT);
                                    ui.add(image);
                                });
                            }
                        }
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

fn modified_text(node: &BackupNode) -> String {
    node.modified.map_or_else(
        || "Unbekannt".to_string(),
        |time| time.format("%d.%m.%Y %H:%M:%S %:z").to_string(),
    )
}

fn content_from(data: &FilePreviewData, generation: u64) -> Content {
    if !data.error_message.trim().is_empty() {
        return Content::Error(data.error_message.clone());
    }
    if data.is_image {
        return match data.image_bytes.as_deref() {
            Some(bytes) if !bytes.is_empty() => Content::Image {
                uri: format!("bytes://vorschau-{generation}-{}", data.path),
                bytes: Arc::from(bytes),
            },
            _ => Content::Error("Das Bild ist leer.".to_string()),
        };
    }
    if data.is_text {
        let text = data.text_content.as_deref().unwrap_or_default();
        return if looks_binary(text) {
            Content::Error("Keine Textvorschau: Die Datei enthält Binärdaten.".to_string())
        } else {
            Content::Text(prepare_lines(text))
        };
    }
    Content::Error("Keine Vorschau für diesen Dateityp verfügbar.".to_string())
}

/// Binärdaten erkennt man an NUL-Zeichen oder vielen Ersatzzeichen (ungültiges UTF-8).
pub fn looks_binary(text: &str) -> bool {
    let sample: String = text.chars().take(8_192).collect();
    let replacements = sample.chars().filter(|&c| c == '\u{fffd}').count();
    sample.contains('\0') || (!sample.is_empty() && replacements * 20 > sample.chars().count())
}

/// Zerlegt Text in Zeilen und kürzt sehr lange Zeilen, damit die Darstellung schnell bleibt.
pub fn prepare_lines(text: &str) -> Vec<String> {
    text.split('\n')
        .map(|line| {
            let line = line.trim_end_matches('\r');
            if line.chars().count() > MAXIMUM_LINE_CHARS {
                let mut shortened: String = line.chars().take(MAXIMUM_LINE_CHARS).collect();
                shortened.push_str(" … (Zeile gekürzt)");
                shortened
            } else {
                line.replace('\t', "    ")
            }
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lines_are_split_trimmed_and_tabs_expanded() {
        assert_eq!(prepare_lines("a\r\n\tb\nc"), ["a", "    b", "c"]);
        assert_eq!(prepare_lines(""), [""]);
    }

    #[test]
    fn very_long_lines_are_shortened() {
        let lines = prepare_lines(&"x".repeat(5_000));
        assert!(lines[0].chars().count() < 2_100);
        assert!(lines[0].ends_with("(Zeile gekürzt)"));
    }

    #[test]
    fn binary_content_is_recognised_but_text_with_umlauts_is_not() {
        assert!(looks_binary("abc\0def"));
        assert!(looks_binary(&"\u{fffd}".repeat(500)));
        assert!(!looks_binary("Übung mit Ümläuten und ß\nZweite Zeile"));
        assert!(!looks_binary(""));
    }

    #[test]
    fn content_selection_follows_the_preview_data() {
        let base = FilePreviewData {
            path: "/a".into(),
            ..FilePreviewData::default()
        };
        let error = FilePreviewData {
            error_message: "Zu groß".into(),
            ..base.clone()
        };
        assert!(matches!(content_from(&error, 1), Content::Error(m) if m == "Zu groß"));
        let text = FilePreviewData {
            is_text: true,
            text_content: Some("a\nb".into()),
            ..base.clone()
        };
        assert!(matches!(content_from(&text, 1), Content::Text(lines) if lines.len() == 2));
        let binary = FilePreviewData {
            is_text: true,
            text_content: Some("a\0b".into()),
            ..base.clone()
        };
        assert!(matches!(content_from(&binary, 1), Content::Error(m) if m.contains("Binärdaten")));
        let image = FilePreviewData {
            is_image: true,
            image_bytes: Some(vec![1, 2, 3]),
            ..base.clone()
        };
        assert!(matches!(content_from(&image, 7), Content::Image { uri, .. } if uri.contains("vorschau-7")));
        let empty_image = FilePreviewData {
            is_image: true,
            image_bytes: Some(vec![]),
            ..base.clone()
        };
        assert!(matches!(content_from(&empty_image, 1), Content::Error(_)));
        assert!(matches!(content_from(&base, 1), Content::Error(m) if m.contains("Keine Vorschau")));
    }

    #[test]
    fn modified_time_is_formatted_or_unknown() {
        let unknown = BackupNode::default();
        assert_eq!(modified_text(&unknown), "Unbekannt");
        let node: BackupNode = serde_json_like("2026-11-06T22:43:07+01:00");
        assert_eq!(modified_text(&node), "06.11.2026 22:43:07 +01:00");
    }

    fn serde_json_like(time: &str) -> BackupNode {
        BackupNode {
            modified: Some(chrono::DateTime::parse_from_rfc3339(time).unwrap()),
            ..BackupNode::default()
        }
    }
}
