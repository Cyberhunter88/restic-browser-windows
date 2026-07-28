using Avalonia.Controls;
using Avalonia.Interactivity;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class RepositoryCheckWindow : Window
{
    private readonly IResticRepositoryService _service;
    private readonly RepositoryProfile _profile;
    private readonly SessionCredentials _credentials;
    private readonly CancellationTokenSource _cancellation = new();

    public RepositoryCheckWindow()
    {
        InitializeComponent();
        _service = null!;
        _profile = null!;
        _credentials = null!;
    }

    public RepositoryCheckWindow(IResticRepositoryService service, RepositoryProfile profile, SessionCredentials credentials)
    {
        InitializeComponent();
        _service = service;
        _profile = profile;
        _credentials = credentials;
    }

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        Progress.IsVisible = true;
        ResultBox.Text = "Prüfung läuft …";
        try
        {
            var mode = FullBox.IsChecked == true ? CheckMode.Full : CheckMode.Quick;
            var result = await _service.CheckAsync(_profile, _credentials, mode, _cancellation.Token);
            var state = result.IsHealthy ? "Keine Fehler gefunden." : $"{result.ErrorCount} Fehler gefunden.";
            ResultBox.Text = $"{state}\nModus: {(mode == CheckMode.Full ? "vollständige Datenprüfung" : "Schnellprüfung")}\n\n{result.Details}";
        }
        catch (OperationCanceledException) { ResultBox.Text = "Prüfung abgebrochen."; }
        catch (Exception ex) { ResultBox.Text = $"Prüfung fehlgeschlagen:\n{ex.Message}"; }
        finally { Progress.IsVisible = false; CheckButton.IsEnabled = true; }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _cancellation.Cancel();
        Close();
    }
}
