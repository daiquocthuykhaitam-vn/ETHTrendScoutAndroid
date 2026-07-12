namespace TrendGovernor.Wpf;

public sealed class FrozenTradePlan
{
    public string PlanId { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Direction { get; init; } = "";
    public string EntrySide { get; init; } = "";
    public string ExitSide { get; init; } = "";
    public decimal ReferencePrice { get; init; }
    public decimal EntryLow { get; init; }
    public decimal EntryHigh { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal RiskReward { get; init; }
    public decimal MarginUsdt { get; init; }
    public int Leverage { get; init; }
    public int Score { get; init; }
    public int StableCycles { get; init; }
    public decimal FundingRatePercent { get; init; }
    public int FundingIntervalHours { get; init; }
    public string FundingFlow { get; init; } = "";
    public string FundingLevel { get; init; } = "";
    public string FundingBias { get; init; } = "";
    public decimal EstimatedFundingForTrade { get; init; }
    public int FundingScore { get; init; }
    public DateTimeOffset? NextFundingTime { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool HasExtremeFundingCost => FundingBias == "CHI PHÍ GIỮ LỆNH" && Math.Abs(FundingRatePercent / Math.Max(1, FundingIntervalHours)) >= 0.25m;

    public static FrozenTradePlan FromCandidate(MarketRow candidate, decimal marginUsdt, int leverage)
    {
        FundingIntelligenceService.ApplyFundingDecision(candidate, marginUsdt * leverage);
        return new FrozenTradePlan
        {
            PlanId = $"TG-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{candidate.Symbol}",
            Symbol = candidate.Symbol,
            Direction = candidate.Direction,
            EntrySide = candidate.Direction == "LONG" ? "BUY" : "SELL",
            ExitSide = candidate.Direction == "LONG" ? "SELL" : "BUY",
            ReferencePrice = candidate.Price,
            EntryLow = candidate.EntryLow,
            EntryHigh = candidate.EntryHigh,
            StopLoss = candidate.StopLoss,
            TakeProfit = candidate.TakeProfit,
            RiskReward = candidate.RiskReward,
            MarginUsdt = marginUsdt,
            Leverage = leverage,
            Score = candidate.Score,
            StableCycles = candidate.StableCycles,
            FundingRatePercent = candidate.FundingRatePercent,
            FundingIntervalHours = candidate.FundingIntervalHours,
            FundingFlow = candidate.FundingFlow,
            FundingLevel = candidate.FundingLevel,
            FundingBias = candidate.FundingBias,
            EstimatedFundingForTrade = candidate.EstimatedFundingForTrade,
            FundingScore = candidate.FundingScore,
            NextFundingTime = candidate.NextFundingTime,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(5)
        };
    }
}
