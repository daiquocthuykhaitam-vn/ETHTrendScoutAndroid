using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    internal void InitializeHardGridHeaderTheme()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyHardGridHeaderTheme));
    }

    private void ApplyHardGridHeaderTheme()
    {
        foreach (var grid in FindVisualChildren<DataGrid>(this))
        {
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.RowHeaderWidth = 0;
            grid.ColumnHeaderHeight = 34;
            grid.Background = B("#071421");
            grid.Foreground = B("#EAF4FF");
            grid.BorderBrush = B("#21445F");
            grid.HorizontalGridLinesBrush = B("#21445F");
            grid.VerticalGridLinesBrush = B("#21445F");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            grid.ColumnHeaderStyle = CreateHardDarkHeaderStyle();

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#EAF4FF")));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, B("#091827")));
            var alternate = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
            alternate.Setters.Add(new Setter(Control.BackgroundProperty, B("#0D2236")));
            rowStyle.Triggers.Add(alternate);
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
    }

    private static Style CreateHardDarkHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, B("#14324A")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, B("#2B5574")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 11d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, B("#14324A"));
        border.SetValue(Border.BorderBrushProperty, B("#2B5574"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(TextElement.ForegroundProperty, Brushes.White);
        presenter.SetValue(TextElement.FontWeightProperty, FontWeights.Bold);
        border.AppendChild(presenter);

        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(DataGridColumnHeader))
        {
            VisualTree = border
        }));
        return style;
    }
}
