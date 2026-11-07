# Rust-Workspace

Cargo-Workspace von Restic Browser (ab Version 4.0.0 die einzige Implementierung).

- `crates/restic-core` – Restic-Runner, Befehle, Modelle, Auflösung und Provisionierung, Repository-Service
- `crates/restic-app` – Anwendungslogik: Einstellungen, Filter, Cache, Navigation, `BrowserSession`
- `crates/restic-ui` – egui-Oberfläche, Binärdatei `restic-browser` (ausgeliefert als `ResticBrowser`)
- `crates/restic-testkit` – Test-Werkzeuge, Attrappen und Integrationstests mit echtem restic
- `fixtures/` – bereinigte restic-Ausgaben (`fixtures/generate.sh`); `spikes/` – eigenständige UI-Experimente
- `scripts/` – Paketierung (`package-windows.ps1`, `package-linux.sh`), Linux-Prüfung, Drittkomponenten-Liste

Plan, Grenzwerte und Testzuordnung: [`docs/rust-migration.md`](../docs/rust-migration.md). Befehle zum Prüfen: siehe README im Wurzelverzeichnis.
