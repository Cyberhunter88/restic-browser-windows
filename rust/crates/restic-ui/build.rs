//! Build-Skript: bettet optional die geprüfte Windows-Restic-Datei ein und setzt das EXE-Symbol.
//!
//! `RESTIC_BROWSER_EMBED_RESTIC` zeigt auf die von `scripts/prepare-restic.ps1` geprüfte `restic.exe`.
//! Ohne die Variable (Entwicklung, Tests) wird nichts eingebettet; zur Laufzeit gibt es keine Downloads.

use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-env-changed=RESTIC_BROWSER_EMBED_RESTIC");
    println!("cargo:rerun-if-changed=app.rc");
    println!("cargo:rerun-if-changed=assets/app.ico");
    println!("cargo:rustc-check-cfg=cfg(embedded_restic)");

    let windows = std::env::var("CARGO_CFG_TARGET_OS").is_ok_and(|os| os == "windows");
    if !windows {
        return;
    }
    if let Ok(source) = std::env::var("RESTIC_BROWSER_EMBED_RESTIC") {
        let source = PathBuf::from(source);
        println!("cargo:rerun-if-changed={}", source.display());
        let out = PathBuf::from(std::env::var("OUT_DIR").expect("OUT_DIR")).join("restic.exe");
        std::fs::copy(&source, &out)
            .unwrap_or_else(|e| panic!("restic.exe konnte nicht eingebettet werden: {e}"));
        println!("cargo:rustc-cfg=embedded_restic");
    }
    // Symbol und Versionsangaben der EXE; fehlt das Windows SDK, bleibt nur das Fensterbild.
    let _ = embed_resource::compile("app.rc", embed_resource::NONE);
}
