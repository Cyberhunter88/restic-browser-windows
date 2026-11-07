# Paritätsliste C# 3.x gegen Rust

Stand: Rust-Branch `codex/rust-migration`. „geprüft“ heißt: mit echtem restic und dem Demo-Repository
ausprobiert (Windows). „Test“ heißt: automatisierter Test. Der C#-Code wurde mit 4.0.0 entfernt; ein vollständiger manueller Vergleich beider
Anwendungen fand nicht mehr statt, die offenen manuellen Punkte stehen unten und in `docs/rust-linux-test.md`.

| Funktion (C#-Fenster) | Rust | Nachweis |
|---|---|---|
| Verbinden, Profile, Backends, Passwort nur im Speicher | ja | Test, geprüft |
| Restic-Auflösung (Datei, enthalten, portabel, PATH, WinGet) | ja | Test, eingebettet geprüft |
| Snapshot-Liste, Filter (Text, Host, Tag, nur neueste) | ja | Test, geprüft |
| Dateibrowser, Navigation, Pfadleiste, Sortierung, Mehrfachauswahl | ja | Test, geprüft |
| Suche im Snapshot (10.000 Treffer) | ja | Test, geprüft |
| Neueste Version wiederherstellen | ja | Test (Sitzung); Oberfläche nicht geprüft |
| Wiederherstellen inkl. Vorschau, Abbruch, Zielordner | ja | Test, geprüft |
| Dateivorschau (Text, Bild, Binär) | ja | Test, geprüft |
| Dateiversionen und Wiederherstellung einer Version | ja | Test, geprüft |
| Integritätsprüfung (schnell, vollständig) | ja | Test, geprüft |
| Speicheranalyse | ja | Test, geprüft |
| Statistik (nur auf Anforderung) | ja (neu, in C# keine Anzeige) | Test, geprüft |
| Hell/Dunkel, dunkle Titelleiste | ja | geprüft |
| Fenstergröße und -position speichern | ja | nicht manuell geprüft |
| Kontextmenü | ja | nicht manuell geprüft |
| Programmsymbol (Fenster, EXE) | ja | EXE-Symbol geprüft |
| Mount (Linux) | **entfernt** | Projektentscheidung vom 7. November 2026: entfällt ersatzlos (4.0.0) |
| Windows-Installer, Release-Assets | ja (per CI) | Skripte und Workflow umgestellt; erster Lauf auf GitHub steht aus |
| Linux-Betrieb | ja | Test (inkl. unix), Xvfb/X11 geprüft, Paket geprüft; Portal-Dialog, Wayland, Kontextmenü, Fensterzustand manuell offen |
