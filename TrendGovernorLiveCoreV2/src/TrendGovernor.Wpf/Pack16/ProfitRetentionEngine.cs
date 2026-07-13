namespace TrendGovernor.Wpf;

public enum ProfitAction
{
    Hold,
    Protect,
    CloseWrongDirection,
    CloseGiveback,
    CloseMissingProtection
}

public sealed record ProfitRetentionDecision(
    ProfitAction Action,
    string ReasonCode,
    string Detail,
    decimal PeakNetPnl,
    decimal CurrentNetPnl,
    decimal GivebackPercent,
    decimal ProfitFloor);

public sealed class ProfitRetentionEngine
{
    public ProfitRetentionDecision Evaluate(
        PositionRow position,
        MarketRow analysis,
        decimal peakNetPnl,
        Pack16Config config)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        var current = position.NetPnlAfterFunding;
        var peak = Math.Max(peakNetPnl, current);
        var giveback = peak > 0m ? Math.Max(0m, (peak - current) / peak * 100m) : 0m;
        var floor = peak >= config.ProfitFloorActivationUsdt
            ? Math.Max(0m, peak * (1m - config.MaxProfitGivebackPercent / 100m))
            : 0m;

        if (!position.StopLossConfirmed)
            return new(ProfitAction.CloseMissingProtection, "MISSING_HARD_SL", "Không xác minh được hard SL.", peak, current, giveback, floor);

        var wrongDirection = analysis.TrendScore >= 80 &&
            ((position.Side == "LONG" && analysis.Direction == "SHORT") ||
             (position.Side == "SHORT" && analysis.Direction == "LONG"));
        if (wrongDirection)
            return new(ProfitAction.CloseWrongDirection, "WRONG_DIRECTION", $"TrendScore={analysis.TrendScore}; hướng mới={analysis.Direction}.", peak, current, giveback, floor);

        if (peak >= config.ProfitFloorActivationUsdt && current <= floor)
            return new(ProfitAction.CloseGiveback, "PROFIT_FLOOR_BREACHED", $"Net PnL {current:0.####} <= profit floor {floor:0.####}.", peak, current, giveback, floor);

        if (peak >= config.ProfitFloorActivationUsdt && giveback >= config.MaxProfitGivebackPercent)
            return new(ProfitAction.CloseGiveback, "PROFIT_GIVEBACK", $"Giveback {giveback:0.0}% >= {config.MaxProfitGivebackPercent:0.0}%.", peak, current, giveback, floor);

        if (peak >= config.BreakevenActivationUsdt)
            return new(ProfitAction.Protect, "PROFIT_PROTECTION_ACTIVE", $"Peak {peak:0.####}; floor {floor:0.####}.", peak, current, giveback, floor);

        return new(ProfitAction.Hold, "HOLD_TREND", "Xu hướng chưa hỏng và profit floor chưa bị xuyên thủng.", peak, current, giveback, floor);
    }
}
