using System.Windows.Controls;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly List<DataGrid> _lifecycleViews = [];
    private readonly List<DataGrid> _verifyViews = [];

    private DataGrid _lifecycleGrid
    {
        get
        {
            var grid = LifecycleGrid();
            grid.ItemsSource = _state.Lifecycle;
            _lifecycleViews.Add(grid);
            return grid;
        }
    }

    private DataGrid _verifyMatrixGrid
    {
        get
        {
            var grid = VerifyMatrixGrid();
            grid.ItemsSource = _state.VerifyMatrix;
            _verifyViews.Add(grid);
            return grid;
        }
    }

    public void InitializePack14Ui() { }

    private static DataGrid LifecycleGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("GIAI ĐOẠN", "Stage", 145));
        grid.Columns.Add(C("ỔN ĐỊNH", "StableCycles", 75));
        grid.Columns.Add(C("PHIÊN BẢN", "DecisionVersion", 82));
        grid.Columns.Add(C("LÝ DO", "Reason", 300));
        grid.Columns.Add(C("CANDIDATE ID", "CandidateId", 280));
        grid.Columns.Add(C("CẬP NHẬT", "LastSeen", 150, "dd/MM HH:mm:ss"));
        return grid;
    }

    private static DataGrid VerifyMatrixGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(C("CẶP", "Symbol", 95));
        grid.Columns.Add(C("ĐIỀU KIỆN", "GateCode", 185));
        grid.Columns.Add(C("PASS", "Passed", 68));
        grid.Columns.Add(C("LÝ DO", "Reason", 390));
        grid.Columns.Add(C("PLAN ID", "PlanId", 245));
        grid.Columns.Add(C("GRANT ID", "GrantId", 265));
        grid.Columns.Add(C("THỜI GIAN", "IssuedAt", 150, "dd/MM HH:mm:ss"));
        return grid;
    }

    private void RefreshPack14Ui()
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var grid in _lifecycleViews) grid.Items.Refresh();
            foreach (var grid in _verifyViews) grid.Items.Refresh();
        });
    }
}