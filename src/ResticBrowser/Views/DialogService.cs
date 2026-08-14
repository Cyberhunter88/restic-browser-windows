using Avalonia.Controls;
using Avalonia.Layout;

namespace ResticBrowser.Views;

internal static class DialogService
{
    public static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = Create(title, message, out var buttons);
        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => dialog.Close(); buttons.Children.Add(ok);
        await dialog.ShowDialog(owner);
    }
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string acceptText = "Wiederherstellen")
    {
        var dialog = Create(title, message, out var buttons);
        var no = new Button { Content = "Abbrechen", MinWidth = 90, IsCancel = true };
        var yes = new Button { Content = acceptText, MinWidth = 110, Classes = { "primary" }, IsDefault = true };
        no.Click += (_, _) => dialog.Close(false); yes.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(no); buttons.Children.Add(yes);
        return await dialog.ShowDialog<bool>(owner);
    }
    private static Window Create(string title, string message, out StackPanel buttons)
    {
        buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var content = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 18 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Grid.SetRow(buttons, 1);
        content.Children.Add(buttons);
        return new Window
        {
            Title = title,
            Width = 460,
            MinHeight = 180,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
    }
}
