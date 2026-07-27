using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ResticBrowser.Models;

namespace ResticBrowser.Views;

public partial class FilePreviewWindow : Window
{
    public FilePreviewWindow()
    {
        InitializeComponent();
    }

    public FilePreviewWindow(FilePreviewData data) : this()
    {
        FileNameText.Text = data.Node.Name;
        FilePathText.Text = data.Path;
        SizeText.Text = data.Node.SizeText;
        ModifiedText.Text = data.Node.Modified?.ToString("dd.MM.yyyy HH:mm:ss zzz") ?? "Unbekannt";

        if (!string.IsNullOrWhiteSpace(data.ErrorMessage))
        {
            ErrorText.Text = data.ErrorMessage;
            ErrorText.IsVisible = true;
            return;
        }

        if (data.IsImage && data.ImageBytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(data.ImageBytes);
                var bitmap = new Bitmap(stream);
                ImagePreview.Source = bitmap;
                ImageViewer.IsVisible = true;
                return;
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Bild konnte nicht gerendert werden: {ex.Message}";
                ErrorText.IsVisible = true;
                return;
            }
        }

        if (data.IsText)
        {
            TextViewer.Text = data.TextContent ?? "";
            TextViewer.IsVisible = true;
            return;
        }

        ErrorText.Text = "Keine Vorschau für diesen Dateityp verfügbar.";
        ErrorText.IsVisible = true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
