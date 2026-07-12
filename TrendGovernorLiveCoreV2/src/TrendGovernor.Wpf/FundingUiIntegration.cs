using System.Windows.Controls;
using System.Windows.Data;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    internal void InitializeFundingUi()
    {
        AddFundingColumns(_marketGrid, compact: true);
        AddFundingColumns(_radarGrid, compact: false);
        AddPositionOperationalColumns(_positionGrid);
        AddPositionOperationalColumns(_positionsTabGrid);
        AddOrderOperationalColumns(_orderGrid);
        AddOrderOperationalColumns(_ordersTabGrid);
    }

    private static void AddFundingColumns(DataGrid grid, bool compact)
    {
        if (grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "FUNDING", StringComparison.Ordinal))) return;
        grid.Columns.Add(Column("FUNDING", "FundingRatePercent", 92, "+0.####;-0.####;0'%'"));
        grid.Columns.Add(Column("CHU KỲ", "FundingIntervalHours", 65, "0'H'"));
        grid.Columns.Add(Column("BÊN TRẢ → NHẬN", "FundingFlow", 120));
        grid.Columns.Add(Column("MỨC", "FundingLevel", 90));
        if (!compact)
        {
            grid.Columns.Add(Column("%/GIỜ", "FundingPerHourPercent", 82, "+0.####;-0.####;0'%'"));
            grid.Columns.Add(Column("CÒN PHÚT", "FundingMinutesRemaining", 78));
            grid.Columns.Add(Column("ỔN ĐỊNH FUNDING", "FundingStability", 110));
            grid.Columns.Add(Column("ĐIỂM FUNDING", "FundingScore", 100));
            grid.Columns.Add(Column("ĐÁNH GIÁ", "FundingBias", 125));
        }
    }

    private static void AddPositionOperationalColumns(DataGrid grid)
    {
        if (!grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "NGUỒN", StringComparison.Ordinal)))
            grid.Columns.Insert(1, Column("NGUỒN", "Owner", 75));

        if (grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "FUNDING 24H", StringComparison.Ordinal))) return;
        grid.Columns.Add(Column("FUNDING 24H", "RealizedFunding", 105, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("FUNDING KẾ", "EstimatedNextFunding", 105, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("NET PNL", "NetPnlAfterFunding", 100, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("NET DỰ KIẾN", "ProjectedNetPnl", 110, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("SL XÁC MINH", "StopLossConfirmed", 95));
        grid.Columns.Add(Column("TP XÁC MINH", "TakeProfitConfirmed", 95));
        grid.Columns.Add(Column("LUỒNG FUNDING", "FundingFlow", 120));
        grid.Columns.Add(Column("MỨC FUNDING", "FundingLevel", 100));
    }

    private static void AddOrderOperationalColumns(DataGrid grid)
    {
        if (!grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "NGUỒN", StringComparison.Ordinal)))
            grid.Columns.Insert(1, Column("NGUỒN", "Source", 70));
        if (!grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "CLIENT ID", StringComparison.Ordinal)))
            grid.Columns.Add(Column("CLIENT ID", "ClientOrderId", 220));
    }

    private static DataGridTextColumn Column(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path);
        if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
        return new DataGridTextColumn { Header = header, Binding = binding, Width = width };
    }
}
