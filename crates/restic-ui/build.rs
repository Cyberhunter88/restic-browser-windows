//! Build-Skript: bettet optional die geprüfte Windows-Restic-Datei ein und setzt das EXE-Symbol.
//!
//! `RESTIC_BROWSER_EMBED_RESTIC` zeigt auf die von `scripts/prepare-restic.ps1` geprüfte `restic.exe`.
//! Ohne die Variable (Entwicklung, Tests) wird nichts eingebettet; zur Laufzeit gibt es keine Downloads.

use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-env-changed=RESTIC_BROWSER_EMBED_RESTIC");
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
    // Symbol und Versionsangaben der EXE (Version aus der zentralen Workspace-Version);
    // fehlt das Windows SDK, bleibt nur das Fensterbild.
    let out = PathBuf::from(std::env::var("OUT_DIR").expect("OUT_DIR"));
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR"));
    let icon = manifest
        .join("assets")
        .join("app.ico")
        .to_string_lossy()
        .replace('\\', "/");
    let version = std::env::var("CARGO_PKG_VERSION").expect("CARGO_PKG_VERSION");
    let numbers: Vec<&str> = version.split('.').collect();
    let (major, minor, patch) = (numbers[0], numbers[1], numbers[2]);
    let resource = format!(
        "1 ICON \"{icon}\"\n\
         1 VERSIONINFO\n\
         FILEVERSION {major},{minor},{patch},0\n\
         PRODUCTVERSION {major},{minor},{patch},0\n\
         BEGIN\n\
           BLOCK \"StringFileInfo\"\n\
           BEGIN\n\
             BLOCK \"040704B0\"\n\
             BEGIN\n\
               VALUE \"ProductName\", \"Restic Browser\"\n\
               VALUE \"FileDescription\", \"Restic Browser\"\n\
               VALUE \"OriginalFilename\", \"ResticBrowser.exe\"\n\
               VALUE \"FileVersion\", \"{version}.0\"\n\
               VALUE \"ProductVersion\", \"{version}\"\n\
             END\n\
           END\n\
           BLOCK \"VarFileInfo\"\n\
           BEGIN\n\
             VALUE \"Translation\", 0x0407, 1200\n\
           END\n\
         END\n"
    );
    let rc = out.join("app.rc");
    std::fs::write(&rc, resource).expect("app.rc");
    let _ = embed_resource::compile(rc, embed_resource::NONE);
}
