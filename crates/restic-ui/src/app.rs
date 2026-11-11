//! Hauptfenster: Verbindung, Snapshot-Auswahl und Dateibrowser mit den primären Dateiaktionen.
//! Der Zustand liegt in `restic_app::BrowserSession`; die Oberfläche liest ihn pro Bild und führt
//! Nutzeraktionen erst nach dem Zeichnen aus.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use eframe::egui::{self, Align, Align2, FontId, Layout, RichText, Sense, vec2};
use restic_app::Change;
use restic_core::models::{BackupNode, PathSegment, SnapshotInfo, format_relative_time};
use restic_core::repository::format_count;

use crate::analysis::AnalysisDialog;
use crate::check::CheckDialog;
use crate::connect::ConnectDialog;
use crate::dev::{DevAction, DevOptions};
use crate::file_table;
use crate::icons;
use crate::preview::PreviewDialog;
use crate::restore::RestoreDialog;
use crate::shared::Shared;
use crate::stats::StatsDialog;
use crate::table_model::TableModel;
use crate::theme::{self, Theme};
use crate::versions::{VersionsAction, VersionsDialog};

/// Kleine, pro Bild kopierte Teile des Sitzungszustands (große Listen werden direkt gelesen).
struct Small {
    connected: bool,
    busy: bool,
    status: String,
    path: String,
    segments: Vec<PathSegment>,
    can_up: bool,
    can_back: bool,
    can_forward: bool,
    is_search: bool,
    repository_label: String,
    snapshot_count: usize,
    visible_count: usize,
    hosts: Vec<String>,
    tags: Vec<String>,
    selected_snapshot: Option<String>,
    last_restore_target: Option<String>,
}

pub struct ResticBrowserApp {
    shared: Arc<Shared>,
    theme: Theme,
    frame: u64,
    table: TableModel,
    nodes_revision: Arc<AtomicU64>,
    seen_revision: u64,
    last_rebuild: Instant,
    summary: (u64, String),
    scroll_to: Option<usize>,
    connect: ConnectDialog,
    restore: RestoreDialog,
    preview: PreviewDialog,
    versions: VersionsDialog,
    newest_match: Arc<Mutex<Option<BackupNode>>>,
    window: Option<restic_app::settings::WindowSettings>,
    check: CheckDialog,
    stats: StatsDialog,
    analysis: AnalysisDialog,
    search_text: String,
    filter_text: String,
    filter_host: usize,
    filter_tag: usize,
    only_latest: bool,
    filters_open: bool,
    last_error: Option<String>,
    initial_dialog_done: bool,
    dev: DevOptions,
}

impl ResticBrowserApp {
    pub fn new(shared: Arc<Shared>, nodes_revision: Arc<AtomicU64>, dev: DevOptions) -> Self {
        let theme = if dev.dark { Theme::Dark } else { Theme::Light };
        let session = shared.session.clone();
        shared.spawn_reporting(async move { session.initialize().await });
        Self {
            shared,
            theme,
            frame: 0,
            table: TableModel::default(),
            nodes_revision,
            seen_revision: u64::MAX,
            last_rebuild: Instant::now(),
            summary: (u64::MAX, String::new()),
            scroll_to: None,
            connect: ConnectDialog::new(),
            restore: RestoreDialog::new(),
            preview: PreviewDialog::new(),
            versions: VersionsDialog::new(),
            newest_match: Arc::new(Mutex::new(None)),
            window: None,
            check: CheckDialog::new(),
            stats: StatsDialog::new(),
            analysis: AnalysisDialog::new(),
            search_text: String::new(),
            filter_text: String::new(),
            filter_host: 0,
            filter_tag: 0,
            only_latest: false,
            filters_open: false,
            last_error: None,
            initial_dialog_done: false,
            dev,
        }
    }

    fn small(&self) -> Small {
        self.shared.session.view(|v| {
            let repository_label = match (v.active_profile(), v.validated_restic_version()) {
                (Some(profile), Some(version)) => format!(
                    "{} · {} · restic {} ({})",
                    profile.display_name(),
                    profile.repository,
                    version.version,
                    v.validated_restic_source().unwrap_or("")
                ),
                _ => String::new(),
            };
            Small {
                connected: v.is_connected(),
                busy: v.is_busy(),
                status: v.status().to_string(),
                path: v.current_path().to_string(),
                segments: v.path_segments(),
                can_up: v.can_go_up(),
                can_back: v.can_go_back(),
                can_forward: v.can_go_forward(),
                is_search: v.is_search_result(),
                repository_label,
                snapshot_count: v.snapshots().len(),
                visible_count: v.visible_snapshots().len(),
                hosts: v.available_hosts().to_vec(),
                tags: v.available_tags().to_vec(),
                selected_snapshot: v.selected_snapshot().map(|s| s.id.clone()),
                last_restore_target: v.last_restore_target().map(str::to_string),
            }
        })
    }

    fn open_connect(&mut self) {
        let (profiles, last) = self
            .shared
            .session
            .view(|v| (v.profiles().to_vec(), v.last_profile_id().map(str::to_string)));
        self.connect.open_with(&profiles, last.as_deref());
    }

    fn dialog_open(&self) -> bool {
        self.connect.open
            || self.restore.open
            || self.preview.open
            || self.versions.open
            || self.check.open
            || self.stats.open
            || self.analysis.open
    }

    fn run_session<F, Fut>(&self, task: F)
    where
        F: FnOnce(crate::shared::Session) -> Fut,
        Fut: std::future::Future<Output = Result<(), restic_core::ResticError>> + Send + 'static,
    {
        let future = task(self.shared.session.clone());
        self.shared.spawn_reporting(async move {
            match future.await {
                Err(error) if error.is_cancelled() => Ok(()),
                other => other.map_err(|error| restic_core::guidance::describe(&error)),
            }
        });
    }

    fn rebuild_table_if_needed(&mut self, busy: bool) {
        let revision = self.nodes_revision.load(Ordering::SeqCst);
        if revision == self.seen_revision
            || (busy && self.last_rebuild.elapsed() < Duration::from_millis(150))
        {
            return;
        }
        self.shared.session.view(|v| self.table.rebuild(v.nodes()));
        self.seen_revision = revision;
        self.last_rebuild = Instant::now();
    }

    fn handle_keys(&mut self, ctx: &egui::Context, small: &Small) {
        if self.dialog_open() || ctx.egui_wants_keyboard_input() {
            return;
        }
        let (command, shift, alt) = ctx.input(|i| (i.modifiers.command, i.modifiers.shift, i.modifiers.alt));
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::COMMAND, egui::Key::O)) {
            self.open_connect();
        }
        if !small.connected {
            return;
        }
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::NONE, egui::Key::F5)) {
            self.run_session(|s| async move { s.refresh_snapshots().await });
        }
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::COMMAND, egui::Key::A)) {
            self.table.select_all();
        }
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::COMMAND, egui::Key::F)) {
            ctx.memory_mut(|m| m.request_focus(egui::Id::NULL.with("suche")));
        }
        for (key, delta) in [(egui::Key::ArrowDown, 1i64), (egui::Key::ArrowUp, -1)] {
            if !alt
                && ctx.input_mut(|i| {
                    i.consume_key(egui::Modifiers::NONE, key) || i.consume_key(egui::Modifiers::SHIFT, key)
                })
            {
                self.table.move_cursor(delta, shift);
                self.scroll_to = Some(self.table.cursor_row());
            }
        }
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::NONE, egui::Key::Enter))
            && self.table.selected_count() == 1
        {
            self.open_selected();
        }
        if alt
            && ctx.input_mut(|i| i.consume_key(egui::Modifiers::ALT, egui::Key::ArrowLeft))
            && small.can_back
        {
            self.run_session(|s| async move { s.go_back().await });
        }
        if alt
            && ctx.input_mut(|i| i.consume_key(egui::Modifiers::ALT, egui::Key::ArrowRight))
            && small.can_forward
        {
            self.run_session(|s| async move { s.go_forward().await });
        }
        if alt && ctx.input_mut(|i| i.consume_key(egui::Modifiers::ALT, egui::Key::ArrowUp)) && small.can_up {
            self.run_session(|s| async move { s.go_up().await });
        }
        if ctx.input_mut(|i| i.consume_key(egui::Modifiers::NONE, egui::Key::Escape)) {
            if small.busy {
                self.shared.session.cancel();
            } else {
                self.table.clear_selection();
            }
        }
        let _ = command;
    }

    fn run_row_action(&mut self, action: file_table::RowAction, small: &Small) {
        use file_table::RowAction;
        match action {
            RowAction::Open => self.open_selected(),
            RowAction::Preview => {
                if let Some(node) = self.single_file() {
                    self.preview.open_node(&self.shared, node);
                }
            }
            RowAction::Versions => {
                if let Some(node) = self.single_file() {
                    self.versions.open_with(&self.shared, node);
                }
            }
            RowAction::Restore => {
                let nodes = self.selected_nodes();
                let summary = self.shared.session.view(|v| self.table.summary(v.nodes()));
                self.restore
                    .open_with(nodes, summary, small.last_restore_target.as_deref());
            }
        }
    }

    /// Sucht die neueste Version zum Suchtext; der Treffer kommt über `newest_match` zurück.
    fn find_newest(&mut self) {
        let pattern = self.search_text.clone();
        let slot = Arc::clone(&self.newest_match);
        let session = self.shared.session.clone();
        self.shared.spawn_reporting(async move {
            if let Some(node) = session.find_newest(&pattern).await? {
                *slot.lock().expect("Sperre") = Some(node);
            }
            Ok::<(), restic_core::ResticError>(())
        });
    }

    /// Merkt die zuletzt bekannte Fenstergeometrie (im maximierten Zustand bleibt die normale Größe erhalten).
    fn track_window(&mut self, ctx: &egui::Context) {
        if self.dev.is_active() {
            return;
        }
        ctx.input(|input| {
            let viewport = input.viewport();
            let maximized = viewport.maximized.unwrap_or(false);
            let mut window = self
                .window
                .clone()
                .unwrap_or(restic_app::settings::WindowSettings {
                    width: 1240.0,
                    height: 780.0,
                    x: None,
                    y: None,
                    is_maximized: false,
                });
            window.is_maximized = maximized;
            if !maximized {
                if let Some(rect) = viewport.inner_rect {
                    window.width = f64::from(rect.width());
                    window.height = f64::from(rect.height());
                }
                if let Some(rect) = viewport.outer_rect {
                    window.x = Some(rect.min.x.round() as i32);
                    window.y = Some(rect.min.y.round() as i32);
                }
            }
            self.window = Some(window);
        });
    }

    fn open_selected(&mut self) {
        let selected = self.table.selected_nodes();
        if let [index] = selected[..] {
            let node = self.shared.session.view(|v| v.nodes().get(index).cloned());
            if let Some(node) = node {
                self.open_node(node);
            }
        }
    }

    /// Ordner werden geöffnet, Dateien in der Vorschau gezeigt.
    fn open_node(&mut self, node: BackupNode) {
        if node.is_directory() {
            self.run_session(|s| async move { s.open_node(&node).await });
        } else {
            self.preview.open_node(&self.shared, node);
        }
    }

    /// Wie in der C#-Linie: Der Snapshot der Version wird ausgewählt, danach öffnet die Wiederherstellung.
    fn restore_version(&mut self, version: restic_core::models::FileVersion, last_target: Option<&str>) {
        {
            let _guard = self.shared.enter();
            self.shared
                .session
                .select_snapshot(Some(version.snapshot.clone()));
        }
        self.versions.close();
        let when = version
            .snapshot
            .time
            .map(|t| t.format("%d.%m.%Y %H:%M").to_string())
            .unwrap_or_default();
        self.restore.open_with(
            vec![version.node.clone()],
            format!("Version vom {when} ({})", version.snapshot.hostname),
            last_target,
        );
    }

    /// Die einzige ausgewählte Datei (keine Ordner), sonst `None`.
    fn single_file(&self) -> Option<BackupNode> {
        let selected = self.table.selected_nodes();
        let [index] = selected[..] else { return None };
        self.shared
            .session
            .view(|v| v.nodes().get(index).cloned())
            .filter(|node| !node.is_directory())
    }

    fn selected_nodes(&self) -> Vec<BackupNode> {
        let indices = self.table.selected_nodes();
        self.shared.session.view(|v| {
            indices
                .iter()
                .filter_map(|&i| v.nodes().get(i).cloned())
                .collect()
        })
    }

    // --- Bereiche ---

    fn header(&mut self, ui: &mut egui::Ui, small: &Small) {
        let palette = self.theme.palette();
        egui::Panel::top("kopf").show(ui, |ui| {
            ui.add_space(4.0);
            ui.horizontal(|ui| {
                ui.label(theme::title("Restic Browser"));
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if small.connected {
                        if ui
                            .add(egui::Button::new("Verbinden …"))
                            .on_hover_text("Mit einem anderen Repository verbinden (Strg+O)")
                            .clicked()
                        {
                            self.open_connect();
                        }
                        if ui
                            .button("Trennen")
                            .on_hover_text("Verbindung trennen und Passwort aus dem Speicher entfernen")
                            .clicked()
                        {
                            self.shared.session.disconnect();
                            self.table = TableModel::default();
                        }
                    } else if ui
                        .add(theme::primary_button(&palette, "Verbinden …"))
                        .on_hover_text("Mit einem Repository verbinden (Strg+O)")
                        .clicked()
                    {
                        self.open_connect();
                    }
                    let label = if self.theme.is_dark() {
                        "Helles Design"
                    } else {
                        "Dunkles Design"
                    };
                    if ui
                        .button(label)
                        .on_hover_text("Zwischen hellem und dunklem Design wechseln")
                        .clicked()
                    {
                        self.theme = self.theme.toggled();
                        theme::apply(ui.ctx(), self.theme);
                    }
                    // Der Rest der Zeile gehört der Repository-Angabe; sie wird gekürzt, nie unter Schaltflächen gelegt.
                    if small.connected {
                        ui.with_layout(Layout::left_to_right(Align::Center), |ui| {
                            ui.add(
                                egui::Label::new(theme::small(&palette, &small.repository_label)).truncate(),
                            );
                        });
                    }
                });
            });
            if small.connected {
                ui.horizontal(|ui| {
                    ui.label(theme::small(&palette, "Werkzeuge"));
                    let has_snapshot = small.selected_snapshot.is_some();
                    if ui
                        .add_enabled(has_snapshot, theme::quiet_button("Speicheranalyse"))
                        .on_hover_text("Größen und Dateikategorien des ausgewählten Snapshots auswerten")
                        .clicked()
                    {
                        self.analysis.open_dialog(&self.shared);
                    }
                    if ui
                        .add(theme::quiet_button("Repository prüfen"))
                        .on_hover_text("Integrität des Repositorys prüfen (nur lesend)")
                        .clicked()
                    {
                        self.check.open_dialog();
                    }
                    if ui
                        .add(theme::quiet_button("Statistik"))
                        .on_hover_text("Repository-Statistik berechnen (nur auf Anforderung)")
                        .clicked()
                    {
                        self.stats.open_dialog(&self.shared);
                    }
                });
            }
            ui.add_space(4.0);
        });
    }

    fn status_bar(&mut self, ui: &mut egui::Ui, small: &Small) {
        let palette = self.theme.palette();
        egui::Panel::bottom("status").show(ui, |ui| {
            ui.horizontal(|ui| {
                if let Some(error) = self.last_error.clone() {
                    ui.add(egui::Label::new(RichText::new(error).color(palette.danger)).truncate());
                    if ui.add(theme::quiet_button("Ausblenden")).clicked() {
                        self.last_error = None;
                    }
                } else {
                    ui.add(egui::Label::new(theme::small(&palette, &small.status)).truncate());
                }
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if small.busy {
                        if ui
                            .add(theme::quiet_button("Abbrechen"))
                            .on_hover_text("Laufenden Vorgang abbrechen (Esc)")
                            .clicked()
                        {
                            self.shared.session.cancel();
                        }
                        ui.add(egui::ProgressBar::new(0.4).desired_width(160.0).animate(true));
                    }
                });
            });
        });
    }

    fn snapshot_panel(&mut self, ui: &mut egui::Ui, small: &Small) {
        let palette = self.theme.palette();
        egui::Panel::left("snapshots")
            .default_size(320.0)
            .size_range(240.0..=520.0)
            .show(ui, |ui| {
                ui.add_space(4.0);
                ui.horizontal(|ui| {
                    ui.label(theme::section_title("Snapshots"));
                    ui.label(theme::small(
                        &palette,
                        format!(
                            "{} von {}",
                            format_count(small.visible_count),
                            format_count(small.snapshot_count)
                        ),
                    ));
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        let response = ui
                            .add(theme::quiet_button("Filter"))
                            .on_hover_text("Snapshot-Filter ein- oder ausklappen");
                        icons::arrow(ui, self.filters_open, palette.secondary_text);
                        if response.clicked() {
                            self.filters_open = !self.filters_open;
                        }
                    });
                });
                if self.filters_open {
                    self.filters(ui, small);
                }
                ui.separator();
                self.snapshot_list(ui, small);
            });
    }

    fn filters(&mut self, ui: &mut egui::Ui, small: &Small) {
        if ui
            .add(
                egui::TextEdit::singleline(&mut self.filter_text)
                    .hint_text("Snapshots durchsuchen")
                    .desired_width(f32::INFINITY),
            )
            .changed()
        {
            let _guard = self.shared.enter();
            self.shared.session.set_snapshot_filter(self.filter_text.clone());
        }
        for (id, entries, selected, apply) in [
            ("host", &small.hosts, &mut self.filter_host, 0),
            ("tag", &small.tags, &mut self.filter_tag, 1),
        ] {
            let current = entries.get(*selected).cloned().unwrap_or_default();
            let mut changed = None;
            egui::ComboBox::from_id_salt(id)
                .width(ui.available_width())
                .selected_text(current)
                .show_ui(ui, |ui| {
                    for (index, entry) in entries.iter().enumerate() {
                        if ui.selectable_value(selected, index, entry).clicked() {
                            changed = Some(entry.clone());
                        }
                    }
                });
            if let Some(value) = changed {
                let _guard = self.shared.enter();
                if apply == 0 {
                    self.shared.session.set_filter_host(value);
                } else {
                    self.shared.session.set_filter_tag(value);
                }
            }
        }
        if ui
            .checkbox(&mut self.only_latest, "Nur neueste je Host und Pfad")
            .changed()
        {
            let _guard = self.shared.enter();
            self.shared.session.set_filter_only_latest(self.only_latest);
        }
    }

    fn snapshot_list(&mut self, ui: &mut egui::Ui, small: &Small) {
        let palette = self.theme.palette();
        let row_height = 62.0;
        let mut chosen: Option<SnapshotInfo> = None;
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .ok()
            .and_then(|d| chrono::DateTime::from_timestamp(i64::try_from(d.as_secs()).unwrap_or(0), 0))
            .map(|t| t.fixed_offset());
        self.shared.session.view(|v| {
            let snapshots = v.visible_snapshots();
            egui::ScrollArea::vertical()
                .auto_shrink([false, false])
                .show_rows(ui, row_height, snapshots.len(), |ui, range| {
                    for index in range {
                        let snapshot = &snapshots[index];
                        let (rect, response) = ui.allocate_exact_size(
                            vec2(ui.available_width(), row_height - 4.0),
                            Sense::click(),
                        );
                        let selected = small.selected_snapshot.as_deref() == Some(snapshot.id.as_str());
                        let fill = if selected {
                            palette.selected
                        } else if response.hovered() {
                            palette.hover
                        } else {
                            egui::Color32::TRANSPARENT
                        };
                        ui.painter().rect_filled(rect, 4.0, fill);
                        let when = snapshot
                            .time
                            .map(|t| t.format("%d.%m.%Y %H:%M").to_string())
                            .unwrap_or_default();
                        let relative = snapshot
                            .time
                            .zip(now)
                            .map(|(t, n)| format_relative_time(t, n))
                            .unwrap_or_default();
                        let painter = ui.painter().with_clip_rect(rect.shrink(2.0));
                        painter.text(
                            rect.left_top() + vec2(8.0, 6.0),
                            Align2::LEFT_TOP,
                            format!("{when} · {relative}"),
                            FontId::proportional(14.0),
                            palette.text,
                        );
                        painter.text(
                            rect.left_top() + vec2(8.0, 25.0),
                            Align2::LEFT_TOP,
                            format!("{} · {}", snapshot.hostname, snapshot.path_text()),
                            FontId::proportional(12.0),
                            palette.secondary_text,
                        );
                        painter.text(
                            rect.left_top() + vec2(8.0, 41.0),
                            Align2::LEFT_TOP,
                            format!(
                                "{} · {} · {}",
                                snapshot.tag_text(),
                                snapshot.size_text(),
                                snapshot.display_id()
                            ),
                            FontId::proportional(12.0),
                            palette.secondary_text,
                        );
                        if response.clicked() {
                            chosen = Some(snapshot.clone());
                        }
                    }
                });
        });
        if let Some(snapshot) = chosen {
            let _guard = self.shared.enter();
            self.shared.session.select_snapshot(Some(snapshot));
        }
    }

    fn browser(&mut self, ui: &mut egui::Ui, small: &Small) {
        let palette = self.theme.palette();
        // Navigation und Pfadleiste
        ui.horizontal(|ui| {
            if ui
                .add_enabled(small.can_back, theme::quiet_button("Zurück"))
                .on_hover_text("Zum vorigen Ordner (Alt+Links)")
                .clicked()
            {
                self.run_session(|s| async move { s.go_back().await });
            }
            if ui
                .add_enabled(small.can_forward, theme::quiet_button("Vor"))
                .on_hover_text("Wieder vor (Alt+Rechts)")
                .clicked()
            {
                self.run_session(|s| async move { s.go_forward().await });
            }
            if ui
                .add_enabled(small.can_up, theme::quiet_button("Nach oben"))
                .on_hover_text("Zum übergeordneten Ordner (Alt+Hoch)")
                .clicked()
            {
                self.run_session(|s| async move { s.go_up().await });
            }
            ui.separator();
            let mut target: Option<String> = None;
            if small.is_search {
                ui.label(theme::section_title(&small.path));
            } else {
                for (index, segment) in small.segments.iter().enumerate() {
                    if index > 0 {
                        ui.label(theme::small(&palette, "›"));
                    }
                    if ui.add(theme::quiet_button(&segment.name)).clicked() {
                        target = Some(segment.path.clone());
                    }
                }
            }
            if let Some(path) = target {
                self.run_session(|s| async move { s.load_directory(&path).await });
            }
        });
        ui.horizontal(|ui| {
            let response = ui.add(
                egui::TextEdit::singleline(&mut self.search_text)
                    .id(egui::Id::NULL.with("suche"))
                    .hint_text("Im Snapshot suchen (Strg+F)")
                    .desired_width(300.0),
            );
            let enter = response.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter));
            if (ui
                .add_enabled(!self.search_text.trim().is_empty(), egui::Button::new("Suchen"))
                .clicked()
                || enter)
                && !self.search_text.trim().is_empty()
            {
                let pattern = self.search_text.clone();
                self.run_session(|s| async move { s.search(&pattern).await });
            }
            if ui
                .add_enabled(
                    !self.search_text.trim().is_empty(),
                    egui::Button::new("Neueste Version wiederherstellen …"),
                )
                .on_hover_text(
                    "Die neueste Version der gesuchten Datei in allen Snapshots finden und wiederherstellen",
                )
                .clicked()
            {
                self.find_newest();
            }
        });
        ui.add_space(2.0);

        // Aktionsleiste direkt am Dateibrowser: primäre Dateiaktionen.
        let (count, summary) = {
            let version = self.table.selection_version();
            if self.summary.0 != version {
                let text = self.shared.session.view(|v| self.table.summary(v.nodes()));
                self.summary = (version, text);
            }
            (self.table.selected_count(), self.summary.1.clone())
        };
        let single_file = if count == 1 { self.single_file() } else { None };
        egui::Panel::bottom("aktionen").show(ui, |ui| {
            ui.add_space(2.0);
            ui.horizontal(|ui| {
                ui.label(theme::secondary(&palette, summary.as_str()));
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let ready = count > 0 && small.selected_snapshot.is_some();
                    if ui
                        .add_enabled(ready, theme::primary_button(&palette, "Wiederherstellen …"))
                        .on_hover_text("Ausgewählte Elemente in einen Zielordner wiederherstellen")
                        .clicked()
                    {
                        let nodes = self.selected_nodes();
                        self.restore
                            .open_with(nodes, summary.clone(), small.last_restore_target.as_deref());
                    }
                    let versions_clicked = ui
                        .add_enabled(single_file.is_some(), egui::Button::new("Dateiversionen"))
                        .on_hover_text("Alle Versionen der ausgewählten Datei in den Snapshots zeigen")
                        .clicked();
                    let preview_clicked = ui
                        .add_enabled(single_file.is_some(), egui::Button::new("Vorschau"))
                        .on_hover_text("Inhalt der ausgewählten Datei ansehen (Eingabetaste)")
                        .clicked();
                    if let Some(node) = single_file.clone() {
                        if versions_clicked {
                            self.versions.open_with(&self.shared, node);
                        } else if preview_clicked {
                            self.preview.open_node(&self.shared, node);
                        }
                    }
                });
            });
            ui.add_space(2.0);
        });

        let menu_state = file_table::MenuState {
            can_open: count == 1,
            can_preview: single_file.is_some(),
            can_restore: count > 0 && small.selected_snapshot.is_some(),
        };

        // Tabelle
        let scroll_to = self.scroll_to.take();
        let output = self.shared.session.view(|v| {
            // Länge und Knotenliste müssen übereinstimmen, sonst zeigt die Tabelle Reste des alten Ordners.
            if v.nodes().len() != self.table.len() {
                self.table.rebuild(v.nodes());
                self.seen_revision = self.nodes_revision.load(Ordering::SeqCst);
            }
            file_table::show(ui, &self.table, v.nodes(), &palette, scroll_to, menu_state)
        });
        if self.table.is_empty() && !small.busy {
            ui.add_space(12.0);
            ui.vertical_centered(|ui| {
                ui.label(theme::secondary(
                    &palette,
                    if small.is_search {
                        "Keine Treffer"
                    } else {
                        "Dieser Ordner ist leer"
                    },
                ));
            });
        }
        if let Some(column) = output.sort {
            self.shared
                .session
                .view(|v| self.table.toggle_sort(column, v.nodes()));
        }
        if let Some((row, ctrl, shift)) = output.clicked {
            self.table.click(row, ctrl, shift);
        }
        if let Some(action) = output.menu {
            self.run_row_action(action, small);
        }
        if let Some(row) = output.activated {
            let index = self.table.node_index(row);
            if let Some(node) = self.shared.session.view(|v| v.nodes().get(index).cloned()) {
                self.open_node(node);
            }
        }
    }

    fn empty_state(&mut self, ui: &mut egui::Ui) {
        let palette = self.theme.palette();
        let profiles = self.shared.session.view(|v| v.profiles().to_vec());
        ui.vertical_centered(|ui| {
            ui.add_space(ui.available_height() * 0.28);
            ui.label(theme::title("Noch mit keinem Repository verbunden"));
            ui.label(theme::secondary(&palette, "Restic Browser durchsucht vorhandene Restic-Repositorys und stellt ausgewählte Dateien wieder her."));
            ui.add_space(10.0);
            if ui.add(theme::primary_button(&palette, "Mit Repository verbinden …")).clicked() {
                self.open_connect();
            }
            if !profiles.is_empty() {
                ui.add_space(12.0);
                ui.label(theme::small(&palette, "Gespeicherte Profile"));
                for profile in profiles.iter().take(6) {
                    if ui.add(theme::quiet_button(format!("{} · {}", profile.display_name(), profile.repository))).clicked() {
                        self.connect.open_with(&profiles, Some(&profile.id));
                    }
                }
            }
        });
    }
}

impl eframe::App for ResticBrowserApp {
    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        // Beim Beenden Fenstergröße und -position speichern; Fehler dürfen das Schließen nicht verhindern.
        if let Some(window) = self.window.clone() {
            let session = self.shared.session.clone();
            let _ = self
                .shared
                .handle
                .block_on(async move { session.save_window_settings(window).await });
        }
    }

    fn raw_input_hook(&mut self, _ctx: &egui::Context, raw_input: &mut egui::RawInput) {
        self.dev.inject_events(self.frame, raw_input);
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        if self.frame == 0 {
            theme::apply(&ctx, self.theme);
            // Nur Decoder für Bilder aus dem Speicher; keine Datei- oder Netzwerk-Lader.
            egui_extras::install_image_loaders(&ctx);
        }
        self.frame += 1;
        if self.frame == 6 {
            // Das Betriebssystem-Fenster existiert erst nach den ersten Bildern.
            theme::apply(&ctx, self.theme);
        }
        if let Some(error) = self.shared.take_error() {
            self.last_error = Some(error);
        }

        let small = self.small();
        self.rebuild_table_if_needed(small.busy);
        self.handle_keys(&ctx, &small);
        let dialog_ready = self.restore.is_finished()
            || (self.preview.open && self.preview.is_ready())
            || (self.versions.open && self.versions.is_ready())
            || (self.check.open && self.check.is_ready())
            || (self.stats.open && self.stats.is_ready())
            || (self.analysis.open && self.analysis.is_ready());
        match self.dev.step(
            &ctx,
            &self.shared,
            &small_state(&small, &self.table, dialog_ready),
        ) {
            DevAction::None => {}
            DevAction::OpenPreview => {
                if let Some(node) = self.single_file() {
                    self.preview.open_node(&self.shared, node);
                }
            }
            DevAction::RestoreVersion(index) => {
                if let Some(version) = self.versions.version_at(index) {
                    self.restore_version(version, small.last_restore_target.as_deref());
                }
            }
            DevAction::OpenCheck => {
                self.check.open_dialog();
                self.check.dev_start(&self.shared);
            }
            DevAction::OpenStats => self.stats.open_dialog(&self.shared),
            DevAction::OpenAnalysis => self.analysis.open_dialog(&self.shared),
            DevAction::OpenVersions => {
                if let Some(node) = self.single_file() {
                    self.versions.open_with(&self.shared, node);
                }
            }
            DevAction::SelectRange(from, to) => {
                let last = self.table.len().saturating_sub(1);
                for row in from.min(last)..=to.min(last) {
                    self.table.click(row, true, false);
                }
            }
            DevAction::OpenRestoreDialog => {
                let nodes = self.selected_nodes();
                let summary = self.shared.session.view(|v| self.table.summary(v.nodes()));
                self.restore
                    .open_with(nodes, summary, small.last_restore_target.as_deref());
            }
            DevAction::OpenConnectDialog => self.open_connect(),
            DevAction::LoadDirectory(path) => {
                self.run_session(|s| async move { s.load_directory(&path).await });
            }
            DevAction::Search(pattern) => self.run_session(|s| async move { s.search(&pattern).await }),
            DevAction::RunRestore(target) => self.restore.dev_run(target, &self.shared),
        }

        // Beim ersten Start den Verbindungsdialog zeigen, sobald die Profile geladen sind.
        if !self.initial_dialog_done && self.frame > 2 && !self.dev.auto_connect() {
            self.initial_dialog_done = true;
            if !small.connected {
                self.open_connect();
            }
        }

        self.header(ui, &small);
        self.status_bar(ui, &small);
        if small.connected {
            self.snapshot_panel(ui, &small);
            egui::CentralPanel::default().show(ui, |ui| self.browser(ui, &small));
        } else {
            egui::CentralPanel::default().show(ui, |ui| self.empty_state(ui));
        }

        let palette = self.theme.palette();
        let profiles = if self.connect.open {
            self.shared.session.view(|v| v.profiles().to_vec())
        } else {
            Vec::new()
        };
        self.connect.ui(&ctx, &self.shared, &palette, &profiles);
        self.restore.ui(&ctx, &self.shared, &palette);
        let versions_action = self.versions.ui(&ctx, &self.shared, &palette, !self.preview.open);
        match versions_action {
            VersionsAction::None => {}
            VersionsAction::Preview(version) => self.preview.open_version(&self.shared, version),
            VersionsAction::Restore(version) => {
                self.restore_version(version, small.last_restore_target.as_deref());
            }
        }
        self.preview.ui(&ctx, &palette, true);
        if let Some(node) = self.newest_match.lock().expect("Sperre").take() {
            let summary = format!("Neueste Version: {}", node.display_name());
            self.restore
                .open_with(vec![node], summary, small.last_restore_target.as_deref());
        }
        self.track_window(&ctx);
        self.check.ui(&ctx, &self.shared, &palette, true);
        self.stats.ui(&ctx, &palette, true);
        self.analysis.ui(&ctx, &palette, true);

        if small.busy {
            ctx.request_repaint_after(Duration::from_millis(50)); // Ladezustand: Animation und Zwischenstände
        }
    }
}

fn small_state(small: &Small, table: &TableModel, dialog_ready: bool) -> crate::dev::UiState {
    crate::dev::UiState {
        connected: small.connected,
        busy: small.busy,
        rows: table.len(),
        selected: table.selected_count(),
        dialog_ready,
        selected_snapshot: small.selected_snapshot.is_some(),
    }
}

/// Registriert den Beobachter, der bei jeder Änderung neu zeichnen lässt und Knotenänderungen zählt.
pub fn subscribe(shared: &Shared, revision: Arc<AtomicU64>) {
    let ctx = shared.ctx.clone();
    shared.session.subscribe(move |change| {
        if matches!(change, Change::Nodes(_)) {
            revision.fetch_add(1, Ordering::SeqCst);
        }
        ctx.request_repaint();
    });
}
