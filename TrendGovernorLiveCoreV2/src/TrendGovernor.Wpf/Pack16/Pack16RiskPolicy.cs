namespace TrendGovernor.Wpf;

public sealed class Pack16Config
{
    public decimal MaxBotMarginUsdt { get; set; } = 10m;
    public decimal MaxBotNotionalUsdt { get; set; } = 50m;
    public decimal MaxExposurePercentOfEquity { get; set; } = 15m;
    public decimal MaxSymbolNotionalUsdt { get; set; } = 15m;
    public decimal MaxSpreadPercent { get; set; } = 0.20m;
    public decimal MaxEntryDriftPercent { get; set; } = 0.20m;
    public int MaxOpenPositionsPerSymbol { get; set; } = 1;
    public bool LimitFirst