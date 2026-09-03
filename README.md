# Restic Browser

Eine portable, deutschsprachige Oberfläche zum übersichtlichen Durchsuchen, Prüfen und Wiederherstellen vorhandener [Restic](https://restic.net/)-Backups. Restic bleibt die einzige Schnittstelle zum Repository. Die Anwendung arbeitet lesend und verändert weder Snapshots noch Repository-Daten.

## Downloads

[**Neueste portable ResticBrowser.exe herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser.exe)

[**Neueste portable Linux-x64-Version herunterladen**](https://github.com/Cyberhunter88/restic-browser-windows/releases/latest/download/ResticBrowser-linux-x64.tar.gz)

Alle veröffentlichten Versionen und Versionshinweise stehen unter [GitHub Releases](https://github.com/Cyberhunter88/restic-browser-windows/releases). Vertrauenswürdig sind ausschließlich Dateien aus diesem offiziellen GitHub-Repository und dessen offiziellen Releases. Die Windows-Binärdateien sind derzeit nicht digital signiert.

## Plattformen und Funktionen

- Windows x64 als selbstständige `ResticBrowser.exe`
- Linux x64 als selbstständiges `ResticBrowser-linux-x64.tar.gz`
- lokale Repository-Ordner sowie entfernte SFTP-Repositories
- übersichtliche Snapshot-Auswahl mit einklappbaren Filtern für Host, Pfad, Tag und ID
- dateisystemartige Navigation, Suche im gewählten Snapshot und Suche nach der neuesten Dateiversion
- Dateivorschau für unterstützte Text- und Bilddateien
- Wiederherstellung einzelner oder mehrerer Dateien und Ordner mit Fortschritt, Abbruch und Ergebnisbericht
- direkte Wiederherstellung auf einen Linux-x64-VPS über SSH
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

Restic Browser steht als portable Anwendung für Windows und Linux sowie optional als Windows-Installer zur Verfügung:

- **Portable Nutzung unter Windows**: `ResticBrowser.exe` herunterladen und direkt starten. Zum Entfernen einfach die Datei oder den Ordner löschen.
- **Portable Nutzung unter Linux/Ubuntu**: `ResticBrowser-linux-x64.tar.gz` herunterladen, in einen eigenen Ordner entpacken und die enthaltene Datei `ResticBrowser` direkt starten. Zum Entfernen einfach den Ordner löschen.
- **Windows-Installation via Setup**: `ResticBrowser-Setup.exe` ausführen, um die Anwendung im Standard-Programmordner (`C:\Program Files\Restic Browser`) mit Startmenü-Verknüpfung zu installieren. Die Deinstallation erfolgt sauber über die Windows-Systemsteuerung (Apps & Features).

Gespeicherte Profile bleiben bei Deinstallation oder Aktualisierung erhalten; Passwörter werden nie gespeichert.

Restic wird nicht mitgeliefert. Es wird in dieser Reihenfolge gesucht:

1. neben der Anwendung (`restic.exe` unter Windows, `restic` unter Linux)
2. im Unterordner `tools`
3. über `PATH`
4. unter Windows zusätzlich in den üblichen WinGet-Pfaden

Für eine portable Nutzung Restic einfach neben die Anwendung oder in `tools` legen. Profilinformationen liegen unter Windows in `%LOCALAPPDATA%\ResticBrowser` und unter Linux in `$XDG_DATA_HOME/ResticBrowser` beziehungsweise `~/.local/share/ResticBrowser`.

## Linux-Voraussetzungen

Die Linux-Ausgabe enthält die .NET-Runtime, benötigt aber die üblichen Desktop-Grafikbibliotheken. Unter Ubuntu können diese bei Bedarf mit folgendem Befehl installiert werden:

```sh
sudo apt update
sudo apt install libegl1 libgbm1 libgl1 libgl1-mesa-dri libinput10
```

Avalonia verwendet unter Linux den X11-Pfad; auf Wayland-Desktops wird XWayland benötigt. Details stehen in den [Avalonia-Plattformanforderungen](https://docs.avaloniaui.net/docs/supported-platforms).

Das Einbinden eines Snapshots als virtuelles Laufwerk ist in Restic Browser ausschließlich unter Linux verfügbar. Dafür müssen FUSE (`/dev/fuse`) und `fusermount3` (oder `fusermount`) installiert und verfügbar sein. Der Mount-Pfad muss leer sein und darf sich nicht mit einem lokalen Repository überlappen. Unter Windows stehen Dateivorschau und gezielte Wiederherstellung zur Verfügung.

## Wiederherstellung auf einen Linux-VPS

Im Wiederherstellungsdialog kann als Modus **Auf Linux-Server wiederherstellen** gewählt werden. Restic läuft dabei auf dem VPS; die Backupdaten werden nicht über den lokalen Rechner zwischengespeichert. Unterstützt werden Linux-Server mit x86_64-Architektur und Restic 0.17.1 oder neuer.

Auf dem lokalen Rechner müssen die OpenSSH-Programme `ssh`, `sftp` und `ssh-keyscan` verfügbar sein. Unter Windows können sie über das optionale Windows-Feature „OpenSSH-Client“ installiert werden. Die Anmeldung ist über SSH-Agent, private Schlüsseldatei oder SSH-Passwort möglich. Tastatur-interaktive Anmeldung, MFA und automatische Rechteerhöhung per `sudo` werden nicht unterstützt.

Für jedes Sitzungsziel werden eine eigene Repository-Adresse aus Sicht des VPS und ein erlaubter Basisordner angegeben. Restore-Ziele außerhalb dieses Ordners sowie Ausbrüche über vorhandene symbolische Verknüpfungen werden abgewiesen. VPS-Ziele, SSH-Passwörter und Schlüssel-Passphrasen werden nicht dauerhaft gespeichert.

Beim ersten Verbindungsaufbau muss der angezeigte SHA-256-Hostschlüssel-Fingerprint mit einer vertrauenswürdigen Quelle verglichen und bestätigt werden. Nur dieser öffentliche Vertrauenseintrag bleibt in den Einstellungen erhalten. Ein geänderter Hostschlüssel blockiert weitere Verbindungen, bis das bisherige Vertrauen ausdrücklich entfernt wurde.

Die Anwendung installiert ihren nicht privilegierten, geheimnisfreien Helfer automatisch unter `~/.local/share/restic-browser/remote/v1/ResticBrowser.Remote`. Repository-Passwort und Backend-Variablen werden ausschließlich über den verschlüsselten SSH-Kanal übertragen und nur an den gestarteten Restic-Prozess weitergegeben.

## Entwicklung und Prüfung

Voraussetzungen: .NET 10 SDK und für den End-to-End-Test Restic 0.17.1 oder neuer im `PATH` oder neben der App.

```powershell
dotnet build ResticBrowser.slnx -c Release
dotnet run --project tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj -c Release --no-build
```

Der vollständige lokale Release-Check einschließlich Versions- und
Produktionsartefaktprüfung steht in [RELEASE.md](RELEASE.md):

```powershell
pwsh -NoProfile -File .\scripts\verify-release.ps1
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

Linux/Ubuntu (unter Linux ausführen, damit das Ausführungsbit im Archiv erhalten bleibt):

```sh
chmod +x scripts/publish-linux.sh
./scripts/publish-linux.sh
```

Das Ergebnis ist `dist/ResticBrowser-linux-x64.tar.gz`. Das Archiv enthält die ausführbare Datei `ResticBrowser`, `README.md` und `LICENSE`:

```sh
mkdir ResticBrowser-linux-x64
tar -xzf ResticBrowser-linux-x64.tar.gz -C ResticBrowser-linux-x64
cd ResticBrowser-linux-x64
./ResticBrowser
```

`restic` bei Bedarf neben die Binärdatei oder unter `tools/restic` legen. Eine .NET-Installation ist für die portable Ausgabe nicht erforderlich.

## Privacy

Restic Browser erhebt oder übermittelt keine Telemetrie. Die Anwendung greift nur auf lokale oder entfernte Restic-Repositories und Speicherziele zu, die der Benutzer ausdrücklich auswählt oder konfiguriert. Bei entfernten Backends führt die separat installierte Restic-Anwendung die dafür erforderlichen Netzwerkzugriffe aus.

## Security

Sicherheitsprobleme bitte vertraulich über [GitHubs private Sicherheitsmeldung](https://github.com/Cyberhunter88/restic-browser-windows/security/advisories/new) melden und nicht als öffentliches Issue veröffentlichen.

## Lizenz

Restic Browser steht unter der [MIT-Lizenz](LICENSE).
