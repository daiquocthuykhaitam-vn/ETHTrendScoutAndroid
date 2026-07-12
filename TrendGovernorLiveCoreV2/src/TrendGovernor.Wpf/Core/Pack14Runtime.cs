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

    public Pack14Runtime(string baseDirectory, string sessionId)
    {
        SessionId = sessionId;
        _journal = new SessionEventJournal(baseDirectory, sessionId);
        _candidates = new CandidateLifecycleStore(_journal, sessionId);
    }

    public string SessionId { get; }
    public SessionEventJournal Journal => _journal;
    public IReadOnlyCollection<CandidateRecord> Candidates => _candidates.Snapshot();
    public IReadOnlyCollection<VerifyGrant> Grants => _grants.Values.ToArray();
    public IReadOnlyCollection<OrderIntent> Intents => _intents.Values.ToArray();
    public IReadOnlyCollection<ExecutionAuditRecord> Audit => _audit.ToArray();

    public async Task<CandidateRecord> ObserveAsync(MarketRow row, string snapshotId, int requiredStableCycles, CancellationToken ct)
    {
        var record = await _candidates.ObserveAsync(row, snapshotId, row.AutoEligible, requiredStableCycles, ct);
        row.CandidateId = record.CandidateId;
        row.CandidateStage = record.Stage.ToString();
        row.StableCycles = record.StableCycles;
        row.DecisionVersion = record.DecisionVersion;
        row.SourceSnapshotId = record.SourceSnapshotId;
        return record;
    }

    public async Task<VerifyGrant> VerifyAsync(CandidateRecord candidate, FrozenTradePlan plan, MarketRow market,
        SymbolTradingRules rules, decimal quantity, bool accountIsOneWay, bool isolatedReady,
        int positionCount, int maxPositions, bool blocked, CancellationToken ct)
    {
        if (candidate.Stage == CandidateStage.PlanReady)
            await _candidates.TransitionAsync(candidate, CandidateStage.Verifying, "VERIFY_STARTED", null, ct);

        var grant = _verification.Verify(candidate, plan, market, rules, quantity, market.Price,
            accountIsOneWay, isolatedReady, positionCount, maxPositions, blocked,
            market.SourceSnapshotId, DateTimeOffset.UtcNow);
        _grants[grant.GrantId] = grant;
        var reason = grant.Passed ? "VERIFY_PASS" : string.Join(";", grant.Gates.Where(x => !x.Passed).Select(x => x.GateCode));
        await _journal.AppendAsync(SessionEventJournal.Create(grant.Passed ? LifecycleEventType.VerifyPassed : LifecycleEventType.VerifyBlocked,
            SessionId, market.Symbol, plan.PlanId, candidate.DecisionVersion, reason, new { grant.GrantId, grant.ExpiresAt, grant.Gates }), ct);
        await _candidates.TransitionAsync(candidate, grant.Passed ? CandidateStage.Authorized : CandidateStage.WaitingRetest, reason, null, ct);
        market.CandidateStage = candidate.Stage.ToString();
        return grant;
    }

    public async Task<OrderIntent> CreateIntentAsync(VerifyGrant grant, FrozenTradePlan plan, decimal quantity, CandidateRecord candidate, CancellationToken ct)
    {
        var intent = TradeVerificationService.CreateMarketOrderIntent(grant, plan, quantity, DateTimeOffset.UtcNow);
        if (!_intents.TryAdd(intent.OrderIntentId, intent) || !await _journal.TryMarkProcessedAsync(intent.OrderIntentId, ct))
            throw new InvalidOperationException("OrderIntent đã tồn tại hoặc đã xử lý.");
        await _journal.AppendAsync(SessionEventJournal.Create(LifecycleEventType.OrderIntentCreated, SessionId, intent.Symbol,
            intent.OrderIntentId, candidate.DecisionVersion, "ORDER_INTENT_CREATED", intent), ct);
        await _candidates.TransitionAsync(candidate, CandidateStage.OrderPending, "ORDER_INTENT_CREATED", null, ct);
        return intent;
    }

    public async Task MarkFillAsync(CandidateRecord candidate, OrderIntent intent, ExecutionFill fill, CancellationToken ct)
    {
        _audit.Enqueue(new(DateTimeOffset.UtcNow, intent.Symbol, intent.OrderIntentId, "FILL", fill.Status, JsonSerializer.Serialize(fill)));
        await _journal.AppendAsync(SessionEventJournal.Create(LifecycleEventType.OrderFilled, SessionId, intent.Symbol,
            intent.OrderIntentId, candidate.DecisionVersion, "FILL_CONFIRMED", fill), ct);
        await _candidates.TransitionAsync(candidate, CandidateStage.Filled, "FILL_CONFIRMED", fill, ct);
    }

    public async Task MarkProtectionAsync(CandidateRecord candidate, string correlationId, ProtectionVerification protection, CancellationToken ct)
    {
        _audit.Enqueue(new(DateTimeOffset.UtcNow, candidate.Symbol, correlationId, "PROTECTION",
            protection.IsProtected ? "CONFIRMED" : "FAILED", JsonSerializer.Serialize(protection)));
        await _journal.AppendAsync(SessionEventJournal.Create(protection.IsProtected ? LifecycleEventType.ProtectionConfirmed : LifecycleEventType.ProtectionFailed,
            SessionId, candidate.Symbol, correlationId, candidate.DecisionVersion,
            protection.IsProtected ? "PROTECTION_CONFIRMED" : "PROTECTION_FAILED", protection), ct);
        await _candidates.TransitionAsync(candidate, protection.IsProtected ? CandidateStage.Protected : CandidateStage.Closed,
            protection.IsProtected ? "PROTECTION_CONFIRMED" : "UNPROTECTED_EMERGENCY_CLOSE", protection, ct);
    }
}
