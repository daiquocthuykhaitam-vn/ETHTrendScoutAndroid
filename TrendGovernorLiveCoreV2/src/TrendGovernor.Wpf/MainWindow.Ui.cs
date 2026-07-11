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

    private readonly DataGrid _marketGrid = MarketGrid(true);
    private readonly DataGrid _positionGrid = PositionGrid();
    private readonly DataGrid _orderGrid = OrderGrid();
    private readonly DataGrid _radarGrid = MarketGrid(false);
    private readonly DataGrid _positionsTabGrid = PositionGrid();
    private readonly DataGrid _ordersTabGrid = OrderGrid();

    private readonly TextBlock _status = T("ĐANG KHỞI ĐỘNG", 13, Brushes.Gold);
    private readonly TextBlock _statusMetric = T("ĐANG KHỞI ĐỘNG", 18, Brushes.Gold);
    private readonly TextBlock _account = T("CHƯA KẾT NỐI API", 13, Brushes.LightGray);
    private readonly TextBlock _decision = T("WAIT", 30, Brushes.Gold, true);
    private readonly TextBlock _plan = T("CHƯA CÓ KẾ HOẠCH", 13, Brushes.LightGray);
    private readonly TextBlock _marketCount = T("0", 24, Brushes.White, true);
    private readonly TextBlock _candidateCount = T("0", 24, Brushes.Gold, true);
    private readonly TextBlock _selectedSymbol = T("--", 24, Brushes.White, true);
    private readonly TextBlock _positionCount = T("0", 24, Brushes.White, true);
    private readonly TextBlock _orderCount = T("0", 24, Brushes.White, true);
    private readonly TextBlock _chartTitle = T("CHƯA CHỌN CẶP", 14, Brushes.White, true);
    private readonly Canvas _chartCanvas = new() { Background = B("#071421"), MinHeight = 280, ClipToBounds = true };

    private readonly TextBox _apiKey = Input();
    private readonly PasswordBox _apiSecret = new() { Height = 34, Background = B("#0C1B2B"), Foreground = Brushes.White, BorderBrush = B("#284762"), Padding = new Thickness(8), Margin = new Thickness(0,4,0,12) };
    private readonly TextBox _margin = Input("2");
    private readonly TextBox _leverage = Input("3");
    private readonly CheckBox _autoLive = new() { Content = "AUTO LỆNH LIVE", Foreground = Brushes.White, Margin = new Thickness(0,12,0,8) };
    private readonly Button _armButton = Btn("BẬT QUYỀN LIVE", "#9B1C31");
    private readonly TextBox _logs = LogBox();
    private readonly TextBox _logPreview = LogBox();

    private bool _liveArmed;
    private MarketRow? _selected;

    public MainWindow()
    {
        Title = "TRENDGOVERNOR LIVE CORE V2 - TERMINAL BUILD 07";
        Width = 1720; Height = 1000; MinWidth = 1360; MinHeight = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = B("#040C16"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");

        _radarGrid.ItemsSource = _state.Markets;
        _positionsTabGrid.ItemsSource = _state.Positions;
        _ordersTabGrid.ItemsSource = _state.Orders;
        _radarGrid.SelectionChanged += (_, _) => { if (_radarGrid.SelectedItem is MarketRow row) SelectCandidate(row); };
        _armButton.Click += ArmLive;

        Content = Root();
        Loaded += OnLoaded;
        Closed += (_, _) => _cts.Cancel();
        SizeChanged += (_, _) => { if (_selected != null) RenderPlanChart(_selected); };
    }

    private UIElement Root()
    {
        var root = new DockPanel();
        var h = Header(); DockPanel.SetDock(h, Dock.Top); root.Children.Add(h);
        var f = Footer(); DockPanel.SetDock(f, Dock.Bottom); root.Children.Add(f);
        var tabs = new TabControl { Background = B("#040C16"), BorderThickness = new Thickness(0), TabStripPlacement = Dock.Left };
        tabs.Resources.Add(typeof(TabItem), TabStyle());
        tabs.Items.Add(Tab("TỔNG QUAN", "▦", Overview()));
        tabs.Items.Add(Tab("RADAR", "◎", Radar()));
        tabs.Items.Add(Tab("VỊ THẾ", "◈", Positions()));
        tabs.Items.Add(Tab("API & LIVE", "⚙", Api()));
        tabs.Items.Add(Tab("NHẬT KÝ", "≡", Card("NHẬT KÝ HỆ THỐNG / EXECUTION / PROTECTION", _logs)));
        root.Children.Add(tabs);
        return root;
    }

    private UIElement Header()
    {
        var g = new Grid { Height = 72, Background = B("#071421") };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18,10,0,8) };
        left.Children.Add(T("◆", 24, B("#F4B942"), true));
        var names = new StackPanel(); names.Children.Add(T("TRENDGOVERNOR LIVE CORE V2", 22, Brushes.White, true)); names.Children.Add(T("PLAN → VERIFY → EXECUTE → FILL → PROTECT → MANAGE", 11, B("#6E8BA8")));
        left.Children.Add(names); g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,16,0) };
        right.Children.Add(Pill("BINANCE USD-M", "#15324A", Brushes.LightSkyBlue)); right.Children.Add(_status); right.Children.Add(Btn("LÀM MỚI", "#245EDB", (_,_) => _ = RefreshAllAsync()));
        Grid.SetColumn(right,1); g.Children.Add(right); return g;
    }

    private UIElement Overview()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        var m = new Grid(); for (int i=0;i<6;i++) m.ColumnDefinitions.Add(new ColumnDefinition());
        Metric(m,0,"CẶP LIVE",_marketCount,"USDT-M thanh khoản cao"); Metric(m,1,"ỨNG VIÊN",_candidateCount,"Đủ điều kiện hiện tại"); Metric(m,2,"CẶP ĐANG CHỌN",_selectedSymbol,"Đồng bộ workspace"); Metric(m,3,"VỊ THẾ",_positionCount,"Tài khoản Binance"); Metric(m,4,"LỆNH / BẢO VỆ",_orderCount,"SL / TP / lệnh chờ"); Metric(m,5,"TRẠNG THÁI",_statusMetric,"Dữ liệu + radar + tài khoản");
        root.Children.Add(m);

        var center = new Grid(); center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05,GridUnitType.Star) }); center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45,GridUnitType.Star) }); center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75,GridUnitType.Star) });
        center.Children.Add(Card("RADAR — TOP CƠ HỘI", _marketGrid));
        var chart = new DockPanel(); var ch = new Grid { Height=34 }; ch.ColumnDefinitions.Add(new ColumnDefinition()); ch.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); ch.Children.Add(_chartTitle); var tf=T("1H • GIÁ / ENTRY / SL / TP",11,B("#6E8BA8")); Grid.SetColumn(tf,1); ch.Children.Add(tf); DockPanel.SetDock(ch,Dock.Top); chart.Children.Add(ch); chart.Children.Add(_chartCanvas); var cc=Card("BIỂU ĐỒ KẾ HOẠCH GIÁ",chart); Grid.SetColumn(cc,1); center.Children.Add(cc);
        var side=new StackPanel(); side.Children.Add(Card("QUYẾT ĐỊNH",_decision)); side.Children.Add(Card("KẾ HOẠCH VÀO",_plan)); side.Children.Add(Card("TÀI KHOẢN",_account)); side.Children.Add(Card("THAO TÁC",QuickActions())); Grid.SetColumn(side,2); center.Children.Add(side);
        Grid.SetRow(center,1); root.Children.Add(center);

        var lower=new Grid(); lower.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.2,GridUnitType.Star)}); lower.ColumnDefinitions.Add(new ColumnDefinition()); lower.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(.9,GridUnitType.Star)});
        lower.Children.Add(Card("VỊ THẾ ĐANG MỞ",_positionGrid)); var oc=Card("LỆNH / SL / TP",_orderGrid); Grid.SetColumn(oc,1); lower.Children.Add(oc); var lc=Card("NHẬT KÝ VẬN HÀNH",_logPreview); Grid.SetColumn(lc,2); lower.Children.Add(lc); Grid.SetRow(lower,2); root.Children.Add(lower);
        return root;
    }

    private UIElement Radar()
    {
        var g=new Grid { Margin=new Thickness(10)}; g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto}); g.RowDefinitions.Add(new RowDefinition());
        var a=new StackPanel { Orientation=Orientation.Horizontal }; a.Children.Add(Btn("QUÉT RADAR","#245EDB",(_,_)=>_=ScanAsync())); a.Children.Add(Btn("GỬI LỆNH ĐÃ CHỌN","#B66A00",async (_,_)=>await ExecuteSelectedAsync())); a.Children.Add(Pill("KHÔNG ĐUỔI GIÁ","#153B2B",B("#5ED99C"))); a.Children.Add(Pill("KHÔNG MUA ĐỈNH / BÁN ĐÁY","#3A2C12",B("#F4B942"))); g.Children.Add(a);
        var c=Card("RADAR SCANNER — XẾP HẠNG CƠ HỘI",_radarGrid); Grid.SetRow(c,1); g.Children.Add(c); return g;
    }

    private UIElement Positions()
    {
        var g=new Grid { Margin=new Thickness(10)}; g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition()); g.Children.Add(Card("VỊ THẾ BINANCE — BOT + MỞ TAY",_positionsTabGrid)); var o=Card("LỆNH ĐANG MỞ / SL / TP REDUCE-ONLY",_ordersTabGrid); Grid.SetRow(o,1); g.Children.Add(o); return g;
    }

    private UIElement Api()
    {
        var g=new Grid { Margin=new Thickness(18)}; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
        var form=new StackPanel { Margin=new Thickness(16),MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Left}; form.Children.Add(T("API BINANCE FUTURES",22,Brushes.White,true)); form.Children.Add(T("API chỉ giữ trong bộ nhớ của phiên chạy.",12,B("#7F9AB5"))); form.Children.Add(Label("API KEY")); form.Children.Add(_apiKey); form.Children.Add(Label("API SECRET")); form.Children.Add(_apiSecret); form.Children.Add(Label("MARGIN / LỆNH (USDT)")); form.Children.Add(_margin); form.Children.Add(Label("ĐÒN BẨY")); form.Children.Add(_leverage); form.Children.Add(Btn("KẾT NỐI TÀI KHOẢN","#16784A",async (_,_)=>await ConnectAccountAsync())); form.Children.Add(_autoLive); form.Children.Add(_armButton); g.Children.Add(Card("CẤU HÌNH TÀI KHOẢN",form));
        var s=new StackPanel { Margin=new Thickness(16)}; s.Children.Add(T("CHỐT AN TOÀN LIVE",20,Brushes.White,true)); s.Children.Add(Check("Chỉ gửi lệnh khi Radar = ĐỦ ĐIỀU KIỆN")); s.Children.Add(Check("Xác nhận thủ công trước lệnh live")); s.Children.Add(Check("Một vị thế tối đa theo cấu hình")); s.Children.Add(Check("SL và TP reduce-only sau fill")); s.Children.Add(Check("Không DCA, không hedge, không đuổi giá")); s.Children.Add(T("LIVE chỉ mở sau khi kết nối tài khoản và bấm BẬT QUYỀN LIVE.",13,B("#F4B942"))); var r=Card("KIỂM SOÁT RỦI RO",s); Grid.SetColumn(r,1); g.Children.Add(r); return g;
    }

    private UIElement QuickActions()
    {
        var p=new StackPanel(); p.Children.Add(Btn("LÀM MỚI DỮ LIỆU","#245EDB",(_,_)=>_=RefreshAllAsync())); p.Children.Add(Btn("QUÉT RADAR","#275A8C",(_,_)=>_=ScanAsync())); p.Children.Add(Btn("GỬI LỆNH ĐÃ CHỌN","#B66A00",async (_,_)=>await ExecuteSelectedAsync())); return p;
    }

    private UIElement Footer()
    {
        var g=new Grid { Height=30,Background=B("#071421")}; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto}); g.Children.Add(T("TRENDGOVERNOR • SINGLE STATE • LIVE DATA • WINDOWS X64",11,B("#6E8BA8"))); var r=T("Không đuổi giá • Không mua đỉnh • Không bán đáy",11,B("#F4B942")); Grid.SetColumn(r,1); g.Children.Add(r); return g;
    }

    private static Style TabStyle(){var s=new Style(typeof(TabItem));s.Setters.Add(new Setter(Control.ForegroundProperty,B("#AFC1D4")));s.Setters.Add(new Setter(Control.BackgroundProperty,B("#071421")));s.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));s.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(14)));s.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,2)));s.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,150d));var t=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};t.Setters.Add(new Setter(Control.BackgroundProperty,B("#163A61")));t.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));s.Triggers.Add(t);return s;}
    private static TabItem Tab(string n,string i,UIElement c){var h=new StackPanel{Orientation=Orientation.Horizontal};h.Children.Add(T(i,14,B("#61A8E8")));h.Children.Add(T(n,12,Brushes.White,true));return new TabItem{Header=h,Content=c};}
    private static void Metric(Grid g,int col,string title,TextBlock value,string note){var p=new StackPanel();p.Children.Add(T(title,10,B("#7F9AB5"),true));p.Children.Add(value);p.Children.Add(T(note,10,B("#58728C")));var c=Card(null,p);Grid.SetColumn(c,col);g.Children.Add(c);}
    private static Border Card(string? title,UIElement body){var p=new DockPanel();if(!string.IsNullOrWhiteSpace(title)){var h=T(title,12,B("#7FC2F2"),true);h.Margin=new Thickness(8,6,8,8);DockPanel.SetDock(h,Dock.Top);p.Children.Add(h);}p.Children.Add(body);return new Border{Background=B("#091827"),BorderBrush=B("#17344D"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Margin=new Thickness(4),Padding=new Thickness(6),Child=p};}

    private static DataGrid BaseGrid()=>new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,Background=B("#071421"),Foreground=Brushes.White,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,HorizontalGridLinesBrush=B("#17344D"),BorderThickness=new Thickness(0),RowBackground=B("#071421"),AlternatingRowBackground=B("#0B1C2F"),RowHeight=27,ColumnHeaderHeight=30,CanUserAddRows=false,CanUserDeleteRows=false,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private static DataGrid MarketGrid(bool compact){var g=BaseGrid();g.Columns.Add(C("CẶP","Symbol",92));g.Columns.Add(C("GIÁ","Price",90,"0.########"));g.Columns.Add(C("24H %","Change24h",72,"+0.00;-0.00;0.00"));g.Columns.Add(C("HƯỚNG","Direction",70));g.Columns.Add(C("ĐIỂM","Score",58));g.Columns.Add(C("TRẠNG THÁI","Status",compact?140:180));if(!compact){g.Columns.Add(C("VÙNG VÀO THẤP","EntryLow",112,"0.########"));g.Columns.Add(C("VÙNG VÀO CAO","EntryHigh",112,"0.########"));g.Columns.Add(C("SL","StopLoss",100,"0.########"));g.Columns.Add(C("TP","TakeProfit",100,"0.########"));g.Columns.Add(C("RR","RiskReward",60,"0.00"));g.Columns.Add(C("THANH KHOẢN","QuoteVolume",120,"N0"));}return g;}
    private static DataGrid PositionGrid(){var g=BaseGrid();g.Columns.Add(C("CẶP","Symbol",100));g.Columns.Add(C("HƯỚNG","Side",76));g.Columns.Add(C("KHỐI LƯỢNG","Quantity",100,"0.########"));g.Columns.Add(C("GIÁ VÀO","EntryPrice",110,"0.########"));g.Columns.Add(C("MARK","MarkPrice",110,"0.########"));g.Columns.Add(C("PNL USDT","UnrealizedPnl",100,"+0.00;-0.00;0.00"));g.Columns.Add(C("BẢO VỆ","Protection",140));return g;}
    private static DataGrid OrderGrid(){var g=BaseGrid();g.Columns.Add(C("CẶP","Symbol",92));g.Columns.Add(C("LOẠI","Type",130));g.Columns.Add(C("SIDE","Side",65));g.Columns.Add(C("GIÁ","Price",90,"0.########"));g.Columns.Add(C("GIÁ KÍCH HOẠT","StopPrice",115,"0.########"));g.Columns.Add(C("QTY","Quantity",90,"0.########"));g.Columns.Add(C("TRẠNG THÁI","Status",95));g.Columns.Add(C("GIẢM VỊ THẾ","ReduceOnly",95));return g;}
    private static DataGridTextColumn C(string h,string p,double w,string? f=null){var b=new Binding(p);if(!string.IsNullOrWhiteSpace(f))b.StringFormat=f;return new DataGridTextColumn{Header=h,Binding=b,Width=w};}
    private static TextBox Input(string v="")=>new(){Text=v,Height=34,Margin=new Thickness(0,4,0,12),Background=B("#0C1B2B"),Foreground=Brushes.White,BorderBrush=B("#284762"),Padding=new Thickness(8)};
    private static TextBox LogBox()=>new(){IsReadOnly=true,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Background=B("#071421"),Foreground=B("#AFC1D4"),BorderThickness=new Thickness(0),FontFamily=new FontFamily("Consolas"),FontSize=12,Padding=new Thickness(8)};
    private static TextBlock Label(string v)=>T(v,11,B("#AFC1D4"),true); private static TextBlock Check(string v)=>T("✓  "+v,13,B("#66D49A"));
    private static Border Pill(string v,string bg,Brush fg)=>new(){Background=B(bg),CornerRadius=new CornerRadius(4),Margin=new Thickness(6),Padding=new Thickness(10,6,10,6),Child=T(v,11,fg,true)};
    private static TextBlock T(string v,double s,Brush c,bool bold=false)=>new(){Text=v,FontSize=s,Foreground=c,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(6,2,6,2),TextWrapping=TextWrapping.Wrap};
    private static Button Btn(string v,string bg,RoutedEventHandler? click=null){var b=new Button{Content=v,Background=B(bg),Foreground=Brushes.White,Padding=new Thickness(14,8,14,8),Margin=new Thickness(6),BorderThickness=new Thickness(0),FontWeight=FontWeights.SemiBold,Cursor=System.Windows.Input.Cursors.Hand,MinHeight=34};if(click!=null)b.Click+=click;return b;}
    private static SolidColorBrush B(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
}
