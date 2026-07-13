using System.Collections.Concurrent;

namespace TrendGovernor.Wpf;

public sealed record RadarFilterEvent(
    DateTimeOffset Time,
    string SnapshotId,
    string Symbol,
    string Stage,
    bool Passed,
    string ReasonCode,
    decimal Value,
    decimal Threshold);

public sealed record RadarFunnelSnapshot(
    string SnapshotId,
    DateTimeOffset Time,
    int ExchangeSymbols,
    int TradingPerpetualUsdt,
    int PassedAge,
    int PassedLiquidity,
    int PassedSpread,
    int PassedVolatility,
    int UniverseSelected,
    int Analyzed,
    int AutoEligible,
    int PlanReady);

public sealed class RadarDiagnosticsStore
{
    private readonly ConcurrentQueue<RadarFilterEvent> _events = new();
    private readonly ConcurrentQueue<RadarFunnelSnapshot> _snapshots = new();
    private const int MaxEvents = 20_000;
    private const int MaxSnapshots = 1_000;

    public IReadOnlyCollection<RadarFilterEvent> Events => _events.ToArray();
    public IReadOnlyCollection<RadarFunnelSnapshot> Snapshots => _snapshots.ToArray();
    public RadarFunnelSnapshot? Latest => _snapshots.TryPeek(out var value) ? value : _snapshots.LastOrDefault();

    public void AddEvent(RadarFilterEvent item)
    {
        _events.Enqueue(item);
        while (_events.Count > MaxEvents) _events.TryDequeue(out _);
    }

    public void AddSnapshot(RadarFunnelSnapshot item)
    {
        _snapshots.Enqueue(item);
        while (_snapshots.Count > MaxSnapshots) _snapshots.TryDequeue(out _);
    }
}

public sealed record RadarUniverseDiagnostics(
    int ExchangeSymbols,
    int TradingPerpetualUsdt,
    int PassedAge,
    int PassedLiquidity,
    int PassedSpread,
    int PassedVolatility,
    int Selected);
