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

    private readonly DataGrid _marketGrid = MarketGrid(compact: true);
    private readonly DataGrid _positionGrid = PositionGrid();
    private readonly DataGrid _orderGrid = OrderGrid();
    private readonly DataGrid _radarGrid = MarketGrid(compact: false);
    private readonly DataGrid _positionsTabGrid = PositionGrid();
    private readonly DataGrid _ordersTabGrid = OrderGrid();

    private readonly TextBlock _status = ValueText("ĐANG KHỞI ĐỘNG", 13, Brushes.Gold);
    private readonly TextBlock _account = ValueText("CHƯA KẾT NỐI API", 13, Brushes.LightGray);
    private readonly TextBlock _decision = ValueText("WAIT", 30, Brushes.Gold);
    private readonly TextBlock _plan = ValueText("CHƯA CÓ KẾ HOẠCH", 13, Brushes.LightGray);
    private readonly TextBlock _marketCount = ValueText("0", 24, Brushes.White);
    private readonly TextBlock _candidateCount = ValueText("0", 24, Brushes.Gold);
    private readonly TextBlock _selectedSymbol = ValueText("--", 24, Brushes.White);
    private readonly TextBlock _positionCount = ValueText("0", 24, Brushes.White);
    private readonly TextBlock _orderCount = ValueText("0", 24, Brushes.White);
    private readonly TextBlock _chartTitle = ValueText("CHƯA CHỌN CẶP", 14, Brushes.White);
    private readonly Canvas _chartCanvas = new() { Background = Brush("#071421"), MinHeight = 300, ClipToBounds = true };

    private readonly TextBox _apiKey = InputBox();
    private readonly PasswordBox _apiSecret = new() { Height = 34, Margin = new Thickness(0, 4, 0, 12), Background = Brush("#0C1B2B"), Foreground = Brushes.White, BorderBrush = Brush("#284762"), Padding = new Thickness(8) };
    private readonly TextBox _margin = InputBox("2");
    private readonly TextBox _leverage = InputBox("3");
    private readonly CheckBox _autoLive = new() { Content = "AUTO LỆNH LIVE", Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 8) };
    private readonly Button _armButton = ActionButton("BẬT QUYỀN LIVE", "#9B1C31");
    private readonly Button _executeButton = ActionButton("GỬI LỆNH ĐÃ CHỌN", "#B66A00");
    private readonly TextBox _logs = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brush("#071421"),
        Foreground = Brush("#AFC1D4"),
        BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Padding = new Thickness(10)
    };

    private bool _liveArmed;
    private MarketRow? _selected;

    public MainWindow()
    {
        Title = "TRENDGOVERNOR LIVE CORE V2 - TERMINAL BUILD 07";
        Width = 1720;
        Height = 1000;
        MinWidth = 1360;
        MinHeight = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#040C16");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _radarGrid.ItemsSource = _state.Markets;
        _positionsTabGrid.ItemsSource = _state.Positions;
        _ordersTabGrid.ItemsSource = _state.Orders;
        _radarGrid.SelectionChanged += (_, _) =>
        {
            if (_radarGrid.SelectedItem is MarketRow row)
            {
                _marketGrid.SelectedItem = row;
                SelectCandidate(row);
            }
        };

        _executeButton.Click += async (_, _) => await ExecuteSelectedAsync();
        _armButton.Click += ArmLive;

        Content = BuildRoot();
        Loaded += OnLoaded;
        Closed += (_, _) => _cts.Cancel();
        SizeChanged += (_, _) => { if (_selected != null) RenderPlanChart(_selected); };
    }

    private UIElement BuildRoot()
    {
        var root = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tabs = new TabControl
        {
            Background = Brush("#040C16"),
            BorderThickness = new Thickness(0),
            TabStripPlacement = Dock.Left,
            Padding = new Thickness(0)
        };
        tabs.Resources.Add(typeof(TabItem), BuildTabStyle());
        tabs.Items.Add(Tab("TỔNG QUAN", "▦", BuildOverview()));
        tabs.Items.Add(Tab("RADAR", "◎", BuildRadar()));
        tabs.Items.Add(Tab("VỊ THẾ", "◈", BuildPositions()));
        tabs.Items.Add(Tab("API & LIVE", "⚙", BuildApiPanel()));
        tabs.Items.Add(Tab("NHẬT KÝ", "≡", BuildLogs()));
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid { Height = 72, Background = Brush("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 12, 0, 8) };
        titlePanel.Children.Add(ValueText("◆", 24, Brush("#F4B942")));
        var names = new StackPanel();
        names.Children.Add(ValueText("TRENDGOVERNOR LIVE CORE V2", 22, Brushes.White, bold: true));
        names.Children.Add(ValueText("PLAN → VERIFY → EXECUTE → FILL → PROTECT → MANAGE", 11, Brush("#6E8BA8")));
        titlePanel.Children.Add(names);
        grid.Children.Add(titlePanel);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        right.Children.Add(StatusPill("BINANCE USD-M", "#15324A", Brushes.LightSkyBlue));
        right.Children.Add(_status);
        right.Children.Add(ActionButton("LÀM MỚI", "#245EDB", (_, _) => _ = RefreshAllAsync()));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement BuildOverview()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        var metrics = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < 6; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        AddMetric(metrics, 0, "CẶP LIVE", _marketCount, "USDT-M thanh khoản cao");
        AddMetric(metrics, 1, "ỨNG VIÊN", _candidateCount, "Đủ điều kiện hiện tại");
        AddMetric(metrics, 2, "CẶP ĐANG CHỌN", _selectedSymbol, "Đồng bộ toàn workspace");
        AddMetric(metrics, 3, "VỊ THẾ", _positionCount, "Tài khoản Binance");
        AddMetric(metrics, 4, "LỆNH / BẢO VỆ", _orderCount, "SL / TP / lệnh chờ");
        AddMetric(metrics, 5, "TRẠNG THÁI", _status, "Dữ liệu + radar + tài khoản");
        root.Children.Add(metrics);

        var center = new Grid();
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
        center.Children.Add(Card("RADAR — TOP CƠ HỘI", _marketGrid));

        var chart = new DockPanel();
        var chartHead = new Grid { Height = 36 };
        chartHead.ColumnDefinitions.Add(new ColumnDefinition());
        chartHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chartHead.Children.Add(_chartTitle);
        var tf = ValueText("1H  •  VÙNG VÀO / SL / TP", 11, Brush("#6E8BA8"));
        Grid.SetColumn(tf, 1);
        chartHead.Children.Add(tf);
        DockPanel.SetDock(chartHead, Dock.Top);
        chart.Children.Add(chartHead);
        chart.Children.Add(_chartCanvas);
        var chartCard = Card("BIỂU ĐỒ KẾ HOẠCH GIÁ", chart);
        Grid.SetColumn(chartCard, 1);
        center.Children.Add(chartCard);

        var decisionStack = new StackPanel();
        decisionStack.Children.Add(Card("QUYẾT ĐỊNH", _decision));
        decisionStack.Children.Add(Card("KẾ HOẠCH VÀO", _plan));
        decisionStack.Children.Add(Card("TÀI KHOẢN", _account));
        decisionStack.Children.Add(Card("THAO TÁC", BuildQuickActions()));
        Grid.SetColumn(decisionStack, 2);
        center.Children.Add(decisionStack);
        Grid.SetRow(center, 1);
        root.Children.Add(center);

        var lower = new Grid();
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        lower.Children.Add(Card("VỊ THẾ ĐANG MỞ", _positionGrid));
        var orders = Card("LỆNH / SL / TP", _orderGrid);
        Grid.SetColumn(orders, 1);
        lower.Children.Add(orders);
        var logPreview = Card("NHẬT KÝ VẬN HÀNH", _logs);
        Grid.SetColumn(logPreview, 2);
        lower.Children.Add(logPreview);
        Grid.SetRow(lower, 2);
        root.Children.Add(lower);
        return root;
    }

    private UIElement BuildRadar()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(ActionButton("QUÉT RADAR", "#245EDB", (_, _) => _ = ScanAsync()));
        actions.Children.Add(_executeButton);
        actions.Children.Add(StatusPill("KHÔNG ĐUỔI GIÁ", "#153B2B", Brush("#5ED99C")));
        actions.Children.Add(StatusPill("KHÔNG MUA ĐỈNH / BÁN ĐÁY", "#3A2C12", Brush("#F4B942")));
        root.Children.Add(actions);
        var card = Card("RADAR SCANNER — XẾP HẠNG CƠ HỘI", _radarGrid);
        Grid.SetRow(card, 1);
        root.Children.Add(card);
        return root;
    }

    private UIElement BuildPositions()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(Card("VỊ THẾ BINANCE — BOT + MỞ TAY", _positionsTabGrid));
        var orders = Card("LỆNH ĐANG MỞ / SL / TP REDUCE-ONLY", _ordersTabGrid);
        Grid.SetRow(orders, 1);
        root.Children.Add(orders);
        return root;
    }

    private UIElement BuildApiPanel()
    {
        var outer = new Grid { Margin = new Thickness(18) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });

        var form = new StackPanel { Margin = new Thickness(16), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(ValueText("API BINANCE FUTURES", 22, Brushes.White, bold: true));
        form.Children.Add(ValueText("API chỉ lưu trong bộ nhớ của phiên chạy hiện tại.", 12, Brush("#7F9AB5")));
        form.Children.Add(FieldLabel("API KEY"));
        form.Children.Add(_apiKey);
        form.Children.Add(FieldLabel("API SECRET"));
        form.Children.Add(_apiSecret);
        form.Children.Add(FieldLabel("MARGIN / LỆNH (USDT)"));
        form.Children.Add(_margin);
        form.Children.Add(FieldLabel("ĐÒN BẨY"));
        form.Children.Add(_leverage);
        form.Children.Add(ActionButton("KẾT NỐI TÀI KHOẢN", "#16784A", async (_, _) => await ConnectAccountAsync()));
        form.Children.Add(_autoLive);
        form.Children.Add(_armButton);
        var left = Card("CẤU HÌNH TÀI KHOẢN", form);
        outer.Children.Add(left);

        var safety = new StackPanel { Margin = new Thickness(16) };
        safety.Children.Add(ValueText("CHỐT AN TOÀN LIVE", 20, Brushes.White, bold: true));
        safety.Children.Add(CheckLine("Chỉ gửi lệnh sau khi Radar = ĐỦ ĐIỀU KIỆN"));
        safety.Children.Add(CheckLine("Xác nhận thủ công trước lệnh live"));
        safety.Children.Add(CheckLine("Một vị thế tối đa trong cấu hình hiện tại"));
        safety.Children.Add(CheckLine("SL và TP reduce-only sau khi xác nhận fill"));
        safety.Children.Add(CheckLine("Không DCA, không hedge, không đuổi giá"));
        safety.Children.Add(ValueText("LIVE chỉ mở sau khi kết nối tài khoản và bấm BẬT QUYỀN LIVE.", 13, Brush("#F4B942")));
        var right = Card("KIỂM SOÁT RỦI RO", safety);
        Grid.SetColumn(right, 1);
        outer.Children.Add(right);
        return outer;
    }

    private UIElement BuildLogs() => Card("NHẬT KÝ HỆ THỐNG / EXECUTION / PROTECTION", _logs);

    private UIElement BuildQuickActions()
    {
        var panel = new StackPanel();
        panel.Children.Add(ActionButton("LÀM MỚI DỮ LIỆU", "#245EDB", (_, _) => _ = RefreshAllAsync()));
        panel.Children.Add(ActionButton("QUÉT RADAR", "#275A8C", (_, _) => _ = ScanAsync()));
        panel.Children.Add(_executeButton);
        return panel;
    }

    private UIElement BuildFooter()
    {
        var grid = new Grid { Height = 30, Background = Brush("#071421") };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(ValueText("TRENDGOVERNOR • SINGLE STATE • LIVE DATA • WINDOWS X64", 11, Brush("#6E8BA8")));
        var right = ValueText("Không đuổi giá  •  Không mua đỉnh  •  Không bán đáy", 11, Brush("#F4B942"));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static Style BuildTabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(TabItem.ForegroundProperty, Brush("#AFC1D4")));
        style.Setters.Add(new Setter(TabItem.BackgroundProperty, Brush("#071421")));
        style.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(14, 14, 14, 14)));
        style.Setters.Add(new Setter(TabItem.MarginProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(TabItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(TabItem.MinWidthProperty, 150d));
        var trigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        trigger.Setters.Add(new Setter(TabItem.BackgroundProperty, Brush("#163A61")));
        trigger.Setters.Add(new Setter(TabItem.ForegroundProperty, Brushes.White));
        style.Triggers.Add(trigger);
        return style;
    }

    private static TabItem Tab(string name, string icon, UIElement content)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(ValueText(icon, 14, Brush("#61A8E8")));
        header.Children.Add(ValueText(name, 12, Brushes.White, bold: true));
        return new TabItem { Header = header, Content = content };
    }

    private static void AddMetric(Grid grid, int column, string title, TextBlock value, string note)
    {
        var panel = new StackPanel();
        panel.Children.Add(ValueText(title, 10, Brush("#7F9AB5"), bold: true));
        panel.Children.Add(value);
        panel.Children.Add(ValueText(note, 10, Brush("#58728C")));
        var card = Card(null, panel);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static Border Card(string? title, UIElement body)
    {
        var panel = new DockPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            var heading = ValueText(title, 12, Brush("#7FC2F2"), bold: true);
            heading.Margin = new Thickness(8, 6, 8, 8);
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
        }
        panel.Children.Add(body);
        return new Border
        {
            Background = Brush("#091827"),
            BorderBrush = Brush("#17344D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
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
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Background = Brush("#071421"),
        Foreground = Brushes.White,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#17344D"),
        BorderThickness = new Thickness(0),
        RowBackground = Brush("#071421"),
        AlternatingRowBackground = Brush("#0B1C2F"),
        RowHeight = 27,
        ColumnHeaderHeight = 30,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static DataGrid MarketGrid(bool compact)
    {
        var grid = BaseGrid();
        grid.Columns.Add(Column("CẶP", "Symbol", 92));
        grid.Columns.Add(Column("GIÁ", "Price", 90, "0.########"));
        grid.Columns.Add(Column("24H %", "Change24h", 72, "+0.00;-0.00;0.00"));
        grid.Columns.Add(Column("HƯỚNG", "Direction", 70));
        grid.Columns.Add(Column("ĐIỂM", "Score", 58));
        grid.Columns.Add(Column("TRẠNG THÁI", "Status", compact ? 140 : 180));
        if (!compact)
        {
            grid.Columns.Add(Column("VÙNG VÀO THẤP", "EntryLow", 112, "0.########"));
            grid.Columns.Add(Column("VÙNG VÀO CAO", "EntryHigh", 112, "0.########"));
            grid.Columns.Add(Column("SL", "StopLoss", 100, "0.########"));
            grid.Columns.Add(Column("TP", "TakeProfit", 100, "0.########"));
            grid.Columns.Add(Column("RR", "RiskReward", 60, "0.00"));
            grid.Columns.Add(Column("THANH KHOẢN", "QuoteVolume", 120, "N0"));
        }
        return grid;
    }

    private static DataGrid PositionGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(Column("CẶP", "Symbol", 100));
        grid.Columns.Add(Column("HƯỚNG", "Side", 76));
        grid.Columns.Add(Column("KHỐI LƯỢNG", "Quantity", 100, "0.########"));
        grid.Columns.Add(Column("GIÁ VÀO", "EntryPrice", 110, "0.########"));
        grid.Columns.Add(Column("MARK", "MarkPrice", 110, "0.########"));
        grid.Columns.Add(Column("PNL USDT", "UnrealizedPnl", 100, "+0.00;-0.00;0.00"));
        grid.Columns.Add(Column("BẢO VỆ", "Protection", 140));
        return grid;
    }

    private static DataGrid OrderGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(Column("CẶP", "Symbol", 92));
        grid.Columns.Add(Column("LOẠI", "Type", 130));
        grid.Columns.Add(Column("SIDE", "Side", 65));
        grid.Columns.Add(Column("GIÁ", "Price", 90, "0.########"));
        grid.Columns.Add(Column("GIÁ KÍCH HOẠT", "StopPrice", 115, "0.########"));
        grid.Columns.Add(Column("QTY", "Quantity", 90, "0.########"));
        grid.Columns.Add(Column("TRẠNG THÁI", "Status", 95));
        grid.Columns.Add(Column("GIẢM VỊ THẾ", "ReduceOnly", 95));
        return grid;
    }

    private static DataGridTextColumn Column(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path);
        if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
        return new DataGridTextColumn { Header = header, Binding = binding, Width = width };
    }

    private static TextBox InputBox(string value = "") => new()
    {
        Text = value,
        Height = 34,
        Margin = new Thickness(0, 4, 0, 12),
        Background = Brush("#0C1B2B"),
        Foreground = Brushes.White,
        BorderBrush = Brush("#284762"),
        Padding = new Thickness(8)
    };

    private static TextBlock FieldLabel(string value) => ValueText(value, 11, Brush("#AFC1D4"), bold: true);

    private static TextBlock CheckLine(string value) => ValueText("✓  " + value, 13, Brush("#66D49A"));

    private static Border StatusPill(string value, string background, Brush foreground) => new()
    {
        Background = Brush(background),
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(6),
        Padding = new Thickness(10, 6, 10, 6),
        Child = ValueText(value, 11, foreground, bold: true)
    };

    private static TextBlock ValueText(string value, double size, Brush color, bool bold = false) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = color,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(6, 2, 6, 2),
        TextWrapping = TextWrapping.Wrap
    };

    private static Button ActionButton(string value, string background, RoutedEventHandler? click = null)
    {
        var button = new Button
        {
            Content = value,
            Background = Brush(background),
            Foreground = Brushes.White,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(6),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            MinHeight = 34
        };
        if (click != null) button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
