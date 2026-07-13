using System.Windows.Controls;
using System.Windows.Data;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly DataGrid _lifecycleGrid = LifecycleGrid();
    private readonly DataGrid _verifyMatrixGrid = VerifyMatrixGrid();

    public void InitializePack14Ui()
    {
        _lifecycleGrid.ItemsSource = _state.Lifecycle;
        _verifyMatrixGrid.ItemsSource = _state.VerifyMatrix;
    }

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
            _lifecycleGrid.Items.Refresh();
            _verifyMatrixGrid.Items.Refresh();
        });
    }
}