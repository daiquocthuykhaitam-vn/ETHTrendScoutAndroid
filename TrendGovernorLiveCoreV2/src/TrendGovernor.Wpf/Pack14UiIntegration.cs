using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly DataGrid _lifecycleGrid = LifecycleGrid();
    private readonly DataGrid _verifyMatrixGrid = VerifyMatrixGrid();

    public void InitializePack14Ui()
    {
        Loaded += (_, _) =>
        {
            _lifecycleGrid.ItemsSource = _state.Lifecycle;
            _verifyMatrixGrid.ItemsSource = _state.VerifyMatrix;
            var tabs = FindDescendant<TabControl>(this);
            if (tabs is null) return;
            if (tabs.Items.OfType<TabItem>().Any(x => Equals(x.Tag, "PACK14"))) return;
            tabs.Items.Insert(Math.Min(4, tabs.Items.Count), new TabItem
            {
                Tag = "PACK14",
                Header = Pack14TabHeader(),
                Content = Pack14Workspace()
            });
        };
    }

    private UIElement Pack14Workspace()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
        root.Children.Add(Card("VÒNG ĐỜI ỨNG VIÊN — RADAR → PLAN → VERIFY → ORDER → FILL → PROTECT → MANAGE", _lifecycleGrid));
        var verify = Card("MA TRẬN KIỂM ĐỊNH TRƯỚC LỆNH — PASS/BLOCK + LÝ DO", _verifyMatrixGrid);
        Grid.SetRow(verify, 1);
        root.Children.Add(verify);
        return root;
    }

    private static object Pack14TabHeader()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(T("◫", 14, B("#61A8E8")));
        header.Children.Add(T("VÒNG ĐỜI LỆNH", 12, Brushes.White, true));
        return header;
    }

    private static DataGrid LifecycleGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("GIAI ĐOẠN", "Stage", 145));
        grid.Columns.Add(C("ỔN ĐỊNH", "StableCycles", 75));
        grid.Columns.Add(C("PHIÊN BẢN", "DecisionVersion", 80));
        grid.Columns.Add(C("LÝ DO", "Reason", 260));
        grid.Columns.Add(C("CANDIDATE ID", "CandidateId", 280));
        grid.Columns.Add(C("CẬP NHẬT", "LastSeen", 150, "dd/MM HH:mm:ss"));
        return grid;
    }

    private static DataGrid VerifyMatrixGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("ĐIỀU KIỆN", "GateCode", 180));
        grid.Columns.Add(C("PASS", "Passed", 65));
        grid.Columns.Add(C("LÝ DO", "Reason", 360));
        grid.Columns.Add(C("PLAN ID", "PlanId", 240));
        grid.Columns.Add(C("GRANT ID", "GrantId", 260));
        grid.Columns.Add(C("THỜI GIAN", "IssuedAt", 150, "dd/MM HH:mm:ss"));
        return grid;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void RefreshPack14Ui()
    {
        Dispatcher.Invoke(() =>
        {
            _lifecycleGrid.Items.Refresh();
            _verifyMatrixGrid.Items.Refresh();
        });
    }
}
