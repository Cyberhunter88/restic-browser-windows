# Entscheidungen

## Restic bleibt die einzige Repository-Schnittstelle

Die Anwendung verwendet Restic für den Zugriff auf Backup-Repositories. Der
normale Workflow bleibt lesend; automatische Lösch- und Bereinigungsfunktionen
sind ausgeschlossen. Destruktive Aktionen benötigen eine separate ausdrückliche
Anforderung und die projektspezifischen Schutzmaßnahmen.

## Schlanker Funktionsumfang ab 2.0.0

SFTP, S3/MinIO, Textversionsvergleich, Snapshot-Vergleich und Sitzungsdiagnose
entfallen. Gespeicherte SFTP- und S3-Profile werden beim Laden dauerhaft entfernt.
Lokale und REST-Repositories sowie Vorschau, Dateiversionen und Restore bleiben erhalten.

## Nur lokale Verbindungen ab 3.0.0

Auf Benutzerwunsch entfällt auch REST einschließlich Zugangsdaten und Adressbau.
Gespeicherte REST-Profile werden wie SFTP/S3 beim Laden dauerhaft gelöscht.
Lokale Ordner und Netzlaufwerke bleiben erhalten; allgemeine Sitzungsvariablen bleiben verfügbar.

## Releases nur nach manueller Anforderung

Die Veröffentlichung wird in Actions mit einer gewünschten Version gestartet.
Neue Versionen gelangen über einen eigenen Versions-PR auf den geschützten
Branch `main`. Der Benutzer entscheidet durch den Merge über die Fortsetzung;
der Release wartet auf erfolgreiche Main-CI. Eine bereits auf `main` vorhandene
Version kann unmittelbar manuell zur Veröffentlichung angefordert werden.
Ein normaler Versionsbump oder Merge erzeugt keinen Release. Die zentrale
Versionsquelle und der Schutz von `main` bleiben erhalten.

## Rust-Migration (Entscheidung vom 6. November 2026)

Die Anwendung soll schrittweise von C#/Avalonia nach Rust portiert werden. Der
Rust-Workspace liegt im Unterordner `rust/` dieses Repositorys; als UI-Framework ist
Slint vorgesehen (Lizenzprüfung und UI-Spike vor Phase 5, Fallback egui). Die
C#-Linie 3.x bleibt bis zum Paritätsnachweis die veröffentlichte Version, die
Rust-Version erscheint als 4.0.0. Alle fachlichen Schutzregeln (nur lesend, Restic als
einzige Schnittstelle, Secrets nur im Speicher, keine Laufzeit-Downloads) gelten
unverändert. Plan und Grenzwerte: `docs/rust-migration.md`.

## UI-Framework der Rust-Version: egui (Entscheidung bestätigt, 6. November 2026)

Wegen Apache 2.0 und der Lizenzprüfung (`rust/deny.toml`, `cargo deny check licenses`) scheidet Slint aus
(nur GPLv3 oder eigene Bedingungen). Gewählt ist egui/eframe (MIT OR Apache-2.0); der Projektinhaber hat
das bestätigt. Der Pull Request zur Lizenzumstellung (Branch `codex/lizenz-apache-2`) wird bewusst später
gemeinsam mit dem Projektinhaber erstellt. Begründung, Messwerte
und offene Punkte: `docs/rust-ui-spike.md`. Slint-Spike bleibt als Vergleich unter `rust/spikes/slint-ui`.

## Umstellung auf Rust, Entfernung von C# und Mount (7. November 2026, Version 4.0.0)

Der Projektinhaber hat entschieden, die Rust-Version als einzige Implementierung auszuliefern und den C#-Code
(`src/`, `tests/ResticBrowser.Tests`, Solution, .NET-Skripte und -Actions) im selben Pull Request zu entfernen. Die zentrale Versionsquelle ist jetzt `rust/Cargo.toml`. Die Mount-Funktion (Linux, FUSE) entfällt ersatzlos; sie wurde in
Rust nie umgesetzt, weil sie `unsafe` oder einen zusätzlichen FUSE-Prozessbaum erfordert hätte. Das ist eine entfernte
Funktionalität und rechtfertigt MAJOR (4.0.0). Eine Wiedereinführung braucht eine ausdrücklich neue Projektentscheidung.
Die Projektlizenz ist Apache 2.0 (`LICENSE`); die Lizenzprüfung der Abhängigkeiten steht in `rust/deny.toml`.
Der manuelle Paritätsvergleich mit C# (`docs/rust-parity.md`) wurde nicht vor dem Entfernen abgeschlossen; die offenen
manuellen Punkte (Portal-Ordnerdialog, Wayland, Kontextmenü, Fensterzustand, Windows-Installer) bleiben vor dem Merge
beziehungsweise Release zu prüfen.
