using System.Text;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class RestoreWindow
{
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
        _lastRemoteTarget = null;
        _lastTarExports.Clear();
        RestoreButton.IsEnabled = false;
        CancelButton.Content = "Abbrechen";
        RestoreModeBox.IsEnabled = false;
        TargetBox.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        RestoreProgressBar.IsIndeterminate = false;
        RestoreProgressBar.Value = 0;
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
                RestoreProgressBar.Value = (index + 1) * 100.0 / _nodes.Count;
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
}
