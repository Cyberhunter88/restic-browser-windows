//! Anwendungslogik von Restic Browser ohne Oberfläche: Einstellungen, Snapshot-Filter,
//! Verzeichnis-Cache, Navigation und die Sitzung, die das Hauptfenster später bedient.

pub mod backend;
pub mod cache;
pub mod filter;
pub mod list;
pub mod navigation;
pub mod session;
pub mod settings;

pub use backend::{BatchSink, RepositoryBackend, batch_channel};
pub use list::{BatchList, ListChange};
pub use session::{BrowserSession, Change, SessionView, restore_request};
pub use settings::{AppSettings, SettingsError, SettingsService, WindowSettings};
