using System.Collections.ObjectModel;

namespace TrendGovernor.Wpf;

public sealed class MarketRow
{
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Change24h { get; set; }
    public decimal QuoteVolume { get; set; }

    public string Direction { get; set; } = "WAIT";
    public int Score { get; set; }
    public string Status { get; set; } = "CHỜ";
    public string Reason { get; set; } = "Chưa phân tích";
    public string Setup { get; set; } = "--";

    public string Trend1D { get; set; } = "--";
    public string Trend4H { get; set; } = "--";
    public string Trend1H { get; set; } = "--";
    public string WaveState { get; set; } = "--";
    public int TrendScore { get; set; }
    public int WaveScore { get; set; }
    public int TimingScore { get; set; }
    public decimal Adx1H { get; set; }
    public decimal AtrPercent { get; set; }
    public decimal PositionPercent { get; set; }
    public decimal DistanceToEmaPercent { get; set; }
    public decimal ExpectedMovePercent { get; set; }

    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }

    public int StableCycles { get; set; }
    public bool AutoEligible { get; set; }
    public DateTime LastAnalyzedUtc { get; set; }
}

public sealed class CandlePoint
{
    public DateTime OpenTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public sealed class PositionRow
{
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal PeakUnrealizedPnl { get; set; }
    public decimal GivebackPercent { get; set; }
    public string Protection { get; set; } = "CHƯA XÁC MINH";
    public string Health { get; set; } = "CHƯA ĐÁNH GIÁ";
    public string Recommendation { get; set; } = "THEO DÕI";
}

public sealed class OrderRow
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = "";
    public string Type { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Price { get; set; }
    public decimal StopPrice { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = "";
    public bool ReduceOnly { get; set; }
    public bool IsAlgo { get; set; }
}

public sealed class AccountState
{
    public decimal TotalWalletBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal TotalUnrealizedProfit { get; set; }
}

public sealed class TradingConfig
{
    public decimal MarginPerTrade { get; set; } = 2m;
    public int Leverage { get; set; } = 3;
    public int MaxPositions { get; set; } = 1;
    public int DeepScanCount { get; set; } = 24;
    public int ScanIntervalSeconds { get; set; } = 60;
    public int RequiredStableCycles { get; set; } = 2;
    public int MinimumScore { get; set; } = 80;
    public decimal MinimumRiskReward { get; set; } = 2.0m;
    public bool RequireIsolated { get; set; } = true;
    public bool AutoLiveEnabled { get; set; }
}

public sealed class DashboardState
{
    public ObservableCollection<MarketRow> Markets { get; } = [];
    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<OrderRow> Orders { get; } = [];
}

public sealed class SymbolTradingRules
{
    public string Symbol { get; init; } = "";
    public decimal TickSize { get; init; }
    public decimal StepSize { get; init; }
    public decimal MinQuantity { get; init; }
    public decimal MaxQuantity { get; init; }
    public decimal MinNotional { get; init; }
    public int PricePrecision { get; init; }
    public int QuantityPrecision { get; init; }
}

public sealed class ExecutionFill
{
    public long OrderId { get; init; }
    public string ClientOrderId { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal ExecutedQuantity { get; init; }
    public decimal AveragePrice { get; init; }
}

public sealed class ProtectionVerification
{
    public bool StopLossConfirmed { get; init; }
    public bool TakeProfitConfirmed { get; init; }
    public long StopAlgoId { get; init; }
    public long TakeProfitAlgoId { get; init; }
    public bool IsProtected => StopLossConfirmed && TakeProfitConfirmed;
}
