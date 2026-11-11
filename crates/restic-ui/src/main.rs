//! Restic Browser (Rust-Oberfläche, egui/eframe). Die Logik liegt in `restic-core` und `restic-app`.

#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod analysis;
mod app;
mod check;
mod connect;
mod dev;
mod embedded;
mod file_table;
mod icons;
mod preview;
mod restore;
mod shared;
mod stats;
mod table_model;
mod theme;
mod versions;

use std::sync::Arc;
use std::sync::atomic::AtomicU64;

use eframe::egui;
use restic_app::{BrowserSession, SettingsService};
use restic_core::ResticRepositoryService;

use crate::app::ResticBrowserApp;
use crate::shared::Shared;

/// Das Programmsymbol aus der eingebetteten PNG-Datei.
fn app_icon() -> Option<egui::IconData> {
    let image = image::load_from_memory_with_format(
        include_bytes!("../assets/app-icon.png"),
        image::ImageFormat::Png,
    )
    .ok()?
    .into_rgba8();
    let (width, height) = image.dimensions();
    Some(egui::IconData {
        rgba: image.into_raw(),
        width,
        height,
    })
}

fn main() -> eframe::Result {
    let dev = dev::DevOptions::parse();
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .thread_name("restic-browser")
        .build()
        .expect("Tokio-Laufzeit konnte nicht gestartet werden");
    let handle = runtime.handle().clone();
    let settings_path = dev.settings.clone();
    let open_settings = move || match &settings_path {
        Some(path) => SettingsService::new(path),
        None => SettingsService::with_default_path(),
    };
    // Die gespeicherte Fenstergröße wird vor dem Erzeugen des Fensters gelesen (Fehler sind unkritisch).
    let loaded = runtime.block_on(open_settings().load_settings()).ok();
    let saved = loaded.as_ref().and_then(|settings| settings.window.clone());
    // Im Entwicklungsmodus bestimmen die Startoptionen das Design, sonst die gespeicherte Wahl (Standard: hell).
    let dark = if dev.is_active() {
        dev.dark
    } else {
        loaded
            .as_ref()
            .and_then(|settings| settings.dark_theme)
            .unwrap_or(false)
    };
    let show_permissions = loaded.as_ref().is_some_and(|settings| settings.show_permissions);
    let mut viewport = egui::ViewportBuilder::default()
        .with_title("Restic Browser")
        // Unter Wayland ordnet der Desktop das Symbol über die app_id zu (Desktop-Eintrag `ResticBrowser`).
        .with_app_id("ResticBrowser")
        .with_inner_size([1240.0, 780.0])
        .with_min_inner_size([900.0, 560.0]);
    if let Some(icon) = app_icon() {
        viewport = viewport.with_icon(icon);
    }
    if let Some(window) = saved.filter(|_| !dev.is_active()) {
        if window.width >= 900.0
            && window.height >= 560.0
            && window.width <= 10_000.0
            && window.height <= 10_000.0
        {
            viewport = viewport.with_inner_size([window.width as f32, window.height as f32]);
            if let (Some(x), Some(y)) = (window.x, window.y)
                && (-100..=8000).contains(&x)
                && (-100..=8000).contains(&y)
            {
                viewport = viewport.with_position([x as f32, y as f32]);
            }
        }
        if window.is_maximized {
            viewport = viewport.with_maximized(true);
        }
    }
    let native = eframe::NativeOptions {
        viewport,
        ..eframe::NativeOptions::default()
    };
    let result = eframe::run_native(
        "Restic Browser",
        native,
        Box::new(move |creation| {
            let settings = open_settings();
            let session = BrowserSession::new(ResticRepositoryService::new(), settings);
            let shared = Arc::new(Shared::new(handle, session, creation.egui_ctx.clone()));
            let revision = Arc::new(AtomicU64::new(0));
            app::subscribe(&shared, Arc::clone(&revision));
            Ok(Box::new(ResticBrowserApp::new(
                shared,
                revision,
                dev.clone(),
                dark,
                show_permissions,
            )))
        }),
    );
    drop(runtime);
    result
}
