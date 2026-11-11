# Changelog

Alle nennenswerten Änderungen an Restic Browser. Die Versionen folgen
[Semantic Versioning](https://semver.org/lang/de/); die Produktversion steht in `Cargo.toml`.
Diese Datei ersetzt seit 4.2.0 `STATUS.md` und `PROJECT-STATE.md` (deren Inhalt liegt in der Git-Historie).

## 4.2.0 – unveröffentlicht

### Neu

- Das gewählte Design (hell oder dunkel) wird gespeichert und beim nächsten Start wiederverwendet.
- Dateitabelle per Tastatur: Pos1, Ende, Bild auf/ab (auch mit Umschalt), Rücktaste für „Nach oben“;
  Tippen springt zum nächsten Eintrag, dessen Name so beginnt.
- Kontextmenü: „Pfad kopieren“ und, in Suchergebnissen, „Ordner des Treffers öffnen“.
- Optionale Spalte „Berechtigungen“ (Kontextmenü der Tabelle oder der Spaltenköpfe); die Wahl wird gespeichert.
- Snapshot-Filter um einen Datumsbereich „Von … bis“ (TT.MM.JJJJ) ergänzt.
- Gespeicherte Profile lassen sich im Verbindungsdialog umbenennen und entfernen. Entfernt wird nur der
  gespeicherte Eintrag, nie das Repository. Neue Profile können beim Speichern einen Namen erhalten.
- Restore-Vorschau zeigt zusätzlich zu den Summen die Liste der betroffenen Pfade (bis 10.000).
- Wiederherstellung zeigt Datenmenge und geschätzte Restzeit; nach Erfolg öffnet „Zielordner öffnen“ den
  Dateimanager.
- Textvorschau: Suche mit „Weitersuchen“ (springt zur Zeile und hebt sie hervor) und „Text kopieren“.

### Geändert

- Der Cargo-Workspace liegt in der Wurzel des Repositorys (`Cargo.toml`, `crates/`, `fixtures/`, `packaging/`);
  den Unterordner `rust/` gibt es nicht mehr. Alle Skripte liegen unter `scripts/`. Workflows, Installer,
  Dependabot und Dokumentation sind angepasst.
- Die Sitzung ist die einzige Quelle der Snapshot-Filter und setzt sie bei jedem Verbinden zurück.
- Interne Optimierungen ohne Verhaltensänderung: mitgeführter Auswahlzähler der Dateitabelle, geteilte statt
  je Bild kopierte Ergebnisse im Versions- und Analysedialog, weniger Kopien der Snapshot-Liste, einmal je
  Lauf vorbereitete Filterkriterien.
- `settings.json` kennt die optionalen Felder `DarkTheme` und `ShowPermissions`; ohne sie bleibt die Datei
  unverändert zum Format 3.x.

### Behoben

- „Abbrechen“ im Verbindungsdialog brach einen laufenden Verbindungsversuch nicht ab; er lief im Hintergrund
  weiter und verband trotzdem.
- Nach einem Repository-Wechsel blieb der alte Host- oder Tag-Filter wirksam, während die Auswahlfelder
  einen anderen Eintrag zeigten.
- „Neueste Version wiederherstellen“: Ein Abbruch mit Esc erschien als Fehler, Fehler hatten keinen
  Handlungshinweis, und der Status blieb auf „wird gesucht …“ stehen.
- Die Restore-Vorschauzeile blieb nach geändertem Ziel oder Überschreibverhalten stehen; eine abgebrochene
  Vorschau wurde als Fehler angezeigt.
- Der Status „N Element(e)“ verwendet wie die übrigen Zähler den Tausenderpunkt.

### Entfernt

- UI-Spikes unter `rust/spikes/` (Slint und egui); die Entscheidung steht in `docs/rust-ui-spike.md`.
- `docs/PERFORMANCE.md` (nur noch Messwerte der C#-Version), `STATUS.md` und `PROJECT-STATE.md`.

### Offene Prüfpunkte

- Lokal unter Windows geprüft: `cargo fmt --check`, `cargo clippy -D warnings` und die Tests von `restic-core`,
  `restic-app` und `restic-testkit` (ohne restic, also ohne die End-to-End-Tests). Nicht ausgeführt: die Tests
  von `restic-ui`, `cargo deny`, `cargo audit`, die Python-Prüfungen, Paketierung und beide Workflows nach dem
  Umzug in die Wurzel; das klärt erst die PR-CI.
- Nicht am Bildschirm geprüft: alle neuen und geänderten Dialoge in Hell und Dunkel.
- Weiterhin offen aus 4.0.x: Portal-Ordnerdialog und echter Wayland-Start unter Linux, Programmsymbol unter
  KDE/Wayland, Einfügen aus der Zwischenablage am Gerät.

## 4.1.0 – 8. November 2026

- Neu: Schnellverbindung. Ein neues Repository wird ohne Profil verbunden; gespeichert wird nur mit
  „Als Profil speichern“.
- Geändert: Build und Release nach Vorlage `restic-backup-app`. `release.yml` startet ohne Eingaben und
  veröffentlicht die Pakete des erfolgreichen Main-Builds; `release-flow.py` entfällt.

## 4.0.1 – 7. November 2026

- Behoben: Strg+V im Passwortfeld (Feature `clipboard`); angehängte Zeilenumbrüche werden entfernt.
- Neu: Programmsymbol unter Wayland über `app_id` und einen optionalen Desktop-Eintrag im Linux-Paket.
- Geändert: Paketprofil `dist` (Fat-LTO, Linux-Binärdatei 24,5 → 14,5 MB), `rfd` 0.17, `sha2` 0.11,
  gebündelte Dependabot-Updates.

## 4.0.0 – 7. November 2026

- Geändert: Die Anwendung ist vollständig in Rust geschrieben (egui/eframe); Lizenz Apache 2.0;
  Versionsquelle ist `Cargo.toml`. Verlauf der Migration: `docs/rust-migration.md`.
- Entfernt: C#-/Avalonia-Code und das Einbinden von Snapshots (Mount).

## 3.x (C#/Avalonia, entfernt)

- 3.0.9: UX-Überarbeitung (Leerzustand mit Profilen, Pfadleiste, sortierbare Spalten, Tastaturkürzel,
  Handlungshinweise zu Restic-Fehlern, gemerktes Wiederherstellungsziel und Fensterzustand).
- 3.0.8: Dateiversionen nutzen die geladene Snapshot-Liste; entfernte Profile werden einmalig in
  `settings.json.bak` gesichert.
- 3.0.7: Restore-Ziele werden gegen Repository-Pfade einschließlich symbolischer Verknüpfungen geprüft;
  Glob-Sonderzeichen in der Auswahl gelten als Dateinamen.
- 3.0.6: Profildateien werden bei Ladefehlern nicht überschrieben (Sperren, atomares Schreiben); der Runner
  räumt Prozesse auch bei Fehlern auf; Integritätsprüfung streamt, Details auf 64 KiB begrenzt.
- 3.0.3 und 3.0.4: manueller Release-Ablauf und tolerantere Versionseingabe.
- 3.0.2: Überarbeiteter Mount-Dialog.
- 3.0.1: `--no-lock` für alle lesenden Repository-Aufrufe.
- 3.0.0: REST-Backend entfernt; die Anwendung unterstützt nur lokale Ordner und Netzlaufwerke.

## 2.0.0

- Entfernt: SFTP, S3/MinIO, Textversions- und Snapshot-Vergleich, Sitzungsdiagnose.

## 1.x

- 1.0.4: Kompakte Fensterlayouts; Restic-Kandidaten werden vor der Auswahl geprüft.
- 1.0.3: Zentrale Produktversion statt `version.txt`.
- 1.0.2: Prüfung des Linux-Archivs auf Ubuntu und Arch in der CI.
- 1.0.1: Dateiversionssuche endet am Trefferlimit; „Nur neueste Snapshots“ wird inkrementell geführt.
- 1.0.0: Zielumfang festgelegt (Profile, Snapshot-Auswahl, Suche, Vorschau, Dateiversionen, Speicheranalyse,
  Integritätsprüfung, lokale Wiederherstellung); VPS-Remote-Restore, SSH-Helfer, TAR-Export und
  Snapshot-Zeitachse entfernt.
