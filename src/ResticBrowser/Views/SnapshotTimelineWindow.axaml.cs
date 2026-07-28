using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ResticBrowser.Models;

namespace ResticBrowser.Views;

public partial class SnapshotTimelineWindow : Window
{
    public SnapshotInfo? SelectedSnapshot { get; private set; }
    public SnapshotTimelineWindow() { InitializeComponent(); }
    public SnapshotTimelineWindow(IEnumerable<SnapshotInfo> snapshots) : this() => TimelineGrid.ItemsSource = snapshots.OrderByDescending(s => s.Time).ToList();
    private void Timeline_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TimelineGrid.SelectedItem is SnapshotInfo snapshot) { SelectedSnapshot = snapshot; Close(snapshot); }
    }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close(SelectedSnapshot);
}
