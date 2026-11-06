//! Entwicklungs-Hilfen, nur in Debug-Builds aktiv: automatisches Verbinden mit einem Test-Repository,
//! Screenshots und synthetische Zeigerereignisse. In Release-Builds sind alle Optionen wirkungslos.
//!
//! Aufruf (Debug): `restic-browser [--dev-dark] [--dev-settings pfad] [--dev-repo ordner]
//!   [--dev-select von bis] [--dev-restore] [--dev-connect-dialog] [--dev-click x y] [--dev-hover x y]
//!   [--dev-screenshot pfad]`. Das Passwort kommt aus `RESTIC_BROWSER_DEV_PASSWORD`.

#![allow(clippy::collapsible_if)] // Entwicklungs-Hilfe: verschachtelte Bedingungen bleiben lesbarer.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use eframe::egui;
use restic_core::provisioning::ResticResolver;
use restic_core::{RepositoryProfile, SessionCredentials};
use tokio_util::sync::CancellationToken;

use crate::shared::Shared;

/// Nur das, was die Entwicklungs-Hilfen vom Zustand der Oberfläche wissen müssen.
pub struct UiState {
    pub connected: bool,
    pub busy: bool,
    pub rows: usize,
    pub selected: usize,
    pub dialog_ready: bool,
}

pub enum DevAction {
    None,
    SelectRange(usize, usize),
    OpenRestoreDialog,
    OpenConnectDialog,
    OpenPreview,
    OpenVersions,
    RestoreVersion(usize),
    LoadDirectory(String),
    Search(String),
    RunRestore(String),
}

#[derive(Clone, Default)]
pub struct DevOptions {
    pub dark: bool,
    pub settings: Option<String>,
    repo: Option<String>,
    select: Option<(usize, usize)>,
    restore: bool,
    preview: bool,
    versions: bool,
    version_restore: Option<usize>,
    restore_run: Option<String>,
    directory: Option<String>,
    search: Option<String>,
    connect_dialog: bool,
    click: Option<(f32, f32)>,
    hover: Option<(f32, f32)>,
    screenshot: Option<String>,
    state: Arc<DevProgress>,
}

#[derive(Default)]
struct DevProgress {
    connect_started: AtomicBool,
    select_done: AtomicBool,
    restore_done: AtomicBool,
    connect_dialog_done: AtomicBool,
    quiet_frames: std::sync::atomic::AtomicU64,
    restore_run_done: AtomicBool,
    preview_done: AtomicBool,
    versions_done: AtomicBool,
    version_restore_done: AtomicBool,
    dir_done: AtomicBool,
    search_done: AtomicBool,
    shot_requested: AtomicBool,
    shot_done: AtomicBool,
}

impl DevOptions {
    pub fn parse() -> Self {
        let mut options = Self::default();
        if !cfg!(debug_assertions) {
            return options;
        }
        let args: Vec<String> = std::env::args().skip(1).collect();
        let number = |i: usize| args.get(i).and_then(|v| v.parse::<f32>().ok()).unwrap_or(0.0);
        let mut i = 0;
        while i < args.len() {
            match args[i].as_str() {
                "--dev-dark" => options.dark = true,
                "--dev-restore" => options.restore = true,
                "--dev-connect-dialog" => options.connect_dialog = true,
                "--dev-restore-run" => {
                    options.restore = true;
                    options.restore_run = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-preview" => options.preview = true,
                "--dev-version-restore" => {
                    options.versions = true;
                    options.version_restore = args.get(i + 1).and_then(|v| v.parse().ok());
                    i += 1;
                }
                "--dev-versions" => options.versions = true,
                "--dev-dir" => {
                    options.directory = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-search" => {
                    options.search = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-settings" => {
                    options.settings = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-repo" => {
                    options.repo = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-screenshot" => {
                    options.screenshot = args.get(i + 1).cloned();
                    i += 1;
                }
                "--dev-select" => {
                    options.select = Some((number(i + 1) as usize, number(i + 2) as usize));
                    i += 2;
                }
                "--dev-click" => {
                    options.click = Some((number(i + 1), number(i + 2)));
                    i += 2;
                }
                "--dev-hover" => {
                    options.hover = Some((number(i + 1), number(i + 2)));
                    i += 2;
                }
                _ => {}
            }
            i += 1;
        }
        options
    }

    /// Bei automatischem Verbinden entfällt der Dialog beim Start.
    pub fn auto_connect(&self) -> bool {
        self.repo.is_some()
    }

    pub fn inject_events(&self, frame: u64, raw_input: &mut egui::RawInput) {
        if !cfg!(debug_assertions) {
            return;
        }
        let ready = self.state.quiet_frames.load(Ordering::SeqCst) > 0;
        if frame > 20 && ready {
            if let Some((x, y)) = self
                .click
                .filter(|_| !self.state.shot_requested.load(Ordering::SeqCst))
            {
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
        if let Some((x, y)) = self.hover {
            if self.state.shot_requested.load(Ordering::SeqCst) || (frame > 20 && ready) {
                raw_input.events.push(egui::Event::PointerMoved(egui::pos2(x, y)));
            }
        }
    }

    pub fn step(&self, ctx: &egui::Context, shared: &Arc<Shared>, ui: &UiState) -> DevAction {
        if !cfg!(debug_assertions) {
            return DevAction::None;
        }
        let progress = &self.state;
        if let Some(repo) = &self.repo {
            if !progress.connect_started.swap(true, Ordering::SeqCst) {
                let pw = std::env::var("RESTIC_BROWSER_DEV_PASSWORD").unwrap_or_default();
                let (session, repo) = (shared.session.clone(), repo.clone());
                shared.spawn(async move {
                    let base = std::env::current_exe()
                        .ok()
                        .and_then(|p| p.parent().map(std::path::Path::to_path_buf))
                        .unwrap_or_default();
                    let resolver =
                        ResticResolver::for_application(restic_app::settings::data_directory(), base);
                    let Ok(info) = resolver.resolve(None, &CancellationToken::new()).await else {
                        eprintln!("DEV: restic nicht gefunden");
                        return;
                    };
                    let mut profile = RepositoryProfile::new(repo);
                    profile.name = "Entwicklungstest".into();
                    profile.resolved_restic_executable = Some(info.path.to_string_lossy().into_owned());
                    profile.resolved_restic_source = Some(info.source);
                    if let Err(error) = session
                        .connect(profile, SessionCredentials::new(pw, Default::default()))
                        .await
                    {
                        eprintln!("DEV: Verbinden fehlgeschlagen: {error}");
                    }
                });
            }
        }
        if self.connect_dialog && !progress.connect_dialog_done.swap(true, Ordering::SeqCst) {
            return DevAction::OpenConnectDialog;
        }
        let loaded = (self.repo.is_none() || (ui.connected && ui.rows > 0)) && !ui.busy;
        if !loaded {
            progress.quiet_frames.store(0, Ordering::SeqCst);
            if self.screenshot.is_some() {
                ctx.request_repaint();
            }
            return DevAction::None;
        }
        // Aktionen laufen nacheinander, jeweils nach einer kurzen Ruhephase ohne laufende Operation.
        let quiet = progress.quiet_frames.fetch_add(1, Ordering::SeqCst) + 1;
        if self.screenshot.is_some() {
            ctx.request_repaint();
        }
        let ready_for_action = quiet >= 8;
        let act = |flag: &AtomicBool| {
            if ready_for_action && !flag.swap(true, Ordering::SeqCst) {
                progress.quiet_frames.store(0, Ordering::SeqCst);
                true
            } else {
                false
            }
        };
        if let Some(dir) = &self.directory {
            if !progress.dir_done.load(Ordering::SeqCst) && act(&progress.dir_done) {
                return DevAction::LoadDirectory(dir.clone());
            }
        }
        if let Some(pattern) = &self.search {
            if !progress.search_done.load(Ordering::SeqCst) && act(&progress.search_done) {
                return DevAction::Search(pattern.clone());
            }
        }
        if let Some((from, to)) = self.select {
            if !progress.select_done.load(Ordering::SeqCst) && act(&progress.select_done) {
                return DevAction::SelectRange(from, to);
            }
        }
        // Beim Versions-Restore öffnet die Version den Dialog selbst; der normale Weg darf ihn nicht umgehen.
        let plain_restore = self.restore && self.version_restore.is_none();
        if plain_restore
            && ui.selected > 0
            && !progress.restore_done.load(Ordering::SeqCst)
            && act(&progress.restore_done)
        {
            return DevAction::OpenRestoreDialog;
        }
        if self.preview && ui.selected > 0 && act(&progress.preview_done) {
            return DevAction::OpenPreview;
        }
        if self.versions && ui.selected > 0 && act(&progress.versions_done) {
            return DevAction::OpenVersions;
        }
        if let Some(index) = self.version_restore {
            if progress.versions_done.load(Ordering::SeqCst)
                && ui.dialog_ready
                && act(&progress.version_restore_done)
            {
                progress.restore_done.store(true, Ordering::SeqCst);
                return DevAction::RestoreVersion(index);
            }
        }
        if let Some(target) = &self.restore_run {
            if progress.restore_done.load(Ordering::SeqCst)
                && !progress.restore_run_done.load(Ordering::SeqCst)
                && act(&progress.restore_run_done)
            {
                return DevAction::RunRestore(target.clone());
            }
        }
        // Aufnahme erst, wenn alle gewünschten Schritte erledigt sind und das Bild ruhig ist.
        let pending = self.directory.is_some() && !progress.dir_done.load(Ordering::SeqCst)
            || self.search.is_some() && !progress.search_done.load(Ordering::SeqCst)
            || self.select.is_some() && !progress.select_done.load(Ordering::SeqCst)
            || self.preview && !progress.preview_done.load(Ordering::SeqCst)
            || self.versions && !progress.versions_done.load(Ordering::SeqCst)
            || plain_restore && !progress.restore_done.load(Ordering::SeqCst)
            || self.version_restore.is_some() && !progress.version_restore_done.load(Ordering::SeqCst)
            || self.restore_run.is_some() && !progress.restore_run_done.load(Ordering::SeqCst);
        let blocked = (self.restore_run.is_some() || self.preview || self.versions) && !ui.dialog_ready;
        let settle = if self.restore_run.is_some() {
            30
        } else if self.click.is_some() || self.hover.is_some() {
            45
        } else {
            25
        };
        if let Some(path) = &self.screenshot {
            if !pending
                && !blocked
                && quiet >= settle
                && !progress.shot_requested.swap(true, Ordering::SeqCst)
            {
                let (path, done) = (path.clone(), Arc::clone(&self.state));
                ctx.request_screenshot(move |image| {
                    save_png(&image, &path);
                    done.shot_done.store(true, Ordering::SeqCst);
                });
            }
            if progress.shot_done.load(Ordering::SeqCst) {
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        }
        DevAction::None
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
    encoder
        .write_header()
        .expect("PNG-Kopf")
        .write_image_data(&bytes)
        .expect("PNG-Daten");
    println!("Screenshot: {path} ({width}x{height})");
}
