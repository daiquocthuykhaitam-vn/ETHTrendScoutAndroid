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
    public DateTime CreatedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;

    public static FrozenTradePlan FromCandidate(MarketRow candidate, decimal marginUsdt, int leverage)
        => new()
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
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(5)
        };
}
