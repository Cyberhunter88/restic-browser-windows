//! Restic Browser (Rust-Oberfläche, egui/eframe). Die Logik liegt in `restic-core` und `restic-app`.

#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod app;
mod connect;
mod dev;
mod file_table;
mod icons;
mod preview;
mod restore;
mod shared;
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

fn main() -> eframe::Result {
    let dev = dev::DevOptions::parse();
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .thread_name("restic-browser")
        .build()
        .expect("Tokio-Laufzeit konnte nicht gestartet werden");
    let handle = runtime.handle().clone();
    let native = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("Restic Browser")
            .with_inner_size([1240.0, 780.0])
            .with_min_inner_size([900.0, 560.0]),
        ..eframe::NativeOptions::default()
    };
    let result = eframe::run_native(
        "Restic Browser",
        native,
        Box::new(move |creation| {
            let settings = match &dev.settings {
                Some(path) => SettingsService::new(path),
                None => SettingsService::with_default_path(),
            };
            let session = BrowserSession::new(ResticRepositoryService::new(), settings);
            let shared = Arc::new(Shared::new(handle, session, creation.egui_ctx.clone()));
            let revision = Arc::new(AtomicU64::new(0));
            app::subscribe(&shared, Arc::clone(&revision));
            Ok(Box::new(ResticBrowserApp::new(shared, revision, dev.clone())))
        }),
    );
    drop(runtime);
    result
}
