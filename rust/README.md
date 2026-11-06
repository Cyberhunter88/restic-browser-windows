# Rust-Migration (Entwurf)

Dieser Ordner enthält die Vorbereitung für die Portierung von Restic Browser nach Rust.
Plan, Grenzwerte und Testzuordnung: [`docs/rust-migration.md`](../docs/rust-migration.md).

Aktueller Inhalt (Phase 0):

- `fixtures/generate.sh` – erzeugt bereinigte restic-Ausgaben aus einem Wegwerf-Repository
- `fixtures/restic-<Version>/` – die erzeugten Fixtures

Der Cargo-Workspace (`Cargo.toml`, `crates/`) enthält seit Phase 1 `restic-core` und `restic-testkit`. Die C#-Anwendung unter `src/` bleibt bis zum
Paritätsnachweis die maßgebliche Implementierung.
