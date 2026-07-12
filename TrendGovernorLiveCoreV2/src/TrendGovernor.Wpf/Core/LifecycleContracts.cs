namespace TrendGovernor.Wpf;

public enum CandidateStage
{
    Discovered,
    Watching,
    WaitingRetest,
    PlanReady,
    Verifying,
    Authorized,
    OrderPending,
    Filled,
    Protected,
    Managing,
    Closed,
    Audited,
    Rejected,
    Expired
}

public enum LifecycleEventType
{
    CandleClosed,
    MarketSnapshotCaptured,
    RadarCandidateFound,
    CandidateStageChanged,
    EntryWindowOpened,
    PlanCreated,
    VerifyPassed,
    VerifyBlocked,
    OrderIntentCreated,
    OrderSubmitted,
    OrderPartiallyFilled,
    OrderFilled,
    ProtectionRequested,
    ProtectionConfirmed,
    ProtectionFailed,
    PositionRiskChanged,
    ExitRequested,
    ExitConfirmed,
    SessionLocked
}

public sealed record LifecycleEvent(
    string EventId,
    LifecycleEventType EventType,
    DateTimeOffset EventTime,
    DateTimeOffset SourceTime,
    string SessionId,
    string Symbol,
    string CorrelationId,
    int DecisionVersion,
    string ReasonCode,
    string PayloadJson);

public sealed class CandidateRecord
{
    public string CandidateId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public CandidateStage Stage { get; private set; } = CandidateStage.Discovered;
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; private set; }
    public int StableCycles { get; private set; }
    public string StageReason { get; private set; } = "RADAR_DISCOVERED";
    public DateTimeOffset ExpiresAt { get; private set; }
    public int DecisionVersion { get; private set; }
    public string SourceSnapshotId { get; private set; } = string.Empty;

    public void Refresh(bool stable, string snapshotId, DateTimeOffset now, TimeSpan ttl)
    {
        LastSeen = now;
        SourceSnapshotId = snapshotId;
        ExpiresAt = now.Add(ttl);
        StableCycles = stable ? StableCycles + 1 : 0;
        DecisionVersion++;
    }

    public void Transition(CandidateStage next, string reason)
    {
        if (!CandidateStagePolicy.CanTransition(Stage, next))
            throw new InvalidOperationException($"Candidate transition không hợp lệ: {Stage} -> {next}.");

        Stage = next;
        StageReason = string.IsNullOrWhiteSpace(reason) ? "UNSPECIFIED" : reason.Trim();
        DecisionVersion++;
    }
}

public static class CandidateStagePolicy
{
    private static readonly IReadOnlyDictionary<CandidateStage, CandidateStage[]> Allowed =
        new Dictionary<CandidateStage, CandidateStage[]>
        {
            [CandidateStage.Discovered] = [CandidateStage.Watching, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.Watching] = [CandidateStage.WaitingRetest, CandidateStage.PlanReady, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.WaitingRetest] = [CandidateStage.PlanReady, CandidateStage.Watching, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.PlanReady] = [CandidateStage.Verifying, CandidateStage.WaitingRetest, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.Verifying] = [CandidateStage.Authorized, CandidateStage.WaitingRetest, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.Authorized] = [CandidateStage.OrderPending, CandidateStage.Expired],
            [CandidateStage.OrderPending] = [CandidateStage.Filled, CandidateStage.Rejected, CandidateStage.Expired],
            [CandidateStage.Filled] = [CandidateStage.Protected, CandidateStage.Closed],
            [CandidateStage.Protected] = [CandidateStage.Managing, CandidateStage.Closed],
            [CandidateStage.Managing] = [CandidateStage.Closed],
            [CandidateStage.Closed] = [CandidateStage.Audited],
            [CandidateStage.Audited] = [],
            [CandidateStage.Rejected] = [],
            [CandidateStage.Expired] = []
        };

    public static bool CanTransition(CandidateStage current, CandidateStage next)
        => current == next || (Allowed.TryGetValue(current, out var targets) && targets.Contains(next));
}

public sealed record VerifyGateResult(string GateCode, bool Passed, string Reason);

public sealed class VerifyGrant
{
    public string GrantId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string SnapshotId { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlyList<VerifyGateResult> Gates { get; init; } = Array.Empty<VerifyGateResult>();
    public DateTimeOffset? UsedAt { get; private set; }

    public bool Passed => Gates.Count > 0 && Gates.All(x => x.Passed);
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsUsed => UsedAt.HasValue;

    public void Consume(DateTimeOffset now)
    {
        if (!Passed) throw new InvalidOperationException("VerifyGrant không PASS.");
        if (IsExpired(now)) throw new InvalidOperationException("VerifyGrant đã hết hạn.");
        if (IsUsed) throw new InvalidOperationException("VerifyGrant đã được sử dụng.");
        UsedAt = now;
    }
}

public sealed record OrderIntent(
    string OrderIntentId,
    string GrantId,
    string PlanId,
    string CandidateId,
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal? Price,
    bool ReduceOnly,
    DateTimeOffset CreatedAt);
