namespace TrendGovernor.Wpf;

public sealed class TradeVerificationService
{
    private const int RequiredStableCycles = 1;
    private const int MinimumScore = 75;
    private const decimal MinimumRiskReward = 1.80m;

    public VerifyGrant Verify(
        CandidateRecord candidate,
        FrozenTradePlan plan,
        MarketRow market,
        SymbolTradingRules rules,
        decimal normalizedQuantity,
        decimal currentPrice,
        bool accountIsOneWay,
        bool isolatedReady,
        int currentPositionCount,
        int maxPositions,
        bool newEntriesBlocked,
        string snapshotId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(rules);

        var entryBand = Math.Abs(plan.EntryHigh - plan.EntryLow);
        var entryTolerance = Math.Max(currentPrice * 0.0015m, entryBand * 0.50m);
        var entryWindowPassed = currentPrice >= plan.EntryLow - entryTolerance &&
                                currentPrice <= plan.EntryHigh + entryTolerance;

        var gates = new List<VerifyGateResult>
        {
            Gate("SYSTEM_ENTRY_LOCK", !newEntriesBlocked, newEntriesBlocked ? "Hệ thống đang khóa lệnh mới." : "Không có khóa lệnh mới."),
            Gate("CANDIDATE_STAGE", candidate.Stage == CandidateStage.Verifying || candidate.Stage == CandidateStage.PlanReady,
                $"Candidate stage hiện tại: {candidate.Stage}."),
            Gate("PLAN_NOT_EXPIRED", !plan.IsExpired, plan.IsExpired ? "Kế hoạch đã hết hạn." : "Kế hoạch còn hiệu lực."),
            Gate("PLAN_SYMBOL_MATCH", string.Equals(plan.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase),
                "Symbol Plan/Market phải khớp."),
            Gate("PLAN_DIRECTION", plan.Direction is "LONG" or "SHORT", $"Direction: {plan.Direction}."),
            Gate("TREND_ALIGNMENT", market.Trend1D == market.Trend4H && market.Trend4H == market.Trend1H && market.Trend1H is "TĂNG" or "GIẢM",
                $"Trend 1D/4H/1H: {market.Trend1D}/{market.Trend4H}/{market.Trend1H}."),
            Gate("CANDIDATE_STABILITY", candidate.StableCycles >= RequiredStableCycles,
                $"Ổn định {candidate.StableCycles}/{RequiredStableCycles} chu kỳ."),
            Gate("MINIMUM_SCORE", plan.Score >= MinimumScore, $"Score {plan.Score}/{MinimumScore}."),
            Gate("MINIMUM_RR", plan.RiskReward >= MinimumRiskReward, $"RR {plan.RiskReward:0.00}/{MinimumRiskReward:0.00}."),
            Gate("ENTRY_WINDOW", entryWindowPassed,
                $"Giá {currentPrice:0.########}; vùng {plan.EntryLow:0.########}-{plan.EntryHigh:0.########}; dung sai {entryTolerance:0.########}."),
            Gate("SL_TP_GEOMETRY", ValidProtectionGeometry(plan.Direction, currentPrice, plan.StopLoss, plan.TakeProfit),
                $"SL {plan.StopLoss:0.########}; TP {plan.TakeProfit:0.########}."),
            Gate("FUNDING_NOT_BLOCKED", !market.FundingWarning.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase),
                string.IsNullOrWhiteSpace(market.FundingWarning) ? "Funding không chặn." : market.FundingWarning),
            Gate("ONE_WAY_MODE", accountIsOneWay, accountIsOneWay ? "One-way Mode." : "Tài khoản đang Hedge Mode."),
            Gate("ISOLATED_READY", isolatedReady, isolatedReady ? "ISOLATED sẵn sàng." : "Chưa xác minh ISOLATED."),
            Gate("POSITION_CAPACITY", currentPositionCount < Math.Max(1, maxPositions),
                $"Vị thế bot phiên hiện tại {currentPositionCount}/{Math.Max(1, maxPositions)}."),
            Gate("QUANTITY_VALID", normalizedQuantity > 0m, $"Quantity chuẩn hóa: {normalizedQuantity:0.########}."),
            Gate("MIN_NOTIONAL", rules.MinNotional <= 0m || normalizedQuantity * currentPrice >= rules.MinNotional,
                $"Notional {normalizedQuantity * currentPrice:0.########}; minimum {rules.MinNotional:0.########}."),
            Gate("PRICE_RULES", rules.TickSize > 0m && rules.StepSize > 0m,
                $"tickSize={rules.TickSize}; stepSize={rules.StepSize}.")
        };

        return new VerifyGrant
        {
            GrantId = $"grant-{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            PlanId = plan.PlanId,
            CandidateId = candidate.CandidateId,
            Symbol = plan.Symbol,
            SnapshotId = snapshotId,
            IssuedAt = now,
            ExpiresAt = now.AddSeconds(20),
            Gates = gates
        };
    }

    public static OrderIntent CreateMarketOrderIntent(VerifyGrant grant, FrozenTradePlan plan, decimal quantity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(plan);
        grant.Consume(now);

        return new OrderIntent(
            OrderIntentId: $"intent-{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            GrantId: grant.GrantId,
            PlanId: plan.PlanId,
            CandidateId: grant.CandidateId,
            Symbol: plan.Symbol,
            Side: plan.EntrySide,
            Type: "MARKET",
            Quantity: quantity,
            Price: null,
            ReduceOnly: false,
            CreatedAt: now);
    }

    private static VerifyGateResult Gate(string code, bool passed, string reason)
        => new(code, passed, reason);

    private static bool ValidProtectionGeometry(string direction, decimal price, decimal stopLoss, decimal takeProfit)
        => direction switch
        {
            "LONG" => stopLoss > 0m && takeProfit > 0m && stopLoss < price && takeProfit > price,
            "SHORT" => stopLoss > 0m && takeProfit > 0m && takeProfit < price && stopLoss > price,
            _ => false
        };
}
