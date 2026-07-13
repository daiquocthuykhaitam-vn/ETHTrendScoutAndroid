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
    public bool LimitFirstEnabled { get; set; } = true;
    public int LimitTimeoutSeconds { get; set; } = 8;
    public bool AllowMarketFallback { get; set; } = true;
    public decimal BreakevenActivationUsdt { get; set; } = 0.20m;
    public decimal ProfitFloorActivationUsdt { get; set; } = 0.50m;
    public decimal MaxProfitGivebackPercent { get; set; } = 30m;
}

public sealed record PreSubmitContext(
    string Symbol,
    decimal CurrentPrice,
    decimal BidPrice,
    decimal AskPrice,
    decimal PlannedEntryLow,
    decimal PlannedEntryHigh,
    decimal OrderNotional,
    decimal CurrentBotMargin,
    decimal CurrentBotNotional,
    decimal CurrentSymbolNotional,
    decimal AccountEquity,
    int CurrentSymbolBotPositions,
    bool IsOneWay,
    bool IsIsolated,
    bool HasDuplicateIntent,
    bool NewEntriesBlocked);

public sealed record PreSubmitResult(bool Passed, string GateCode, string Reason, decimal SpreadPercent, decimal EntryDriftPercent);

public sealed class Pack16RiskPolicy
{
    public PreSubmitResult Evaluate(PreSubmitContext context, Pack16Config config)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(config);

        var spread = context.AskPrice > 0m && context.BidPrice > 0m
            ? Math.Max(0m, (context.AskPrice - context.BidPrice) / context.AskPrice * 100m)
            : decimal.MaxValue;
        var entryCenter = context.PlannedEntryLow > 0m && context.PlannedEntryHigh > 0m
            ? (context.PlannedEntryLow + context.PlannedEntryHigh) / 2m
            : context.CurrentPrice;
        var entryDrift = entryCenter > 0m
            ? Math.Abs(context.CurrentPrice - entryCenter) / entryCenter * 100m
            : decimal.MaxValue;

        if (context.NewEntriesBlocked) return Fail("SYSTEM_ENTRY_LOCK", "Hệ thống đang khóa lệnh mới.", spread, entryDrift);
        if (!context.IsOneWay) return Fail("ONE_WAY_MODE", "Tài khoản không ở One-way Mode.", spread, entryDrift);
        if (!context.IsIsolated) return Fail("ISOLATED_MODE", "Symbol chưa được xác minh ISOLATED.", spread, entryDrift);
        if (context.HasDuplicateIntent) return Fail("DUPLICATE_INTENT", "Đã tồn tại OrderIntent cho symbol/plan này.", spread, entryDrift);
        if (context.CurrentSymbolBotPositions >= config.MaxOpenPositionsPerSymbol)
            return Fail("SYMBOL_POSITION_LIMIT", $"Symbol đã có {context.CurrentSymbolBotPositions} vị thế bot.", spread, entryDrift);
        if (spread > config.MaxSpreadPercent)
            return Fail("SPREAD_TOO_WIDE", $"Spread {spread:0.####}% > {config.MaxSpreadPercent:0.####}%.", spread, entryDrift);
        if (entryDrift > config.MaxEntryDriftPercent)
            return Fail("ENTRY_DRIFT", $"Giá lệch vùng vào {entryDrift:0.####}% > {config.MaxEntryDriftPercent:0.####}%.", spread, entryDrift);
        if (context.CurrentBotMargin + context.OrderNotional <= 0m)
            return Fail("INVALID_CAPITAL", "Giá trị vốn không hợp lệ.", spread, entryDrift);
        if (context.CurrentBotMargin + context.OrderNotional > config.MaxBotMarginUsdt)
            return Fail("MAX_BOT_MARGIN", $"Bot margin sau lệnh vượt {config.MaxBotMarginUsdt:0.##} USDT.", spread, entryDrift);
        if (context.CurrentBotNotional + context.OrderNotional > config.MaxBotNotionalUsdt)
            return Fail("MAX_BOT_NOTIONAL", $"Bot notional sau lệnh vượt {config.MaxBotNotionalUsdt:0.##} USDT.", spread, entryDrift);
        if (context.CurrentSymbolNotional + context.OrderNotional > config.MaxSymbolNotionalUsdt)
            return Fail("MAX_SYMBOL_NOTIONAL", $"Notional symbol vượt {config.MaxSymbolNotionalUsdt:0.##} USDT.", spread, entryDrift);
        if (context.AccountEquity > 0m &&
            (context.CurrentBotNotional + context.OrderNotional) / context.AccountEquity * 100m > config.MaxExposurePercentOfEquity)
            return Fail("MAX_EXPOSURE", $"Exposure bot vượt {config.MaxExposurePercentOfEquity:0.##}% equity.", spread, entryDrift);

        return new PreSubmitResult(true, "PRE_SUBMIT_PASS", "Đủ điều kiện gửi lệnh.", spread, entryDrift);
    }

    private static PreSubmitResult Fail(string code, string reason, decimal spread, decimal drift)
        => new(false, code, reason, spread, drift);
}
