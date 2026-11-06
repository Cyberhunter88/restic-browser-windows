# Rust-Migration

Stand: Phase 0 (Vorbereitung). Die C#/Avalonia-Anwendung (Linie 3.x) bleibt bis zum
Paritätsnachweis die einzige veröffentlichte Version. Die Rust-Version erscheint als 4.0.0.

## Entscheidungen

1. **Struktur:** Der Rust-Workspace liegt im Unterordner `rust/` dieses Repositorys.
   Verhaltens-Fixtures, Doku, Versionsquelle und Release-Ablauf werden geteilt.
2. **UI-Framework:** Slint (nativ, eine Binärdatei, kein WebView). **Vor Phase 5 offen:**
   Lizenzprüfung (GPLv3, Royalty-free oder kommerziell) und ein UI-Spike für Tabellen,
   Themes und dunkle Titelleiste. Fallback: egui.
3. **Regeln:** Fachliche Regeln aus `AGENTS.md` (nur lesend, deutsche Texte, Secrets nur im
   Speicher, keine Laufzeit-Downloads, keine dauerhaften Caches, Restic als einzige
   Schnittstelle) gelten unverändert. Die technischen Prüfbefehle für Rust stehen in
   `AGENTS.md`; die .NET-Befehle gelten weiter für die C#-Linie.

## Zielarchitektur (`rust/`)

| Crate | Inhalt | Vorlage in C# |
|---|---|---|
| `restic-core` | Runner, Command-Builder, Locator, Provisioning, JSON-Modelle, Repository-Service, Batching, Fehlertexte | `Services/*`, `Models/Models.cs` |
| `restic-app` | Einstellungen, Profile, Filter, Navigation, Verzeichnis-Cache | `SettingsService.cs`, `ViewModels/*` |
| `restic-ui` | Slint-Oberfläche, Dialoge, Themes | `Views/*`, `MainWindow.axaml`, `App.axaml` |
| `restic-testkit` | Fake-Runner, Prozess-Fixture-Binary | `tests/.../Fakes.cs`, `--runner-fixture` |

Geplante Bibliotheken: `tokio`, `serde`/`serde_json`, `sha2`, `directories`, `fd-lock`,
`tempfile`, `secrecy`/`zeroize`.

## Verbindliche Grenzwerte (müssen identisch bleiben)

- `FindNewest` beendet restic nach dem ersten gültigen Treffer, `Find` nach 10.000 Treffern.
- Batches höchstens 256 Einträge, der erste Batch sofort, Backpressure zum Produzenten.
- Verzeichnis-Cache: 24 Einträge und höchstens 50.000 Knoten, LRU, nie dauerhaft.
- Genau ein Collection-Reset pro Batch-Befüllung, genau ein restic-Aufruf für „Neueste Datei“.
- Kein automatisches `stats` beim Verbinden.
- `--no-lock` bei allen lesenden Befehlen.
- Check-Details auf 64 KiB UTF-8 begrenzt; Speicheranalyse bis 100.000 Ordnerpfade.
- Versionsprüfung von restic mit 5 s Timeout, Version 0.19.1 aus `build/restic-manifest.json`.
- Einstellungen: Prozess-Sperrdatei höchstens 5 s, atomares Schreiben, defekte Datei nie
  überschreiben, `settings.json.bak` vor Entfernung alter Profiltypen.

## Verhaltensparität: Zuordnung der 66 C#-Tests

Quelle: `tests/ResticBrowser.Tests/Program.cs`. Jeder Test braucht in Rust ein Gegenstück
in der genannten Phase. Der Abschnitt „Fixtures“ nennt die dafür vorgesehenen Dateien.

| Phase | Zu portierende Tests |
|---|---|
| 1 Runner und Befehle | Runner beendet Prozesse bei Fehlern, Abbruch und frühem Ende; JSON-EOF wartet auf Exitcode und bleibt abbrechbar; Integritätsprüfung streamt und begrenzt Details; JSON-Array beendet Restic nach frühem Ende; JSONL-Verzeichnis zeilenweise; Binärvorschau erhält Originalbytes; Binäre Prozessausgabe begrenzt; Snapshot JSON toleriert Zusatzfelder; JSONL ignoriert unbekannte Zeilen; Restore-Argumente getrennt und vollständig; Repository-Befehle lesend ohne Lock; Überschreibmodi; Restore-Vorschau-Argumente; Backend-Variablen sicher geprüft; Zugangsdaten beim Dispose geleert; Stats und Dump Befehle; Zugriffsfehler plattformneutral; Symlink-Fehler ergeben Teilerfolg; zusätzliche Restore-Fehler bleiben Fehler; Restore-Fehlerausgabe begrenzt; Fehlerhinweise für bekannte Restic-Fehler; Snapshot-Pfade normalisiert; Mount-Optionen für beide Ansichten; Linux-Aushängen |
| 2 Auflösung | Restic-Suche unterscheidet Windows und Linux; Auswahl gewinnt und manipulierte Datei wird abgewiesen; Kandidaten eindeutig und Linux-Systempfade; Automatik überspringt beschädigte Kandidaten; Prüfung behandelt JSON, Version, Fehler, Timeout, Abbruch; Linux prüft Rechte und Symlinks; Herkunft bleibt Laufzeitinformation |
| 3 Repository-Service | Streaming: Treffer vor Ende einer Snapshot-Gruppe; UTF-8 und Zusatzfelder an Chunk-Grenzen; Snapshots vor Abschluss und final sortiert; Snapshot-Fehler und getrennte Verbindung; Suchlimit und begrenzte Batches; Suche begrenzt sichtbare Treffer; Dateiversionssuche stoppt am Limit; Dateiversionen nutzen bekannte Snapshots; Neueste Datei mit genau einem Prozess; Restore-Ziel schützt das Repository; Speicheranalyse (Fortschritt, Ordnerlimit); E2E mit echtem restic |
| 4 Anwendungslogik | Entfernte Profile gesichert; Profildateifehler erhalten Original; Paralleles Speichern; Altprofile entfernt; Entfernte Backends abgelehnt; Linux-Einstellungen mit `XDG_DATA_HOME`; Einstellungen speichern Bedienzustand abwärtskompatibel; Snapshot-Filter (unsortierte Eingabe, Streaming-Aktualisierung); Batch-Collection (Auswahl ohne Reset, genau ein Reset); Verzeichnis-Cache (Einträge, Knoten); Verbindungszustand vor Snapshot-Auswahl; Veraltete Navigation überschreibt nichts; Späte Statistiken getrennter Verbindung ignoriert; Relative Zeit; Pfadleiste; Performance bei großen Datenmengen |
| 6 Pakete und CI | Zentrale Produktversion und Build-Konsistenz; Release-Vorabprüfung (fehlende, passende, abweichende Assets) |
| 5 UI | Ersatz für `UiLayoutChecks.cs`: Screenshots Hauptfenster und Verbindungsdialog in Hell und Dunkel |

Hilfsmodi des C#-Runners, die als `restic-testkit`-Binary nachzubauen sind:
`--runner-fixture <failed|array|find|invalid|settings-lock|…> <pidfile>` (schreibt PID, gibt
definierte Ausgabe auf stdout, flutet stderr, beendet sich nicht von selbst) und der
Askpass-Modus über `RESTIC_BROWSER_ASKPASS_PIPE`.

## Fixtures

`rust/fixtures/generate.sh` erzeugt aus einem Wegwerf-Repository mit zwei Snapshots
(Host `fixture-host`, Tag `fixture`, Umlaute in Pfaden) bereinigte Ausgaben von restic 0.19.1
unter `rust/fixtures/restic-<Version>/`:

- `version.json`, `snapshots.json`, `backup-1.jsonl`, `backup-2.jsonl`
- `ls-snapshot.jsonl`, `ls-recursive.jsonl`, `find-a.json`, `find-empty.json`
- `stats.json`, `check.txt`
- `error-wrongpassword.txt`, `error-norepo.txt`

Bereinigung: Temp-Pfade werden zu `/fixture`, Benutzername zu `fixture-user`, übergeordnete
Verzeichniseinträge entfallen. Das Skript läuft unter Windows (Git Bash) und Linux. Das
Wegwerf-Passwort dient nur dem temporären Repository und wird nicht gespeichert. Vor jedem
Commit der Fixtures auf Namen, Pfade und Rechnernamen prüfen.

Hinweise zur restic-Ausgabe unter Windows: `restore` auf einen Pfad mit Laufwerksbuchstaben
meldet Zeitstempelfehler für übergeordnete Ordner; Restore-Fixtures daher nur mit `--include`
auf einen Unterordner erzeugen (noch nicht enthalten, Teil von Phase 3).

## Phasen und Status

| Phase | Inhalt | Status |
|---|---|---|
| 0 | Entscheidungen, Fixtures, Testzuordnung, Regeln | abgeschlossen (Restore-Fixture folgt in Phase 3) |
| 1 | Runner, Command-Builder, serde-Modelle | Kern fertig (53 Tests); offen: Prozessbaum beenden, Rest siehe unten |
| 2 | Restic-Auflösung, Provisionierung | offen |
| 3 | Repository-Service, Mount, Askpass | offen |
| 4 | Einstellungen, Filter, Cache | offen |
| 5 | UI (Slint), Themes, Dialoge | offen, Lizenz- und UI-Spike zuerst |
| 6 | Pakete, Installer, CI/CD | offen |
| 7 | Paritätsprüfung, 4.0.0, Entfernung des C#-Codes | offen |

## Risiken

| Risiko | Gegenmaßnahme |
|---|---|
| Tabellen- und Theme-Qualität des UI-Frameworks reicht nicht | UI-Spike vor Phase 5, Fallback egui |
| Slint-Lizenz passt nicht | vorab prüfen, sonst egui oder iced |
| Abweichungen beim Streaming und frühen Abbruch | Fixture-Tests und identische Grenzwerte |
| Sicherheitsregressionen bei Secrets und Pfadprüfung | `secrecy`/`zeroize`, übernommene Pfadtests, Review |
| Doppelte Pflege während der Migration | 3.x nur noch Fehlerbehebungen |

## Stand Phase 1 (Runner, Befehle, Modelle)

Umgesetzt in `rust/crates/restic-core` (Gegenstücke in Klammern):

- `runner.rs` – asynchroner Runner mit `run`, `run_lines`, `run_binary`, `run_json`,
  `run_json_array(_until)` und `run_find_matches_until` (`ResticProcessRunner.cs`).
  Start ohne Shell, stdin geschlossen, stderr auf 64 KiB begrenzt aber vollständig geleert,
  früher Abbruch beendet den Prozess, Benutzerabbruch bleibt `ResticError::Cancelled`.
- `json_stream.rs` – byteweiser Scanner für JSON-Arrays und `find`-Treffer innerhalb einer
  Snapshot-Gruppe (`FindMatchReader.cs`), tolerantes JSONL-Lesen.
- `command.rs`, `env_validator.rs`, `guidance.rs`, `bounded_details.rs`, `credentials.rs`,
  `models.rs` (`ResticCommandBuilder`, `BackendEnvironmentValidator`, `ErrorGuidance`,
  `BoundedCheckDetails`, `SessionCredentials`, `Models.cs`).
- `rust/crates/restic-testkit` – Fixture-Binary `runner-fixture` und 17 Prozess-Tests.
  Prozessende wird über einen Heartbeat geprüft (`tasklist` braucht auf manchen Windows-Systemen
  über 30 s je Aufruf).

Bewusste Abweichungen und offene Punkte:

- Callbacks sind `async`-Closures mit `Flow::{Continue, Stop}` statt `Task<bool>`.
- Größenangaben verwenden das deutsche Dezimalkomma (`1,5 KB`) unabhängig von der Systemsprache;
  in C# hing das von der Thread-Kultur ab.
- Zeilenumbrüche in gesammelter Ausgabe und Prüfdetails sind immer `\n`.
- **Offen:** C# beendet mit `Kill(entireProcessTree: true)` den ganzen Prozessbaum. Rust beendet
  bisher nur den Hauptprozess. Für `restic mount` und Hilfsprozesse in Phase 3 über Job Objects
  (Windows) bzw. Prozessgruppen (Linux) lösen; dafür ist `unsafe` oder eine Bibliothek nötig und
  daher eine eigene Entscheidung (`unsafe_code = "forbid"` gilt derzeit im Workspace).
- **Offen:** `RepositoryProfile`, `SettingsService` und `AppSettings` gehören zu Phase 4.
- Prüfwerkzeuge: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`,
  `cargo test --release`, `cargo audit` laufen sauber (Rust 1.99.1, MSVC-Build-Tools).
