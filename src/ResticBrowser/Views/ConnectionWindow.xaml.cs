using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
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
        if (ProfileBox.Items.Count > 0) ProfileBox.SelectedIndex = 0;
        Loaded += (_, _) => WindowTheme.Apply(this, App.IsDark);
    }

    private void ProfileBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not RepositoryProfile profile) return;
        NameBox.Text = profile.Name;
        RepositoryBox.Text = profile.Repository;
        ResticBox.Text = profile.ResticExecutable ?? ResticLocator.Find() ?? "";
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfileBox.SelectedItem = null;
        NameBox.Clear();
        RepositoryBox.Clear();
        PasswordBox.Clear();
        _environment.Clear();
        ResticBox.Text = ResticLocator.Find() ?? "";
        NameBox.Focus();
    }

    private void BrowseRepository_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Lokales Restic-Repository auswählen" };
        if (dialog.ShowDialog(this) == true) RepositoryBox.Text = dialog.FolderName;
    }

    private void BrowseRestic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "restic.exe auswählen", Filter = "Restic (restic.exe)|*.exe|Programme|*.exe" };
        if (dialog.ShowDialog(this) == true) ResticBox.Text = dialog.FileName;
    }

    private void AddVariable_Click(object sender, RoutedEventArgs e) => _environment.Add(new EnvironmentEntry());
    private void RemoveVariable_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentGrid.SelectedItem is EnvironmentEntry entry) _environment.Remove(entry);
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(RepositoryBox.Text))
        {
            MessageBox.Show(this, "Bitte Profilname und Repository angeben.", "Angaben fehlen");
            return;
        }
        if (string.IsNullOrWhiteSpace(ResticBox.Text) || !File.Exists(ResticBox.Text))
        {
            MessageBox.Show(this, "Bitte eine vorhandene restic.exe auswählen.", "Restic fehlt");
            return;
        }
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            MessageBox.Show(this, "Bitte das Repository-Passwort eingeben.", "Passwort fehlt");
            return;
        }

        var selected = ProfileBox.SelectedItem as RepositoryProfile;
        Profile = new RepositoryProfile
        {
            Id = selected?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            Repository = RepositoryBox.Text.Trim(),
            ResticExecutable = ResticBox.Text.Trim()
        };
        Credentials = new SessionCredentials(PasswordBox.Password,
            _environment.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToDictionary(e => e.Name, e => e.Value));
        PasswordBox.Clear();
        DialogResult = true;
    }
}
