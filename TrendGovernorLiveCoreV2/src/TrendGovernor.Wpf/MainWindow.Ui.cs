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

    private readonly Canvas _chartCanvas = new() { Background = B("#06111D"), MinHeight = 320, ClipToBounds = true };
    private readonly Canvas _chartCanvasFull = new() { Background = B("#06111D"), MinHeight = 600, ClipToBounds = true };
    private readonly Canvas _equityCanvasFull = new() { Background = B("#06111D"), MinHeight = 300, ClipToBounds = true };

    private readonly TextBox _apiKey = Input();
    private readonly PasswordBox _apiSecret = new()
    {
        Height = 36,
        Background = B("#0A1A29"),
        Foreground = Brushes.White,
        BorderBrush = B("#284762"),
        Padding = new Thickness(9),
        Margin = new Thickness(0, 4, 0, 12)
    };
    private readonly TextBox _margin = Input("2");
    private readonly TextBox _leverage = Input("3");
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
        Title = "TRENDGOVERNOR PRO - PACK 15 PROFESSIONAL TERMINAL";
        Width = 1900;
        Height = 1080;
        MinWidth = 1500;
        MinHeight = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = B("#030A12");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _radarGrid.ItemsSource = _state.Markets;
        _positionsTabGrid.ItemsSource = _state.Positions;
        _ordersTabGrid.ItemsSource = _state.Orders;
        _radarGrid.SelectionChanged += (_, _) =>
        {
            if (_radarGrid.SelectedItem is MarketRow row) _ = SelectCandidateAsync(row);
        };

        Content = BuildProfessionalRoot();
        Loaded += OnLoaded;
        Closed += async (_, _) =>
        {
            try { _autoCts?.Cancel(); } catch { }
            try { await _execution.DisposeAsync(); } catch { }
            _cts.Cancel();
        };
        SizeChanged += (_, _) =>
        {
            if (_selected is not null) _ = SelectCandidateAsync(_selected);
        };
    }

    private UIElement BuildProfessionalRoot()
    {
        var root = new DockPanel { LastChildFill = true };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tabs = new TabControl
        {
            Background = B("#030A12"),
            BorderThickness = new Thickness(0),
            TabStripPlacement = Dock.Left,
            Padding = new Thickness(0)
        };
        tabs.Resources.Add(typeof(TabItem), TabStyle());
        tabs.Items.Add(Tab("VẬN HÀNH", "▦", BuildOperationsWorkspace()));
        tabs.Items.Add(Tab("RADAR", "◎", BuildRadarWorkspace()));
        tabs.Items.Add(Tab("KẾ HOẠCH & KIỂM ĐỊNH", "◆", BuildPlanVerificationWorkspace()));
        tabs.Items.Add(Tab("LỆNH & VỊ THẾ", "◈", BuildTradeWorkspace()));
        tabs.Items.Add(Tab("BIỂU ĐỒ", "⌁", BuildChartWorkspace()));
        tabs.Items.Add(Tab("RỦI RO & HIỆU SUẤT", "↗", BuildRiskPerformanceWorkspace()));
        tabs.Items.Add(Tab("CẢNH BÁO & NHẬT KÝ", "≡", BuildLogsWorkspace()));
        tabs.Items.Add(Tab("CÀI ĐẶT", "⚙", BuildSettingsWorkspace()));
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid { Height = 86, Background = B("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 12, 0, 10) };
        brand.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            Background = B("#F0A23A"),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 12, 0),
            Child = T("TG", 17, B("#071421"), true)
        });
        var name = new StackPanel();
        name.Children.Add(T("TRENDGOVERNOR PRO", 22, Brushes.White, true));
        name.Children.Add(T("BINANCE FUTURES • TỰ ĐỘNG A-Z • DỮ LIỆU THẬT", 10.5, B("#7F9AB5"), true));
        brand.Children.Add(name);
        grid.Children.Add(brand);

        var lifecycle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var stage in new[] { "PLAN", "VERIFY", "EXECUTE", "FILL", "PROTECT", "MANAGE" })
        {
            lifecycle.Children.Add(Pill(stage, "#102A40", B("#D7E8F7")));
            if (stage != "MANAGE") lifecycle.Children.Add(T("→", 13, B("#F0A23A"), true));
        }
        Grid.SetColumn(lifecycle, 1);
        grid.Children.Add(lifecycle);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        right.Children.Add(Pill("USD-M", "#15324A", Brushes.LightSkyBlue));
        right.Children.Add(Pill("USER STREAM + REST", "#153B2B", B("#66D49A")));
        right.Children.Add(_status);
        var exit = Btn("THOÁT TOOL", "#A82936", (_, _) => Close());
        exit.MinWidth = 105;
        right.Children.Add(exit);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement BuildOperationsWorkspace()
    {
        var root = WorkspaceGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3.1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.0, GridUnitType.Star) });

        var metrics = new Grid();
        for (var i = 0; i < 6; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        Metric(metrics, 0, "TỔNG TÀI SẢN", _equityMetric, "Futures wallet");
        Metric(metrics, 1, "NET PNL", _dailyPnlMetric, "Giá + funding");
        Metric(metrics, 2, "VỊ THẾ", _positionCount, "Bot + mở tay");
        Metric(metrics, 3, "RỦI RO", _sessionRiskMetric, "Exposure / equity");
        Metric(metrics, 4, "PLAN READY", _candidateCount, "Ứng viên đủ điều kiện");
        Metric(metrics, 5, "HỆ THỐNG", _statusMetric, "Data + execution");
        root.Children.Add(metrics);

        var center = new Grid();
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.78, GridUnitType.Star) });
        center.Children.Add(Card("ỨNG VIÊN ƯU TIÊN", _marketGrid));
        var chart = Card("BIỂU ĐỒ 1H • ENTRY / SL / TP", ChartPanel(_chartTitle, _chartCanvas));
        Grid.SetColumn(chart, 1);
        center.Children.Add(chart);
        var decisionStack = new StackPanel();
        decisionStack.Children.Add(Card("QUYẾT ĐỊNH", _decision));
        decisionStack.Children.Add(Card("KẾ HOẠCH HIỆN TẠI", _plan));
        decisionStack.Children.Add(Card("GATE GẦN NHẤT", _lastEntryBlocker));
        Grid.SetColumn(decisionStack, 2);
        center.Children.Add(decisionStack);
        Grid.SetRow(center, 1);
        root.Children.Add(center);

        var lower = new Grid();
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        lower.Children.Add(Card("VỊ THẾ ĐANG MỞ", _positionGrid));
        var orders = Card("LỆNH / SL / TP", _orderGrid);
        Grid.SetColumn(orders, 1);
        lower.Children.Add(orders);
        var risk = Card("RỦI RO HIỆN TẠI", _riskSummary);
        Grid.SetColumn(risk, 2);
        lower.Children.Add(risk);
        Grid.SetRow(lower, 2);
        root.Children.Add(lower);
        return root;
    }

    private UIElement BuildRadarWorkspace()
    {
        var root = WorkspaceGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.55, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });

        var toolbar = new Grid { Margin = new Thickness(4, 0, 4, 6) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(Pill("UNIVERSE 40", "#15324A", Brushes.LightSkyBlue));
        filters.Children.Add(Pill("THANH KHOẢN + BIÊN ĐỘ + TREND", "#153B2B", B("#66D49A")));
        filters.Children.Add(Pill("KHÔNG ĐUỔI GIÁ", "#3A2C12", B("#F4B942")));
        toolbar.Children.Add(filters);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Btn("LÀM MỚI", "#245EDB", (_, _) => _ = RefreshAllAsync()));
        actions.Children.Add(Btn("QUÉT THỦ CÔNG", "#275A8C", async (_, _) => await RunAutoCycleAsync(true, _cts.Token)));
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(actions);
        root.Children.Add(toolbar);

        var radarCard = Card("RADAR UNIVERSE 40 • 1D / 4H / 1H / 15M / 5M", _radarGrid);
        Grid.SetRow(radarCard, 1);
        root.Children.Add(radarCard);

        var lifecycle = Card("VÒNG ĐỜI ỨNG VIÊN • DISCOVERED → WATCHING → PLAN READY", _lifecycleGrid);
        Grid.SetRow(lifecycle, 2);
        root.Children.Add(lifecycle);
        return root;
    }

    private UIElement BuildPlanVerificationWorkspace()
    {
        var root = WorkspaceGrid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.78, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.42, GridUnitType.Star) });

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        left.Children.Add(Card("QUYẾT ĐỊNH", _decisionFull));
        var plan = Card("FROZEN PLAN • ENTRY / SL / TP / RR", _planFull);
        Grid.SetRow(plan, 1);
        left.Children.Add(plan);
        root.Children.Add(left);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.65, GridUnitType.Star) });
        right.Children.Add(Card("MA TRẬN VERIFYGRANT • PASS / BLOCK / LÝ DO", _verifyMatrixGrid));
        var lifecycle = Card("TRUY VẾT VÒNG ĐỜI", _lifecycleGrid);
        Grid.SetRow(lifecycle, 1);
        right.Children.Add(lifecycle);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        return root;
    }

    private UIElement BuildTradeWorkspace()
    {
        var root = WorkspaceGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.95, GridUnitType.Star) });
        root.Children.Add(Card("VỊ THẾ BINANCE • BOT PHIÊN HIỆN TẠI + VỊ THẾ CHỈ ĐỌC", _positionsTabGrid));
        var lower = new Grid();
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.65, GridUnitType.Star) });
        lower.Children.Add(Card("LỆNH ĐANG MỞ • ALGO SL / TP • REDUCE ONLY", _ordersTabGrid));
        var policy = new StackPanel();
        policy.Children.Add(Check("BOT phiên hiện tại mới được quản lý"));
        policy.Children.Add(Check("Vị thế tay / có sẵn / phiên cũ chỉ đọc"));
        policy.Children.Add(Check("Không đủ hard SL: đóng reduce-only"));
        policy.Children.Add(Check("Không DCA • Không hedge • Không gửi trùng"));
        var ownership = Card("QUYỀN SỞ HỮU & BẢO VỆ", policy);
        Grid.SetColumn(ownership, 1);
        lower.Children.Add(ownership);
        Grid.SetRow(lower, 1);
        root.Children.Add(lower);
        return root;
    }

    private UIElement BuildChartWorkspace()
    {
        var root = WorkspaceGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(Card("BIỂU ĐỒ NẾN BINANCE 1H • EMA20 / EMA50 • ENTRY / SL / TP", ChartPanel(_chartTitleFull, _chartCanvasFull)));
        var note = Card("BỐI CẢNH ĐA KHUNG", T("Radar quyết định hướng từ 1D / 4H / 1H; 15M / 5M chỉ xác nhận timing. Biểu đồ 1H dùng để quan sát vùng vào và cấu trúc giá hiện tại.", 12, B("#9AB4C9")));
        Grid.SetRow(note, 1);
        root.Children.Add(note);
        return root;
    }

    private UIElement BuildRiskPerformanceWorkspace()
    {
        var root = WorkspaceGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.8, GridUnitType.Star) });

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        upper.Children.Add(Card("ĐƯỜNG CONG TÀI SẢN • CHỈ DỮ LIỆU THẬT", _equityCanvasFull));
        var safety = new StackPanel();
        safety.Children.Add(Card("RỦI RO PHIÊN", _riskSummaryFull));
        var checklist = new StackPanel();
        checklist.Children.Add(Check("One-way + ISOLATED + leverage giới hạn"));
        checklist.Children.Add(Check("FILL thật mới phát hành protection"));
        checklist.Children.Add(Check("Protection phải xác minh trên Binance"));
        checklist.Children.Add(Check("Funding cực đoan được phép chặn"));
        safety.Children.Add(Card("KIỂM SOÁT AN TOÀN", checklist));
        Grid.SetColumn(safety, 1);
        upper.Children.Add(safety);
        root.Children.Add(upper);

        var lower = new Grid();
        lower.ColumnDefinitions.Add(new ColumnDefinition());
        lower.ColumnDefinitions.Add(new ColumnDefinition());
        lower.Children.Add(Card("GIAO DỊCH GẦN ĐÂY", _recentTradesFull));
        var perf = Card("NHẬT KÝ HIỆU SUẤT", _performanceLog);
        Grid.SetColumn(perf, 1);
        lower.Children.Add(perf);
        Grid.SetRow(lower, 1);
        root.Children.Add(lower);
        return root;
    }

    private UIElement BuildLogsWorkspace()
    {
        var root = WorkspaceGrid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(Card("CẢNH BÁO", _alertsFull));
        var logs = Card("NHẬT KÝ LIFECYCLE / EXECUTION", _logs);
        Grid.SetColumn(logs, 1);
        root.Children.Add(logs);
        var trades = Card("GIAO DỊCH ĐÃ XÁC NHẬN", _recentTrades);
        Grid.SetColumn(trades, 2);
        root.Children.Add(trades);
        return root;
    }

    private UIElement BuildSettingsWorkspace()
    {
        var root = WorkspaceGrid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.95, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.7, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });

        var form = new StackPanel { Margin = new Thickness(12), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Stretch };
        form.Children.Add(T("BINANCE FUTURES LIVE", 20, Brushes.White, true));
        form.Children.Add(T("API chỉ lưu cục bộ trên máy. Không dùng key có quyền rút tiền.", 11, B("#7F9AB5")));
        form.Children.Add(Label("API KEY"));
        form.Children.Add(_apiKey);
        form.Children.Add(Label("API SECRET"));
        form.Children.Add(_apiSecret);
        form.Children.Add(Label("MARGIN / LỆNH (USDT)"));
        form.Children.Add(_margin);
        form.Children.Add(Label("ĐÒN BẨY"));
        form.Children.Add(_leverage);
        form.Children.Add(Label("SỐ VỊ THẾ BOT TỐI ĐA"));
        form.Children.Add(_maxBotPositions);
        root.Children.Add(Card("CẤU HÌNH AUTO LIVE", form));

        var controls = new StackPanel { Margin = new Thickness(12) };
        controls.Children.Add(BuildAutoControlPanel());
        var controlCard = Card("ĐIỀU KHIỂN", controls);
        Grid.SetColumn(controlCard, 1);
        root.Children.Add(controlCard);

        var right = new StackPanel { Margin = new Thickness(12) };
        right.Children.Add(Card("TÀI KHOẢN", _accountFull));
        right.Children.Add(Card("TRẠNG THÁI LIVE", _statusFull));
        right.Children.Add(Card("GATE CHẶN GẦN NHẤT", _lastEntryBlocker));
        right.Children.Add(Card("QUY TRÌNH", T("Đồng bộ → Radar → Plan → Verify → Execute → Fill → Protect → Manage", 12, B("#F0A23A"), true)));
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        return root;
    }

    private UIElement ChartPanel(TextBlock title, Canvas canvas)
    {
        var panel = new DockPanel();
        var head = new Grid { Height = 34 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        var tf = T("5M   15M   1H   4H   1D", 10.5, B("#7F9AB5"), true);
        Grid.SetColumn(tf, 1);
        head.Children.Add(tf);
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);
        panel.Children.Add(canvas);
        return panel;
    }

    private UIElement BuildFooter()
    {
        var grid = new Grid { Height = 30, Background = B("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(T("TRENDGOVERNOR PRO • SINGLE STATE • LIVE DATA • WINDOWS X64", 10.5, B("#6E8BA8")));
        var right = T("PACK 15 • RADAR 40 • VERIFYGRANT • SESSION OWNERSHIP", 10.5, B("#F0A23A"), true);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static Grid WorkspaceGrid() => new()
    {
        Margin = new Thickness(8),
        Background = B("#030A12")
    };

    private static Style TabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, B("#AFC1D4")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, B("#071421")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, B("#17344D")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 13, 14, 13)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 210d));
        var trigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, B("#123556")));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        trigger.Setters.Add(new Setter(Control.BorderBrushProperty, B("#F0A23A")));
        trigger.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
        style.Triggers.Add(trigger);
        return style;
    }

    private static TabItem Tab(string name, string icon, UIElement content)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(T(icon, 14, B("#61A8E8")));
        header.Children.Add(T(name, 11.5, Brushes.White, true));
        return new TabItem { Header = header, Content = content };
    }

    private static void Metric(Grid grid, int column, string title, TextBlock value, string note)
    {
        var panel = new StackPanel();
        panel.Children.Add(T(title, 10, B("#7F9AB5"), true));
        panel.Children.Add(value);
        panel.Children.Add(T(note, 9.5, B("#58728C")));
        var card = Card(null, panel);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static Border Card(string? title, UIElement body)
    {
        var panel = new DockPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            var headingGrid = new Grid { Margin = new Thickness(8, 5, 8, 7) };
            headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            headingGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headingGrid.Children.Add(new Border { Background = B("#F0A23A"), CornerRadius = new CornerRadius(2) });
            var heading = T(title, 11.5, B("#8ED0FF"), true);
            heading.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(heading, 1);
            headingGrid.Children.Add(heading);
            DockPanel.SetDock(headingGrid, Dock.Top);
            panel.Children.Add(headingGrid);
        }
        panel.Children.Add(body);
        return new Border
        {
            Background = B("#081624"),
            BorderBrush = B("#17344D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(4),
            Padding = new Thickness(6),
            Child = panel
        };
    }

    private static DataGrid BaseGrid() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        Background = B("#06111D"),
        Foreground = Brushes.White,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HorizontalGridLinesBrush = B("#17344D"),
        VerticalGridLinesBrush = B("#17344D"),
        BorderThickness = new Thickness(0),
        RowBackground = B("#071421"),
        AlternatingRowBackground = B("#0B1C2F"),
        RowHeight = 29,
        ColumnHeaderHeight = 40,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserResizeRows = false,
        FrozenColumnCount = 1,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static DataGrid MarketGrid(bool compact)
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("HẠNG", "RadarRank", 55));
        grid.Columns.Add(C("CẶP", "Symbol", 92));
        grid.Columns.Add(C("NGUỒN CHỌN", "UniverseSource", compact ? 118 : 145));
        grid.Columns.Add(C("ĐIỂM U", "UniverseScore", 65));
        grid.Columns.Add(C("GIÁ", "Price", 100, "0.########"));
        grid.Columns.Add(C("BIÊN 24H %", "Range24hPercent", 82, "0.0"));
        grid.Columns.Add(C("HƯỚNG", "Direction", 72));
        grid.Columns.Add(C("ĐIỂM", "Score", 62));
        grid.Columns.Add(C("GIAI ĐOẠN", "CandidateStage", 126));
        grid.Columns.Add(C("ỔN ĐỊNH", "StableCycles", 72));
        grid.Columns.Add(C("TRẠNG THÁI", "Status", compact ? 150 : 175));
        grid.Columns.Add(C("1D", "Trend1D", 64));
        grid.Columns.Add(C("4H", "Trend4H", 64));
        grid.Columns.Add(C("1H", "Trend1H", 64));
        if (!compact)
        {
            grid.Columns.Add(C("ADX", "Adx1H", 60, "0.0"));
            grid.Columns.Add(C("VỊ TRÍ %", "PositionPercent", 72, "0.0"));
            grid.Columns.Add(C("ENTRY THẤP", "EntryLow", 108, "0.########"));
            grid.Columns.Add(C("ENTRY CAO", "EntryHigh", 108, "0.########"));
            grid.Columns.Add(C("SL", "StopLoss", 100, "0.########"));
            grid.Columns.Add(C("TP", "TakeProfit", 100, "0.########"));
            grid.Columns.Add(C("RR", "RiskReward", 62, "0.00"));
            grid.Columns.Add(C("FUNDING", "FundingRatePercent", 88, "+0.####;-0.####;0'%'"));
            grid.Columns.Add(C("CHU KỲ", "FundingIntervalHours", 65, "0'H'"));
            grid.Columns.Add(C("ĐÁNH GIÁ", "FundingBias", 120));
            grid.Columns.Add(C("LÝ DO", "Reason", 360));
        }
        return grid;
    }

    private static DataGrid PositionGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("NGUỒN", "Owner", 105));
        grid.Columns.Add(C("HƯỚNG", "Side", 72));
        grid.Columns.Add(C("KHỐI LƯỢNG", "Quantity", 100, "0.########"));
        grid.Columns.Add(C("GIÁ VÀO", "EntryPrice", 108, "0.########"));
        grid.Columns.Add(C("MARK", "MarkPrice", 108, "0.########"));
        grid.Columns.Add(C("PNL USDT", "UnrealizedPnl", 95, "+0.00;-0.00;0.00"));
        grid.Columns.Add(C("NET PNL", "NetPnlAfterFunding", 95, "+0.00;-0.00;0.00"));
        grid.Columns.Add(C("PEAK", "PeakNetPnl", 90, "+0.00;-0.00;0.00"));
        grid.Columns.Add(C("GIVEBACK %", "GivebackPercent", 88, "0.0"));
        grid.Columns.Add(C("BẢO VỆ", "Protection", 135));
        grid.Columns.Add(C("SL", "StopLossConfirmed", 70));
        grid.Columns.Add(C("TP", "TakeProfitConfirmed", 70));
        grid.Columns.Add(C("KHUYẾN NGHỊ", "Recommendation", 210));
        return grid;
    }

    private static DataGrid OrderGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 92));
        grid.Columns.Add(C("NGUỒN", "Source", 72));
        grid.Columns.Add(C("LOẠI", "Type", 135));
        grid.Columns.Add(C("SIDE", "Side", 66));
        grid.Columns.Add(C("GIÁ", "Price", 95, "0.########"));
        grid.Columns.Add(C("GIÁ KÍCH HOẠT", "StopPrice", 118, "0.########"));
        grid.Columns.Add(C("QTY", "Quantity", 95, "0.########"));
        grid.Columns.Add(C("TRẠNG THÁI", "Status", 100));
        grid.Columns.Add(C("GIẢM VỊ THẾ", "ReduceOnly", 98));
        grid.Columns.Add(C("CLIENT ID", "ClientOrderId", 230));
        return grid;
    }

    private static DataGridTextColumn C(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path);
        if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
        return new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            Width = width,
            MinWidth = Math.Min(width, 55),
            ElementStyle = CellTextStyle()
        };
    }

    private static Style CellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, B("#EAF4FF")));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(2, 0, 2, 0)));
        return style;
    }

    private static TextBox Input(string value = "") => new()
    {
        Text = value,
        Height = 36,
        Margin = new Thickness(0, 4, 0, 12),
        Background = B("#0A1A29"),
        Foreground = Brushes.White,
        BorderBrush = B("#284762"),
        Padding = new Thickness(9)
    };

    private static TextBox LogBox() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = B("#06111D"),
        Foreground = B("#AFC1D4"),
        BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 11.5,
        Padding = new Thickness(8)
    };

    private static TextBlock Label(string value) => T(value, 10.5, B("#AFC1D4"), true);
    private static TextBlock Check(string value) => T("✓  " + value, 12.5, B("#66D49A"));
    private static Border Pill(string value, string background, Brush foreground) => new()
    {
        Background = B(background),
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(5),
        Padding = new Thickness(9, 5, 9, 5),
        Child = T(value, 10.5, foreground, true)
    };

    private static TextBlock T(string value, double size, Brush color, bool bold = false) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = color,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(6, 2, 6, 2),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button Btn(string value, string background, RoutedEventHandler? click = null)
    {
        var button = new Button
        {
            Content = value,
            Background = B(background),
            Foreground = Brushes.White,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(5),
            BorderBrush = B("#31516C"),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            MinHeight = 36
        };
        if (click is not null) button.Click += click;
        return button;
    }

    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
