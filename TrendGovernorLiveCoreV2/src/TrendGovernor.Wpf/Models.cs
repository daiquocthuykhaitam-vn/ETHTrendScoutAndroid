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

    public decimal FundingRate { get; set; }
    public decimal FundingRatePercent { get; set; }
    public int FundingIntervalHours { get; set; }
    public decimal FundingPerHourPercent { get; set; }
    public decimal Funding24hPercent { get; set; }
    public DateTimeOffset? NextFundingTime { get; set; }
    public int FundingMinutesRemaining { get; set; }
    public string FundingFlow { get; set; } = "CHƯA CÓ";
    public string FundingLevel { get; set; } = "CHƯA CÓ";
    public decimal FundingAveragePercent { get; set; }
    public int FundingSameSignPeriods { get; set; }
    public int FundingSignChanges { get; set; }
    public string FundingStability { get; set; } = "CHƯA CÓ";
    public decimal MarkPrice { get; set; }
    public decimal IndexPrice { get; set; }
    public decimal PremiumPercent { get; set; }
    public decimal EstimatedFundingForTrade { get; set; }
    public int FundingScore { get; set; }
    public string FundingBias { get; set; } = "TRUNG LẬP";
    public bool FundingFavorsDirection { get; set; }
    public string FundingWarning { get; set; } = "";

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
    public decimal RealizedFunding { get; set; }
    public decimal EstimatedNextFunding { get; set; }
    public decimal NetPnlAfterFunding { get; set; }
    public decimal ProjectedNetPnl { get; set; }
    public decimal FundingRatePercent { get; set; }
    public int FundingIntervalHours { get; set; }
    public DateTimeOffset? NextFundingTime { get; set; }
    public string FundingFlow { get; set; } = "CHƯA CÓ";
    public string FundingLevel { get; set; } = "CHƯA CÓ";
    public decimal PeakUnrealizedPnl { get; set; }
    public decimal PeakNetPnl { get; set; }
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

public sealed class FundingIncomeRow
{
    public string Symbol { get; init; } = "";
    public decimal Income { get; init; }
    public string Asset { get; init; } = "USDT";
    public DateTimeOffset Time { get; init; }
    public long TransactionId { get; init; }
}

public sealed class FundingSnapshot
{
    public string Symbol { get; init; } = "";
    public decimal MarkPrice { get; init; }
    public decimal IndexPrice { get; init; }
    public decimal PremiumPercent { get; init; }
    public decimal FundingRate { get; init; }
    public decimal FundingRatePercent { get; init; }
    public int FundingIntervalHours { get; init; }
    public decimal FundingPerHourPercent { get; init; }
    public decimal Funding24hPercent { get; init; }
    public DateTimeOffset? NextFundingTime { get; init; }
    public int MinutesRemaining { get; init; }
    public string Flow { get; init; } = "CHƯA CÓ";
    public string Level { get; init; } = "CHƯA CÓ";
    public decimal AverageFundingPercent { get; init; }
    public int SameSignPeriods { get; init; }
    public int SignChanges { get; init; }
    public string Stability { get; init; } = "CHƯA CÓ";
    public decimal EstimatedLongFunding { get; init; }
    public decimal EstimatedShortFunding { get; init; }
}

public sealed class AccountState
{
    public decimal TotalWalletBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal TotalUnrealizedProfit { get; set; }
    public decimal FundingIncome24h { get; set; }
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
    public int FundingWeightPercent { get; set; } = 10;
    public bool BlockExtremeFundingCost { get; set; } = true;
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
