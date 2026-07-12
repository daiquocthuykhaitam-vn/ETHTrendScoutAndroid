using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private void InitializeCredentialThemeAndFundingUi()
    {
        Loaded += (_, _) =>
        {
            var key = Environment.GetEnvironmentVariable("BINANCE_API_KEY", EnvironmentVariableTarget.User);
            var secret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(secret))
            {
                _apiKey.Text = key;
                _apiSecret.Password = secret;
                _binance.SetCredentials(key, secret);
                _execution.SetCredentials(key, secret);
                Log("SECURITY", "Đã tự nạp API từ biến môi trường Windows của tài khoản hiện tại.");
            }
            ApplyReadableGridTheme(this);
        };
    }

    private static void ApplyReadableGridTheme(DependencyObject root)
    {
        foreach (var grid in VisualChildren<DataGrid>(root))
        {
            grid.Background = B("#071421");
            grid.Foreground = B("#EAF4FF");
            grid.RowBackground = B("#091827");
            grid.AlternatingRowBackground = B("#0D2236");
            grid.HorizontalGridLinesBrush = B("#21445F");
            grid.VerticalGridLinesBrush = B("#21445F");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.All;

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, B("#14324A")));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#F4F8FC")));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, B("#2B5574")));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            grid.ColumnHeaderStyle = headerStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#E7F1FA")));
            cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, B("#17344D")));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, B("#1E5A8A")));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selected);
            grid.CellStyle = cellStyle;
        }
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
}
