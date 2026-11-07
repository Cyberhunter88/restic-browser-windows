# Architektur

- Portable Rust-Anwendung (egui/eframe) für Windows x64 und Linux x64, ab Version 4.0.0. Die frühere
  C#-/Avalonia-Version (3.x) wurde entfernt; ihr Stand bleibt über die Release-Tags `v3.*` erreichbar.
- Restic ist die einzige Schnittstelle zum Backup-Repository. Es wird keine Restic-Go-Bibliothek eingebunden.
- Restic läuft als separater Prozess, immer ohne Shell mit getrennten Argumenten. Windows enthält eine
  signaturgeprüfte, hash-geprüfte Ressource, die erst beim ersten Bedarf atomar und gesperrt im Benutzerdatenordner
  bereitgestellt wird; Linux liefert `tools/restic` im Paket mit. Externe Dateien bleiben eine ausdrückliche
  Override-Option.
- Profile speichern keine Backend-Geheimnisse. Passwörter und Sitzungsvariablen bleiben im Arbeitsspeicher
  (`SessionCredentials`, `zeroize`) und werden nur an den jeweiligen Restic-Prozess vererbt.
- Es gibt kein Mount, keine Telemetrie, keine Laufzeit-Downloads und keine dauerhaften Ergebnis-Caches.

## Crates (`rust/`)

| Crate | Zuständigkeit |
|---|---|
| `restic-core` | Prozess-Runner (Abbruch, stdout/stderr-Cleanup, früher Stream-Abbruch), Command-Builder, Locator, Versionsprüfung (`version --json`, 5 s), Resolver und Provisionierung, JSON-Modelle, Fehlertexte, Pfadschutz, Repository-Service mit Batching und Speicheranalyse |
| `restic-app` | Einstellungen und Profile (Format 3.x), Snapshot-Filter, LRU-Verzeichnis-Cache, Navigation, `BrowserSession` mit Operationsversionierung und Streaming |
| `restic-ui` | egui-Oberfläche: Hauptfenster, Verbinden, Wiederherstellen, Vorschau, Versionen, Prüfung, Statistik, Speicheranalyse, Theme, Fenstergeometrie; Binärdatei `restic-browser` |
| `restic-testkit` | Attrappen (`scripted-restic`, Runner-Fixture) und Integrationstests mit echtem restic |

## Verhalten

Der Repository-Service liefert Snapshots, Suchtreffer und Verzeichniseinträge in Batches von höchstens 256
Einträgen; der erste Eintrag kommt sofort. Die Suchgrenze beträgt 10.000 Treffer. Der Runner beendet JSON-Array-
und `find`-Streams kontrolliert, sobald das Ergebnis vollständig ist (`FindNewestAsync` nach dem ersten gültigen
Treffer, `FindAsync` nach 10.000 Treffern). Ein absichtlicher früher Abbruch wird nicht als Fehler gemeldet; ein
Benutzerabbruch bleibt ein Abbruch. Veraltete Vorgänge übernehmen keine weiteren Batches. Der Filter „Nur neueste
Snapshots“ hält die jeweils neueste Version je Host und Pfad inkrementell aktuell.

Beim Verbinden wird kein `stats` gestartet; die Snapshot-Anzahl stammt aus der Snapshot-Liste. Die Statistik bleibt
eine bewusst gestartete Funktion. Die Speicheranalyse aggregiert höchstens 100.000 unterschiedliche Ordnerpfade und
markiert eine erreichte Grenze ausdrücklich.

Alle lesenden Repository-Aufrufe erhalten zentral genau ein `--no-lock`. Wiederherstellungsziele werden gegen das
lokale Repository geprüft (keine Überlappung, Symlink-Alias, mehrdeutige `..`-Segmente). Nicht lokale Backends werden
vor dem Start von restic abgewiesen. Restore schreibt ausschließlich in das gewählte Ziel.

Einstellungsfehler werden gemeldet; ausschließlich eine fehlende Profildatei bedeutet eine leere Profilliste.
Schreibvorgänge sind gesperrt und atomar (temporäre Datei im selben Verzeichnis). Die Auflösung der Restic-Datei
folgt der festen Reihenfolge aus `README.md`; die tatsächlich gestartete Datei wird immer per `restic version --json`
validiert, und Herkunft und Version der Anzeige stammen aus dieser Prüfung. Der Start einer gerade geschriebenen
Datei wird bei „Text file busy“ unter Linux kurz wiederholt.

## Tests und CI

`cargo test --workspace --release --locked` führt Unit- und Integrationstests aus; mit `RESTIC_BROWSER_REQUIRE_RESTIC=1`
wird ein fehlendes restic zum Fehler. Fehlende Plattformabhängigkeiten werden als übersprungen ausgewiesen. Die CI
(`.github/workflows/build.yml`) prüft Formatierung, Clippy, Lizenzen (`cargo deny`), Schwachstellen (`cargo audit`),
Versionskonsistenz, Release-Skripte, Tests, den Start unter Xvfb in beiden Themes und mehreren Skalierungen sowie die
Linux-Pakete auf Ubuntu und Arch. Zentrale Composite Actions liegen unter `.github/actions`.

## Produktversion und Releases

`version` unter `[workspace.package]` in `rust/Cargo.toml` ist die einzige Produktversionsquelle; alle Crates erben
sie, `Cargo.lock` wird im selben PR mitgeführt, die Windows-EXE erhält daraus Datei- und Produktversion.
`scripts/verify-version.py` prüft das. `release.yml` startet manuell auf `main` mit einer Versionseingabe.
`scripts/release-flow.py` normalisiert die Eingabe (optionales `v`/`V`, äußere Leerzeichen); die Version bleibt strikt
`MAJOR.MINOR.PATCH`. Eine höhere Version erzeugt einen reinen Versions-PR (`rust/Cargo.toml`, `rust/Cargo.lock`) und
startet dessen CI per `workflow_dispatch`. Nach dem manuellen Merge setzt ein erfolgreicher Main-CI-Lauf
(`workflow_run`, ursprüngliches Ereignis `push`) den Release fort. Geprüft werden Bot-Autor, Herkunftsrepository,
Branchname, Anforderungsmarker, Merge-SHA und die Begrenzung des PRs auf die beiden Dateien. Bei identischer Version
kann der Benutzer direkt die Veröffentlichung des Main-Commits anfordern. Beide Pfade setzen erfolgreiche CI für
exakt diesen Commit voraus und durchlaufen Windows- und Linux-Builds, Tests und Paketprüfungen. Tag und Assets gehören
genau zu diesem Commit; vorhandene Tags und Assets werden geprüft, nie blind überschrieben. Normale Versionsänderungen
lösen keine Veröffentlichung aus.
