//! UI-Spike mit egui/eframe: Gegenstück zum Slint-Spike (gleiche Prüfpunkte).
//!
//! Aufruf: `egui-ui-spike [--dark] [--rows N] [--select von bis] [--click x y] [--hover x y]
//!          [--loading] [--modal] [--dialog] [--bench] [--hold sekunden] [--screenshot pfad]`

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use eframe::egui::{self, Color32, RichText, Sense, SystemTheme, ViewportCommand};
use egui_extras::{Column, TableBuilder};

struct Item {
    name: String,
    is_dir: bool,
    size: u64,
    modified: i64,
    selected: bool,
}

#[derive(Default, Clone)]
struct Options {
    dark: bool,
    rows: usize,
    select: Option<(usize, usize)>,
    click: Option<(f32, f32)>,
    hover: Option<(f32, f32)>,
    loading: bool,
    modal: bool,
    dialog: bool,
    bench: bool,
    hold: Option<f64>,
    screenshot: Option<String>,
}

fn parse_options() -> Options {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut o = Options { rows: 100_000, ..Options::default() };
    let num = |i: usize| args.get(i).and_then(|v| v.parse::<f32>().ok()).unwrap_or(0.0);
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--dark" => o.dark = true,
            "--loading" => o.loading = true,
            "--modal" => o.modal = true,
            "--dialog" => o.dialog = true,
            "--bench" => o.bench = true,
            "--rows" => {
                o.rows = num(i + 1) as usize;
                i += 1;
            }
            "--select" => {
                o.select = Some((num(i + 1) as usize, num(i + 2) as usize));
                i += 2;
            }
            "--click" => {
                o.click = Some((num(i + 1), num(i + 2)));
                i += 2;
            }
            "--hover" => {
                o.hover = Some((num(i + 1), num(i + 2)));
                i += 2;
            }
            "--hold" => {
                o.hold = Some(f64::from(num(i + 1)));
                i += 1;
            }
            "--screenshot" => {
                o.screenshot = args.get(i + 1).cloned();
                i += 1;
            }
            _ => {}
        }
        i += 1;
    }
    o
}

fn format_size(bytes: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let (mut value, mut unit) = (bytes as f64, 0);
    while value >= 1024.0 && unit < UNITS.len() - 1 {
        value /= 1024.0;
        unit += 1;
    }
    let text = format!("{value:.1}");
    format!("{} {}", text.strip_suffix(".0").unwrap_or(&text).replace('.', ","), UNITS[unit])
}

fn format_time(minutes: i64) -> String {
    let days = minutes / (24 * 60);
    let (hour, minute) = ((minutes / 60) % 24, minutes % 60);
    let day_of_year = days % 365;
    format!("{:02}.{:02}.{} {hour:02}:{minute:02}", day_of_year % 28 + 1, day_of_year / 28 % 12 + 1, 2020 + days / 365)
}

fn icon_for(item: &Item) -> &'static str {
    if item.is_dir {
        "📁"
    } else {
        match item.name.rsplit('.').next().unwrap_or_default() {
            "png" | "jpg" => "🖼",
            "pdf" | "txt" | "md" => "📝",
            "zip" | "gz" => "📦",
            _ => "📄",
        }
    }
}

fn sample_items(count: usize) -> Vec<Item> {
    let extensions = ["txt", "pdf", "png", "zip", "md", "jpg", "dat", "gz"];
    (0..count)
        .map(|i| {
            let is_dir = i % 25 == 0;
            Item {
                name: if is_dir {
                    format!("Ordner {i:05}")
                } else {
                    format!("Datei mit längerem Namen {i:06}.{}", extensions[i % extensions.len()])
                },
                is_dir,
                size: ((i as u64 * 7919) % 5_000_000) + 12,
                modified: ((i as i64 * 104_729) % 2_000_000) + 1,
                selected: false,
            }
        })
        .collect()
}

struct SpikeApp {
    options: Options,
    dark: bool,
    items: Vec<Item>,
    anchor: usize,
    cursor: usize,
    sort_column: usize,
    ascending: bool,
    snapshots: Vec<(String, String, String)>,
    current_snapshot: usize,
    filters_open: bool,
    filter_text: String,
    host_index: usize,
    only_latest: bool,
    loading: bool,
    modal_open: bool,
    dialog_open: bool,
    status: String,
    frame: u64,
    shot_done: Arc<AtomicBool>,
    frame_times: Vec<f64>,
}

impl SpikeApp {
    fn new(options: Options) -> Self {
        let started = Instant::now();
        let mut app = Self {
            dark: options.dark,
            items: sample_items(options.rows),
            anchor: 0,
            cursor: 0,
            sort_column: 0,
            ascending: true,
            snapshots: (0..200)
                .map(|i| {
                    (
                        format!("{:02}.11.2026 {:02}:{:02} · {}", 28 - (i % 28), 6 + i % 12, (i * 7) % 60, ["arbeits-pc", "nas", "laptop"][i % 3]),
                        format!("{} · {:08x}", ["D:\\Dokumente", "/home/anna", "C:\\Users\\Anna"][i % 3], 0x9e37_79b9u32.wrapping_mul(i as u32 + 1)),
                        String::new(),
                    )
                })
                .collect(),
            current_snapshot: 0,
            filters_open: true,
            filter_text: String::new(),
            host_index: 0,
            only_latest: false,
            loading: options.loading,
            modal_open: options.modal,
            dialog_open: options.dialog,
            status: "200 Snapshot(s) geladen".into(),
            frame: 0,
            shot_done: Arc::new(AtomicBool::new(false)),
            frame_times: Vec::new(),
            options,
        };
        app.sort();
        println!("METRIK Modellaufbau {} Zeilen: {} ms", app.items.len(), started.elapsed().as_millis());
        if let Some((from, to)) = app.options.select {
            let to = to.min(app.items.len().saturating_sub(1));
            app.set_range(from, to, false);
        }
        app
    }

    fn sort(&mut self) {
        let (column, ascending) = (self.sort_column, self.ascending);
        self.items.sort_by(|a, b| {
            let ordering = match column {
                1 => a.is_dir.cmp(&b.is_dir),
                2 => a.size.cmp(&b.size),
                3 => a.modified.cmp(&b.modified),
                _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
            };
            b.is_dir.cmp(&a.is_dir).then(if ascending { ordering } else { ordering.reverse() })
        });
        self.anchor = 0;
        self.cursor = 0;
    }

    fn set_range(&mut self, from: usize, to: usize, keep_others: bool) {
        let (low, high) = (from.min(to), from.max(to));
        for (index, item) in self.items.iter_mut().enumerate() {
            item.selected = (low..=high).contains(&index) || (keep_others && item.selected);
        }
    }

    fn selection_summary(&self) -> (usize, String) {
        let selected: Vec<&Item> = self.items.iter().filter(|i| i.selected).collect();
        let bytes: u64 = selected.iter().filter(|i| !i.is_dir).map(|i| i.size).sum();
        let folders = selected.iter().filter(|i| i.is_dir).count();
        let text = if selected.is_empty() {
            "Nichts ausgewählt".to_string()
        } else {
            format!("{} Element(e) ausgewählt ({folders} Ordner, {})", selected.len(), format_size(bytes))
        };
        (selected.len(), text)
    }

    fn row_clicked(&mut self, index: usize, ctrl: bool, shift: bool) {
        if shift {
            self.set_range(self.anchor, index, ctrl);
        } else if ctrl {
            self.items[index].selected = !self.items[index].selected;
            self.anchor = index;
        } else {
            self.set_range(index, index, false);
            self.anchor = index;
        }
        self.cursor = index;
    }

    fn apply_theme(&self, ctx: &egui::Context) {
        ctx.set_visuals(if self.dark { egui::Visuals::dark() } else { egui::Visuals::light() });
        // Die Titelleiste folgt der App-Einstellung statt dem System.
        ctx.send_viewport_cmd(ViewportCommand::SetTheme(if self.dark { SystemTheme::Dark } else { SystemTheme::Light }));
    }

    fn run_bench(&mut self) {
        let started = Instant::now();
        self.row_clicked(10, false, false);
        self.row_clicked(5_000, false, true);
        let select = Instant::now();
        let last = self.items.len().saturating_sub(1);
        self.set_range(0, last, false);
        println!("METRIK Alles auswählen ({} Zeilen): {} ms", self.items.len(), select.elapsed().as_millis());
        for column in [2, 2, 0] {
            let sort = Instant::now();
            self.ascending = if self.sort_column == column { !self.ascending } else { true };
            self.sort_column = column;
            self.sort();
            println!("METRIK Sortieren (Spalte {column}): {} ms", sort.elapsed().as_millis());
        }
        self.row_clicked(0, false, false);
        self.row_clicked(99_999.min(self.items.len() - 1), false, true);
        println!("METRIK Bench gesamt: {} ms", started.elapsed().as_millis());
    }
}

impl eframe::App for SpikeApp {
    fn raw_input_hook(&mut self, _ctx: &egui::Context, raw_input: &mut egui::RawInput) {
        // Synthetische Zeigerereignisse für Hover- und Klick-Aufnahmen.
        if self.frame == 15 {
            if let Some((x, y)) = self.options.click {
                let pos = egui::pos2(x, y);
                raw_input.events.push(egui::Event::PointerMoved(pos));
                for pressed in [true, false] {
                    raw_input.events.push(egui::Event::PointerButton {
                        pos,
                        button: egui::PointerButton::Primary,
                        pressed,
                        modifiers: egui::Modifiers::NONE,
                    });
                }
            }
        }
        if self.frame == 20 {
            if let Some((x, y)) = self.options.hover {
                raw_input.events.push(egui::Event::PointerMoved(egui::pos2(x, y)));
            }
        }
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        let frame_started = Instant::now();
        if self.frame == 0 {
            self.apply_theme(&ctx);
            if self.options.bench {
                self.run_bench();
            }
        }
        self.frame += 1;
        if self.frame == 6 {
            // Zweiter Versuch: das Betriebssystem-Fenster existiert erst nach den ersten Bildern.
            self.apply_theme(&ctx);
        }

        // Tastatur: Alles auswählen und Pfeiltasten (nur wenn kein Textfeld den Fokus hat).
        if !ctx.egui_wants_keyboard_input() {
            if ctx.input_mut(|i| i.consume_key(egui::Modifiers::COMMAND, egui::Key::A)) {
                let last = self.items.len().saturating_sub(1);
                self.set_range(0, last, false);
            }
            let shift = ctx.input(|i| i.modifiers.shift);
            for (key, delta) in [(egui::Key::ArrowDown, 1i64), (egui::Key::ArrowUp, -1)] {
                if ctx.input_mut(|i| i.consume_key(egui::Modifiers::NONE, key) || i.consume_key(egui::Modifiers::SHIFT, key)) {
                    let target = (self.cursor as i64 + delta).clamp(0, self.items.len() as i64 - 1) as usize;
                    if shift {
                        self.set_range(self.anchor, target, false);
                    } else {
                        self.set_range(target, target, false);
                        self.anchor = target;
                    }
                    self.cursor = target;
                }
            }
        }

        let accent = if self.dark { Color32::from_rgb(0x60, 0xcd, 0xff) } else { Color32::from_rgb(0x00, 0x67, 0xc0) };
        let on_accent = if self.dark { Color32::BLACK } else { Color32::WHITE };

        egui::Panel::top("header").show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.heading("Restic Browser");
                ui.label(RichText::new("Repository: D:\\Backups\\Dokumente · restic 0.19.1 (In Restic Browser enthalten)").small().weak());
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.add(egui::Button::new(RichText::new("Verbinden …").color(on_accent)).fill(accent)).on_hover_text("Mit einem Repository verbinden (Strg+O)").clicked() {}
                    ui.add_enabled(false, egui::Button::new("Trennen"));
                    let label = if self.dark { "Helles Design" } else { "Dunkles Design" };
                    if ui.button(label).on_hover_text("Zwischen hellem und dunklem Design wechseln").clicked() {
                        self.dark = !self.dark;
                        self.apply_theme(&ctx);
                    }
                });
            });
        });

        egui::Panel::top("tools").show(ui, |ui| {
            ui.horizontal(|ui| {
                for label in ["Integrität prüfen", "Statistik", "Speicheranalyse"] {
                    if ui.add(egui::Button::new(label).frame(false)).on_hover_text("Seltenere Repository-Werkzeuge").clicked() && label == "Integrität prüfen" {
                        self.dialog_open = true;
                    }
                }
                ui.add_enabled(false, egui::Button::new("Als Laufwerk einbinden").frame(false));
            });
        });

        egui::Panel::bottom("status").show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(RichText::new(&self.status).small().weak());
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let text = if self.loading { "Laden beenden" } else { "Laden simulieren" };
                    if ui.add(egui::Button::new(text).frame(false)).clicked() {
                        self.loading = !self.loading;
                    }
                    if self.loading {
                        ui.add(egui::ProgressBar::new(0.4).desired_width(180.0).animate(true));
                    }
                });
            });
        });

        egui::Panel::left("snapshots").default_size(300.0).show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(RichText::new("Snapshots").strong());
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.add(egui::Button::new(if self.filters_open { "Filter ▲" } else { "Filter ▼" }).frame(false)).clicked() {
                        self.filters_open = !self.filters_open;
                    }
                });
            });
            if self.filters_open {
                ui.add(egui::TextEdit::singleline(&mut self.filter_text).hint_text("Snapshots durchsuchen").desired_width(f32::INFINITY));
                let hosts = ["Alle Hosts", "arbeits-pc", "laptop", "nas"];
                egui::ComboBox::from_id_salt("host").width(ui.available_width()).selected_text(hosts[self.host_index]).show_ui(ui, |ui| {
                    for (index, host) in hosts.iter().enumerate() {
                        ui.selectable_value(&mut self.host_index, index, *host);
                    }
                });
                ui.checkbox(&mut self.only_latest, "Nur neueste je Host und Pfad");
            }
            ui.separator();
            let row_height = 44.0;
            egui::ScrollArea::vertical().auto_shrink([false, false]).show_rows(ui, row_height, self.snapshots.len(), |ui, range| {
                for index in range {
                    let (title, subtitle, _) = &self.snapshots[index];
                    let selected = index == self.current_snapshot;
                    let response = ui.add_sized(
                        [ui.available_width(), row_height - 4.0],
                        egui::Button::selectable(selected, RichText::new(format!("{title}\n{subtitle}")).size(12.0)).frame_when_inactive(false),
                    );
                    if response.clicked() {
                        self.current_snapshot = index;
                    }
                }
            });
        });

        egui::CentralPanel::default().show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label("Snapshot › Dokumente › Projekte › Restic");
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let _ = ui.button("Suchen");
                    let mut search = String::new();
                    ui.add(egui::TextEdit::singleline(&mut search).hint_text("Im Snapshot suchen").desired_width(220.0));
                });
            });
            let (count, summary) = self.selection_summary();
            egui::Panel::bottom("actions").show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.label(RichText::new(&summary).weak());
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui
                            .add_enabled(count > 0, egui::Button::new(RichText::new("Wiederherstellen …").color(on_accent)).fill(accent))
                            .on_hover_text("Ausgewählte Elemente wiederherstellen")
                            .clicked()
                        {
                            self.modal_open = true;
                        }
                        ui.add_enabled(count == 1, egui::Button::new("Dateiversionen"));
                        ui.add_enabled(count == 1, egui::Button::new("Vorschau"));
                    });
                });
            });
            self.file_table(ui);
        });

        self.dialogs(&ctx, accent, on_accent);

        // Aufnahme: nach ein paar Bildern, wenn Layout und Zustände stabil sind.
        if self.options.screenshot.is_some() || self.options.hold.is_some() || self.options.bench {
            ctx.request_repaint(); // Nur für Aufnahme- und Halte-Modus, nicht im Normalbetrieb.
        }
        if let Some(path) = self.options.screenshot.clone() {
            if self.frame == 40 {
                let done = Arc::clone(&self.shot_done);
                ctx.request_screenshot(move |image| {
                    save_png(&image, &path);
                    done.store(true, Ordering::SeqCst);
                });
            }
            if self.shot_done.load(Ordering::SeqCst) {
                ctx.send_viewport_cmd(ViewportCommand::Close);
            }
        }
        if let Some(seconds) = self.options.hold {
            if ctx.input(|i| i.time) > seconds {
                ctx.send_viewport_cmd(ViewportCommand::Close);
            }
        }
        if self.options.bench && self.frame > 30 && self.frame <= 130 {
            self.frame_times.push(frame_started.elapsed().as_secs_f64() * 1000.0);
            ctx.request_repaint();
            if self.frame == 130 {
                let average = self.frame_times.iter().sum::<f64>() / self.frame_times.len() as f64;
                let worst = self.frame_times.iter().cloned().fold(0.0, f64::max);
                println!("METRIK UI-Aufbau je Bild (100 Bilder, {} Zeilen): Mittel {average:.2} ms, schlechtestes {worst:.2} ms", self.items.len());
                ctx.send_viewport_cmd(ViewportCommand::Close);
            }
        }
        if self.loading {
            ctx.request_repaint(); // Endlosanimation nur während echter Ladezustände.
        }
    }
}

impl SpikeApp {
    fn file_table(&mut self, ui: &mut egui::Ui) {
        let mut clicked: Option<(usize, bool, bool)> = None;
        let mut sort_click: Option<usize> = None;
        let modifiers = ui.input(|i| i.modifiers);
        let (sort_column, ascending) = (self.sort_column, self.ascending);
        let items = &self.items;
        TableBuilder::new(ui)
            .striped(true)
            .resizable(true)
            .sense(Sense::click())
            .column(Column::initial(340.0).at_least(60.0).clip(true))
            .column(Column::initial(110.0).at_least(60.0).clip(true))
            .column(Column::initial(100.0).at_least(60.0).clip(true))
            .column(Column::remainder().at_least(60.0).clip(true))
            .header(28.0, |mut header| {
                for (index, label) in ["Name", "Typ", "Größe", "Geändert"].iter().enumerate() {
                    header.col(|ui| {
                        let arrow = if index == sort_column { if ascending { " ▲" } else { " ▼" } } else { "" };
                        if ui.add(egui::Label::new(RichText::new(format!("{label}{arrow}")).small().strong()).sense(Sense::click())).clicked() {
                            sort_click = Some(index);
                        }
                    });
                }
            })
            .body(|body| {
                body.rows(26.0, items.len(), |mut row| {
                    let index = row.index();
                    let item = &items[index];
                    row.set_selected(item.selected);
                    row.col(|ui| {
                        let text = format!("{} {}", icon_for(item), item.name);
                        ui.label(if item.is_dir { RichText::new(text).strong() } else { RichText::new(text) });
                    });
                    row.col(|ui| {
                        ui.label(RichText::new(if item.is_dir { "Ordner" } else { "Datei" }).weak());
                    });
                    row.col(|ui| {
                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                            ui.label(RichText::new(if item.is_dir { "—".to_string() } else { format_size(item.size) }).weak());
                        });
                    });
                    row.col(|ui| {
                        ui.label(RichText::new(format_time(item.modified)).weak());
                    });
                    if row.response().clicked() {
                        clicked = Some((index, modifiers.command, modifiers.shift));
                    }
                });
            });
        if let Some(column) = sort_click {
            let started = Instant::now();
            self.ascending = if self.sort_column == column { !self.ascending } else { true };
            self.sort_column = column;
            self.sort();
            println!("METRIK Sortieren: {} ms", started.elapsed().as_millis());
        }
        if let Some((index, ctrl, shift)) = clicked {
            self.row_clicked(index, ctrl, shift);
        }
    }

    fn dialogs(&mut self, ctx: &egui::Context, accent: Color32, on_accent: Color32) {
        if self.modal_open {
            let (_, summary) = self.selection_summary();
            let mut close = false;
            #[allow(deprecated)]
            let modal_id = egui::Id::new("restore");
            egui::Modal::new(modal_id).show(ctx, |ui| {
                ui.set_width(480.0);
                ui.heading("Wiederherstellen");
                ui.label(RichText::new(summary).weak());
                let mut target = "D:\\Wiederhergestellt".to_string();
                ui.add(egui::TextEdit::singleline(&mut target).desired_width(f32::INFINITY));
                if ui.button("Ordner wählen …").on_hover_text("Systemdialog zur Ordnerauswahl").clicked() {
                    // Getestet wird hier nur, dass der native Dialog eingebunden werden kann.
                    let _ = rfd::FileDialog::new().set_title("Wiederherstellungsziel wählen");
                }
                egui::ComboBox::from_id_salt("overwrite")
                    .width(ui.available_width())
                    .selected_text("Vorhandene Dateien nicht überschreiben")
                    .show_ui(ui, |ui| {
                        for label in ["Vorhandene Dateien nicht überschreiben", "Nur wenn neuer", "Wenn geändert", "Immer überschreiben"] {
                            let _ = ui.selectable_label(false, label);
                        }
                    });
                let mut preview = true;
                ui.checkbox(&mut preview, "Vorschau der Änderungen anzeigen");
                ui.horizontal(|ui| {
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui.add(egui::Button::new(RichText::new("Wiederherstellen").color(on_accent)).fill(accent)).clicked() {
                            close = true;
                        }
                        if ui.button("Abbrechen").clicked() {
                            close = true;
                        }
                    });
                });
            });
            if close || ctx.input(|i| i.key_pressed(egui::Key::Escape)) {
                self.modal_open = false;
            }
        }
        if self.dialog_open {
            let dark = self.dark;
            let mut close = false;
            let id = egui::ViewportId::from_hash_of("pruefung");
            let first = ctx.data_mut(|d| d.get_temp::<bool>(id.into()).is_none());
            ctx.show_viewport_immediate(
                id,
                egui::ViewportBuilder::default().with_title("Integritätsprüfung (UI-Spike)").with_inner_size([460.0, 220.0]),
                |ui, _class| {
                    egui::CentralPanel::default().show(ui, |ui| {
                        ui.heading("Integritätsprüfung");
                        ui.label(RichText::new("Es wurden keine Fehler gefunden.").weak());
                        ui.add_space(ui.available_height() - 36.0);
                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                            if ui.button("Schließen").clicked() {
                                close = true;
                            }
                        });
                    });
                    if ui.input(|i| i.viewport().close_requested()) {
                        close = true;
                    }
                },
            );
            if first {
                ctx.data_mut(|d| d.insert_temp(id.into(), true));
                ctx.send_viewport_cmd_to(id, ViewportCommand::SetTheme(if dark { SystemTheme::Dark } else { SystemTheme::Light }));
            }
            if close {
                self.dialog_open = false;
                ctx.data_mut(|d| d.remove_temp::<bool>(id.into()));
            }
        }
    }
}

fn save_png(image: &egui::ColorImage, path: &str) {
    let [width, height] = image.size;
    let mut bytes = Vec::with_capacity(width * height * 4);
    for pixel in &image.pixels {
        bytes.extend_from_slice(&pixel.to_array());
    }
    let file = std::fs::File::create(path).expect("Screenshot-Datei");
    let mut encoder = png::Encoder::new(std::io::BufWriter::new(file), width as u32, height as u32);
    encoder.set_color(png::ColorType::Rgba);
    encoder.set_depth(png::BitDepth::Eight);
    encoder.write_header().expect("PNG-Kopf").write_image_data(&bytes).expect("PNG-Daten");
    println!("Screenshot: {path} ({width}x{height})");
}

fn main() -> eframe::Result {
    let options = parse_options();
    let native = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("Restic Browser (egui-Spike)")
            .with_inner_size([1056.0, 760.0])
            .with_min_inner_size([760.0, 480.0]),
        ..eframe::NativeOptions::default()
    };
    eframe::run_native("Restic Browser (egui-Spike)", native, Box::new(move |_cc| Ok(Box::new(SpikeApp::new(options.clone())))))
}
