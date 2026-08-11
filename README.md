# Restic Browser

Eine portable, deutschsprachige Oberfläche zum übersichtlichen Durchsuchen, Prüfen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Restic bleibt die einzige Schnittstelle zum Repository. Die Anwendung arbeitet lesend und verändert weder Snapshots noch Repository-Daten.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

Alle veröffentlichten Versionen und Versionshinweise stehen unter [GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases). Vertrauenswürdig sind ausschließlich Dateien aus diesem offiziellen GitHub-Repository und dessen offiziellen Releases. Die Windows-Binärdateien sind derzeit nicht digital signiert.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe`
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz`
- lokale Repository-Ordner sowie entfernte SFTP-Repositories
- übersichtliche Snapshot-Auswahl mit einklappbaren Filtern für Host, Pfad, Tag und ID
- dateisystemartige Navigation, Suche im gewählten Snapshot und Suche nach der neuesten Dateiversion
- Dateivorschau für unterstützte Text- und Bilddateien
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt, Abbruch und Ergebnisbericht
- Snapshot-Vergleich, Zeitachse und Speicheranalyse
- lesende schnelle oder vollständige Integritätsprüfung des Repositorys
- Einbinden von Snapshots als virtuelles Laufwerk unter Linux
- Passwörter und Backend-Zugangsdaten nur im Arbeitsspeicher
- aufgeräumte deutsche Oberfläche mit hellem und dunklem Design

## Bewusst nur lesender Zugriff

Restic Browser bietet keine Funktion zum Löschen, Bereinigen, Reparieren oder Erstellen
von Snapshots und Repository-Daten. Dadurch können beim Durchsuchen und
Wiederherstellen keine Sicherungen versehentlich verändert werden. Administrative
Restic-Befehle müssen bei Bedarf bewusst außerhalb der Anwendung ausgeführt werden.

Lesezeichen werden nicht gespeichert. Die Navigation konzentriert sich auf die
Snapshot-Auswahl, den aktuellen Pfad und die Vorwärts-/Zurück-Navigation innerhalb
der laufenden Sitzung.

## Installation und Deinstallation

Restic Browser steht sowohl als portable Anwendung als auch optional als Windows-Installer zur Verfügung:

- **Portable Nutzung (Windows & Linux)**: Die Windows-EXE (`ResticBrowser.exe`) herunterladen oder das ZIP/TAR.GZ-Archiv entpacken und direkt starten. Zum Entfernen einfach die Anwendung bzw. den Ordner löschen.
- **Windows-Installation via Setup**: `ResticBrowser-Setup.exe` ausführen, um die Anwendung im Standard-Programmordner (`C:\Program Files\Restic Browser`) mit Startmenü-Verknüpfung zu installieren. Die Deinstallation erfolgt sauber über die Windows-Systemsteuerung (Apps & Features).

Gespeicherte Profile bleiben bei Deinstallation oder Aktualisierung erhalten; Passwörter werden nie gespeichert.

Restic wird nicht mitgeliefert. Es wird in dieser Reihenfolge gesucht:

1. neben der Anwendung (`restic.exe` unter Windows, `restic` unter Linux)
2. im Unterordner `tools`
3. über `PATH`
4. unter Windows zusätzlich in den üblichen WinGet-Pfaden

Für eine portable Nutzung Restic einfach neben die Anwendung oder in `tools` legen. Profilinformationen liegen unter Windows in `%LOCALAPPDATA%\ResticBrowser` und unter Linux in `$XDG_DATA_HOME/ResticBrowser` beziehungsweise `~/.local/share/ResticBrowser`.

## Linux-Voraussetzungen

Die Linux-Ausgabe enthält die .NET-Runtime, benötigt aber die üblichen Desktop-Grafikbibliotheken. Für Ubuntu 24.04 und Debian 13 müssen insbesondere `libgbm1`, `libgl1-mesa-dri`, `libegl1-mesa` und `libinput10` verfügbar sein. Avalonia verwendet unter Linux den X11-Pfad; auf Wayland-Desktops wird XWayland benötigt. Details stehen in den [Avalonia-Plattformanforderungen](https://docs.avaloniaui.net/docs/supported-platforms).

Das Einbinden eines Snapshots als virtuelles Laufwerk ist in Restic Browser ausschließlich unter Linux verfügbar. Dafür müssen FUSE (`/dev/fuse`) und `fusermount3` (oder `fusermount`) installiert und verfügbar sein. Der Mount-Pfad muss leer sein und darf sich nicht mit einem lokalen Repository überlappen. Unter Windows stehen Dateivorschau und gezielte Wiederherstellung zur Verfügung.

## Entwicklung und Prüfung

Voraussetzungen: .NET 10 SDK und für den End-to-End-Test Restic 0.17.1 oder neuer im `PATH` oder neben der App.

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
```

## Ausgaben und Installer

Windows Portable:

```powershell
./scripts/publish-windows.ps1
```

Das Ergebnis ist `dist/ResticBrowser.exe`.

Windows Installer (benötigt [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
./scripts/publish-windows-installer.ps1
```

Das Ergebnis ist `dist/ResticBrowser-Setup.exe`.

Linux (unter Linux ausführen, damit das Ausführungsbit im Archiv erhalten bleibt):

```sh
chmod +x scripts/publish-linux.sh
./scripts/publish-linux.sh
```

Das Ergebnis ist `dist/ResticBrowser-linux-x64.tar.gz`. Das Archiv entpacken, `restic` bei Bedarf neben die Binärdatei oder unter `tools/restic` legen und `./ResticBrowser` starten.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie. Die Anwendung greift nur auf lokale oder entfernte Restic-Repositories und Speicherziele zu, die der Benutzer ausdrücklich auswählt oder konfiguriert. Bei entfernten Backends führt die separat installierte Restic-Anwendung die dafür erforderlichen Netzwerkzugriffe aus.

## Security

Sicherheitsprobleme bitte vertraulich über [GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new) melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [MIT-Lizenz](LICENSE).
