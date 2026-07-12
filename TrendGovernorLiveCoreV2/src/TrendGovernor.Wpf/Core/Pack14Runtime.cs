using System.Collections.Concurrent;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class Pack14Runtime
{
    private readonly CandidateLifecycleStore _candidates;
    private readonly TradeVerificationService _verification = new();
    private readonly SessionEventJournal _journal;
    private readonly ConcurrentDictionary<string, VerifyGrant> _grants = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OrderIntent> _intents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ExecutionAuditRecord> _audit = new();
    private readonly ConcurrentQueue<MissedOpportunityRecord> _missed = new();

    public Pack14Runtime(string baseDirectory, string sessionId)
    {
        SessionId = sessionId;
        _journal = new SessionEventJournal(baseDirectory, sessionId);
        _candidates = new CandidateLifecycleStore(_journal, sessionId);
    }

    public string SessionId { get; }
    public SessionEventJournal Journal => _journal;
    public IReadOnlyCollection<CandidateRecord> Candidates => _candidates.Snapshot();
    public IReadOnlyCollection<VerifyGrant> Grants => _grants.Values.OrderByDescending(x => x.IssuedAt).ToArray();
    public IReadOnlyCollection<OrderIntent> Intents => _intents.Values.OrderByDescending(x => x.CreatedAt).ToArray();
    public IReadOnlyCollection<ExecutionAuditRecord> Audit => _audit.ToArray();
    public IReadOnlyCollection<MissedOpportunityRecord> MissedOpportunities => _missed.ToArray();

    public CandidateRecord? FindCandidate(string symbol)
        => _candidates.Snapshot().FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    public async Task<CandidateRecord> ObserveAsync(MarketRow row, string snapshotId, int requiredStableCycles, CancellationToken ct)
    {
        var record = await _candidates.ObserveAsync(row, snapshotId, row.AutoEligible, requiredStableCycles, ct);
        ApplyCandidateToRow(row, record);
        return record;
    }

    public async Task RecordPlanAsync(CandidateRecord candidate, FrozenTradePlan plan, CancellationToken ct)
    {
        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.PlanCreated,
            SessionId,
            plan.Symbol,
            plan.PlanId,
            candidate.DecisionVersion,
            "PLAN_CREATED",
            plan), ct);
    }

    public async Task<VerifyGrant> VerifyAsync(
        CandidateRecord candidate,
        FrozenTradePlan plan,
        MarketRow market,
        SymbolTradingRules rules,
        decimal quantity,
        bool accountIsOneWay,
        bool isolatedReady,
        int positionCount,
        int maxPositions,
        bool blocked,
        CancellationToken ct)
    {
        if (candidate.Stage == CandidateStage.PlanReady)
            await _candidates.TransitionAsync(candidate, CandidateStage.Verifying, "VERIFY_STARTED", null, ct);

        var grant = _verification.Verify(
            candidate,
            plan,
            market,
            rules,
            quantity,
            market.Price,
            accountIsOneWay,
            isolatedReady,
            positionCount,
            maxPositions,
            blocked,
            market.SourceSnapshotId,
            DateTimeOffset.UtcNow);

        _grants[grant.GrantId] = grant;
        var reason = grant.Passed
            ? "VERIFY_PASS"
            : string.Join(";", grant.Gates.Where(x => !x.Passed).Select(x => x.GateCode));

        await _journal.AppendAsync(SessionEventJournal.Create(
            grant.Passed ? LifecycleEventType.VerifyPassed : LifecycleEventType.VerifyBlocked,
            SessionId,
            market.Symbol,
            plan.PlanId,
            candidate.DecisionVersion,
            reason,
            new { grant.GrantId, grant.ExpiresAt, grant.Gates }), ct);

        await _candidates.TransitionAsync(
            candidate,
            grant.Passed ? CandidateStage.Authorized : CandidateStage.WaitingRetest,
            reason,
            null,
            ct);

        ApplyCandidateToRow(market, candidate);
        return grant;
    }

    public async Task<OrderIntent> CreateIntentAsync(
        VerifyGrant grant,
        FrozenTradePlan plan,
        decimal quantity,
        CandidateRecord candidate,
        CancellationToken ct)
    {
        var intent = TradeVerificationService.CreateMarketOrderIntent(grant, plan, quantity, DateTimeOffset.UtcNow);
        if (!_intents.TryAdd(intent.OrderIntentId, intent))
            throw new InvalidOperationException("OrderIntent bị trùng trong session.");
        if (!await _journal.TryMarkProcessedAsync(intent.OrderIntentId, ct))
            throw new InvalidOperationException("OrderIntent đã được xử lý trước đó.");

        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.OrderIntentCreated,
            SessionId,
            intent.Symbol,
            intent.OrderIntentId,
            candidate.DecisionVersion,
            "ORDER_INTENT_CREATED",
            intent), ct);

        await _candidates.TransitionAsync(candidate, CandidateStage.OrderPending, "ORDER_INTENT_CREATED", null, ct);
        return intent;
    }

    public async Task MarkOrderSubmittedAsync(CandidateRecord candidate, OrderIntent intent, CancellationToken ct)
    {
        _audit.Enqueue(new(DateTimeOffset.UtcNow, intent.Symbol, intent.OrderIntentId, "ORDER", "SUBMITTED", JsonSerializer.Serialize(intent)));
        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.OrderSubmitted,
            SessionId,
            intent.Symbol,
            intent.OrderIntentId,
            candidate.DecisionVersion,
            "ORDER_SUBMITTED",
            intent), ct);
    }

    public async Task MarkOrderTerminalAsync(CandidateRecord candidate, OrderIntent intent, ExecutionFill fill, CancellationToken ct)
    {
        _audit.Enqueue(new(DateTimeOffset.UtcNow, intent.Symbol, intent.OrderIntentId, "ORDER", fill.Status, JsonSerializer.Serialize(fill)));

        var eventType = fill.Status switch
        {
            "FILLED" => LifecycleEventType.OrderFilled,
            "PARTIALLY_FILLED" => LifecycleEventType.OrderPartiallyFilled,
            "CANCELED" or "EXPIRED" => LifecycleEventType.OrderCanceled,
            _ => LifecycleEventType.OrderRejected
        };

        await _journal.AppendAsync(SessionEventJournal.Create(
            eventType,
            SessionId,
            intent.Symbol,
            intent.OrderIntentId,
            candidate.DecisionVersion,
            $"ORDER_{fill.Status}",
            fill), ct);

        if (fill.Status == "FILLED")
            await _candidates.TransitionAsync(candidate, CandidateStage.Filled, "FILL_CONFIRMED", fill, ct);
        else if (fill.Status is "CANCELED" or "EXPIRED" or "REJECTED")
            await _candidates.TransitionAsync(candidate, CandidateStage.Rejected, $"ORDER_{fill.Status}", fill, ct);
    }

    public async Task MarkProtectionAsync(
        CandidateRecord candidate,
        string correlationId,
        ProtectionVerification protection,
        CancellationToken ct)
    {
        _audit.Enqueue(new(
            DateTimeOffset.UtcNow,
            candidate.Symbol,
            correlationId,
            "PROTECTION",
            protection.IsProtected ? "CONFIRMED" : "FAILED",
            JsonSerializer.Serialize(protection)));

        await _journal.AppendAsync(SessionEventJournal.Create(
            protection.IsProtected ? LifecycleEventType.ProtectionConfirmed : LifecycleEventType.ProtectionFailed,
            SessionId,
            candidate.Symbol,
            correlationId,
            candidate.DecisionVersion,
            protection.IsProtected ? "PROTECTION_CONFIRMED" : "PROTECTION_FAILED",
            protection), ct);

        await _candidates.TransitionAsync(
            candidate,
            protection.IsProtected ? CandidateStage.Protected : CandidateStage.Closed,
            protection.IsProtected ? "PROTECTION_CONFIRMED" : "UNPROTECTED_EMERGENCY_CLOSE",
            protection,
            ct);
    }

    public async Task MarkManagingAsync(string symbol, string reason, object payload, CancellationToken ct)
    {
        var candidate = FindCandidate(symbol);
        if (candidate is not null && candidate.Stage == CandidateStage.Protected)
            await _candidates.TransitionAsync(candidate, CandidateStage.Managing, reason, payload, ct);

        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.PositionRiskChanged,
            SessionId,
            symbol,
            candidate?.CandidateId ?? symbol,
            candidate?.DecisionVersion ?? 0,
            reason,
            payload), ct);
    }

    public async Task MarkClosedAsync(string symbol, string reason, object payload, CancellationToken ct)
    {
        var candidate = FindCandidate(symbol);
        if (candidate is not null && candidate.Stage is CandidateStage.Filled or CandidateStage.Protected or CandidateStage.Managing)
            await _candidates.TransitionAsync(candidate, CandidateStage.Closed, reason, payload, ct);

        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.ExitConfirmed,
            SessionId,
            symbol,
            candidate?.CandidateId ?? symbol,
            candidate?.DecisionVersion ?? 0,
            reason,
            payload), ct);
    }

    public void RecordMissed(string symbol, string candidateId, string stage, string reasonCode, string detail)
        => _missed.Enqueue(new(DateTimeOffset.UtcNow, symbol, candidateId, stage, reasonCode, detail));

    public IReadOnlyCollection<LifecycleBoardRow> BuildLifecycleRows()
        => Candidates.Select(x => new LifecycleBoardRow
        {
            Symbol = x.Symbol,
            CandidateId = x.CandidateId,
            Stage = x.Stage.ToString(),
            StableCycles = x.StableCycles,
            DecisionVersion = x.DecisionVersion,
            Reason = x.StageReason,
            LastSeen = x.LastSeen
        }).ToArray();

    public IReadOnlyCollection<VerifyMatrixRow> BuildVerifyRows()
        => Grants.SelectMany(grant => grant.Gates.Select(gate => new VerifyMatrixRow
        {
            Symbol = grant.Symbol,
            PlanId = grant.PlanId,
            GrantId = grant.GrantId,
            GateCode = gate.GateCode,
            Passed = gate.Passed,
            Reason = gate.Reason,
            IssuedAt = grant.IssuedAt
        })).OrderByDescending(x => x.IssuedAt).ToArray();

    private static void ApplyCandidateToRow(MarketRow row, CandidateRecord record)
    {
        row.CandidateId = record.CandidateId;
        row.CandidateStage = record.Stage.ToString();
        row.StableCycles = record.StableCycles;
        row.DecisionVersion = record.DecisionVersion;
        row.SourceSnapshotId = record.SourceSnapshotId;
    }
}
