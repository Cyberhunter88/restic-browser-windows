using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
        InitializeComponent();
        _viewModel = viewModel;
        _nodes = nodes;
        SummaryText.Text = $"{nodes.Count} Element(e) aus Snapshot {viewModel.SelectedSnapshot?.DisplayId}";
        TargetBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Restic-Wiederherstellung");
        Loaded += (_, _) => WindowTheme.Apply(this, App.IsDark);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Zielordner auswählen" };
        if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FolderName;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            MessageBox.Show(this, "Bitte einen Zielordner auswählen.", "Ziel fehlt");
            return;
        }
        var policy = Enum.Parse<OverwritePolicy>(((ComboBoxItem)OverwriteBox.SelectedItem).Tag!.ToString()!);
        var warning = policy == OverwritePolicy.Never
            ? "Vorhandene Dateien werden übersprungen."
            : "Je nach Auswahl können vorhandene Dateien ersetzt werden.";
        if (MessageBox.Show(this, $"{_nodes.Count} Element(e) nach\n{TargetBox.Text}\nwiederherstellen?\n\n{warning}",
                "Wiederherstellung bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _cancellation = new CancellationTokenSource();
        RestoreButton.IsEnabled = false;
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
            RestoreProgressBar.Value = 100;
            ProgressText.Text = "Abgeschlossen";
            ResultBox.Text = $"{result.Message}\nWiederhergestellt: {result.FilesRestored:N0}\nÜbersprungen: {result.FilesSkipped:N0}";
            ResultBox.Visibility = Visibility.Visible;
            OpenButton.Visibility = Visibility.Visible;
            CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Abgebrochen";
            ResultBox.Text = "Die Wiederherstellung wurde abgebrochen. Bereits geschriebene Dateien können im Zielordner vorhanden sein.";
            ResultBox.Visibility = Visibility.Visible;
        }
        catch (ResticException ex)
        {
            ProgressText.Text = "Fehlgeschlagen";
            ResultBox.Text = ex.Message;
            ResultBox.Visibility = Visibility.Visible;
        }
        finally
        {
            RestoreButton.IsEnabled = true;
            OverwriteBox.IsEnabled = true;
            TargetBox.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is { IsCancellationRequested: false }) _cancellation.Cancel();
        else Close();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TargetBox.Text))
            Process.Start(new ProcessStartInfo("explorer.exe", TargetBox.Text) { UseShellExecute = true });
    }
}
