# CLAUDE.md

Die verbindlichen Projektregeln stehen in `AGENTS.md` und gelten unverändert auch für Claude Code:

@AGENTS.md

## Kurzüberblick

- Rust-Workspace in der Wurzel: `crates/restic-core` (Restic-Zugriff), `crates/restic-app` (Sitzung,
  Einstellungen, Filter), `crates/restic-ui` (egui-Oberfläche), `crates/restic-testkit` (Attrappen und
  Integrationstests). Skripte unter `scripts/`, Fixtures unter `fixtures/`.
- Verlauf und offene Prüfpunkte stehen in `CHANGELOG.md`; jede funktionale Änderung bekommt dort einen Eintrag
  und erhöht die Version in `Cargo.toml` und `Cargo.lock`.
- Prüfen vor einem Commit: `cargo fmt --check`, `cargo clippy --workspace --all-targets --locked -- -D warnings`,
  `cargo test --workspace --release --locked` (Einzelheiten in `AGENTS.md`, Abschnitt „Prüfen“).
- Nie ungefragt pushen, nie direkt auf `main` arbeiten, keine Tags oder Releases von Hand.
