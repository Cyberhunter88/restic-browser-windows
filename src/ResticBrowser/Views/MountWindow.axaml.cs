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
    private string? _staleMountPoint;
    private CancellationTokenSource? _mountCancellation;

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

        Closing += (_, e) =>
        {
            if (_mountCancellation is null) return;
            e.Cancel = true;
            _mountCancellation.Cancel();
        };
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
            CustomPathBox.Text = Path.Combine(Path.GetTempPath(), "restic-browser-" + Guid.NewGuid().ToString("N")[..8]);
        }

        UpdateMountStatusUi();
    }

    private void UpdateMountStatusUi()
    {
        RepairMountButton.IsVisible = false;
        _staleMountPoint = null;
        if (_mountHandle != null && !_mountHandle.Process.HasExited)
        {
            StatusTitleText.Text = "Laufwerk eingebunden";
            StatusText.Text = _mountHandle.BrowsePath;
            MountButton.IsEnabled = false;
            UnmountButton.IsEnabled = true;
            OpenExplorerButton.IsEnabled = true;
        }
        else
        {
            StatusTitleText.Text = "Bereit zum Einbinden";
            StatusText.Text = "Wähle die Sicherungen und einen Zielordner.";
            MountButton.IsEnabled = true;
            UnmountButton.IsEnabled = false;
            OpenExplorerButton.IsEnabled = false;
            _mountHandle = null;
        }
    }

    private async void Mount_Click(object? sender, RoutedEventArgs e)
    {
        if (_mountCancellation is not null) { _mountCancellation.Cancel(); return; }
        // Restic mount is only supported on Linux/macOS via FUSE.
        // On Windows it requires WinFsp; if WinFsp is not installed restic exits immediately
        // with "unknown command \"mount\" for \"restic\"" which is confusing. Give a clear
        // German error message before even trying to start the process.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bool winfspFound = CheckWinFspInstalled();
            if (!winfspFound)
            {
                await DialogService.ShowMessageAsync(this, "Mount nicht möglich",
                    "Das Einbinden als virtuelles Laufwerk erfordert unter Windows das Programm \"WinFsp\" (Windows File System Proxy).\n\n" +
                    "WinFsp ist auf diesem System nicht installiert oder wurde nicht gefunden.\n\n" +
                    "Bitte installiere WinFsp von https://winfsp.dev/ und starte die Anwendung danach erneut.");
                return;
            }
        }

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
                ShowMountError(ex);
                return;
            }
        }

        string? snapshotId = RadioSingleSnapshot.IsChecked == true ? _selectedSnapshot?.Id : null;
        var request = new MountRequest(snapshotId, mountPoint);

        _mountCancellation = new CancellationTokenSource();
        SetMountBusy(true);
        MountButton.Content = "Abbrechen";
        MountButton.IsEnabled = true;
        MountProgressBar.IsVisible = true;
        StatusTitleText.Text = "Laufwerk wird eingebunden …";
        StatusText.Text = mountPoint;

        try
        {
            _mountHandle = await _service.StartMountAsync(_profile, _credentials, request, _mountCancellation.Token);
            UpdateMountStatusUi();
        }
        catch (OperationCanceledException)
        {
            UpdateMountStatusUi();
            StatusText.Text = "Einbinden abgebrochen.";
        }
        catch (Exception ex)
        {
            ShowMountError(ex);
        }
        finally
        {
            _mountCancellation?.Dispose();
            _mountCancellation = null;
            MountButton.Content = "Einbinden";
            MountProgressBar.IsVisible = false;
            SetMountBusy(false);
        }
    }

    private void SetMountBusy(bool busy)
    {
        RadioSingleSnapshot.IsEnabled = !busy && _selectedSnapshot is not null;
        RadioAllSnapshots.IsEnabled = !busy;
        CustomPathBox.IsEnabled = !busy;
        DriveLetterCombo.IsEnabled = !busy;
        BrowseFolderButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
    }

    private void ShowMountError(Exception error)
    {
        UpdateMountStatusUi();
        StatusTitleText.Text = "Einbinden nicht möglich";
        var stale = error.Message.Contains("Transport endpoint is not connected", StringComparison.OrdinalIgnoreCase);
        _staleMountPoint = stale ? CustomPathBox.Text?.Trim() : null;
        RepairMountButton.IsVisible = stale;
        StatusText.Text = stale
            ? "Der Zielordner gehört zu einer nicht mehr erreichbaren FUSE-Einbindung. Wähle einen anderen leeren Ordner oder trenne die alte Einbindung mit der Aktion unten."
            : $"Der Zielordner konnte nicht eingebunden werden.\n{error.Message}";
    }

    private async void RepairMount_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_staleMountPoint)) return;
        try
        {
            ResticRepositoryService.ValidateMountLocation(_profile, _staleMountPoint);
            RepairMountButton.IsEnabled = false;
            await LinuxMountUtilities.UnmountAsync(_staleMountPoint);
            UpdateMountStatusUi();
            StatusText.Text = "Die alte Einbindung wurde getrennt. Du kannst den Zielordner jetzt erneut verwenden.";
        }
        catch (Exception ex) { StatusText.Text = $"Die alte Einbindung konnte nicht getrennt werden.\n{ex.Message}"; }
        finally { RepairMountButton.IsEnabled = true; }
    }

    private async void Unmount_Click(object? sender, RoutedEventArgs e)
    {
        if (_mountHandle is null) return;
        SetMountBusy(true);
        UnmountButton.IsEnabled = false;
        OpenExplorerButton.IsEnabled = false;
        MountProgressBar.IsVisible = true;
        StatusTitleText.Text = "Laufwerk wird getrennt …";
        StatusText.Text = _mountHandle.MountPoint;
        try
        {
            await _mountHandle.StopAsync();
            _mountHandle.Process.Dispose();
            _mountHandle = null;
            UpdateMountStatusUi();
        }
        catch (Exception ex)
        {
            UpdateMountStatusUi();
            StatusTitleText.Text = "Einbindung konnte nicht getrennt werden";
            StatusText.Text = ex.Message;
        }
        finally
        {
            MountProgressBar.IsVisible = false;
            SetMountBusy(false);
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
                    FileName = _mountHandle.BrowsePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Fehler", $"Der Dateimanager konnte nicht geöffnet werden: {ex.Message}");
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

    /// <summary>
    /// Checks whether WinFsp is available on Windows by looking for its driver file.
    /// WinFsp installs to %ProgramFiles%\WinFsp or %ProgramFiles(x86)%\WinFsp.
    /// </summary>
    private static bool CheckWinFspInstalled()
    {
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var pf in programFiles)
        {
            if (string.IsNullOrEmpty(pf)) continue;
            // The WinFsp FUSE library is the main requirement for restic mount on Windows.
            var candidates = new[]
            {
                Path.Combine(pf, "WinFsp", "bin", "winfsp-x64.dll"),
                Path.Combine(pf, "WinFsp", "bin", "winfsp-x86.dll"),
                Path.Combine(pf, "WinFsp", "bin", "winfsp.dll")
            };
            if (candidates.Any(File.Exists)) return true;
        }
        return false;
    }
}
