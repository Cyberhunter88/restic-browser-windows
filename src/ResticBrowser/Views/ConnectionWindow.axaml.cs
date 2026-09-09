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
    public RepositoryProfile? Profile { get; private set; }
    public SessionCredentials? Credentials { get; private set; }

    public ConnectionWindow() : this([]) { }

    public ConnectionWindow(IEnumerable<RepositoryProfile> profiles)
    {
        InitializeComponent();
        ProfileBox.ItemsSource = profiles;
        EnvironmentGrid.ItemsSource = _environment;
        ResticInfoText.Text = "Restic wird automatisch aus der Anwendung oder dem System verwendet.";
        Opened += async (_, _) => await RefreshResticInfoAsync();
        if (profiles.Any()) ProfileBox.SelectedIndex = 0;
    }

    private void ProfileBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not RepositoryProfile profile) return;
        NameBox.Text = profile.Name;
        RepositoryBox.Text = profile.Repository;
        ResticBox.Text = profile.ResticExecutable ?? "";
        RepoTypeBox.SelectedIndex = profile.Type switch { RepositoryType.SFTP => 1, RepositoryType.S3 => 2, RepositoryType.REST => 3, _ => 0 };
        SftpHostBox.Text = profile.SftpHost;
        SftpPortBox.Text = profile.SftpPort > 0 ? profile.SftpPort.ToString() : "22";
        SftpUserBox.Text = profile.SftpUser;
        SftpPathBox.Text = profile.SftpPath;
        SftpKeyBox.Text = profile.SftpKeyFile;
        S3EndpointBox.Text = profile.S3Endpoint;
        S3BucketBox.Text = profile.S3Bucket;
        S3PrefixBox.Text = profile.S3Prefix;
        S3RegionBox.Text = profile.S3Region;
        RestServerUrlBox.Text = profile.RestServerUrl;
        RestRepositoryPathBox.Text = profile.RestRepositoryPath;
        _ = RefreshResticInfoAsync();
    }

    private void RepoTypeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SftpPanel is null || LocalRepoPanel is null) return;
        var isSftp = RepoTypeBox.SelectedIndex == 1;
        var isS3 = RepoTypeBox.SelectedIndex == 2;
        var isRest = RepoTypeBox.SelectedIndex == 3;
        SftpPanel.IsVisible = isSftp;
        S3Panel.IsVisible = isS3;
        RestPanel.IsVisible = isRest;
        LocalRepoPanel.IsVisible = !isSftp && !isS3 && !isRest;
    }

    private void NewProfile_Click(object? sender, RoutedEventArgs e)
    {
        ProfileBox.SelectedItem = null;
        NameBox.Text = "";
        RepositoryBox.Text = "";
        PasswordBox.Text = "";
        _environment.Clear();
        SftpHostBox.Text = "";
        SftpPortBox.Text = "22";
        SftpUserBox.Text = "";
        SftpPathBox.Text = "";
        SftpKeyBox.Text = "";
        S3EndpointBox.Text = S3BucketBox.Text = S3PrefixBox.Text = S3RegionBox.Text = "";
        S3AccessKeyBox.Text = S3SecretKeyBox.Text = S3SessionTokenBox.Text = "";
        RestServerUrlBox.Text = RestRepositoryPathBox.Text = RestUserBox.Text = RestPasswordBox.Text = "";
        ResticBox.Text = "";
        _ = RefreshResticInfoAsync();
        NameBox.Focus();
    }

    private async void BrowseRepository_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Lokales Restic-Repository auswählen", AllowMultiple = false });
        if (folders.Count > 0) RepositoryBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private async void BrowseSftpKey_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "SSH Private Key auswählen", AllowMultiple = false });
        if (files.Count > 0) SftpKeyBox.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
    }

    private async void BrowseRestic_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Restic-Programm auswählen", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] });
        if (files.Count > 0)
        {
            ResticBox.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
            await RefreshResticInfoAsync();
        }
    }

    private void AddVariable_Click(object? sender, RoutedEventArgs e) => _environment.Add(new EnvironmentEntry());
    private void RemoveVariable_Click(object? sender, RoutedEventArgs e) { if (EnvironmentGrid.SelectedItem is EnvironmentEntry entry) _environment.Remove(entry); }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        var type = RepoTypeBox.SelectedIndex switch { 1 => RepositoryType.SFTP, 2 => RepositoryType.S3, 3 => RepositoryType.REST, _ => RepositoryType.Local };
        var isSftp = type == RepositoryType.SFTP;

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

        if (isSftp && (string.IsNullOrWhiteSpace(SftpHostBox.Text) || string.IsNullOrWhiteSpace(SftpPathBox.Text)))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte Host und Remote-Pfad für das SFTP-Repository angeben.");
            return;
        }

        ResticExecutableInfo executable;
        try
        {
            executable = await _resticProvisioning.ResolveAsync(ResticBox.Text);
        }
        catch (ResticException ex)
        {
            await DialogService.ShowMessageAsync(this, "Restic fehlt", ex.Message);
            return;
        }
        if (type == RepositoryType.S3 && (string.IsNullOrWhiteSpace(S3EndpointBox.Text) || string.IsNullOrWhiteSpace(S3BucketBox.Text)))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte HTTPS-Endpunkt und Bucket für das S3-Repository angeben.");
            return;
        }
        if (type == RepositoryType.REST && (string.IsNullOrWhiteSpace(RestServerUrlBox.Text) || string.IsNullOrWhiteSpace(RestRepositoryPathBox.Text)))
        {
            await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte HTTPS-Serveradresse und Repository-Pfad angeben.");
            return;
        }
        if ((type == RepositoryType.S3 && !IsSecureUrl(S3EndpointBox.Text)) || (type == RepositoryType.REST && !IsSecureUrl(RestServerUrlBox.Text)))
        {
            await DialogService.ShowMessageAsync(this, "Adresse ungültig", "Bitte eine HTTPS-Adresse ohne eingebettete Zugangsdaten verwenden.");
            return;
        }

        if (string.IsNullOrEmpty(PasswordBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Passwort fehlt", "Bitte das Repository-Passwort eingeben.");
            return;
        }

        int.TryParse(SftpPortBox.Text, out var sftpPort);
        if (sftpPort <= 0) sftpPort = 22;

        var selected = ProfileBox.SelectedItem as RepositoryProfile;
        Profile = new RepositoryProfile
        {
            Id = selected?.Id ?? Guid.NewGuid(),
            Name = (NameBox.Text ?? "").Trim(),
            Type = type,
            Repository = type == RepositoryType.Local ? (RepositoryBox.Text ?? "").Trim() : "",
            SftpHost = (SftpHostBox.Text ?? "").Trim(),
            SftpPort = sftpPort,
            SftpUser = (SftpUserBox.Text ?? "").Trim(),
            SftpPath = (SftpPathBox.Text ?? "").Trim(),
            SftpKeyFile = (SftpKeyBox.Text ?? "").Trim(),
            S3Endpoint = (S3EndpointBox.Text ?? "").Trim(),
            S3Bucket = (S3BucketBox.Text ?? "").Trim(),
            S3Prefix = (S3PrefixBox.Text ?? "").Trim(),
            S3Region = (S3RegionBox.Text ?? "").Trim(),
            RestServerUrl = (RestServerUrlBox.Text ?? "").Trim(),
            RestRepositoryPath = (RestRepositoryPathBox.Text ?? "").Trim(),
            ResticExecutable = string.IsNullOrWhiteSpace(ResticBox.Text) ? null : executable.Path
        };
        Profile.ResolvedResticExecutable = executable.Path;

        if (isSftp || type == RepositoryType.S3 || type == RepositoryType.REST)
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
            var type = RepoTypeBox.SelectedIndex switch
            {
                1 => RepositoryType.SFTP,
                2 => RepositoryType.S3,
                3 => RepositoryType.REST,
                _ => RepositoryType.Local
            };
            var executable = await _resticProvisioning.ResolveAsync(ResticBox.Text, _connectionTest.Token);
            var profile = new RepositoryProfile
            {
                Type = type,
                Repository = type == RepositoryType.Local ? RepositoryBox.Text?.Trim() ?? "" : "",
                SftpHost = SftpHostBox.Text?.Trim() ?? "",
                SftpUser = SftpUserBox.Text?.Trim() ?? "",
                SftpPath = SftpPathBox.Text?.Trim() ?? "",
                SftpKeyFile = SftpKeyBox.Text?.Trim() ?? "",
                S3Endpoint = S3EndpointBox.Text?.Trim() ?? "",
                S3Bucket = S3BucketBox.Text?.Trim() ?? "",
                S3Prefix = S3PrefixBox.Text?.Trim() ?? "",
                S3Region = S3RegionBox.Text?.Trim() ?? "",
                RestServerUrl = RestServerUrlBox.Text?.Trim() ?? "",
                RestRepositoryPath = RestRepositoryPathBox.Text?.Trim() ?? "",
                ResolvedResticExecutable = executable.Path
            };
            profile.SftpPort = int.TryParse(SftpPortBox.Text, out var port) && port > 0 ? port : 22;
            if (type != RepositoryType.Local) profile.Repository = profile.BuildRepositoryString();
            if (string.IsNullOrWhiteSpace(profile.Repository)) throw new ResticException("Die Repository-Adresse ist unvollständig.");
            using var credentials = new SessionCredentials(PasswordBox.Text, BuildBackendEnvironment());
            var repository = new ResticRepositoryService(new ResticProcessRunner());
            var version = await repository.ValidateAsync(profile, _connectionTest.Token);
            await repository.GetSnapshotsAsync(profile, credentials, _connectionTest.Token);
            await DialogService.ShowMessageAsync(this, "Verbindung erfolgreich", $"Restic {version.Version} und das Repository sind erreichbar.");
        }
        catch (OperationCanceledException) { await DialogService.ShowMessageAsync(this, "Prüfung abgebrochen", "Die Verbindungsprüfung wurde abgebrochen."); }
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
        AddSecret(values, "AWS_ACCESS_KEY_ID", S3AccessKeyBox.Text);
        AddSecret(values, "AWS_SECRET_ACCESS_KEY", S3SecretKeyBox.Text);
        AddSecret(values, "AWS_SESSION_TOKEN", S3SessionTokenBox.Text);
        AddSecret(values, "AWS_DEFAULT_REGION", S3RegionBox.Text);
        AddSecret(values, "RESTIC_REST_USERNAME", RestUserBox.Text);
        AddSecret(values, "RESTIC_REST_PASSWORD", RestPasswordBox.Text);
        return values;
    }

    private static bool IsSecureUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrWhiteSpace(uri.UserInfo);
}
