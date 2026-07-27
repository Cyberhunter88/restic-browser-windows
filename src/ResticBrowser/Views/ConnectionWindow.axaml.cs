using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class ConnectionWindow : Window
{
    private readonly ObservableCollection<EnvironmentEntry> _environment = [];
    public RepositoryProfile? Profile { get; private set; }
    public SessionCredentials? Credentials { get; private set; }
    public ConnectionWindow(IEnumerable<RepositoryProfile> profiles)
    {
        InitializeComponent();
        ProfileBox.ItemsSource = profiles;
        EnvironmentGrid.ItemsSource = _environment;
        ResticBox.Text = ResticLocator.Find() ?? "";
        if (profiles.Any()) ProfileBox.SelectedIndex = 0;
    }
    private void ProfileBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not RepositoryProfile profile) return;
        NameBox.Text = profile.Name; RepositoryBox.Text = profile.Repository; ResticBox.Text = profile.ResticExecutable ?? ResticLocator.Find() ?? "";
    }
    private void NewProfile_Click(object? sender, RoutedEventArgs e)
    {
        ProfileBox.SelectedItem = null; NameBox.Text = ""; RepositoryBox.Text = ""; PasswordBox.Text = ""; _environment.Clear(); ResticBox.Text = ResticLocator.Find() ?? ""; NameBox.Focus();
    }
    private async void BrowseRepository_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Lokales Restic-Repository auswählen", AllowMultiple = false });
        if (folders.Count > 0) RepositoryBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }
    private async void BrowseRestic_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Restic-Programm auswählen", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] });
        if (files.Count > 0) ResticBox.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
    }
    private void AddVariable_Click(object? sender, RoutedEventArgs e) => _environment.Add(new EnvironmentEntry());
    private void RemoveVariable_Click(object? sender, RoutedEventArgs e) { if (EnvironmentGrid.SelectedItem is EnvironmentEntry entry) _environment.Remove(entry); }
    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(RepositoryBox.Text)) { await DialogService.ShowMessageAsync(this, "Angaben fehlen", "Bitte Profilname und Repository angeben."); return; }
        if (string.IsNullOrWhiteSpace(ResticBox.Text) || !File.Exists(ResticBox.Text)) { await DialogService.ShowMessageAsync(this, "Restic fehlt", "Bitte ein vorhandenes Restic-Programm auswählen."); return; }
        if (string.IsNullOrEmpty(PasswordBox.Text)) { await DialogService.ShowMessageAsync(this, "Passwort fehlt", "Bitte das Repository-Passwort eingeben."); return; }
        var selected = ProfileBox.SelectedItem as RepositoryProfile;
        Profile = new RepositoryProfile { Id = selected?.Id ?? Guid.NewGuid(), Name = NameBox.Text.Trim(), Repository = RepositoryBox.Text.Trim(), ResticExecutable = ResticBox.Text.Trim() };
        Credentials = new SessionCredentials(PasswordBox.Text, _environment.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToDictionary(e => e.Name, e => e.Value));
        PasswordBox.Text = ""; Close(true);
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
