# Project State

## Aktuelle Aufgabe

Prüf- und Auslieferungskette stabilisieren sowie einen datenschutzfreundlichen
Sitzungs-Diagnose-Export ergänzen.

## Aktueller Status

- Arbeitsbranch: `codex/reliability-diagnostics-0-3-10`; Ausgangspunkt ist `origin/main` nach PR #77.
  Die Produktversion wird im selben Änderungsstand von `0.3.9` auf `0.3.10` erhöht.
- Die optionale Sitzungsdiagnose ist standardmäßig deaktiviert, hält nach bewusster Aktivierung höchstens 200 anonymisierte Restic-Metriken nur im Arbeitsspeicher und exportiert sie auf Nutzerwahl als UTF-8-Textbericht. Pfade, Argumente, Programme, Umgebungsvariablen, Geheimnisse, Hostnamen, Benutzerkennungen, Standardausgaben und Rohfehlermeldungen bleiben ausgeschlossen.
- Der Restic-Bundle-Stand aus PR #74 sowie die Backend-, Restore- und Dateiversionsfunktionen
  aus `main` bleiben erhalten.
- Windows enthält Restic 0.19.1 als eingebettete, beim ersten Einsatz hash-geprüft bereitgestellte Ressource. Linux liefert `tools/restic` im Archiv aus.
- Das Build-Manifest pinnt Archive, Hashes, den Signatur-Fingerprint und den öffentlichen Restic-Schlüssel. Die Paketvorbereitung verlangt GnuPG und prüft die offizielle Prüfsummen-Signatur ohne Keyserver-Abhängigkeit.
- Der lokale Restore-Dialog bietet eine unveränderliche `--dry-run --json --verbose=2`-Vorschau mit Ergebnissummen und sichtbarem Limit.
- S3/MinIO- und REST-Profile speichern nur Endpunktdaten; Zugangsdaten bleiben sitzungsgebunden.
- Dateiversionen lassen sich je Host oder über alle Hosts finden, vorschauen, wiederherstellen und als Text nebeneinander vergleichen.
- Upstream begrenzt Suchtreffer auf 10.000 und Ordneraggregation auf 100.000 Pfade.
- Tags und Releases bleiben ausschließlich Aufgabe von GitHub Actions.
- Interne Checkpoint-Referenz mit Endung `1787913869623/d01f5d0c-f149-495f-8b2f-82b72be358a5`
  verweist auf ein vorhandenes Tree-Objekt. Ursache war die Windows-Pfadlänge.
  Mit repository-lokalem `core.longpaths=true` funktionieren Referenzauflösung
  und normaler Fetch wieder. Keine interne Referenz wurde verändert.
- ViewModel in Themenbereiche aufgeteilt; Navigation, Filter und Restore-Aufträge
  besitzen eigene Komponenten.
- Compiled Bindings und schrittweise Snapshot-/Suchergebnisse implementiert.
- CI-Duplikate sind weitgehend in lokale Composite Actions ausgelagert. Die Linux-Abhängigkeiten
  werden zusätzlich in einem wiederverwendbaren, begrenzten Installationsskript gebündelt.
- Die Performance- und Bundle-Änderungen sind implementiert. Der Release-Build, der Windows-
  Portable-Publish, der Linux-Paketaufbau und der lokale vollständige Test-Runner sind erfolgt.
- Die ausführbare Restic-Version wird beim Verbinden tatsächlich per `restic version --json`
  validiert. Windows provisioniert die eingebettete Datei erst bei Bedarf; Linux-Pakete enthalten
  `tools/restic`.
- Beim Verbinden wird kein automatischer `stats`-Aufruf mehr gestartet; die Snapshot-Anzahl
  stammt direkt aus der geladenen Liste. Suche und neueste Dateiversion beenden Restic nach
  dem Trefferlimit beziehungsweise dem ersten gültigen Treffer.
- Die Test- und CI-Restic-Datei wird über `RESTIC_BROWSER_TEST_RESTIC` explizit übergeben und
  vor dem Testlauf mit `restic version --json` geprüft. Ohne diesen Wert bleibt die normale
  Locator-Suche als lokaler Fallback erhalten.
- Vorhandene Drafts oder Releases dürfen nur fortgesetzt werden, wenn jedes bereits vorhandene
  Asset exakt mit dem neuen Build übereinstimmt. Fehlende Assets können ergänzt werden;
  abweichende oder unerwartete Assets bleiben ein harter Fehler ohne Überschreiben.
- Lokal erfolgreich: Release-Build, Formatprüfung, Versionsprüfung, Git-Diff-Prüfung, Bash-Syntax
  und 54 Tests; 3 Linux-spezifische Tests wurden unter Windows übersprungen. Die E2E-Prüfung lief
  mit der signatur- und hashgeprüften Restic-0.19.1-Datei erfolgreich.
- Die NuGet-Schwachstellenprüfung meldet keine bekannten anfälligen Pakete; die lokale Umgebung
  gibt dabei weiterhin `NU1900` für den Advisory-Endpunkt aus. Native Linux-, OpenSSH-, GUI- und
  GitHub-CI-Prüfungen müssen nach dem Pull Request erfolgen.
- Paketierung benötigt lokal GnuPG für die vorgeschriebene Signaturprüfung; dies ist in GitHub Actions installiert.
- Installer auf einem System mit Inno Setup prüfen; lokal nicht installiert.
- PR #75 ist bereits in `main` gemergt; ein neuer Pull Request für die Zuverlässigkeitsänderungen
  muss nach dem Push erneut die vollständige CI durchlaufen.
