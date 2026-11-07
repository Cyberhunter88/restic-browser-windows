# Paritätsliste C# 3.x gegen Rust

Stand: Rust-Branch `codex/rust-migration`. „geprüft“ heißt: mit echtem restic und dem Demo-Repository
ausprobiert (Windows). „Test“ heißt: automatisierter Test. Ein vollständiger manueller Vergleich beider
Anwendungen steht noch aus; erst danach ist 4.0.0 vertretbar.

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
| Mount (Linux) | **nein** | braucht `unsafe`-Entscheidung |
| Windows-Installer, Release-Assets | nein | Phase 6 |
| Linux-Betrieb | ja | Test (inkl. unix), Xvfb/X11 geprüft, Paket geprüft; Portal-Dialog, Wayland, Kontextmenü, Fensterzustand manuell offen |
