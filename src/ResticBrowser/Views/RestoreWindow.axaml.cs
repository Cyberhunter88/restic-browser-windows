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
    private readonly MainViewModel _viewModel;
    private readonly IReadOnlyList<BackupNode> _nodes;
    private CancellationTokenSource? _cancellation;
    private RestoreResult? _lastResult;

    public RestoreWindow()
    {
        InitializeComponent();
        _viewModel = null!;
        _nodes = [];
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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Zielordner auswählen", AllowMultiple = false });
        if (folders.Count > 0) TargetBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte einen Zielordner auswählen.");
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
        OverwriteBox.IsEnabled = false;
        TargetBox.IsEnabled = false;
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
            OverwriteBox.IsEnabled = true;
            TargetBox.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_cancellation is { IsCancellationRequested: false }) _cancellation.Cancel();
        else Close();
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TargetBox.Text))
            await Launcher.LaunchUriAsync(new Uri(Path.GetFullPath(TargetBox.Text)));
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
                    sb.AppendLine("Zeitpunkt;SnapshotID;Host;Zielordner;Überschreibmodus;Wiederhergestellt;Übersprungen;Status");
                    sb.AppendLine($"\"{DateTime.Now:dd.MM.yyyy HH:mm:ss}\";\"{_viewModel.SelectedSnapshot?.DisplayId}\";\"{_viewModel.SelectedSnapshot?.Hostname}\";\"{TargetBox.Text}\";\"{OverwriteBox.Text}\";\"{_lastResult?.FilesRestored ?? 0}\";\"{_lastResult?.FilesSkipped ?? 0}\";\"{ProgressText.Text}\"");
                    sb.AppendLine();
                    sb.AppendLine("Dateipfad;Typ;Größe");
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
                    sb.AppendLine($"Zielordner:           {TargetBox.Text}");
                    sb.AppendLine($"Überschreibmodus:     {OverwriteBox.Text}");
                    sb.AppendLine($"Status:               {ProgressText.Text}");
                    sb.AppendLine($"Dateien wiederhergestellt: {_lastResult?.FilesRestored ?? 0:N0}");
                    sb.AppendLine($"Dateien übersprungen:      {_lastResult?.FilesSkipped ?? 0:N0}");
                    sb.AppendLine("--------------------------------------------------");
                    sb.AppendLine("Wiederhergestellte Elemente:");
                    foreach (var n in _nodes)
                        sb.AppendLine($"  - [{n.TypeText}] {n.Path} ({n.SizeText})");
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
