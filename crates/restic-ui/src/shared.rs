//! Gemeinsamer Zugriff der Oberfläche auf Sitzung und Tokio-Laufzeit.

use std::future::Future;
use std::sync::{Arc, Mutex};

use eframe::egui;
use restic_app::BrowserSession;
use restic_core::ResticRepositoryService;

pub type Session = BrowserSession<ResticRepositoryService>;

pub struct Shared {
    pub handle: tokio::runtime::Handle,
    pub session: Session,
    pub ctx: egui::Context,
    /// Fehlermeldungen aus Hintergrundaufgaben, die die Statusleiste zeigt.
    errors: Mutex<Vec<String>>,
}

impl Shared {
    pub fn new(handle: tokio::runtime::Handle, session: Session, ctx: egui::Context) -> Self {
        Self {
            handle,
            session,
            ctx,
            errors: Mutex::new(Vec::new()),
        }
    }

    /// Startet eine Aufgabe auf der Laufzeit und fordert danach ein Neuzeichnen an.
    pub fn spawn(self: &Arc<Self>, future: impl Future<Output = ()> + Send + 'static) {
        let shared = Arc::clone(self);
        self.handle.spawn(async move {
            future.await;
            shared.ctx.request_repaint();
        });
    }

    /// Wie [`Self::spawn`]; ein Fehler erscheint danach in der Statusleiste.
    pub fn spawn_reporting<E: ToString>(
        self: &Arc<Self>,
        future: impl Future<Output = Result<(), E>> + Send + 'static,
    ) {
        let shared = Arc::clone(self);
        self.handle.spawn(async move {
            if let Err(error) = future.await {
                shared.push_error(error.to_string());
            }
            shared.ctx.request_repaint();
        });
    }

    /// Betritt die Laufzeit für Aufrufe der Sitzung, die intern `tokio::spawn` nutzen.
    pub fn enter(&self) -> tokio::runtime::EnterGuard<'_> {
        self.handle.enter()
    }

    pub fn take_error(&self) -> Option<String> {
        self.errors.lock().expect("Sperre").pop()
    }

    pub fn push_error(&self, message: String) {
        self.errors.lock().expect("Sperre").push(message);
        self.ctx.request_repaint();
    }
}
