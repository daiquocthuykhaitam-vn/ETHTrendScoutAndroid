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
        _sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("sessionId rỗng.", nameof(sessionId))
            : sessionId;
    }

    public IReadOnlyCollection<CandidateRecord> Snapshot()
        => _records.Values.OrderByDescending(x => x.LastSeen).ToArray();

    public CandidateRecord GetOrCreate(string symbol, string snapshotId, DateTimeOffset now, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol rỗng.", nameof(symbol));

        return _records.AddOrUpdate(
            symbol,
            key => Create(key, snapshotId, now, ttl),
            (key, current) => IsTerminal(current.Stage) && current.LastSeen < now.AddSeconds(-5)
                ? Create(key, snapshotId, now, ttl)
                : current);
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
                new
                {
                    previous = previous.ToString(),
                    current = target.ToString(),
                    row.Status,
                    row.Reason,
                    record.StableCycles,
                    row.RadarRank,
                    row.UniverseSource,
                    row.UniverseScore
                }), ct);
        }

        return record;
    }

    public async Task TransitionAsync(
        CandidateRecord record,
        CandidateStage next,
        string reasonCode,
        object? payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Stage == next) return;
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
            if (IsTerminal(pair.Value.Stage) && pair.Value.ExpiresAt <= now)
                _records.TryRemove(pair.Key, out _);
        }
    }

    private static CandidateRecord Create(string symbol, string snapshotId, DateTimeOffset now, TimeSpan ttl)
        => new()
        {
            CandidateId = $"cand-{symbol}-{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            Symbol = symbol,
            FirstSeen = now,
            ExpiresAt = now.Add(ttl),
            SourceSnapshotId = snapshotId,
            LastSeen = now
        };

    private static bool IsTerminal(CandidateStage stage)
        => stage is CandidateStage.Audited or CandidateStage.Rejected or CandidateStage.Expired or CandidateStage.Closed;

    private static CandidateStage ResolveTargetStage(MarketRow row, int stableCycles, int requiredStableCycles)
    {
        if (row.Status.Contains("CHỜ PULLBACK", StringComparison.OrdinalIgnoreCase) ||
            row.Status.Contains("CHỜ XÁC NHẬN", StringComparison.OrdinalIgnoreCase) ||
            row.Status.Contains("DATA_PENDING", StringComparison.OrdinalIgnoreCase))
            return CandidateStage.WaitingRetest;

        if (row.AutoEligible && stableCycles >= Math.Max(1, requiredStableCycles))
            return CandidateStage.PlanReady;

        return CandidateStage.Watching;
    }

    private static string ResolveReasonCode(MarketRow row, CandidateStage stage) => stage switch
    {
        CandidateStage.WaitingRetest => row.Status.Contains("DATA_PENDING", StringComparison.OrdinalIgnoreCase)
            ? "RADAR_DATA_PENDING"
            : "ENTRY_WAIT_RETEST",
        CandidateStage.PlanReady => "PLAN_READY_STABLE",
        CandidateStage.Watching when row.Status.StartsWith("BLOCK", StringComparison.OrdinalIgnoreCase) => "RADAR_BLOCKED_WATCH_ONLY",
        CandidateStage.Watching when row.Status.Contains("LỖI", StringComparison.OrdinalIgnoreCase) => "RADAR_ERROR_WATCH_ONLY",
        CandidateStage.Watching => "CANDIDATE_WATCHING",
        _ => string.IsNullOrWhiteSpace(row.Status)
            ? "UNSPECIFIED"
            : row.Status.Replace(' ', '_').ToUpperInvariant()
    };
}
