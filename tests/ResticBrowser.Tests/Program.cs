using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Remote;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

using static TestSuite;

if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE")))
{
    var pipeName = Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE")!;
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(10000);
    using var reader = new StreamReader(pipe);
    Console.Write(await reader.ReadToEndAsync());
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Streaming: Treffer vor Ende einer Snapshot-Gruppe", FindMatchesBeforeGroupEnds),
    ("Streaming: UTF-8 und unbekannte Felder an Chunk-Grenzen", FindMatchesChunkBoundaries),
    ("Streaming: Snapshots vor Abschluss und final sortiert", SnapshotBatchesBeforeCompletion),
    ("Streaming: Snapshot-Fehler und getrennte Verbindung", SnapshotBatchesFailureAndDisconnect),
    ("Streaming: verspätete Suche und Abbruch", SearchBatchesRaceAndCancel),
    ("Streaming: Suchlimit und begrenzte Batches", SearchBatchesBounded),
    ("Snapshot-Filter wählt neueste Gruppe bei unsortierter Eingabe", () => Sync(SnapshotFilterOrdering)),
    ("Batch-Collection erhält Auswahl ohne Reset", () => Sync(BatchCollectionAppend)),
    ("Snapshot JSON toleriert Zusatzfelder", () => Sync(SnapshotJson)),
    ("JSONL ignoriert unbekannte Zeilen", () => Sync(JsonLines)),
    ("Restore-Argumente sind getrennt und vollständig", () => Sync(RestoreArguments)),
    ("TAR-Export-Argumente sind getrennt und vollständig", () => Sync(TarExportArguments)),
    ("TAR-Dateinamen sind sicher und eindeutig", () => Sync(TarExportNames)),
    ("Unvollständige TAR-Dateien werden entfernt", TarExportCleanup),
    ("Snapshot-Pfade werden normalisiert", () => Sync(Paths)),
    ("Überschreibmodi werden korrekt abgebildet", () => Sync(OverwriteModes)),
    ("Restore-Vorschau verwendet sichere getrennte Argumente", () => Sync(PreviewRestoreArguments)),
    ("Backend-Variablen werden sicher geprüft", () => Sync(BackendEnvironmentValidation)),
    ("S3- und REST-Repository-Adressen werden korrekt erzeugt", () => Sync(CloudRepositoryStrings)),
    ("Zugangsdaten werden beim Dispose geleert", () => Sync(Credentials)),
    ("Remote-Protokoll toleriert Zusatzfelder", () => Sync(RemoteProtocolJson)),
    ("Remote-Zugangsdaten werden beim Dispose geleert", () => Sync(RemoteCredentials)),
    ("SSH-Hostvertrauen bleibt ohne Geheimnisse gespeichert", TrustedHostSettings),
    ("Linux-Helfer ist eingebettet", () => Sync(EmbeddedRemoteHelper)),
    ("VPS-Vorbereitung nutzt zwei SSH-Sitzungen ohne Upload", RemoteTransportRoundTrips),
    ("Remote-Zielpfade bleiben im Basisordner", () => Sync(RemotePaths)),
    ("SFTP Repository-String wird ordnungsgemäß gebaut", () => Sync(SftpRepoString)),
    ("Diff, Stats und Dump Befehle sind korrekt", () => Sync(CommandBuilders)),
    ("Restic-Suche unterscheidet Windows und Linux", () => Sync(LocatorCandidates)),
    ("Linux-Einstellungen respektieren XDG_DATA_HOME", () => Sync(XdgSettings)),
    ("Zugriffsfehler bleiben plattformneutral", PermissionError),
    ("Symlink-Fehler ergeben einen Teilerfolg", SymbolicLinkPermissionError),
    ("Zusätzliche Restore-Fehler bleiben Fehler", MixedRestoreErrors),
    ("Binärvorschau erhält Originalbytes", BinaryPreview),
    ("Binäre Prozessausgabe wird begrenzt", BinaryOutputLimit),
    ("JSONL-Verzeichnis wird zeilenweise verarbeitet", StreamingDirectory),
    ("Suche begrenzt sichtbare Treffer", SearchResultLimit),
    ("Neueste Datei benötigt genau einen Restic-Prozess", NewestSearchSingleProcess),
    ("Verbindung meldet Zustand vor automatischer Snapshot-Auswahl", ConnectStateBeforeSnapshotLoad),
    ("Veraltete Navigation überschreibt keine neuen Daten", OperationRace),
    ("Getrennte Verbindung übernimmt keine späten Statistiken", StaleRepositoryStats),
    ("Batch-Collection meldet genau einen Reset", () => Sync(BatchCollectionReset)),
    ("Restore-Fehlerausgabe bleibt begrenzt", RestoreErrorLimit),
    ("Speicheranalyse meldet Fortschritt", StorageAnalysisProgressReporting),
    ("Speicheranalyse begrenzt Ordneraggregation", StorageAnalysisFolderLimit),
    ("Performance: große Snapshot- und Analysedaten", LargeDatasetPerformance),
    ("Verzeichnis-Cache bleibt begrenzt", DirectoryCacheBounded),
    ("Verzeichnis-Cache begrenzt die Gesamtknotenzahl", DirectoryCacheNodeBounded),
    ("E2E: Restic Repository, Suche, Stats, Diff und Restore", ResticIntegration),
    ("E2E Linux: Remote-Helfer stellt ausgewählte Datei wieder her", RemoteHelperIntegration),
    ("E2E Linux: OpenSSH stellt über den VPS-Dienst wieder her", RemoteSshIntegration)
};

var failures = 0;
var skipped = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (SkippedTestException ex) { skipped++; Console.WriteLine($"SKIP  {test.Name}: {ex.Message}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failures - skipped} bestanden, {skipped} übersprungen, {failures} fehlgeschlagen ({tests.Length} registriert).");
return failures == 0 ? 0 : 1;
