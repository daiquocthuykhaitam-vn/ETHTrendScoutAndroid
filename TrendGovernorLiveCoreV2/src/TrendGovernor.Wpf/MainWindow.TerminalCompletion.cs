using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    internal void InitializeTerminalCompletionUi()
    {
        Title = "TRENDGOVERNOR LIVE CORE V2 - PACK 14 LIVE TERMINAL";
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ApplyUnifiedVisualTheme();
            InstallExitButton();
        }));
    }

    private void InstallExitButton()
    {
        if (FindVisualChildren<Button>(this).Any(x => string.Equals(x.Content?.ToString(), "THOÁT TOOL", StringComparison.Ordinal)))
            return;

        var header = FindVisualChildren<Grid>(this)
            .FirstOrDefault(x => Math.Abs(x.Height - 76d) < 0.1d && x.ColumnDefinitions.Count >= 2);
        if (header is null) return;

        var button = new Button
        {
            Content = "THOÁT TOOL",
            Background = B("#A82936"),
            Foreground = Brushes.White,
            BorderBrush = B("#D5525D"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 8, 15, 8),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 105,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => Close();

        var right = header.Children.OfType<StackPanel>()
            .FirstOrDefault(x => Grid.GetColumn(x) == 1 && x.Orientation == Orientation.Horizontal);
        right?.Children.Add(button);
    }

    private void ApplyUnifiedVisualTheme()
    {
        foreach (var tabs in FindVisualChildren<TabControl>(this))
        {
            tabs.Background = B("#040C16");
            tabs.Foreground = Brushes.White;
            tabs.BorderBrush = B("#17344D");
            tabs.BorderThickness = new Thickness(0);
            tabs.Resources[SystemColors.ControlBrushKey] = B("#163A61");
            tabs.Resources[SystemColors.HighlightBrushKey] = B("#163A61");
            tabs.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;

            foreach (var item in tabs.Items.OfType<TabItem>())
                ApplyTabItemTheme(item, item.IsSelected);

            tabs.SelectionChanged -= TabsOnSelectionChanged;
            tabs.SelectionChanged += TabsOnSelectionChanged;
        }

        foreach (var grid in FindVisualChildren<DataGrid>(this))
            ApplyGridTheme(grid);
    }

    private void TabsOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        foreach (var item in tabs.Items.OfType<TabItem>())
            ApplyTabItemTheme(item, item.IsSelected);
    }

    private static void ApplyTabItemTheme(TabItem item, bool selected)
    {
        item.Background = selected ? B("#163A61") : B("#071421");
        item.Foreground = Brushes.White;
        item.BorderBrush = selected ? B("#3A7EB6") : B("#17344D");
        item.BorderThickness = new Thickness(0, 0, 0, 1);
        item.Opacity = 1;
    }

    private static void ApplyGridTheme(DataGrid grid)
    {
        grid.Background = B("#071421");
        grid.Foreground = B("#EAF4FF");
        grid.RowBackground = B("#091827");
        grid.AlternatingRowBackground = B("#0D2236");
        grid.BorderBrush = B("#21445F");
        grid.HorizontalGridLinesBrush = B("#21445F");
        grid.VerticalGridLinesBrush = B("#21445F");
        grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        grid.ColumnHeaderStyle = CreateHardDarkHeaderStyle();

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#EAF4FF")));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, B("#091827")));
        var alt = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
        alt.Setters.Add(new Setter(Control.BackgroundProperty, B("#0D2236")));
        rowStyle.Triggers.Add(alt);
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, B("#1E5A8A")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        rowStyle.Triggers.Add(selected);
        grid.RowStyle = rowStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#EAF4FF")));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, B("#17344D")));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
        grid.CellStyle = cellStyle;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
}
