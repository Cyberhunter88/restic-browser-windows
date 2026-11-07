//! Zugang zur eingebetteten Windows-Restic-Datei und Aufbau des Resolvers der Anwendung.

use std::sync::Arc;

use restic_core::manifest::ResticManifest;
use restic_core::provisioning::{EmbeddedRestic, ResticResolver};
use restic_core::version_probe::ProcessVersionProbe;

#[cfg(embedded_restic)]
fn embedded_bytes() -> Option<&'static [u8]> {
    Some(include_bytes!(concat!(env!("OUT_DIR"), "/restic.exe")))
}

#[cfg(not(embedded_restic))]
fn embedded_bytes() -> Option<&'static [u8]> {
    None
}

/// Die eingebettete Datei samt der im Build-Manifest gepinnten SHA-256-Prüfsumme.
pub fn embedded_restic() -> Option<EmbeddedRestic> {
    let bytes = embedded_bytes()?;
    Some(EmbeddedRestic {
        bytes: Arc::from(bytes),
        sha256: ResticManifest::embedded().windows.binary_sha256,
    })
}

/// Resolver mit Benutzerdatenordner, Anwendungsordner und (falls vorhanden) eingebetteter Datei.
pub fn resolver() -> ResticResolver<ProcessVersionProbe> {
    let base = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(std::path::Path::to_path_buf))
        .unwrap_or_default();
    let resolver = ResticResolver::for_application(restic_app::settings::data_directory(), base);
    match embedded_restic() {
        Some(embedded) => resolver.with_embedded_windows(embedded),
        None => resolver,
    }
}
