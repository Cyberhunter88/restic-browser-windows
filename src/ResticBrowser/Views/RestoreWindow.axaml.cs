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

    public RestoreWindow() => InitializeComponent();

    public RestoreWindow(MainViewModel viewModel, IReadOnlyList<BackupNode> nodes) : this()
    {
        _viewModel = viewModel;
        _nodes = nodes;
        var snapshot = viewModel.SelectedSnapshot;
        SummaryText.Text = $"{nodes.Count} Element(e) aus Snapshot {snapshot?.DisplayId} ({snapshot?.Time.ToString("dd.MM.yyyy HH:mm")})";
        var files = nodes.Count(node => !node.IsDirectory);
        var directories = nodes.Count(node => node.IsDirectory);
        var size = directories > 0 ? "Größe der Ordner wird während der Wiederherstellung ermittelt" :
            $"Gesamtgröße: {SnapshotInfo.FormatBytes(nodes.Sum(node => node.Size))}";
        SelectionDetailsText.Text = $"{files} Datei(en), {directories} Ordner · {size}\nHost: {snapshot?.Hostname ?? "Unbekannt"} · Snapshot-Pfade: {snapshot?.PathText ?? "—"}";
        TargetBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Restic-Wiederherstellung");
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Zielordner auswählen", AllowMultiple = false });
        if (folders.Count > 0) TargetBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private void PreviewInputChanged(object? sender, TextChangedEventArgs e) => InvalidatePreview();
    private void PreviewInputChanged(object? sender, SelectionChangedEventArgs e) => InvalidatePreview();
    private void InvalidatePreview() { if (PreviewButton is not null) PreviewButton.Content = "Vorschau prüfen"; }

    private async void PreviewRestore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte einen Zielordner für die Vorschau auswählen.");
            return;
        }
        var policy = Enum.Parse<OverwritePolicy>(((ComboBoxItem)OverwriteBox.SelectedItem!).Tag!.ToString()!);
        _cancellation = new CancellationTokenSource();
        PreviewButton.IsEnabled = false;
        RestoreButton.IsEnabled = false;
        ProgressText.Text = "Wiederherstellung wird geprüft …";
        try
        {
            var preview = await _viewModel.PreviewRestoreAsync(_nodes, TargetBox.Text!, policy, _cancellation.Token);
            var shown = preview.Items.Take(20).Select(item => $"{ActionText(item.Action)}: {item.Path}");
            ResultBox.Text = $"Vorschau abgeschlossen. Neu: {preview.Restored:N0}, aktualisiert: {preview.Updated:N0}, unverändert: {preview.Unchanged:N0}." +
                (preview.IsTruncated ? "\nDie sichtbare Vorschau wurde auf 10.000 Einträge begrenzt." : "") +
                (preview.Items.Count == 0 ? "" : "\n\n" + string.Join(Environment.NewLine, shown));
            ResultBox.IsVisible = true;
            ProgressText.Text = "Vorschau abgeschlossen";
            PreviewButton.Content = "Vorschau aktuell";
        }
        catch (OperationCanceledException) { ProgressText.Text = "Vorschau abgebrochen"; }
        catch (ResticException ex) { ProgressText.Text = "Vorschau fehlgeschlagen"; ResultBox.Text = ex.Message; ResultBox.IsVisible = true; }
        finally { _cancellation.Dispose(); _cancellation = null; PreviewButton.IsEnabled = true; RestoreButton.IsEnabled = true; }
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte einen Zielordner auswählen.");
            return;
        }
        var policy = Enum.Parse<OverwritePolicy>(((ComboBoxItem)OverwriteBox.SelectedItem!).Tag!.ToString()!);
        var warning = policy == OverwritePolicy.Never ? "Vorhandene Dateien werden übersprungen." : "Je nach Auswahl können vorhandene Dateien ersetzt werden.";
        var directory = Directory.Exists(TargetBox.Text) ? "Der Zielordner existiert bereits." : "Der Zielordner wird neu erstellt.";
        if (!await DialogService.ConfirmAsync(this, "Wiederherstellung bestätigen", $"{_nodes.Count} Element(e) nach\n{TargetBox.Text}\nwiederherstellen?\n\n{directory}\n{warning}")) return;

        _cancellation = new CancellationTokenSource();
        RestoreButton.IsEnabled = false;
        PreviewButton.IsEnabled = false;
        OverwriteBox.IsEnabled = false;
        TargetBox.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        var progress = new Progress<RestoreProgress>(item =>
        {
            RestoreProgressBar.Value = Math.Clamp(item.PercentDone * 100, 0, 100);
            ProgressText.Text = $"{item.FilesRestored:N0} von {item.TotalFiles:N0} Dateien · {SnapshotInfo.FormatBytes(item.BytesRestored)}";
        });
        try
        {
            var result = await _viewModel.RestoreAsync(_nodes, TargetBox.Text!, policy, progress, _cancellation.Token);
            RestoreProgressBar.Value = 100;
            ProgressText.Text = result.ExitCode == 0 ? "Abgeschlossen" : "Mit Hinweisen abgeschlossen";
            ResultBox.Text = $"{result.Message}\nWiederhergestellt: {result.FilesRestored:N0}\nÜbersprungen: {result.FilesSkipped:N0}";
            ResultBox.IsVisible = true;
            OpenButton.IsVisible = true;
            CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException) { ProgressText.Text = "Abgebrochen"; ResultBox.Text = "Die Wiederherstellung wurde abgebrochen. Bereits geschriebene Dateien können im Zielordner vorhanden sein."; ResultBox.IsVisible = true; CancelButton.Content = "Schließen"; }
        catch (ResticException ex) { ProgressText.Text = "Fehlgeschlagen"; ResultBox.Text = ex.Message; ResultBox.IsVisible = true; CancelButton.Content = "Schließen"; }
        finally
        {
            _cancellation.Dispose(); _cancellation = null;
            RestoreButton.IsEnabled = true; PreviewButton.IsEnabled = true; OverwriteBox.IsEnabled = true;
            TargetBox.IsEnabled = true; BrowseButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_cancellation is { IsCancellationRequested: false }) _cancellation.Cancel(); else Close();
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TargetBox.Text) && Directory.Exists(TargetBox.Text))
            await Launcher.LaunchUriAsync(new Uri(Path.GetFullPath(TargetBox.Text)));
    }

    private static string ActionText(string action) => action switch
    {
        "restored" => "Neu",
        "updated" => "Aktualisiert",
        "unchanged" => "Unverändert",
        _ => action
    };
}
