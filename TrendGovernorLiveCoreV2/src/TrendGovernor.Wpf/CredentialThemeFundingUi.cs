using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly DataGrid _fundingGrid = FundingGrid();
    private readonly TextBlock _credentialState = T("CHƯA LƯU CREDENTIAL CỤC BỘ", 11, B("#F4B942"), true);

    private void InitializeCredentialThemeAndFundingUi()
    {
        Loaded += (_, _) =>
        {
            RestoreCredentialsFromVault();
            ApplyReadableTheme(this);
            AttachFundingWorkspace();
        };

        _startAutoButton.Click += (_, _) => SaveCredentialsToVaultIfValid();
    }

    private void RestoreCredentialsFromVault()
    {
        if (!LocalCredentialVault.TryLoad(out var key, out var secret))
        {
            _credentialState.Text = "CHƯA LƯU — nhập một lần, tool sẽ mã hóa theo tài khoản Windows";
            _credentialState.Foreground = B("#F4B942");
            return;
        }

        _apiKey.Text = key;
        _apiSecret.Password = secret;
        _binance.SetCredentials(key, secret);
        _execution.SetCredentials(key, secret);
        _credentialState.Text = "ĐÃ NẠP TỪ KHO MÃ HÓA WINDOWS";
        _credentialState.Foreground = B("#64E0A3");
        Log("SECURITY", "Đã nạp API từ Windows DPAPI vault; không ghi key/secret vào source hoặc ZIP.");
    }

    private void SaveCredentialsToVaultIfValid()
    {
        var key = _apiKey.Text.Trim();
        var secret = _apiSecret.Password.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)) return;

        try
        {
            LocalCredentialVault.Save(key, secret);
            _credentialState.Text = "ĐÃ LƯU MÃ HÓA — TỰ NẠP Ở CÁC BẢN UPDATE SAU";
            _credentialState.Foreground = B("#64E0A3");
            Log("SECURITY", $"Credential đã lưu bằng Windows DPAPI tại {LocalCredentialVault.DisplayPath}.");
        }
        catch (Exception ex)
        {
            _credentialState.Text = "LƯU CREDENTIAL THẤT BẠI";
            _credentialState.Foreground = B("#FF6B7A");
            AddAlert("SECURITY", ex.Message);
        }
    }

    private void AttachFundingWorkspace()
    {
        var tabs = FindVisualChild<TabControl>(this);
        if (tabs is null || tabs.Items.OfType<TabItem>().Any(x => x.Tag as string == "FUNDING")) return;

        _fundingGrid.ItemsSource = _state.Markets;
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var summary = new StackPanel { Orientation = Orientation.Horizontal };
        summary.Children.Add(Pill("FUNDING ÂM: SHORT TRẢ LONG", "#123B32", B("#64E0A3")));
        summary.Children.Add(Pill("FUNDING DƯƠNG: LONG TRẢ SHORT", "#3D2729", B("#FF8793")));
        summary.Children.Add(Pill("AUTO CHỈ ƯU TIÊN KHI CÙNG TREND", "#3A2C12", B("#F4C15D")));
        root.Children.Add(summary);

        var card = Card("FUNDING INTELLIGENCE — RATE / INTERVAL / FLOW / PREMIUM / 24H / STABILITY", _fundingGrid);
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(T("₣", 14, B("#61A8E8")));
        header.Children.Add(T("FUNDING", 12, B("#EAF4FF"), true));
        var item = new TabItem { Header = header, Content = root, Tag = "FUNDING" };
        var radarIndex = Math.Min(2, tabs.Items.Count);
        tabs.Items.Insert(radarIndex, item);
    }

    private static DataGrid FundingGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("HƯỚNG", "Direction", 76));
        grid.Columns.Add(C("FUNDING %", "FundingRatePercent", 92, "+0.0000;-0.0000;0.0000"));
        grid.Columns.Add(C("CHU KỲ", "FundingIntervalHours", 70, "0'H'"));
        grid.Columns.Add(C("MỖI GIỜ %", "FundingPerHourPercent", 92, "+0.0000;-0.0000;0.0000"));
        grid.Columns.Add(C("24H %", "Funding24hPercent", 88, "+0.000;-0.000;0.000"));
        grid.Columns.Add(C("BÊN TRẢ → NHẬN", "FundingFlow", 160));
        grid.Columns.Add(C("MỨC", "FundingLevel", 105));
        grid.Columns.Add(C("CÒN PHÚT", "FundingMinutesRemaining", 82));
        grid.Columns.Add(C("ỔN ĐỊNH", "FundingStability", 115));
        grid.Columns.Add(C("PREMIUM %", "PremiumPercent", 92, "+0.000;-0.000;0.000"));
        grid.Columns.Add(C("FUNDING SCORE", "FundingScore", 105));
        grid.Columns.Add(C("BIAS", "FundingBias", 130));
        grid.Columns.Add(C("CẢNH BÁO", "FundingWarning", 280));
        return grid;
    }

    private static void ApplyReadableTheme(DependencyObject root)
    {
        foreach (var grid in FindVisualChildren<DataGrid>(root))
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
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, B("#F2F7FC")));
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

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in FindVisualChildren<T>(root)) return child;
        return null;
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
