using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

namespace ResticBrowser.Views;

public partial class RestoreWindow : Window
{
    private readonly MainViewModel _viewModel = null!;
    private readonly IReadOnlyList<BackupNode> _nodes = [];
    private CancellationTokenSource? _cancellation;
    private RestoreResult? _lastResult;
    private readonly List<TarExportResult> _lastTarExports = [];

    public RestoreWindow()
    {
        InitializeComponent();
    }

    public RestoreWindow(MainViewModel viewModel, IReadOnlyList<BackupNode> nodes) : this()
    {
        _viewModel = viewModel;
        _nodes = nodes;

        var snap = viewModel.SelectedSnapshot;
        SummaryText.Text = $"{nodes.Count} Element(e) aus Snapshot {snap?.DisplayId} ({snap?.Time.ToString("dd.MM.yyyy HH:mm")})";

        long totalBytes = nodes.Sum(n => n.Size);
        int fileCount = nodes.Count(n => !n.IsDirectory);
        int dirCount = nodes.Count(n => n.IsDirectory);
        var sizeText = dirCount > 0
            ? "Größe der Ordner wird während der Wiederherstellung ermittelt"
            : $"Gesamtgröße: {SnapshotInfo.FormatBytes(totalBytes)}";

        SelectionDetailsText.Text = $"{fileCount} Datei(en), {dirCount} Ordner · {sizeText}\n" +
                                    $"Host: {snap?.Hostname ?? "Unbekannt"} · Snapshot-Pfade: {snap?.PathText ?? "—"}";

        TargetBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Restic-Wiederherstellung");
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (IsTarMode && _nodes.Count == 1)
        {
            var snapshotId = _viewModel?.SelectedSnapshot?.DisplayId ?? "Snapshot";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "TAR-Archiv speichern",
                DefaultExtension = "tar",
                SuggestedFileName = TarExportPathHelper.BuildFileName(_nodes[0].Name, snapshotId),
                FileTypeChoices = [new FilePickerFileType("TAR-Archiv") { Patterns = ["*.tar"] }]
            });
            if (file is not null) TargetBox.Text = file.TryGetLocalPath() ?? file.Path.LocalPath;
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Zielordner auswählen", AllowMultiple = false });
        if (folders.Count > 0) TargetBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private void RestoreMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetBox is null || OverwriteBox is null || RestoreButton is null) return;
        OverwriteBox.IsVisible = !IsTarMode;
        TargetBox.PlaceholderText = IsTarMode
            ? (_nodes.Count == 1 ? "TAR-Datei" : "Zielordner für TAR-Archive")
            : "Zielordner";
        RestoreButton.Content = IsTarMode ? "Exportieren" : "Wiederherstellen";

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (IsTarMode)
        {
            var snapshotId = _viewModel?.SelectedSnapshot?.DisplayId ?? "Snapshot";
            TargetBox.Text = _nodes.Count == 1
                ? Path.Combine(userProfile, TarExportPathHelper.BuildFileName(_nodes[0].Name, snapshotId))
                : Path.Combine(userProfile, "Restic-TAR-Export");
        }
        else
        {
            TargetBox.Text = Path.Combine(userProfile, "Restic-Wiederherstellung");
        }

        ResultBox.IsVisible = false;
        OpenButton.IsVisible = false;
        ExportReportButton.IsVisible = false;
        RestoreProgressBar.Value = 0;
        ProgressText.Text = "Bereit";
    }

    private bool IsTarMode => (RestoreModeBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Tar";

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte einen Zielordner auswählen.");
            return;
        }

        if (IsTarMode)
        {
            await ExportTarAsync();
            return;
        }
        var policy = Enum.Parse<OverwritePolicy>(((ComboBoxItem)OverwriteBox.SelectedItem!).Tag!.ToString()!);
        var warning = policy == OverwritePolicy.Never
            ? "Vorhandene Dateien werden übersprungen."
            : "Je nach Auswahl können vorhandene Dateien ersetzt werden.";

        var targetExists = Directory.Exists(TargetBox.Text);
        var dirInfo = targetExists ? "Der Zielordner existiert bereits." : "Der Zielordner wird neu erstellt.";

        if (!await DialogService.ConfirmAsync(this, "Wiederherstellung bestätigen", $"{_nodes.Count} Element(e) nach\n{TargetBox.Text}\nwiederherstellen?\n\n{dirInfo}\n{warning}"))
            return;

        _cancellation = new CancellationTokenSource();
        RestoreButton.IsEnabled = false;
        CancelButton.Content = "Abbrechen";
        RestoreModeBox.IsEnabled = false;
        OverwriteBox.IsEnabled = false;
        TargetBox.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        var progress = new Progress<RestoreProgress>(p =>
        {
            RestoreProgressBar.Value = Math.Clamp(p.PercentDone * 100, 0, 100);
            ProgressText.Text = $"{p.FilesRestored:N0} von {p.TotalFiles:N0} Dateien · {SnapshotInfo.FormatBytes(p.BytesRestored)}";
        });
        try
        {
            var result = await _viewModel.RestoreAsync(_nodes, TargetBox.Text, policy, progress, _cancellation.Token);
            _lastResult = result;
            RestoreProgressBar.Value = 100;
            ProgressText.Text = result.ExitCode == 0 ? "Abgeschlossen" : "Mit Hinweisen abgeschlossen";
            ResultBox.Text = $"{result.Message}\nWiederhergestellt: {result.FilesRestored:N0}\nÜbersprungen: {result.FilesSkipped:N0}";
            ResultBox.IsVisible = true;
            OpenButton.IsVisible = true;
            ExportReportButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Abgebrochen";
            ResultBox.Text = "Die Wiederherstellung wurde abgebrochen. Bereits geschriebene Dateien können im Zielordner vorhanden sein.";
            ResultBox.IsVisible = true;
            ExportReportButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        catch (ResticException ex)
        {
            ProgressText.Text = "Fehlgeschlagen";
            ResultBox.Text = ex.Message;
            ResultBox.IsVisible = true;
            ExportReportButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            RestoreButton.IsEnabled = true;
            RestoreModeBox.IsEnabled = true;
            OverwriteBox.IsEnabled = true;
            TargetBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;
        }
    }

    private async Task ExportTarAsync()
    {
        var outputPaths = await BuildTarOutputPathsAsync();
        if (outputPaths is null) return;

        var description = _nodes.Count == 1
            ? $"Das ausgewählte Element als TAR-Datei exportieren?\n\n{outputPaths[0]}"
            : $"{_nodes.Count} Elemente als einzelne TAR-Dateien exportieren?\n\nZielordner:\n{TargetBox.Text}";
        if (!await DialogService.ConfirmAsync(this, "TAR-Export bestätigen", description)) return;

        _cancellation = new CancellationTokenSource();
        _lastResult = null;
        _lastTarExports.Clear();
        RestoreButton.IsEnabled = false;
        CancelButton.Content = "Abbrechen";
        RestoreModeBox.IsEnabled = false;
        TargetBox.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        RestoreProgressBar.IsIndeterminate = true;
        ResultBox.IsVisible = false;
        OpenButton.IsVisible = false;
        ExportReportButton.IsVisible = false;

        try
        {
            for (var index = 0; index < _nodes.Count; index++)
            {
                var node = _nodes[index];
                ProgressText.Text = $"Archiv {index + 1} von {_nodes.Count}: {node.Name}";
                try
                {
                    var result = await _viewModel.ExportTarAsync(node, outputPaths[index], _cancellation.Token);
                    _lastTarExports.Add(result);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _lastTarExports.Add(new TarExportResult(node.Path, outputPaths[index], false, ex.Message));
                }
            }

            var succeeded = _lastTarExports.Count(result => result.Success);
            var failed = _lastTarExports.Count - succeeded;
            ProgressText.Text = failed == 0 ? "Export abgeschlossen" : "Export mit Fehlern abgeschlossen";
            ResultBox.Text = BuildTarResultText(succeeded, failed);
            ResultBox.IsVisible = true;
            OpenButton.IsVisible = succeeded > 0;
            ExportReportButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Abgebrochen";
            ResultBox.Text = "Der TAR-Export wurde abgebrochen. Unvollständige Archive wurden entfernt.";
            ResultBox.IsVisible = true;
            ExportReportButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        finally
        {
            RestoreProgressBar.IsIndeterminate = false;
            RestoreProgressBar.Value = 100;
            _cancellation.Dispose();
            _cancellation = null;
            RestoreButton.IsEnabled = true;
            RestoreModeBox.IsEnabled = true;
            TargetBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;
        }
    }

    private async Task<IReadOnlyList<string>?> BuildTarOutputPathsAsync()
    {
        if (_nodes.Count == 1)
        {
            var target = Path.GetFullPath(TargetBox.Text ?? "");
            if (!target.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) target += ".tar";
            if (File.Exists(target))
            {
                await DialogService.ShowMessageAsync(this, "Datei vorhanden", "Die gewählte TAR-Datei existiert bereits. Bitte wähle einen anderen Namen.");
                return null;
            }
            if (!Directory.Exists(Path.GetDirectoryName(target)))
            {
                await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Der Zielordner für die TAR-Datei existiert nicht.");
                return null;
            }
            TargetBox.Text = target;
            return [target];
        }

        var directory = Path.GetFullPath(TargetBox.Text ?? "");
        if (!Directory.Exists(directory))
        {
            await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte wähle einen vorhandenen Zielordner aus.");
            return null;
        }

        var snapshotId = _viewModel.SelectedSnapshot?.DisplayId ?? "Snapshot";
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var reserved = new HashSet<string>(comparer);
        var paths = new List<string>(_nodes.Count);
        foreach (var node in _nodes)
        {
            var path = TarExportPathHelper.GetUniquePath(directory,
                TarExportPathHelper.BuildFileName(node.Name, snapshotId), reserved);
            reserved.Add(path);
            paths.Add(path);
        }
        return paths;
    }

    private string BuildTarResultText(int succeeded, int failed)
    {
        var builder = new StringBuilder($"TAR-Export abgeschlossen. Erfolgreich: {succeeded:N0}, fehlgeschlagen: {failed:N0}.");
        foreach (var result in _lastTarExports)
        {
            builder.AppendLine().Append(result.Success ? "Erstellt: " : "Fehler: ").Append(result.TargetFile);
            if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                builder.AppendLine().Append("  ").Append(result.ErrorMessage);
        }
        return builder.ToString();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_cancellation is { IsCancellationRequested: false }) _cancellation.Cancel();
        else Close();
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        var target = IsTarMode && _nodes.Count == 1 ? Path.GetDirectoryName(TargetBox.Text) : TargetBox.Text;
        if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
            await Launcher.LaunchUriAsync(new Uri(Path.GetFullPath(target)));
    }

    private async void ExportReport_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Wiederherstellungsbericht speichern",
            DefaultExtension = "txt",
            SuggestedFileName = $"RestoreReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        });

        if (file is not null)
        {
            try
            {
                var filePath = file.TryGetLocalPath() ?? file.Path.LocalPath;
                var sb = new StringBuilder();
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".csv")
                {
                    sb.AppendLine("Zeitpunkt;SnapshotID;Host;Modus;Ziel;Wiederhergestellt;Übersprungen;Status");
                    sb.AppendLine($"\"{DateTime.Now:dd.MM.yyyy HH:mm:ss}\";\"{_viewModel.SelectedSnapshot?.DisplayId}\";\"{_viewModel.SelectedSnapshot?.Hostname}\";\"{(IsTarMode ? "TAR-Export" : "Normale Wiederherstellung")}\";\"{TargetBox.Text}\";\"{_lastResult?.FilesRestored ?? 0}\";\"{_lastResult?.FilesSkipped ?? 0}\";\"{ProgressText.Text}\"");
                    sb.AppendLine();
                    sb.AppendLine(IsTarMode ? "Snapshot-Pfad;Archiv;Erfolg;Fehler" : "Dateipfad;Typ;Größe");
                    if (IsTarMode)
                        foreach (var result in _lastTarExports)
                            sb.AppendLine($"\"{result.SnapshotPath}\";\"{result.TargetFile}\";\"{result.Success}\";\"{result.ErrorMessage}\"");
                    else
                        foreach (var n in _nodes)
                            sb.AppendLine($"\"{n.Path}\";\"{n.TypeText}\";\"{n.SizeText}\"");
                }
                else
                {
                    sb.AppendLine("==================================================");
                    sb.AppendLine("        RESTIC BROWSER WIEDERHERSTELLUNGSBERICHT  ");
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"Zeitpunkt:            {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    sb.AppendLine($"Repository:           {_viewModel.ActiveProfile?.Name}");
                    sb.AppendLine($"Snapshot ID:          {_viewModel.SelectedSnapshot?.Id}");
                    sb.AppendLine($"Host:                 {_viewModel.SelectedSnapshot?.Hostname}");
                    sb.AppendLine($"Modus:                {(IsTarMode ? "TAR-Export" : "Normale Wiederherstellung")}");
                    sb.AppendLine($"Ziel:                 {TargetBox.Text}");
                    if (!IsTarMode) sb.AppendLine($"Überschreibmodus:     {OverwriteBox.Text}");
                    sb.AppendLine($"Status:               {ProgressText.Text}");
                    if (IsTarMode)
                    {
                        sb.AppendLine($"Archive erfolgreich:  {_lastTarExports.Count(result => result.Success):N0}");
                        sb.AppendLine($"Archive fehlgeschlagen: {_lastTarExports.Count(result => !result.Success):N0}");
                    }
                    else
                    {
                        sb.AppendLine($"Dateien wiederhergestellt: {_lastResult?.FilesRestored ?? 0:N0}");
                        sb.AppendLine($"Dateien übersprungen:      {_lastResult?.FilesSkipped ?? 0:N0}");
                    }
                    sb.AppendLine("--------------------------------------------------");
                    if (IsTarMode)
                    {
                        sb.AppendLine("TAR-Archive:");
                        foreach (var result in _lastTarExports)
                            sb.AppendLine($"  - [{(result.Success ? "Erstellt" : "Fehler")}] {result.SnapshotPath} -> {result.TargetFile}{(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" ({result.ErrorMessage})")}");
                    }
                    else
                    {
                        sb.AppendLine("Wiederhergestellte Elemente:");
                        foreach (var n in _nodes)
                            sb.AppendLine($"  - [{n.TypeText}] {n.Path} ({n.SizeText})");
                    }
                    sb.AppendLine("==================================================");
                }

                await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
                await DialogService.ShowMessageAsync(this, "Erfolg", "Wiederherstellungsbericht wurde gespeichert.");
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Fehler", $"Bericht konnte nicht gespeichert werden: {ex.Message}");
            }
        }
    }
}
