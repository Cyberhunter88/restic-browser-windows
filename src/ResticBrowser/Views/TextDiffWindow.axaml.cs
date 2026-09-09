using Avalonia.Controls;
using Avalonia.Interactivity;
using ResticBrowser.Models;

namespace ResticBrowser.Views;

public partial class TextDiffWindow : Window
{
    public TextDiffWindow()
    {
        InitializeComponent();
    }

    public TextDiffWindow(FileVersion first, FilePreviewData firstData, FileVersion second, FilePreviewData secondData) : this()
    {
        TitleText.Text = $"{first.Snapshot.Time:g} und {second.Snapshot.Time:g} vergleichen";
        var pair = FormatPair(firstData, secondData);
        FirstText.Text = pair.First;
        SecondText.Text = pair.Second;
    }

    private static (string First, string Second) FormatPair(FilePreviewData first, FilePreviewData second)
    {
        if (!first.IsText || !second.IsText)
            return ($"Keine Textvorschau verfügbar. {first.ErrorMessage}", $"Keine Textvorschau verfügbar. {second.ErrorMessage}");
        var left = (first.TextContent ?? "").Replace("\r\n", "\n").Split('\n');
        var right = (second.TextContent ?? "").Replace("\r\n", "\n").Split('\n');
        var count = Math.Max(left.Length, right.Length);
        var leftOutput = new List<string>(count);
        var rightOutput = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var leftLine = index < left.Length ? left[index] : "";
            var rightLine = index < right.Length ? right[index] : "";
            var equal = leftLine == rightLine;
            leftOutput.Add($"{(equal ? "  " : "− ")}{leftLine}");
            rightOutput.Add($"{(equal ? "  " : "+ ")}{rightLine}");
        }
        return (string.Join(Environment.NewLine, leftOutput), string.Join(Environment.NewLine, rightOutput));
    }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
