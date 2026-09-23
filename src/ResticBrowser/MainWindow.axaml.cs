using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Reflection;
using System.Text;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;
using ResticBrowser.Views;

namespace ResticBrowser;

public partial class MainWindow : Window
{
    private readonly IResticRepositoryService _repository;
    private readonly MainViewModel _viewModel;
    private readonly SessionDiagnosticCollector _sessionDiagnostics = new();
    private ResticMountHandle? _activeMountHandle;

    public MainWindow()
    {
        InitializeComponent();
        _repository = new ResticRepositoryService(new ResticProcessRunner(_sessionDiagnostics));
        _viewModel = new MainViewModel(_repository, new SettingsService());
        DataContext = _viewModel;
        MountButton.IsVisible = OperatingSystem.IsLinux();
        Opened += async (_, _) => await RunSafeAsync(_viewModel.InitializeAsync);
        Closed += async (_, _) =>
        {
            if (_activeMountHandle != null)
            {
                await _activeMountHandle.StopAsync();
                _activeMountHandle = null;
            }
            _viewModel.Dispose();
        };
    }

    private async void Mount_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsLinux())
        {
            await DialogService.ShowMessageAsync(this, "Mount nicht verfügbar", "Restic unterstützt das Einbinden als Laufwerk in dieser Anwendung nur unter Linux.");
            return;
        }
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository.");
            return;
        }
        var mountWindow = new MountWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.SelectedSnapshot, _activeMountHandle);
        var resultHandle = await mountWindow.ShowDialog<ResticMountHandle?>(this);
        _activeMountHandle = resultHandle ?? _activeMountHandle;
    }

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository.");
            return;
        }
        await new RepositoryCheckWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials).ShowDialog(this);
    }

    private async void StorageAnalysis_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null || _viewModel.SelectedSnapshot is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte wähle zuerst einen Snapshot in der linken Liste aus.");
            return;
        }
        await new StorageAnalysisWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.SelectedSnapshot).ShowDialog(this);
    }
    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionWindow(_viewModel.Profiles);
        if (await dialog.ShowDialog<bool>(this) != true || dialog.Profile is null || dialog.Credentials is null) return;
        await RunSafeAsync(() => _viewModel.ConnectAsync(dialog.Profile, dialog.Credentials));
    }
    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.RefreshSnapshotsAsync);
    private void Disconnect_Click(object? sender, RoutedEventArgs e) => _viewModel.Disconnect();
    private void Theme_Click(object? sender, RoutedEventArgs e) => App.SetTheme(!App.IsDark);
    private async void SessionDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        if (SessionDiagnosticsButton.IsChecked == true)
        {
            _sessionDiagnostics.Start();
            SessionDiagnosticsButton.Content = "Sitzungsdiagnose beenden";
            ExportDiagnosticsButton.IsEnabled = true;
            await DialogService.ShowMessageAsync(this, "Sitzungsdiagnose aktiviert",
                "Es werden bis zum Beenden dieser App nur anonyme Laufzeitwerte im Arbeitsspeicher erfasst. Pfade, Argumente, Zugangsdaten und Rohfehlermeldungen werden nicht gespeichert.");
            return;
        }

        _sessionDiagnostics.Stop();
        SessionDiagnosticsButton.Content = "Sitzungsdiagnose aktivieren";
    }

    private async void ExportDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        var session = _sessionDiagnostics.CreateSnapshot();
        if (session is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Diagnosebericht speichern",
            DefaultExtension = "txt",
            SuggestedFileName = $"ResticBrowser-Diagnose_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("Textdatei") { Patterns = ["*.txt"] }]
        });
        if (file is null) return;

        try
        {
            var path = file.TryGetLocalPath() ?? file.Path.LocalPath;
            var appVersion = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unbekannt";
            var context = DiagnosticReportContext.Create(appVersion, _viewModel.ValidatedResticVersion?.Version, _viewModel.ValidatedResticSource);
            await File.WriteAllTextAsync(path, SessionDiagnosticReportFormatter.Format(session, context, DateTimeOffset.Now), Encoding.UTF8);
            await DialogService.ShowMessageAsync(this, "Erfolg", "Der anonymisierte Diagnosebericht wurde gespeichert.");
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, "Fehler", $"Diagnosebericht konnte nicht gespeichert werden: {ex.Message}");
        }
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
    private async void Up_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoUpAsync);
    private async void Back_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoBackAsync);
    private async void Forward_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoForwardAsync);
    private async void FileList_DoubleTapped(object? sender, TappedEventArgs e) { if (FileList.SelectedItem is BackupNode node) await RunSafeAsync(() => _viewModel.OpenNodeAsync(node)); }
    private async void Search_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(() => _viewModel.SearchAsync(SearchBox.Text ?? ""));
    private async void RestoreNewest_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Suchbegriff fehlt", "Bitte gib einen Dateinamen oder ein Restic-Muster ein.");
            return;
        }
        BackupNode? node = null;
        await RunSafeAsync(async () => node = await _viewModel.FindNewestAsync(SearchBox.Text));
        if (node is not null) await new RestoreWindow(_viewModel, [node]).ShowDialog(this);
    }
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) Search_Click(sender, e); }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var selected = FileList.SelectedItems?.OfType<BackupNode>().ToList() ?? [];
        if (selected.Count == 0 && FileList.SelectedItem is BackupNode singleNode) selected.Add(singleNode);
        if (selected.Count == 0) { await DialogService.ShowMessageAsync(this, "Keine Auswahl", "Bitte mindestens eine Datei oder einen Ordner auswählen."); return; }
        await new RestoreWindow(_viewModel, selected).ShowDialog(this);
    }

    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not BackupNode node)
        {
            await DialogService.ShowMessageAsync(this, "Keine Auswahl", "Bitte eine Datei für die Vorschau auswählen.");
            return;
        }

        if (node.IsDirectory)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Ordner können nicht in der Vorschau angezeigt werden.");
            return;
        }

        await RunSafeAsync(async () =>
        {
            var previewData = await _viewModel.GetFilePreviewAsync(node);
            await new FilePreviewWindow(previewData).ShowDialog(this);
        });
    }

    private async void Versions_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not BackupNode node || node.IsDirectory)
        {
            await DialogService.ShowMessageAsync(this, "Keine Datei ausgewählt", "Bitte wähle eine Datei aus.");
            return;
        }
        await new FileVersionsWindow(_viewModel, node).ShowDialog(this);
    }

    private async void Diff_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null || _viewModel.Snapshots.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository mit Snapshots.");
            return;
        }
        await new SnapshotDiffWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.Snapshots).ShowDialog(this);
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is BackupNode node)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(node.Path);
            await DialogService.ShowMessageAsync(this, "Kopiert", $"Pfad in die Zwischenablage kopiert:\n{node.Path}");
        }
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Restic-Fehler", ex.Message); }
        catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Fehler", $"Unerwarteter Fehler: {ex.Message}"); }
    }
}
