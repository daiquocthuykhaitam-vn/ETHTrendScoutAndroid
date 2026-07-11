using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow : Window
{
    private readonly DashboardState _state = new();
    private readonly BinanceClient _binance = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DataGrid _marketGrid = GridFor<MarketRow>();
    private readonly DataGrid _positionGrid = GridFor<PositionRow>();
    private readonly DataGrid _orderGrid = GridFor<OrderRow>();
    private readonly TextBlock _status = Text("ĐANG KHỞI ĐỘNG", 14, Brushes.Gold);
    private readonly TextBlock _account = Text("TÀI KHOẢN: CHƯA KẾT NỐI", 14, Brushes.LightGray);
    private readonly TextBlock _decision = Text("WAIT", 28, Brushes.Gold);
    private readonly TextBlock _plan = Text("CHƯA CÓ KẾ HOẠCH", 14, Brushes.LightGray);
    private readonly TextBox _apiKey = new();
    private readonly PasswordBox _apiSecret = new();
    private readonly TextBox _margin = new() { Text = "2" };
    private readonly TextBox _leverage = new() { Text = "3" };
    private readonly CheckBox _autoLive = new() { Content = "AUTO LỆNH LIVE", Foreground = Brushes.White };
    private readonly Button _armButton = Button("BẬT QUYỀN LIVE", Brushes.DarkRed);
    private readonly Button _executeButton = Button("GỬI LỆNH ĐÃ CHỌN", Brushes.DarkOrange);
    private readonly TextBox _logs = new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#07111D"), Foreground = Brushes.LightGray, BorderThickness = new Thickness(0) };
    private bool _liveArmed;
    private MarketRow? _selected;

    public MainWindow()
    {
        Title = "TRENDGOVERNOR LIVE CORE V2 - BUILD 04";
        Width = 1680; Height = 980; MinWidth = 1280; MinHeight = 760;
        Background = Brush("#06101C"); Foreground = Brushes.White;
        Content = BuildRoot();
        Loaded += OnLoaded;
        Closed += (_, _) => _cts.Cancel();
    }

    private UIElement BuildRoot()
    {
        var root = new DockPanel();
        var header = Header(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = StatusBar(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Brush("#06101C"), BorderThickness = new Thickness(0) };
        tabs.Items.Add(Tab("TỔNG QUAN", Overview()));
        tabs.Items.Add(Tab("RADAR", Radar()));
        tabs.Items.Add(Tab("VỊ THẾ & LỆNH", Positions()));
        tabs.Items.Add(Tab("API & LIVE", ApiPanel()));
        tabs.Items.Add(Tab("NHẬT KÝ", _logs));
        root.Children.Add(tabs);
        return root;
    }

    private UIElement Header()
    {
        var g = new Grid { Height = 68, Background = Brush("#081525"), Margin = new Thickness(0,0,0,2) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text("TRENDGOVERNOR LIVE CORE V2", 24, Brushes.White); title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(18,18,0,0); g.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,14,18,0) };
        right.Children.Add(_status); right.Children.Add(Button("LÀM MỚI", Brushes.RoyalBlue, (_,_) => _ = RefreshAllAsync()));
        Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }

    private UIElement Overview()
    {
        var g = new Grid { Margin = new Thickness(12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var market = Card("RADAR - TOP CƠ HỘI", _marketGrid); g.Children.Add(market);
        var right = new StackPanel(); right.Children.Add(Card("QUYẾT ĐỊNH", _decision)); right.Children.Add(Card("KẾ HOẠCH VÀO", _plan)); right.Children.Add(Card("TÀI KHOẢN", _account));
        Grid.SetColumn(right,1); g.Children.Add(right);
        var pos = Card("VỊ THẾ ĐANG MỞ", _positionGrid); Grid.SetRow(pos,1); g.Children.Add(pos);
        var orders = Card("LỆNH / SL / TP", _orderGrid); Grid.SetRow(orders,1); Grid.SetColumn(orders,1); g.Children.Add(orders);
        return g;
    }

    private UIElement Radar()
    {
        var g = new Grid { Margin = new Thickness(12) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("QUÉT RADAR", Brushes.RoyalBlue, (_,_) => _ = ScanAsync()));
        actions.Children.Add(_executeButton); _executeButton.Click += async (_,_) => await ExecuteSelectedAsync();
        g.Children.Add(actions); Grid.SetRow(_marketGrid,1); g.Children.Add(_marketGrid); return g;
    }

    private UIElement Positions()
    {
        var g = new Grid { Margin = new Thickness(12) };
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(Card("VỊ THẾ BINANCE", _positionGrid)); var o = Card("LỆNH ĐANG MỞ", _orderGrid); Grid.SetRow(o,1); g.Children.Add(o); return g;
    }

    private UIElement ApiPanel()
    {
        var p = new StackPanel { Margin = new Thickness(24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Text("API BINANCE FUTURES", 22, Brushes.White));
        p.Children.Add(Label("API KEY")); p.Children.Add(_apiKey);
        p.Children.Add(Label("API SECRET")); p.Children.Add(_apiSecret);
        p.Children.Add(Label("MARGIN / LỆNH (USDT)")); p.Children.Add(_margin);
        p.Children.Add(Label("ĐÒN BẨY")); p.Children.Add(_leverage);
        p.Children.Add(Button("KẾT NỐI TÀI KHOẢN", Brushes.SeaGreen, async (_,_) => await ConnectAccountAsync()));
        p.Children.Add(_autoLive); p.Children.Add(_armButton); _armButton.Click += ArmLive;
        var note = Text("LIVE chỉ mở sau khi nhập API, xác nhận tài khoản và bấm BẬT QUYỀN LIVE. Tool không lưu API Secret vào source.", 13, Brushes.Orange); note.TextWrapping = TextWrapping.Wrap; p.Children.Add(note);
        return p;
    }

    private UIElement StatusBar()
    {
        var b = new Border { Background = Brush("#081525"), Height = 30, Padding = new Thickness(12,6,12,0) };
        b.Child = Text("PLAN → VERIFY → EXECUTE → FILL → PROTECT → MANAGE", 12, Brushes.LightSkyBlue); return b;
    }

    private static TabItem Tab(string header, UIElement body) => new() { Header = header, Content = body };
    private static Border Card(string title, UIElement body)
    {
        var p = new DockPanel(); var h = Text(title, 13, Brushes.LightSkyBlue); h.FontWeight = FontWeights.Bold; h.Margin = new Thickness(8); DockPanel.SetDock(h,Dock.Top); p.Children.Add(h); p.Children.Add(body);
        return new Border { Background = Brush("#0A192A"), BorderBrush = Brush("#1C3854"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(5), Padding = new Thickness(6), Child = p };
    }
    private static DataGrid GridFor<T>() => new() { AutoGenerateColumns = true, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Background = Brush("#081525"), Foreground = Brushes.White, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0), RowBackground = Brush("#081525"), AlternatingRowBackground = Brush("#0B1C2F") };
    private static TextBlock Text(string s, double size, Brush color) => new() { Text = s, FontSize = size, Foreground = color, Margin = new Thickness(8) };
    private static TextBlock Label(string s) => Text(s, 12, Brushes.LightGray);
    private static Button Button(string s, Brush color, RoutedEventHandler? click = null) { var b = new Button { Content = s, Background = color, Foreground = Brushes.White, Padding = new Thickness(14,8,14,8), Margin = new Thickness(8), BorderThickness = new Thickness(0) }; if (click != null) b.Click += click; return b; }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
