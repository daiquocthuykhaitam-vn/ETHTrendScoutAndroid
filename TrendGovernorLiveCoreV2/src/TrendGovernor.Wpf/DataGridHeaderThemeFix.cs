using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    internal void InitializeHardGridHeaderTheme()
    {
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyHardGridHeaderTheme));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ApplyHardGridHeaderTheme();
            };
            timer.Start();
        };
    }

    private void ApplyHardGridHeaderTheme()
    {
        foreach (var grid in EnumerateVisualChildren<DataGrid>(this))
        {
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.RowHeaderWidth = 0;
            grid.ColumnHeaderHeight = 40;
            grid.Background = B("#06111D");
            grid.Foreground = B("#EAF4FF");
            grid.BorderBrush = B("#1D3B55");
            grid.HorizontalGridLinesBrush = B("#17344D");
            grid.VerticalGridLinesBrush = B("#17344D");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            grid.ColumnHeaderStyle = CreateProfessionalHeaderStyle();
            grid.AlternationCount = 2;

            foreach (var column in grid.Columns)
            {
                if (column.Header is Border) continue;
                var title = Convert.ToString(column.Header)?.Trim();
                column.Header = CreateHeaderContent(string.IsNullOrWhiteSpace(title) ? "—" : title);
            }

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#EAF4FF")));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, B("#071421")));
            rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, B("#17344D")));
            rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            var alternate = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
            alternate.Setters.Add(new Setter(Control.BackgroundProperty, B("#0B1C2F")));
            rowStyle.Triggers.Add(alternate);
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, B("#184C75")));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            rowStyle.Triggers.Add(selected);
            grid.RowStyle = rowStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#EAF4FF")));
            cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, B("#17344D")));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 3, 7, 3)));
            cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            grid.CellStyle = cellStyle;
        }
    }

    private static Border CreateHeaderContent(string title)
    {
        var layout = new Grid();
        layout.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 4, 2)
        });
        layout.Children.Add(new Border
        {
            Height = 2,
            Background = B("#F0A23A"),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        return new Border
        {
            Background = B("#102A40"),
            BorderBrush = B("#2B5574"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(4, 5, 4, 5),
            Child = layout,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private static Style CreateProfessionalHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, B("#102A40")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, B("#2B5574")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, B("#173E5E")));
        style.Triggers.Add(hover);
        return style;
    }

    private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in EnumerateVisualChildren<T>(child)) yield return nested;
        }
    }
}