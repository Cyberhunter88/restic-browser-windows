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
    public RestoreWindow(MainViewModel viewModel, IReadOnlyList<BackupNode> nodes)
    {
        InitializeComponent(); _viewModel = viewModel; _nodes = nodes;
        SummaryText.Text = $"{nodes.Count} Element(e) aus Snapshot {viewModel.SelectedSnapshot?.DisplayId}";
        TargetBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Restic-Wiederherstellung");
    }
    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Zielordner auswählen", AllowMultiple = false });
        if (folders.Count > 0) TargetBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }
    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text)) { await DialogService.ShowMessageAsync(this, "Ziel fehlt", "Bitte einen Zielordner auswählen."); return; }
        var policy = Enum.Parse<OverwritePolicy>(((ComboBoxItem)OverwriteBox.SelectedItem!).Tag!.ToString()!);
        var warning = policy == OverwritePolicy.Never ? "Vorhandene Dateien werden übersprungen." : "Je nach Auswahl können vorhandene Dateien ersetzt werden.";
        if (!await DialogService.ConfirmAsync(this, "Wiederherstellung bestätigen", $"{_nodes.Count} Element(e) nach\n{TargetBox.Text}\nwiederherstellen?\n\n{warning}")) return;
        _cancellation = new CancellationTokenSource(); RestoreButton.IsEnabled = false; OverwriteBox.IsEnabled = false; TargetBox.IsEnabled = false;
        var progress = new Progress<RestoreProgress>(p => { RestoreProgressBar.Value = Math.Clamp(p.PercentDone * 100, 0, 100); ProgressText.Text = $"{p.FilesRestored:N0} von {p.TotalFiles:N0} Dateien · {SnapshotInfo.FormatBytes(p.BytesRestored)}"; });
        try
        {
            var result = await _viewModel.RestoreAsync(_nodes, TargetBox.Text, policy, progress, _cancellation.Token);
            RestoreProgressBar.Value = 100; ProgressText.Text = "Abgeschlossen"; ResultBox.Text = $"{result.Message}\nWiederhergestellt: {result.FilesRestored:N0}\nÜbersprungen: {result.FilesSkipped:N0}"; ResultBox.IsVisible = true; OpenButton.IsVisible = true; CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException) { ProgressText.Text = "Abgebrochen"; ResultBox.Text = "Die Wiederherstellung wurde abgebrochen. Bereits geschriebene Dateien können im Zielordner vorhanden sein."; ResultBox.IsVisible = true; }
        catch (ResticException ex) { ProgressText.Text = "Fehlgeschlagen"; ResultBox.Text = ex.Message; ResultBox.IsVisible = true; }
        finally { RestoreButton.IsEnabled = true; OverwriteBox.IsEnabled = true; TargetBox.IsEnabled = true; }
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) { if (_cancellation is { IsCancellationRequested: false }) _cancellation.Cancel(); else Close(); }
    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TargetBox.Text)) await Launcher.LaunchUriAsync(new Uri(Path.GetFullPath(TargetBox.Text)));
    }
}
