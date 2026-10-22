# Project State

## Dokumentationsänderung: PR-Vorlage entfernen

- Auf `codex/remove-pr-template` von aktuellem `origin/main` wird die
  Repository-Vorlage `.github/pull_request_template.md` entfernt.
- Neue PRs sollen nach dem Merge dieser Änderung keine vorgegebenen
  Abschnitte oder Versionsplatzhalter enthalten. Der einzige offene PR #102
  wurde bereits separat bereinigt; sein fachlicher Beschreibungstext blieb erhalten.
- Reine Dokumentationsänderung; Produktversion bleibt unverändert.
- PR-Erstellung ausdrücklich beauftragt; den nach Merge von PR #102 aktualisierten
  `origin/main` (`dd0c36a`) integriert. Der Vorlagen-Commit `2744a7f` und sämtliche
  Dokumentationsnotizen bleiben erhalten.

## Aktuelle Aufgabe

Restic Browser 3.0.6 verbessert Profilspeicherung, Prozess-Cleanup,
Integritätsprüfung und reproduzierbare Builds.

## CI-Reparatur für PR #102

- PR-Head `a283ae2` und lokaler Branch stimmen überein. Die vorigen Angaben zum
  uncommitteten Arbeitsstand sind historisch; PR #102 ist inzwischen veröffentlicht.
- Run `39733145950` baut erfolgreich, scheitert aber unter Windows und Ubuntu im
  Runner-Test. Bei leerem stdout kann dessen Ende vor dem Prozessende eintreten.
  Der bisherige `HasExited`-Filter verliert dann den regulären Fehler-Exitcode.
- Ein Stream-Wrapper erkennt tatsächliches EOF. Erst danach wird unter Beachtung
  des CancellationTokens auf den Exitcode gewartet; Fehler bei noch offenem
  JSON-Stream werden weiterhin sofort mit Prozess-Cleanup behandelt.
- UTF-8 wird im Testhelfer ausdrücklich gesetzt. Fehlermeldungen nennen den
  Testfall. Python-Testprozesse erzeugen EOF vor dem gesteuert freigegebenen
  Prozessende; Fehler-Exitcode, erfolgreicher Exit ohne JSON und Abbruch sind
  getrennt abgedeckt. Der Test weist den Fehler im unveränderten PR-Code nach.
- Version 3.0.6 bleibt die bereits erhöhte Zielversion desselben PRs.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 59/59 Anwendungstests
  ohne übersprungene Tests, Format- und Versionsprüfung, `git diff --check` und
  Secret-Musterscan des Arbeitsbaums ohne Treffer. Der gezielte EOF-Test besteht
  mit Fix in zehn aufeinanderfolgenden Durchläufen und scheitert ohne Fix.
- Commit und Push wurden nach den lokalen Prüfungen ausdrücklich freigegeben.
  Windows-Bestätigung und PR-CI für den veröffentlichten Fix bleiben separat
  zu bestätigen; lokale Tests gelten nicht als Remote-CI-Ergebnis.

## Aktuelle Änderung 3.0.6

- Feature-Branch `codex/reliability-optimizations` von frisch abgeglichenem
  `origin/main` (`2c81f3e`); zuvor sauberer Arbeitsbaum auf `main`.
- Beschädigte/unlesbare Profildateien ergeben einen deutschen Fehler und werden
  nicht überschrieben. Schreibvorgänge sind innerhalb einer Instanz und über
  Instanzen hinweg gesperrt (maximal fünf Sekunden Wartezeit). Eindeutige temporäre
  Dateien werden vor dem atomaren Austausch auf Datenträger geschrieben.
- Prozess-Cleanup ist für alle Runner-Pfade zentralisiert. Auswertungsfehler
  beenden Restic, warten auf das Prozessende und beobachten die stderr-Aufgabe;
  absichtliches frühes Ende und Benutzerabbruch bleiben unterscheidbar.
- Integritätsprüfung verarbeitet stdout zeilenweise. Details bleiben inklusive
  Kürzungshinweis innerhalb von 64 KiB UTF-8; Ergebnisfelder werden vollständig
  ausgewertet. Der Dialog meldet die Kürzung vor den Details; die lange
  Beschriftung der vollständigen Prüfung bricht um.
- SDK-Vorgabe 10.0.402 (`latestPatch`, keine Vorabversionen), versionierte
  NuGet-Lockdateien und `--locked-mode` in CI. Auch implizite Restores während
  CI-Paketierungen dürfen Lockdateien nicht ändern. Paketversionen unverändert.
- Regressionstests für Profildateifehler, gleichzeitiges Speichern, Prozesssperren,
  echte Runner-Hilfsprozesse und begrenzte Prüfausgaben ergänzt. Die UI-Prüfung
  erfasst zusätzlich Profilfehler und gekürzte Prüfdetails in Hell/Dunkel.
- PATCH-Zielversion auf Benutzerwunsch 3.0.6; keine Veröffentlichung angefordert.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 58/58 Anwendungstests
  (keine übersprungen), 23 Python-Tests, Formatprüfung, zentrale Versionsprüfung,
  NuGet-Schwachstellenprüfung ohne Treffer, Locked Restore sowie dessen Negativtest.
- Hauptfenster bei 760/1099/1100/1360 DIP, Verbindungsdialog, Profilfehler und
  gekürzte Prüfdetails in Hell/Dunkel unter Xvfb erfasst und visuell geprüft;
  Hover und Fokus simuliert.
- Windows-EXE erfolgreich cross-kompiliert; direkte PE-Prüfung bestätigt
  Dateiversion 3.0.6.0 und Produktversion 3.0.6. Die optionale EXE-Versionsprüfung
  über FileVersionInfo liefert unter Linux leere Werte und bleibt für Windows offen.
- Linux-Archiv einschließlich signaturgeprüftem Restic erstellt und mit
  `verify-linux-package.sh` erfolgreich auf Ausführungsrechte, Bundle-Version und
  GUI-Start aus einem Pfad mit Leerzeichen geprüft. Bei der Restic-Paketierung
  erscheinen bestehende GnuPG-Warnungen zu temporären Ordnerrechten/Schlüsselvertrauen;
  die Signatur stimmt mit dem festgelegten Fingerprint überein.
- Pre-Push-Hook installiert und mit unkritischen sowie synthetischen Treffer-Commits
  geprüft. Secret-Musterscan des Arbeitsbaums und `git diff --check` bestanden.
- Native Windows-/Installer-Prüfung (Inno Setup fehlt), Arch-Container (Docker fehlt),
  echter FUSE-Mount und Remote-CI bleiben offen. Keine Commits oder Veröffentlichung.

## Aktuelle Änderung 3.0.4

- Release-Run `39017899886` scheiterte an der Eingabe `v3.0.3`, bevor ein
  Release-Build oder eine Veröffentlichung gestartet wurde.
- Manuelle Eingaben akzeptieren jetzt `3.0.4` und `v3.0.4` sowie `V` und äußere
  Leerzeichen; intern bleiben Version, Branch und Tag eindeutig normalisiert.
- Ungültige Versionen erscheinen als deutsche Actions-Fehlermeldung und in der
  Laufzusammenfassung statt als Python-Stacktrace. Der Lauf bleibt fehlgeschlagen.
- PATCH 3.0.4 für die Korrektur der Release-Eingabe; Regressionstests ergänzen
  die betroffene Eingabe und beide Folgepfade. Lokal bestanden: 15 Release-Tests,
  Release-Build ohne Warnungen/Fehler, 54/54 Anwendungstests, Formatierung,
  Versionsprüfung und Actionlint mit ShellCheck. PR-CI folgt nach dem Push.

## Aktuelle Änderung 3.0.3

- `Actions → Release starten` nimmt eine SemVer-Version entgegen; höhere Versionen
  erzeugen einen Versions-PR mit ausdrücklich gestarteter CI.
- Nach dem manuellen Merge und erfolgreicher Main-CI veröffentlicht der Workflow
  genau diesen Commit. Normale Merges lösen keine Veröffentlichung aus.
- Eine bereits auf `main` vorhandene Version kann direkt manuell veröffentlicht
  oder mit denselben unveränderten Artefakten erneut geprüft werden.
- Abschließender CI-Pflichtcheck wertet auch fehlgeschlagene oder übersprungene
  Vorabprüfungen aus. Zeitlimits begrenzen Jobs; NuGet-Cache berücksichtigt zentrale
  Build- und Paketdateien. Der veraltete automatische Release-Detektor entfällt.
- Artifact-Actions verwenden Node 24; bereits komprimierte Linux-/Preview-Pakete
  und PNGs werden beim Upload nicht nochmals komprimiert.
- Patch-Version für interne CI-/Release-Verbesserung: 3.0.3.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 54/54 Anwendungstests,
  12 Release-Flow-Tests, 8 Paketprüfer-Tests, Formatierung, Versionskonsistenz,
  NuGet-Schwachstellenprüfung und Actionlint einschließlich ShellCheck.
- GitHub-PR-CI folgt nach dem Push. Der neue manuelle Ablauf wird nach dem Merge
  auf `main` verfügbar; ein echter Release wurde bei der Umsetzung nicht angefordert.

## Zielumfang 1.0.0

- Repository-Profile für lokale Ordner und Netzlaufwerke bleiben erhalten.
- Snapshot-Auswahl, Suche, Vorschau, Dateiversionen,
  Speicheranalyse, lesende Integritätsprüfung und lokale Wiederherstellung bleiben erhalten.
- Linux-Mount bleibt als getrenntes lokales Werkzeug erhalten.
- VPS-Remote-Restore, SSH-Helfer, TAR-Export und Snapshot-Zeitachse sind entfernt.
- Große Verzeichnisse werden schrittweise angezeigt, vollständig sortiert und nur innerhalb
  des bestehenden begrenzten LRU-Caches zwischengespeichert.

## Prüfung

- Release-Build und lokaler Test-Runner werden auf dem Feature-Branch ausgeführt.
- Native Linux-, Mount-, Installer- und visuelle Theme-Prüfungen erfolgen in der passenden Umgebung.

## Aktuelle Änderung 3.0.2

- Mount-Dialog mit scrollbarem Inhalt, getrennten Optionen und Status sowie separater primärer Aktion überarbeitet.
- Fehler bleiben im Statusbereich sichtbar, statt durch den allgemeinen Bereitschaftsstatus überschrieben zu werden.
- Nicht erreichbare FUSE-Ziele erhalten einen deutschen Handlungshinweis ohne automatische Trennung oder Löschung.
- Während des Einbindens bleiben Zielauswahl und Schließen gesperrt; die primäre Aktion ermöglicht Abbruch.
- Ungültiges `mount --snapshot` entfernt; ausgewählter Snapshot wird über die unterstützte `ids/%I`-Ansicht im Dateimanager geöffnet. Das Repository bleibt eingebunden; es gibt keinen Snapshot-ID-Mount-Filter in Restic 0.19.1.
- Reguläres Aushängen über fusermount3/fusermount vor Prozessende; Fehler behalten die aktive Einbindung. Verwaiste FUSE-Ziele können ausdrücklich getrennt werden.
- Mount-Start prüft die echte Einbindung statt einer festen Wartezeit, leert beide Prozess-Pipes und behandelt Zeitlimit/Abbruch mit Cleanup.
- Patch-Version 3.0.2 auf Feature-Branch `codex/mount-dialog-layout`.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 54/54 Tests einschließlich Mount-CLI-Kompatibilität und Aushänge-Helfer, Format- und Versionsprüfung. Mount-Dialog in Hell/Dunkel, mit FUSE-Fehler und kleiner Fenstergröße unter Xvfb geprüft.
- Echter FUSE-Mount lokal nicht ausführbar, da /dev/fuse in der Ausführungsumgebung fehlt; Bestätigung am Zielsystem bleibt offen. PR-CI wird nach Veröffentlichung geprüft.

### Vorherige Änderung 3.0.1

- Benutzer meldet hängendes Snapshot-Laden über einen KIO-FUSE-SMB-Mount; direkter Aufruf mit `snapshots --no-lock` funktioniert. Der konkrete Mount ist hier nicht zugänglich.
- Zentraler CommandBuilder ergänzt `--no-lock` für alle Repository-Aufrufe, um Lock-Dateien im lesenden Workflow zu vermeiden.
- Regression prüft die Befehlsargumente und lokalen End-to-End-Zugriff auf ein Repository ohne Schreibrechte unter Linux.
- Patch-Version 3.0.1; Feature-Branch `codex/read-only-network-snapshots` auf aktuellem main nach PR #89.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 52/52 Tests einschließlich Lesen und Restore ohne Repository-Schreibrechte auf Arch/CachyOS, Format- und Versionsprüfung. PR-CI wird nach Veröffentlichung geprüft; Bestätigung auf dem tatsächlichen KIO-FUSE-Mount bleibt erforderlich.

### Vorherige Änderung 3.0.0

- REST samt Verbindungsoption, Profilfeldern, Adressbau und Zugangsdaten entfernt.
- Auf ausdrücklichen Benutzerwunsch werden auch REST-Altprofile beim Laden gelöscht.
- Restic-Feld zeigt „Automatisch auswählen“ statt eines leeren Felds; tatsächliche Herkunft, Version und Pfad erscheinen nach erfolgreicher Prüfung.
- Eigenständiger Feature-Branch `codex/local-only-connection` auf `origin/main` nach PR #88.
- Major-Version 3.0.0 für entfernte Funktionalität.
- Lokal bestanden: Release-Build ohne Warnungen/Fehler, 51/51 Tests einschließlich Profilmigration und Restic-End-to-End-Restore, Format- und Versionsprüfung.
- Hauptfenster bei 760/1099/1100/1360 DIP und Verbindungsdialog in Hell/Dunkel unter Xvfb geprüft; automatische Restic-Anzeige, entfernte Verbindungsauswahl sowie Hover/Fokus bestätigt. PR-CI wird nach Veröffentlichung geprüft.

### Vorherige Änderung 2.0.0

- SFTP, S3/MinIO, Textversionsvergleich, Snapshot-Vergleich und Sitzungsdiagnose entfernt.
- Gespeicherte SFTP- und S3-Profile werden beim Laden dauerhaft entfernt; lokale und REST-Profile bleiben erhalten.
- Veraltete OpenSSH-CI-Vorbereitung und Laufzeit-Messinstrumentierung entfernt.
- Major-Version gemäß Projektregel für entfernte Funktionalität: 2.0.0.
- Aktuelles `origin/main` mit den Verbesserungen aus 1.0.4 (`10b3325`, PR #87) integriert; kompakte Fensterlayouts und Linux-Restic-Auflösung bleiben erhalten.
- Lokal am 8. Oktober 2026 bestanden: Release-Build ohne Warnungen, 52/52 Tests einschließlich Profilmigration in beiden Einstellungsformaten und lokalem Restic-End-to-End-Restore, Formatprüfung, Versionsprüfung und NuGet-Schwachstellenprüfung.
- Hauptfenster und Verbindungsdialog unter Linux/Xvfb in Hell und Dunkel erfasst und geprüft, einschließlich lokaler Vorauswahl, REST-Feldern/Zugangsdaten, geöffnetem Verbindungstyp-Popup sowie simuliertem Hover und Fokus.
- Git-Ausgangszustand durch Aktualisierung der veralteten Index-Metadaten bereinigt; Umsetzung auf `codex/slim-browser`; Windows-/Linux-GitHub-CI wird im Pull Request geprüft; native Windows-/Wayland- und Installer-Prüfungen wurden nicht ausgeführt.
- Tatsächlicher Paketstand: Avalonia 12.1.3, DataGrid 12.1.2. Die AGENTS-Zielangabe 12.1.2 weicht davon ab; Paketversionen sind durch diese Änderung nicht angepasst worden.

### Vorherige Änderung 1.0.4

- Ausgangspunkt: aktuelles `main` nach PR #86 (`4b0071e`). Die Bereinigung für 1.0.3 ist bereits integriert; die zuvor lokale `origin/main`-Referenz war veraltet.
- Im bisherigen Arbeitsbaum sind nur Zeilenendenänderungen gegenüber `HEAD` vorhanden. Dieser Arbeitsbaum bleibt unverändert; die Umsetzung erfolgt auf `codex/arch-layout-restic` in einem separaten Arbeitsbaum.
- Unter 1100 DIP bleibt der Browser zweispaltig mit einer 210-DIP-Snapshot-Spalte, umgebrochener Werkzeugleiste und eigener Zeile für das Suchfeld. Dateinamen behalten mindestens 120 DIP; die Berechtigungsspalte erscheint bei größeren Fenstern wieder.
- Restic-Kandidaten werden vor der Auswahl auf Lesbarkeit, Ausführbarkeit und Version geprüft. Automatische Fehler werden übersprungen; ausdrückliche Auswahl, Zeitlimit und Benutzerabbruch werden getrennt behandelt.
- Linux-Bundle-Hash stammt aus dem offiziell signierten 0.19.1-Archiv und wird beim Build sowie vor dem Start geprüft. Die Herkunft wird ausschließlich als Laufzeitinformation übernommen.
- Lokal auf CachyOS (Arch-basiert), KDE/Wayland über XWayland: Release-Build und 54/54 Tests bestanden; keine bekannten NuGet-Schwachstellen. Hauptfenster bei 760/1099/1100/1360 DIP sowie Verbindungsdialog in Hell/Dunkel erfasst; Hover und Fokus simuliert. Die grafische Prüfung besteht auch bei 100/150/200 Prozent Skalierung.
- Portable Linux-Ausgabe und Windows-Cross-Publish lokal erstellt; Linux-Archiv samt gebündeltem Restic und GUI-Start unter Xvfb geprüft.
- Windows- und reiner Arch-Container-Pakettest erfolgen in CI; lokale Umgebung enthält kein Docker oder Inno Setup. Ergebnis der PR-/Release-CI und Release-Asset-Prüfung werden im Abschlussbericht festgehalten.

### Vorherige Änderung 1.0.3

- version.txt entfällt; ResticBrowserProductVersion in Directory.Build.props steuert Build, Installer und Releases.
- Automatische Releases vergleichen die Produktversion vor und nach einem Push; manuelle Starts auf main bleiben erhalten.
- Erzeugte Build-Ausgaben und der abgeschlossene Optimierungsplan werden entfernt; aktive Paket- und Signaturprüfungen bleiben erhalten.

- Lokal am 5. Oktober 2026 bestanden: Release-Build, Formatprüfung, NuGet-Schwachstellenprüfung, 49/49 Tests, PowerShell-Syntax und Versions-/Release-Szenarien.
- Portable Windows-EXE gebaut; Restic-Signatur und EXE-Version 1.0.3 geprüft. Installer offen: Inno Setup fehlt. Linux-CI offen: kein Push beauftragt.

### Vorherige Änderung 1.0.2

- Patch-Version 1.0.2: gemeinsame Prüfung des Linux-Archivs auf Ubuntu und aktuellem Arch Linux in CI und Release; kein systemweites .NET oder Restic im Arch-Test.
- Paketprüfung startet die GUI unter Xvfb ohne Root-Rechte auf Arch, aus einem Pfad mit Leerzeichen und einem anderen Arbeitsverzeichnis; gebündeltes Restic wird über `version --json` gegen das Manifest geprüft.
- Linux-Paketierung bereitet Restic einmal vor; Release-Artefaktprüfung verlangt `tools/restic`.
- Lokal bestanden: Shell-Syntax, Workflow-YAML/Abhängigkeiten und acht Paketprüfer-Tests mit Fixture-Binärdateien (einschließlich Fehlerszenarien und Cleanup).
- CI am 1. Oktober 2026 für Commit `0f62698` erfolgreich: Formatierung, Paket-Schwachstellenprüfung, Versionsprüfung, Windows-/Linux-Builds und Tests sowie Paketstart auf Ubuntu und Arch unter Xvfb (GitHub Actions Run `36845974555`).
- Die Windows-Arbeitsumgebung wurde am 2. Oktober 2026 erneut geprüft: .NET SDK und PowerShell sind verfügbar; Docker und eine installierte WSL-Distribution stehen für den Arch-Desktop-Test nicht zur Verfügung.
- Offen: manuelle Arch-Desktop-Prüfung von Hauptfenster/Verbindungsdialog in Hell und Dunkel, Verbindung/Vorschau/Restore sowie Wayland über XWayland.
- Ausgangsstand ist der über PR #83 integrierte Stand 1.0.1. Der Arbeitsbaum war bei der Prüfung am 2. Oktober 2026 sauber; die früher vermerkten lokalen Zeilenendenänderungen sind nicht mehr offen.

### Vorherige Änderung 1.0.1

- Patch-Version 1.0.1: Dateiversionssuche beendet den Restic-Stream beim Trefferlimit.
- „Nur neueste Snapshots“ hält während des Ladens eine inkrementelle Projektion je Host und Pfad aktuell; beim Abschluss wird die Liste einmal sortiert.
- Die Restic-Bundle-Version bleibt 0.19.1. Die interne Bereinigungsprüfung ergab keine sichere private oder interne Entfernung; der ungenutzte öffentliche Runner-Wrapper bleibt aus Kompatibilitätsgründen bestehen.
