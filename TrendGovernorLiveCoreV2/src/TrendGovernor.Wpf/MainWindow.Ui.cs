using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow : Window
{
    private readonly DashboardState _state = new();
    private readonly BinanceClient _binance = new();
    private readonly BinanceCandleService _candles = new();
    private readonly CancellationTokenSource _cts = new();

    private readonly DataGrid _marketGrid = MarketGrid(true);
    private readonly DataGrid _radarGrid = MarketGrid(false);
    private readonly DataGrid _positionGrid = PositionGrid();
    private readonly DataGrid _orderGrid = OrderGrid();
    private readonly DataGrid _positionsTabGrid = PositionGrid();
    private readonly DataGrid _ordersTabGrid = OrderGrid();

    private readonly TextBlock _status = T("ĐANG KHỞI ĐỘNG", 12, Brushes.Gold, true);
    private readonly TextBlock _statusMetric = T("ĐANG KHỞI ĐỘNG", 17, Brushes.Gold, true);
    private readonly TextBlock _account = T("CHƯA KẾT NỐI API", 13, Brushes.LightGray);
    private readonly TextBlock _decision = T("WAIT", 28, Brushes.Gold, true);
    private readonly TextBlock _plan = T("CHƯA CÓ KẾ HOẠCH", 13, Brushes.LightGray);
    private readonly TextBlock _riskSummary = T("Chưa có dữ liệu tài khoản.", 13, Brushes.LightGray);
    private readonly TextBlock _marketCount = T("0", 22, Brushes.White, true);
    private readonly TextBlock _candidateCount = T("0", 22, Brushes.Gold, true);
    private readonly TextBlock _positionCount = T("0", 22, Brushes.White, true);
    private readonly TextBlock _equityMetric = T("-- USDT", 22, Brushes.White, true);
    private readonly TextBlock _dailyPnlMetric = T("-- USDT", 22, Brushes.LightGreen, true);
    private readonly TextBlock _sessionRiskMetric = T("0.00%", 22, Brushes.Gold, true);
    private readonly TextBlock _chartTitle = T("CHƯA CHỌN CẶP", 13, Brushes.White, true);

    private readonly TextBlock _decisionFull = T("WAIT", 32, Brushes.Gold, true);
    private readonly TextBlock _planFull = T("CHƯA CÓ KẾ HOẠCH", 14, Brushes.LightGray);
    private readonly TextBlock _riskSummaryFull = T("Chưa có dữ liệu tài khoản.", 14, Brushes.LightGray);
    private readonly TextBlock _accountFull = T("CHƯA KẾT NỐI API", 14, Brushes.LightGray);
    private readonly TextBlock _statusFull = T("ĐANG KHỞI ĐỘNG", 18, Brushes.Gold, true);
    private readonly TextBlock _chartTitleFull = T("CHƯA CHỌN CẶP", 14, Brushes.White, true);

    private readonly Canvas _chartCanvas = new() { Background = B("#071421"), MinHeight = 320, ClipToBounds = true };
    private readonly Canvas _chartCanvasFull = new() { Background = B("#071421"), MinHeight = 600, ClipToBounds = true };
    private readonly Canvas _equityCanvasFull = new() { Background = B("#071421"), MinHeight = 300, ClipToBounds = true };

    private readonly TextBox _apiKey = Input();
    private readonly PasswordBox _apiSecret = new() { Height = 34, Background = B("#0C1B2B"), Foreground = Brushes.White, BorderBrush = B("#284762"), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 12) };
    private readonly TextBox _margin = Input("2");
    private readonly TextBox _leverage = Input("3");

    // Legacy fields are retained only so the old manual path compiles; they are not exposed in the live workflow.
    private readonly CheckBox _autoLive = new() { Visibility = Visibility.Collapsed, IsChecked = false };
    private readonly Button _armButton = Btn("LEGACY LIVE", "#333333");

    private readonly TextBox _logs = LogBox();
    private readonly TextBox _logPreview = LogBox();
    private readonly TextBox _alerts = LogBox();
    private readonly TextBox _alertsFull = LogBox();
    private readonly TextBox _recentTrades = LogBox();
    private readonly TextBox _recentTradesFull = LogBox();
    private readonly TextBox _performanceLog = LogBox();

    private bool _liveArmed;
    private MarketRow? _selected;

    public MainWindow()
    {
        Title = "TRENDGOVERNOR LIVE CORE V2 - AUTO LIVE BUILD 09";
        Width = 1780;
        Height = 1040;
        MinWidth = 1440;
        MinHeight = 860;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = B("#040C16");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _radarGrid.ItemsSource = _state.Markets;
        _positionsTabGrid.ItemsSource = _state.Positions;
        _ordersTabGrid.ItemsSource = _state.Orders;
        _radarGrid.SelectionChanged += (_, _) => { if (_radarGrid.SelectedItem is MarketRow row) _ = SelectCandidateAsync(row); };

        Content = Root();
        Loaded += OnLoaded;
        Closed += async (_, _) =>
        {
            try { _autoCts?.Cancel(); } catch { }
            try { await _execution.DisposeAsync(); } catch { }
            _cts.Cancel();
        };
        SizeChanged += (_, _) => { if (_selected != null) _ = SelectCandidateAsync(_selected); };
    }

    private UIElement Root()
    {
        var root = new DockPanel();
        var header = Header(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = Footer(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var tabs = new TabControl { Background = B("#040C16"), BorderThickness = new Thickness(0), TabStripPlacement = Dock.Left };
        tabs.Resources.Add(typeof(TabItem), TabStyle());
        tabs.Items.Add(Tab("TỔNG QUAN", "▦", Overview()));
        tabs.Items.Add(Tab("RADAR", "◎", Radar()));
        tabs.Items.Add(Tab("BIỂU ĐỒ", "⌁", FullChartWorkspace()));
        tabs.Items.Add(Tab("KẾ HOẠCH", "◆", FullDecisionWorkspace()));
        tabs.Items.Add(Tab("VỊ THẾ", "◈", Positions()));
        tabs.Items.Add(Tab("RỦI RO", "⬡", FullRiskWorkspace()));
        tabs.Items.Add(Tab("HIỆU SUẤT", "↗", PerformanceWorkspace()));
        tabs.Items.Add(Tab("CẢNH BÁO", "△", Card("CẢNH BÁO HỆ THỐNG / THỊ TRƯỜNG / VỊ THẾ", _alertsFull)));
        tabs.Items.Add(Tab("API & AUTO LIVE", "⚙", Api()));
        tabs.Items.Add(Tab("NHẬT KÝ", "≡", Card("NHẬT KÝ PLAN / VERIFY / EXECUTE / FILL / PROTECT / MANAGE", _logs)));
        root.Children.Add(tabs);
        return root;
    }

    private UIElement Header()
    {
        var grid = new Grid { Height = 76, Background = B("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 10, 0, 8) };
        left.Children.Add(T("◆", 24, B("#F4B942"), true));
        var names = new StackPanel();
        names.Children.Add(T("TRENDGOVERNOR LIVE CORE V2", 22, Brushes.White, true));
        names.Children.Add(T("PLAN → VERIFY → EXECUTE → FILL → PROTECT → MANAGE", 11, B("#6E8BA8")));
        left.Children.Add(names); grid.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        right.Children.Add(Pill("BINANCE FUTURES USD-M", "#15324A", Brushes.LightSkyBlue));
        right.Children.Add(Pill("USER STREAM + REST", "#153B2B", B("#66D49A")));
        right.Children.Add(_status);
        right.Children.Add(_autoState);
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    private UIElement Overview()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3.2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.0, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.5, GridUnitType.Star) });

        var metrics = new Grid();
        for (var i = 0; i < 6; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        Metric(metrics, 0, "TỔNG TÀI SẢN", _equityMetric, "Tài khoản Futures");
        Metric(metrics, 1, "PNL ĐANG MỞ", _dailyPnlMetric, "Dữ liệu Binance thật");
        Metric(metrics, 2, "VỊ THẾ", _positionCount, "Bot + vị thế tay");
        Metric(metrics, 3, "RỦI RO PHIÊN", _sessionRiskMetric, "Exposure / equity");
        Metric(metrics, 4, "ỨNG VIÊN", _candidateCount, "Ổn định nhiều chu kỳ");
        Metric(metrics, 5, "SỨC KHỎE HỆ THỐNG", _statusMetric, "Data + radar + execution");
        root.Children.Add(metrics);

        var center = new Grid();
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        center.Children.Add(Card("RADAR — TREND DÀI / SÓNG LỚN", _marketGrid));
        var chart = Card("BIỂU ĐỒ NẾN 1H — EMA / ENTRY / SL / TP", ChartPanel(_chartTitle, _chartCanvas)); Grid.SetColumn(chart, 1); center.Children.Add(chart);
        var side = new StackPanel(); side.Children.Add(Card("QUYẾT ĐỊNH", _decision)); side.Children.Add(Card("KẾ HOẠCH ENTRY / SL / TP", _plan)); side.Children.Add(Card("AUTO LIVE", BuildAutoControlPanel())); Grid.SetColumn(side, 2); center.Children.Add(side);
        Grid.SetRow(center, 1); root.Children.Add(center);

        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition());
        middle.ColumnDefinitions.Add(new ColumnDefinition());
        middle.Children.Add(Card("VỊ THẾ ĐANG MỞ", _positionGrid));
        var orders = Card("LỆNH / SL / TP", _orderGrid); Grid.SetColumn(orders, 1); middle.Children.Add(orders);
        var risk = Card("RỦI RO HIỆN TẠI", _riskSummary); Grid.SetColumn(risk, 2); middle.Children.Add(risk);
        Grid.SetRow(middle, 2); root.Children.Add(middle);

        var bottom = new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.Children.Add(Card("CẢNH BÁO", _alerts));
        var trades = Card("GIAO DỊCH GẦN ĐÂY", _recentTrades); Grid.SetColumn(trades, 1); bottom.Children.Add(trades);
        var logs = Card("NHẬT KÝ HỆ THỐNG", _logPreview); Grid.SetColumn(logs, 2); bottom.Children.Add(logs);
        Grid.SetRow(bottom, 3); root.Children.Add(bottom);
        return root;
    }

    private UIElement Radar()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Btn("QUÉT THỦ CÔNG", "#275A8C", async (_, _) => await RunAutoCycleAsync(true, _cts.Token)));
        actions.Children.Add(Pill("AUTO KHÔNG CẦN CLICK COIN", "#153B2B", B("#5ED99C")));
        actions.Children.Add(Pill("KHÔNG ĐUỔI GIÁ / KHÔNG MUA ĐỈNH / KHÔNG BÁN ĐÁY", "#3A2C12", B("#F4B942")));
        grid.Children.Add(actions);
        var card = Card("RADAR SCANNER — 1D / 4H / 1H / 15M / 5M / ADX / WAVE / RR / STABILITY", _radarGrid); Grid.SetRow(card, 1); grid.Children.Add(card);
        return grid;
    }

    private UIElement FullChartWorkspace() => Card("BIỂU ĐỒ NẾN BINANCE 1H", ChartPanel(_chartTitleFull, _chartCanvasFull));

    private UIElement FullDecisionWorkspace()
    {
        var grid = new Grid { Margin = new Thickness(10) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Card("QUYẾT ĐỊNH", _decisionFull));
        var plan = Card("FROZEN ENTRY / SL / TP PLAN + VERIFY", _planFull); Grid.SetColumn(plan, 1); grid.Children.Add(plan);
        return grid;
    }

    private UIElement Positions()
    {
        var grid = new Grid { Margin = new Thickness(10) }; grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(Card("VỊ THẾ BINANCE — BOT + MỞ TAY", _positionsTabGrid));
        var orders = Card("LỆNH ĐANG MỞ / SL / TP REDUCE-ONLY", _ordersTabGrid); Grid.SetRow(orders, 1); grid.Children.Add(orders);
        return grid;
    }

    private UIElement FullRiskWorkspace()
    {
        var grid = new Grid { Margin = new Thickness(10) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Card("RỦI RO PHIÊN", _riskSummaryFull));
        var safety = new StackPanel();
        safety.Children.Add(Check("Một vị thế tối đa theo cấu hình"));
        safety.Children.Add(Check("One-way + ISOLATED + leverage giới hạn"));
        safety.Children.Add(Check("FILL thật mới phát hành SL/TP algo"));
        safety.Children.Add(Check("Không đủ SL/TP: đóng reduce-only và khóa lệnh mới"));
        safety.Children.Add(Check("Không DCA, không hedge, không gửi trùng"));
        safety.Children.Add(Check("Không đuổi giá, không mua đỉnh, không bán đáy"));
        var right = Card("KIỂM SOÁT AN TOÀN", safety); Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    private UIElement PerformanceWorkspace()
    {
        var grid = new Grid { Margin = new Thickness(10) }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(Card("ĐƯỜNG CONG TÀI SẢN — CHỈ DỮ LIỆU THẬT", _equityCanvasFull));
        var lower = new Grid(); lower.ColumnDefinitions.Add(new ColumnDefinition()); lower.ColumnDefinitions.Add(new ColumnDefinition());
        lower.Children.Add(Card("GIAO DỊCH GẦN ĐÂY", _recentTradesFull));
        var perf = Card("NHẬT KÝ HIỆU SUẤT", _performanceLog); Grid.SetColumn(perf, 1); lower.Children.Add(perf); Grid.SetRow(lower, 1); grid.Children.Add(lower);
        return grid;
    }

    private UIElement Api()
    {
        var grid = new Grid { Margin = new Thickness(18) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var form = new StackPanel { Margin = new Thickness(16), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(T("API BINANCE FUTURES", 22, Brushes.White, true));
        form.Children.Add(T("Nhập API một lần trong phiên, sau đó bấm BẮT ĐẦU AUTO LIVE một lần.", 12, B("#7F9AB5")));
        form.Children.Add(Label("API KEY")); form.Children.Add(_apiKey);
        form.Children.Add(Label("API SECRET")); form.Children.Add(_apiSecret);
        form.Children.Add(Label("MARGIN / LỆNH (USDT)")); form.Children.Add(_margin);
        form.Children.Add(Label("ĐÒN BẨY")); form.Children.Add(_leverage);
        form.Children.Add(BuildAutoControlPanel());
        grid.Children.Add(Card("CẤU HÌNH AUTO LIVE", form));
        var right = new StackPanel { Margin = new Thickness(16) };
        right.Children.Add(Card("TÀI KHOẢN", _accountFull));
        right.Children.Add(Card("TRẠNG THÁI LIVE", _statusFull));
        right.Children.Add(Card("QUY TRÌNH", T("Đồng bộ → Radar → Plan → Verify → Execute → Fill → Protect → Manage → Radar", 13, B("#F4B942"))));
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    private UIElement ChartPanel(TextBlock title, Canvas canvas)
    {
        var panel = new DockPanel();
        var head = new Grid { Height = 32 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        var tf = T("5m   15m   1H   4H   1D", 11, B("#6E8BA8"), true); Grid.SetColumn(tf, 1); head.Children.Add(tf);
        DockPanel.SetDock(head, Dock.Top); panel.Children.Add(head); panel.Children.Add(canvas); return panel;
    }

    private UIElement Footer()
    {
        var grid = new Grid { Height = 30, Background = B("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(T("TRENDGOVERNOR • SINGLE STATE • LIVE DATA • WINDOWS X64", 11, B("#6E8BA8")));
        var right = T("AUTO xuyên suốt • FILL thật • SL/TP algo xác minh", 11, B("#F4B942")); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }

    private static Style TabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, B("#AFC1D4")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, B("#071421")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 168d));
        var trigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, B("#163A61")));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(trigger);
        return style;
    }

    private static TabItem Tab(string name, string icon, UIElement content)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(T(icon, 14, B("#61A8E8"))); header.Children.Add(T(name, 12, Brushes.White, true));
        return new TabItem { Header = header, Content = content };
    }

    private static void Metric(Grid grid, int column, string title, TextBlock value, string note)
    {
        var panel = new StackPanel(); panel.Children.Add(T(title, 10, B("#7F9AB5"), true)); panel.Children.Add(value); panel.Children.Add(T(note, 10, B("#58728C")));
        var card = Card(null, panel); Grid.SetColumn(card, column); grid.Children.Add(card);
    }

    private static Border Card(string? title, UIElement body)
    {
        var panel = new DockPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            var heading = T(title, 12, B("#7FC2F2"), true); heading.Margin = new Thickness(8, 6, 8, 8); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        }
        panel.Children.Add(body);
        return new Border { Background = B("#091827"), BorderBrush = B("#17344D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(4), Padding = new Thickness(6), Child = panel };
    }

    private static DataGrid BaseGrid() => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
        Background = B("#071421"), Foreground = Brushes.White, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = B("#17344D"), BorderThickness = new Thickness(0), RowBackground = B("#071421"), AlternatingRowBackground = B("#0B1C2F"),
        RowHeight = 27, ColumnHeaderHeight = 30, CanUserAddRows = false, CanUserDeleteRows = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static DataGrid MarketGrid(bool compact)
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 92));
        grid.Columns.Add(C("GIÁ", "Price", 90, "0.########"));
        grid.Columns.Add(C("HƯỚNG", "Direction", 70));
        grid.Columns.Add(C("ĐIỂM", "Score", 58));
        grid.Columns.Add(C("TREND", "TrendScore", 62));
        grid.Columns.Add(C("SÓNG", "WaveScore", 58));
        grid.Columns.Add(C("ỔN ĐỊNH", "StableCycles", 66));
        grid.Columns.Add(C("TRẠNG THÁI", "Status", compact ? 150 : 180));
        if (!compact)
        {
            grid.Columns.Add(C("1D", "Trend1D", 70)); grid.Columns.Add(C("4H", "Trend4H", 70)); grid.Columns.Add(C("1H", "Trend1H", 70));
            grid.Columns.Add(C("ADX", "Adx1H", 58, "0.0")); grid.Columns.Add(C("VỊ TRÍ %", "PositionPercent", 70, "0.0"));
            grid.Columns.Add(C("ENTRY THẤP", "EntryLow", 105, "0.########")); grid.Columns.Add(C("ENTRY CAO", "EntryHigh", 105, "0.########"));
            grid.Columns.Add(C("SL", "StopLoss", 100, "0.########")); grid.Columns.Add(C("TP", "TakeProfit", 100, "0.########")); grid.Columns.Add(C("RR", "RiskReward", 60, "0.00"));
            grid.Columns.Add(C("LÝ DO", "Reason", 300));
        }
        return grid;
    }

    private static DataGrid PositionGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 100)); grid.Columns.Add(C("HƯỚNG", "Side", 76)); grid.Columns.Add(C("KHỐI LƯỢNG", "Quantity", 100, "0.########"));
        grid.Columns.Add(C("GIÁ VÀO", "EntryPrice", 110, "0.########")); grid.Columns.Add(C("MARK", "MarkPrice", 110, "0.########"));
        grid.Columns.Add(C("PNL USDT", "UnrealizedPnl", 100, "+0.00;-0.00;0.00")); grid.Columns.Add(C("PEAK", "PeakUnrealizedPnl", 90, "+0.00;-0.00;0.00"));
        grid.Columns.Add(C("GIVEBACK %", "GivebackPercent", 90, "0.0")); grid.Columns.Add(C("BẢO VỆ", "Protection", 130)); grid.Columns.Add(C("KHUYẾN NGHỊ", "Recommendation", 190));
        return grid;
    }

    private static DataGrid OrderGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 92)); grid.Columns.Add(C("LOẠI", "Type", 130)); grid.Columns.Add(C("SIDE", "Side", 65));
        grid.Columns.Add(C("GIÁ", "Price", 90, "0.########")); grid.Columns.Add(C("GIÁ KÍCH HOẠT", "StopPrice", 115, "0.########"));
        grid.Columns.Add(C("QTY", "Quantity", 90, "0.########")); grid.Columns.Add(C("TRẠNG THÁI", "Status", 95)); grid.Columns.Add(C("GIẢM VỊ THẾ", "ReduceOnly", 95));
        return grid;
    }

    private static DataGridTextColumn C(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path); if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
        return new DataGridTextColumn { Header = header, Binding = binding, Width = width };
    }

    private static TextBox Input(string value = "") => new() { Text = value, Height = 34, Margin = new Thickness(0, 4, 0, 12), Background = B("#0C1B2B"), Foreground = Brushes.White, BorderBrush = B("#284762"), Padding = new Thickness(8) };
    private static TextBox LogBox() => new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = B("#071421"), Foreground = B("#AFC1D4"), BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8) };
    private static TextBlock Label(string value) => T(value, 11, B("#AFC1D4"), true);
    private static TextBlock Check(string value) => T("✓  " + value, 13, B("#66D49A"));
    private static Border Pill(string value, string background, Brush foreground) => new() { Background = B(background), CornerRadius = new CornerRadius(4), Margin = new Thickness(6), Padding = new Thickness(10, 6, 10, 6), Child = T(value, 11, foreground, true) };
    private static TextBlock T(string value, double size, Brush color, bool bold = false) => new() { Text = value, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(6, 2, 6, 2), TextWrapping = TextWrapping.Wrap };
    private static Button Btn(string value, string background, RoutedEventHandler? click = null) { var button = new Button { Content = value, Background = B(background), Foreground = Brushes.White, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(6), BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 34 }; if (click != null) button.Click += click; return button; }
    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
