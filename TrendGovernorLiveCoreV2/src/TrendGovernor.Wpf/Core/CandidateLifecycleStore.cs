using System.Collections.Concurrent;

namespace TrendGovernor.Wpf;

public sealed class CandidateLifecycleStore
{
    private readonly ConcurrentDictionary<string, CandidateRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly SessionEventJournal _journal;
    private readonly string _sessionId;

    public CandidateLifecycleStore(SessionEventJournal journal, string sessionId)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("sessionId rỗng.", nameof(sessionId)) : sessionId;
    }

    public IReadOnlyCollection<CandidateRecord> Snapshot()
        => _records.Values.OrderByDescending(x => x.LastSeen).ToArray();

    public CandidateRecord GetOrCreate(string symbol, string snapshotId, DateTimeOffset now, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol rỗng.", nameof(symbol));
        return _records.GetOrAdd(symbol, key => new CandidateRecord
        {
            CandidateId = $"cand-{key}-{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            Symbol = key,
            FirstSeen = now,
            ExpiresAt = now.Add(ttl),
            SourceSnapshotId = snapshotId,
            LastSeen = now
        });
    }

    public async Task<CandidateRecord> ObserveAsync(
        MarketRow row,
        string snapshotId,
        bool stable,
        int requiredStableCycles,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        var now = DateTimeOffset.UtcNow;
        var record = GetOrCreate(row.Symbol, snapshotId, now, TimeSpan.FromMinutes(10));
        record.Refresh(stable, snapshotId, now, TimeSpan.FromMinutes(10));

        var target = ResolveTargetStage(row, record.StableCycles, requiredStableCycles);
        if (target != record.Stage && CandidateStagePolicy.CanTransition(record.Stage, target))
        {
            var previous = record.Stage;
            record.Transition(target, ResolveReasonCode(row, target));
            await _journal.AppendAsync(SessionEventJournal.Create(
                LifecycleEventType.CandidateStageChanged,
                _sessionId,
                row.Symbol,
                record.CandidateId,
                record.DecisionVersion,
                record.StageReason,
                new { previous = previous.ToString(), current = target.ToString(), row.Status, row.Reason, record.StableCycles }), ct);
        }

        return record;
    }

    public async Task TransitionAsync(CandidateRecord record, CandidateStage next, string reasonCode, object? payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var previous = record.Stage;
        record.Transition(next, reasonCode);
        await _journal.AppendAsync(SessionEventJournal.Create(
            LifecycleEventType.CandidateStageChanged,
            _sessionId,
            record.Symbol,
            record.CandidateId,
            record.DecisionVersion,
            reasonCode,
            new { previous = previous.ToString(), current = next.ToString(), payload }), ct);
    }

    public void RemoveTerminalExpired(DateTimeOffset now)
    {
        foreach (var pair in _records)
        {
            var terminal = pair.Value.Stage is CandidateStage.Audited or CandidateStage.Rejected or CandidateStage.Expired;
            if (terminal && pair.Value.ExpiresAt <= now)
                _records.TryRemove(pair.Key, out _);
        }
    }

    private static CandidateStage ResolveTargetStage(MarketRow row, int stableCycles, int requiredStableCycles)
    {
        if (row.Status.StartsWith("BLOCK", StringComparison.OrdinalIgnoreCase) || row.Status.Contains("LỖI", StringComparison.OrdinalIgnoreCase))
            return CandidateStage.Rejected;
        if (row.Status.Contains("CHỜ PULLBACK", StringComparison.OrdinalIgnoreCase) || row.Status.Contains("CHỜ XÁC NHẬN", StringComparison.OrdinalIgnoreCase))
            return CandidateStage.WaitingRetest;
        if (row.AutoEligible && stableCycles >= Math.Max(1, requiredStableCycles))
            return CandidateStage.PlanReady;
        return CandidateStage.Watching;
    }

    private static string ResolveReasonCode(MarketRow row, CandidateStage stage) => stage switch
    {
        CandidateStage.Rejected => "RADAR_REJECTED",
        CandidateStage.WaitingRetest => "ENTRY_WAIT_RETEST",
        CandidateStage.PlanReady => "PLAN_READY_STABLE",
        CandidateStage.Watching => "CANDIDATE_WATCHING",
        _ => string.IsNullOrWhiteSpace(row.Status) ? "UNSPECIFIED" : row.Status.Replace(' ', '_').ToUpperInvariant()
    };
}
