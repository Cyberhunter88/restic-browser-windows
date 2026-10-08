using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class ConnectionWindow : Window
{
    private readonly ObservableCollection<EnvironmentEntry> _environment = [];
    private readonly ResticProvisioningService _resticProvisioning = new();
    private CancellationTokenSource? _connectionTest;
    private CancellationTokenSource? _resticResolution;
    public RepositoryProfile? Profile { get; private set; }
    public SessionCredentials? Credentials { get; private set; }

    public ConnectionWindow() : this([]) { }

    public ConnectionWindow(IEnumerable<RepositoryProfile> profiles)
    {
        InitializeComponent();
        Closed += (_, _) => { _connectionTest?.Cancel(); _resticResolution?.Cancel(); };
        ProfileBox.ItemsSource = profiles;
        EnvironmentGrid.ItemsSource = _environment;
        ResticBox.Text = "";
        UpdateResticInfo();
        if (profiles.Any()) ProfileBox.SelectedIndex = 0;
    }

    private void ProfileBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not RepositoryProfile profile) return;
        NameBox.Text = profile.Name;
        RepositoryBox.Text = profile.Repository;
        ResticBox.Text = profile.ResticExecutable ?? "";
        UpdateResticInfo();
        RepoTypeBox.SelectedIndex = profile.Type == RepositoryType.REST ? 1 : 0;
        RestServerUrlBox.Text = profile.RestServerUrl;
        RestRepositoryPathBox.Text = profile.RestRepositoryPath;
    }

    private void RepoTypeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LocalRepoPanel is null || RestPanel is null) return;
        var isRest = RepoTypeBox.SelectedIndex == 1;
        RestPanel.IsVisible = isRest;
        LocalRepoPanel.IsVisible = !isRest;
    }

    private void NewProfile_Click(object? sender, RoutedEventArgs e)
    {
        ProfileBox.SelectedItem = null;
        NameBox.Text = "";
        RepositoryBox.Text = "";
        PasswordBox.Text = "";
        _environment.Clear();
        RestServerUrlBox.Text = RestRepositoryPathBox.Text = RestUserBox.Text = RestPasswordBox.Text = "";
        ResticBox.Text = "";
        UpdateResticInfo();
        NameBox.Focus();
    }

    private async void BrowseRepository_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Lokales Restic-Repository auswählen", AllowMultiple = false });
        if (folders.Count > 0) RepositoryBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private async void BrowseRestic_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Restic-Programm auswählen", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] });
        if (files.Count > 0)
        {
            ResticBox.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
            UpdateResticInfo();
        }
    }

    private void ResticBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _resticResolution?.Cancel();
        UpdateResticInfo();
    }

    private void UpdateResticInfo() =>
        ResticInfoText.Text = string.IsNullOrWhiteSpace(ResticBox.Text)
            ? "Restic wird automatisch gesucht und vor dem Zugriff auf Version und Ausführbarkeit geprüft."
            : "Die ausgewählte Restic-Datei wird beim Verbinden auf Version und Erreichbarkeit geprüft.";

    private void AddVariable_Click(object? sender, RoutedEventArgs e) => _environment.Add(new EnvironmentEntry());
    private void RemoveVariable_Click(object? sender, RoutedEventArgs e) { if (EnvironmentGrid.SelectedItem is EnvironmentEntry entry) _environment.Remove(entry); }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        var type = RepoTypeBox.SelectedIndex == 1 ? RepositoryType.REST : RepositoryType.Local;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte einen Profilnamen angeben.");
            return;
        }

        if (type == RepositoryType.Local && string.IsNullOrWhiteSpace(RepositoryBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte den Pfad zum lokalen Repository angeben.");
            return;
        }

        if (_resticResolution is not null) return;
        ResticExecutableInfo executable;
        var configuredRestic = ResticBox.Text;
        using var resolution = new CancellationTokenSource();
        _resticResolution = resolution;
        try
        {
            executable = await _resticProvisioning.ResolveAsync(configuredRestic, resolution.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (ResticException ex)
        {
            await DialogService.ShowMessageAsync(this, "Restic fehlt", ex.Message);
            return;
        }
        finally { _resticResolution = null; }
        if (type == RepositoryType.REST && (string.IsNullOrWhiteSpace(RestServerUrlBox.Text) || string.IsNullOrWhiteSpace(RestRepositoryPathBox.Text)))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte HTTPS-Serveradresse und Repository-Pfad angeben.");
            return;
        }
        if (type == RepositoryType.REST && !IsSecureUrl(RestServerUrlBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Adresse ungültig", "Bitte eine HTTPS-Adresse ohne eingebettete Zugangsdaten verwenden.");
            return;
        }

        if (string.IsNullOrEmpty(PasswordBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Passwort fehlt", "Bitte das Repository-Passwort eingeben.");
            return;
        }

        var selected = ProfileBox.SelectedItem as RepositoryProfile;
        Profile = new RepositoryProfile
        {
            Id = selected?.Id ?? Guid.NewGuid(),
            Name = (NameBox.Text ?? "").Trim(),
            Type = type,
            Repository = type == RepositoryType.Local ? (RepositoryBox.Text ?? "").Trim() : "",
            RestServerUrl = (RestServerUrlBox.Text ?? "").Trim(),
            RestRepositoryPath = (RestRepositoryPathBox.Text ?? "").Trim(),
            ResticExecutable = string.IsNullOrWhiteSpace(configuredRestic) ? null : executable.Path,
            ResolvedResticExecutable = executable.Path,
            ResolvedResticSource = executable.Source
        };
        ResticInfoText.Text = $"{executable.Source}, Restic {executable.Version}";

        if (type == RepositoryType.REST)
        {
            Profile.Repository = Profile.BuildRepositoryString();
        }

        Dictionary<string, string> envDict;
        try
        {
            envDict = BuildBackendEnvironment();
        }
        catch (ResticException ex)
        {
            await DialogService.ShowMessageAsync(this, "Backend-Variablen ungültig", ex.Message);
            return;
        }

        Credentials = new SessionCredentials(PasswordBox.Text, envDict);
        PasswordBox.Text = "";
        Close(true);
    }

    private async void TestConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (_connectionTest is not null) { _connectionTest.Cancel(); return; }
        if (string.IsNullOrWhiteSpace(PasswordBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Passwort fehlt", "Bitte das Repository-Passwort für die Verbindungsprüfung eingeben.");
            return;
        }
        _connectionTest = new CancellationTokenSource();
        TestConnectionButton.Content = "Prüfung abbrechen";
        try
        {
            var type = RepoTypeBox.SelectedIndex == 1 ? RepositoryType.REST : RepositoryType.Local;
            var executable = await _resticProvisioning.ResolveAsync(ResticBox.Text, _connectionTest.Token);
            var profile = new RepositoryProfile
            {
                Type = type,
                Repository = type == RepositoryType.Local ? RepositoryBox.Text?.Trim() ?? "" : "",
                RestServerUrl = RestServerUrlBox.Text?.Trim() ?? "",
                RestRepositoryPath = RestRepositoryPathBox.Text?.Trim() ?? "",
                ResolvedResticExecutable = executable.Path,
                ResolvedResticSource = executable.Source
            };
            if (type != RepositoryType.Local) profile.Repository = profile.BuildRepositoryString();
            if (string.IsNullOrWhiteSpace(profile.Repository)) throw new ResticException("Die Repository-Adresse ist unvollständig.");
            using var credentials = new SessionCredentials(PasswordBox.Text, BuildBackendEnvironment());
            var repository = new ResticRepositoryService(new ResticProcessRunner());
            ResticInfoText.Text = $"{executable.Source}, Restic {executable.Version}";
            var version = await repository.ValidateAsync(profile, _connectionTest.Token);
            await repository.GetSnapshotsAsync(profile, credentials, _connectionTest.Token);
            await DialogService.ShowMessageAsync(this, "Verbindung erfolgreich", $"{executable.Source}: Restic {version.Version} und das Repository sind erreichbar.");
        }
        catch (OperationCanceledException) { if (IsVisible) await DialogService.ShowMessageAsync(this, "Prüfung abgebrochen", "Die Verbindungsprüfung wurde abgebrochen."); }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Verbindung fehlgeschlagen", ex.Message); }
        finally
        {
            _connectionTest.Dispose();
            _connectionTest = null;
            TestConnectionButton.Content = "Verbindung prüfen";
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private async Task RefreshResticInfoAsync()
    {
        try
        {
            var restic = await _resticProvisioning.ResolveAsync(ResticBox.Text);
            ResticInfoText.Text = $"{restic.Source}, Restic {restic.Version}";
        }
        catch (ResticException ex) { ResticInfoText.Text = ex.Message; }
    }

    private static void AddSecret(IDictionary<string, string> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[name] = value;
    }

    private Dictionary<string, string> BuildBackendEnvironment()
    {
        var values = BackendEnvironmentValidator.Normalize(_environment);
        AddSecret(values, "RESTIC_REST_USERNAME", RestUserBox.Text);
        AddSecret(values, "RESTIC_REST_PASSWORD", RestPasswordBox.Text);
        return values;
    }

    private static bool IsSecureUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrWhiteSpace(uri.UserInfo);
}
