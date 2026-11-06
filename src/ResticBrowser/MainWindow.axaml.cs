using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;
using ResticBrowser.Views;

namespace ResticBrowser;

public partial class MainWindow : Window
{
    private readonly IResticRepositoryService _repository;
    private readonly MainViewModel _viewModel;
    private ResticMountHandle? _activeMountHandle;
    private bool? _compactLayout;
    private bool _unmountingOnClose;
    private bool _showPermissions;
    private WindowSettings? _lastWindow;

    public MainWindow()
    {
        InitializeComponent();
        _repository = new ResticRepositoryService(new ResticProcessRunner());
        _viewModel = new MainViewModel(_repository, new SettingsService());
        DataContext = _viewModel;
        MountButton.IsVisible = OperatingSystem.IsLinux();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        UpdateResponsiveLayout();
        ConfigureColumnSorting();
        _viewModel.Profiles.CollectionChanged += (_, _) => RecentPanel.IsVisible = _viewModel.Profiles.Count > 0;
        AddHandler(KeyDownEvent, Window_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            await RunSafeAsync(_viewModel.InitializeAsync);
            ApplyWindowSettings(_viewModel.SavedWindow);
        };
        Closing += async (_, e) =>
        {
            if (_activeMountHandle is null) { SaveWindowSettings(); return; }
            e.Cancel = true;
            if (_unmountingOnClose) return;
            _unmountingOnClose = true;
            try
            {
                await _activeMountHandle.StopAsync();
                _activeMountHandle.Process.Dispose();
                _activeMountHandle = null;
                Close();
            }
            catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Einbindung noch aktiv", ex.Message); }
            finally { _unmountingOnClose = false; }
        };
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void UpdateResponsiveLayout()
    {
        var compact = ClientSize.Width < 1100;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        BrowserGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 210 : 280);
        Grid.SetColumn(SearchActions, compact ? 0 : 1);
        Grid.SetRow(SearchActions, compact ? 1 : 0);
        Grid.SetColumnSpan(SearchActions, compact ? 2 : 1);
        Grid.SetColumnSpan(SearchBox, compact ? 2 : 1);
        FileList.Columns[0].Width = new DataGridLength(compact ? 34 : 40);
        FileList.Columns[2].Width = new DataGridLength(compact ? 72 : 100);
        FileList.Columns[3].Width = new DataGridLength(compact ? 84 : 100);
        FileList.Columns[4].Width = new DataGridLength(compact ? 120 : 160);
        FileList.Columns[5].IsVisible = _showPermissions && !compact;
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
        await mountWindow.ShowDialog<ResticMountHandle?>(this);
        _activeMountHandle = mountWindow.ActiveMountHandle;
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
    private async void Connect_Click(object? sender, RoutedEventArgs e) => await OpenConnectionAsync(_viewModel.LastProfileId);

    private async void RecentProfile_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is RepositoryProfile profile) await OpenConnectionAsync(profile.Id);
    }

    private async Task OpenConnectionAsync(Guid? preselectedProfile)
    {
        var dialog = new ConnectionWindow(_viewModel.Profiles, preselectedProfile);
        if (await dialog.ShowDialog<bool>(this) != true || dialog.Profile is null || dialog.Credentials is null) return;
        await RunSafeAsync(() => _viewModel.ConnectAsync(dialog.Profile, dialog.Credentials));
    }
    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.RefreshSnapshotsAsync);
    private void Disconnect_Click(object? sender, RoutedEventArgs e) => _viewModel.Disconnect();
    private void Theme_Click(object? sender, RoutedEventArgs e) => App.SetTheme(!App.IsDark);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
    private async void Up_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoUpAsync);
    private async void Back_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoBackAsync);
    private async void Forward_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoForwardAsync);
    private async void FileList_DoubleTapped(object? sender, TappedEventArgs e) { if (FileList.SelectedItem is BackupNode node) await RunSafeAsync(() => _viewModel.OpenNodeAsync(node)); }
    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        if (SearchScopeBox.SelectedIndex == 1) { RestoreNewest_Click(sender, e); return; }
        await RunSafeAsync(() => _viewModel.SearchAsync(SearchBox.Text ?? ""));
    }

    private async void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        if (!_viewModel.IsSearchResult) return;
        if (_viewModel.CanGoBack) await RunSafeAsync(_viewModel.GoBackAsync);
        else await RunSafeAsync(() => _viewModel.LoadDirectoryAsync("/"));
    }

    private async void Crumb_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsSearchResult || (sender as Button)?.Tag is not string path || path == _viewModel.CurrentPath) return;
        await RunSafeAsync(() => _viewModel.LoadDirectoryAsync(path));
    }

    private void TogglePermissions_Click(object? sender, RoutedEventArgs e)
    {
        _showPermissions = !_showPermissions;
        PermissionsMenuItem.IsChecked = _showPermissions;
        FileList.Columns[5].IsVisible = _showPermissions && ClientSize.Width >= 1100;
    }

    private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = FileList.SelectedItems?.OfType<BackupNode>().ToList() ?? [];
        _viewModel.SelectionSummary = selected.Count == 0 ? "" : selected.All(node => !node.IsDirectory)
            ? $"{selected.Count:N0} ausgewählt, {SnapshotInfo.FormatBytes(selected.Sum(node => node.Size))}"
            : $"{selected.Count:N0} ausgewählt";
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var alt = e.KeyModifiers == KeyModifiers.Alt;
        var none = e.KeyModifiers == KeyModifiers.None;
        if (ctrl && e.Key == Key.O) { e.Handled = true; await OpenConnectionAsync(_viewModel.LastProfileId); return; }
        if (!_viewModel.IsConnected) return;
        if (ctrl && e.Key == Key.F) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (ctrl && e.Key == Key.R) { e.Handled = true; Restore_Click(this, e); }
        else if (none && e.Key == Key.F5) { e.Handled = true; Refresh_Click(this, e); }
        else if (none && e.Key == Key.Escape && _viewModel.IsBusy) { e.Handled = true; _viewModel.Cancel(); }
        else if (alt && e.Key == Key.Left) { e.Handled = true; Back_Click(this, e); }
        else if (alt && e.Key == Key.Right) { e.Handled = true; Forward_Click(this, e); }
        else if (alt && e.Key == Key.Up) { e.Handled = true; Up_Click(this, e); }
        else if (none && e.Key == Key.Enter && FileList.IsKeyboardFocusWithin && FileList.SelectedItem is BackupNode node)
        {
            e.Handled = true;
            if (node.IsDirectory) await RunSafeAsync(() => _viewModel.OpenNodeAsync(node));
            else Preview_Click(this, e);
        }
    }

    private void ConfigureColumnSorting()
    {
        FileList.Columns[1].CustomSortComparer = new NodeComparer((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase), directoriesFirst: true);
        FileList.Columns[2].CustomSortComparer = new NodeComparer((a, b) => string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase));
        FileList.Columns[3].CustomSortComparer = new NodeComparer((a, b) => a.Size.CompareTo(b.Size));
        FileList.Columns[4].CustomSortComparer = new NodeComparer((a, b) => Nullable.Compare(a.Modified, b.Modified));
        FileList.Columns[5].CustomSortComparer = new NodeComparer((a, b) => string.CompareOrdinal(a.Permissions, b.Permissions));
    }

    private sealed class NodeComparer(Comparison<BackupNode> compare, bool directoriesFirst = false) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not BackupNode a || y is not BackupNode b) return 0;
            if (directoriesFirst && a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return compare(a, b);
        }
    }

    private void ApplyWindowSettings(WindowSettings? saved)
    {
        _lastWindow = saved;
        if (saved is null || saved.Width < MinWidth || saved.Height < MinHeight) return;
        var screens = Screens.All;
        if (screens.Count == 0) return;
        var area = screens[0].WorkingArea;
        var scaling = Math.Max(1, screens[0].Scaling);
        if (saved.X is int x && saved.Y is int y && screens.Any(screen => screen.WorkingArea.Contains(new PixelPoint(x + 60, y + 60))))
            Position = new PixelPoint(x, y);
        Width = Math.Min(saved.Width, area.Width / scaling);
        Height = Math.Min(saved.Height, area.Height / scaling);
        if (saved.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowSettings()
    {
        try
        {
            var normal = WindowState == WindowState.Normal;
            var window = new WindowSettings
            {
                Width = Width,
                Height = Height,
                IsMaximized = WindowState == WindowState.Maximized,
                X = normal ? Position.X : _lastWindow?.X,
                Y = normal ? Position.Y : _lastWindow?.Y
            };
            Task.Run(() => _viewModel.SaveWindowSettingsAsync(window)).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception) { /* Die Fensterposition ist optional und darf das Schließen nicht verhindern. */ }
    }
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

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is BackupNode node)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { _viewModel.ReportStatus("Die Zwischenablage ist nicht verfügbar."); return; }
            await clipboard.SetTextAsync(node.Path);
            _viewModel.ReportStatus($"Pfad kopiert: {node.Path}");
        }
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (SettingsException ex) { await DialogService.ShowMessageAsync(this, "Profile nicht verfügbar", ex.Message); }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Restic-Fehler", ErrorGuidance.Describe(ex)); }
        catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Fehler", $"Unerwarteter Fehler: {ex.Message}"); }
    }
}
