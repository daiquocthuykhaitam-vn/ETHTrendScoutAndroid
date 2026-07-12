using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    public void InitializeFundingUi()
    {
        AddFundingColumns(_marketGrid, compact: true);
        AddFundingColumns(_radarGrid, compact: false);
        AddPositionFundingColumns(_positionGrid);
        AddPositionFundingColumns(_positionsTabGrid);

        _marketGrid.SelectionChanged += FundingSelectionChanged;
        _radarGrid.SelectionChanged += FundingSelectionChanged;
    }

    private async void FundingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not MarketRow row) return;
        await Task.Delay(150);
        if (_selected?.Symbol != row.Symbol) return;

        var next = row.NextFundingTime.HasValue
            ? row.NextFundingTime.Value.LocalDateTime.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
            : "--";
        var fundingText =
            $"\n\nFUNDING\n" +
            $"Tỷ lệ: {row.FundingRatePercent:+0.####;-0.####;0}% / {row.FundingIntervalHours}H\n" +
            $"Luồng: {row.FundingFlow}\n" +
            $"Mức: {row.FundingLevel} | Chuẩn hóa: {row.FundingPerHourPercent:+0.####;-0.####;0}%/giờ\n" +
            $"Kỳ kế tiếp: {next} ({row.FundingMinutesRemaining} phút)\n" +
            $"Ổn định: {row.FundingStability}, cùng dấu {row.FundingSameSignPeriods} kỳ\n" +
            $"Đánh giá hướng: {row.FundingBias} | Điểm {row.FundingScore}/100\n" +
            $"Ước tính kỳ tới: {row.EstimatedFundingForTrade:+0.####;-0.####;0} USDT" +
            (string.IsNullOrWhiteSpace(row.FundingWarning) ? "" : $"\nCảnh báo: {row.FundingWarning}");

        if (!_plan.Text.Contains("\n\nFUNDING\n", StringComparison.Ordinal)) _plan.Text += fundingText;
        if (!_planFull.Text.Contains("\n\nFUNDING\n", StringComparison.Ordinal)) _planFull.Text += fundingText;
    }

    private static void AddFundingColumns(DataGrid grid, bool compact)
    {
        if (grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "FUNDING", StringComparison.Ordinal))) return;
        grid.Columns.Add(Column("FUNDING", "FundingRatePercent", 92, "+0.####;-0.####;0'%'"));
        grid.Columns.Add(Column("CHU KỲ", "FundingIntervalHours", 65, "0'H'"));
        grid.Columns.Add(Column("BÊN TRẢ → NHẬN", "FundingFlow", 115));
        grid.Columns.Add(Column("MỨC", "FundingLevel", 85));
        if (!compact)
        {
            grid.Columns.Add(Column("%/GIỜ", "FundingPerHourPercent", 82, "+0.####;-0.####;0'%'"));
            grid.Columns.Add(Column("CÒN PHÚT", "FundingMinutesRemaining", 78));
            grid.Columns.Add(Column("ỔN ĐỊNH", "FundingStability", 88));
            grid.Columns.Add(Column("FUNDING SCORE", "FundingScore", 96));
            grid.Columns.Add(Column("ĐÁNH GIÁ", "FundingBias", 120));
        }
    }

    private static void AddPositionFundingColumns(DataGrid grid)
    {
        if (grid.Columns.Any(x => string.Equals(x.Header?.ToString(), "FUNDING 24H", StringComparison.Ordinal))) return;
        grid.Columns.Add(Column("FUNDING 24H", "RealizedFunding", 105, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("FUNDING KẾ", "EstimatedNextFunding", 105, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("NET PNL", "NetPnlAfterFunding", 100, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("NET DỰ KIẾN", "ProjectedNetPnl", 110, "+0.####;-0.####;0.####"));
        grid.Columns.Add(Column("LUỒNG FUNDING", "FundingFlow", 115));
        grid.Columns.Add(Column("MỨC FUNDING", "FundingLevel", 100));
    }

    private static DataGridTextColumn Column(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path);
        if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
        return new DataGridTextColumn { Header = header, Binding = binding, Width = width };
    }
}
