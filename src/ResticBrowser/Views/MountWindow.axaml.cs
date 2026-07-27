using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class MountWindow : Window
{
    private readonly IResticRepositoryService _service;
    private readonly RepositoryProfile _profile;
    private readonly SessionCredentials _credentials;
    private readonly SnapshotInfo? _selectedSnapshot;
    private ResticMountHandle? _mountHandle;

    public ResticMountHandle? ActiveMountHandle => _mountHandle;

    public MountWindow()
    {
        InitializeComponent();
        _service = null!;
        _profile = null!;
        _credentials = null!;
    }

    public MountWindow(
        IResticRepositoryService service,
        RepositoryProfile profile,
        SessionCredentials credentials,
        SnapshotInfo? selectedSnapshot,
        ResticMountHandle? existingMount = null)
    {
        InitializeComponent();
        _service = service;
        _profile = profile;
        _credentials = credentials;
        _selectedSnapshot = selectedSnapshot;
        _mountHandle = existingMount;

        InitializeUi();
    }

    private void InitializeUi()
    {
        if (_selectedSnapshot != null)
        {
            SnapshotInfoText.Text = $"{_selectedSnapshot.Hostname} - {_selectedSnapshot.DisplayId} ({_selectedSnapshot.Time:g})";
            RadioSingleSnapshot.IsChecked = true;
        }
        else
        {
            RadioSingleSnapshot.IsEnabled = false;
            SnapshotInfoText.Text = "Kein einzelner Snapshot ausgewählt.";
            RadioAllSnapshots.IsChecked = true;
        }

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            var usedDrives = DriveInfo.GetDrives().Select(d => d.Name[..1].ToUpperInvariant()).ToHashSet();
            var availableDrives = new List<string>();
            for (char letter = 'Z'; letter >= 'E'; letter--)
            {
                if (!usedDrives.Contains(letter.ToString()))
                    availableDrives.Add($"{letter}:");
            }
            if (availableDrives.Count == 0) availableDrives.Add("Z:");

            DriveLetterCombo.ItemsSource = availableDrives;
            DriveLetterCombo.SelectedIndex = 0;
            DriveLetterCombo.IsVisible = true;
            CustomPathBox.IsVisible = false;
            BrowseFolderButton.IsVisible = false;
        }
        else
        {
            DriveLetterCombo.IsVisible = false;
            CustomPathBox.IsVisible = true;
            BrowseFolderButton.IsVisible = true;
            CustomPathBox.Text = Path.Combine(Path.GetTempPath(), "restic_mount");
        }

        UpdateMountStatusUi();
    }

    private void UpdateMountStatusUi()
    {
        if (_mountHandle != null && !_mountHandle.Process.HasExited)
        {
            StatusText.Text = $"✅ Laufwerk aktiv eingebunden auf '{_mountHandle.MountPoint}'.";
            MountButton.IsEnabled = false;
            UnmountButton.IsEnabled = true;
            OpenExplorerButton.IsEnabled = true;
        }
        else
        {
            StatusText.Text = "Kein Laufwerk eingebunden.";
            MountButton.IsEnabled = true;
            UnmountButton.IsEnabled = false;
            OpenExplorerButton.IsEnabled = false;
            _mountHandle = null;
        }
    }

    private async void Mount_Click(object? sender, RoutedEventArgs e)
    {
        string mountPoint;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            mountPoint = DriveLetterCombo.SelectedItem as string ?? "Z:";
        }
        else
        {
            mountPoint = CustomPathBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(mountPoint))
            {
                await DialogService.ShowMessageAsync(this, "Fehler", "Bitte gib einen gültigen Zielpfad zum Mounten ein.");
                return;
            }
            try
            {
                Directory.CreateDirectory(mountPoint);
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Fehler", $"Ordner konnte nicht erstellt werden: {ex.Message}");
                return;
            }
        }

        string? snapshotId = RadioSingleSnapshot.IsChecked == true ? _selectedSnapshot?.Id : null;
        var request = new MountRequest(snapshotId, mountPoint);

        MountButton.IsEnabled = false;
        MountProgressBar.IsVisible = true;
        StatusText.Text = $"Mount-Vorgang wird gestartet ({mountPoint}) …";

        try
        {
            _mountHandle = await _service.StartMountAsync(_profile, _credentials, request);
            UpdateMountStatusUi();
        }
        catch (Exception ex)
        {
            StatusText.Text = " Mount-Vorgang ist fehlgeschlagen.";
            UpdateMountStatusUi();
            await DialogService.ShowMessageAsync(this, "Mount-Fehler", ex.Message);
        }
        finally
        {
            MountProgressBar.IsVisible = false;
        }
    }

    private async void Unmount_Click(object? sender, RoutedEventArgs e)
    {
        if (_mountHandle != null)
        {
            MountProgressBar.IsVisible = true;
            StatusText.Text = "Laufwerk wird getrennt …";
            try
            {
                await _mountHandle.StopAsync();
            }
            finally
            {
                _mountHandle = null;
                MountProgressBar.IsVisible = false;
                UpdateMountStatusUi();
            }
        }
    }

    private async void OpenExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (_mountHandle != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _mountHandle.MountPoint,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Fehler", $"Der Explorer konnte nicht geöffnet werden: {ex.Message}");
            }
        }
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Zielordner für Mount auswählen",
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path)
        {
            CustomPathBox.Text = path;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(_mountHandle);
    }
}
