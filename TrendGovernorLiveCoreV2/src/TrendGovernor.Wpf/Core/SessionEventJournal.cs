using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class SessionEventJournal
{
    private readonly string _sessionDirectory;
    private readonly string _eventsPath;
    private readonly string _processedPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public SessionEventJournal(string baseDirectory, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("Base directory rỗng.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id rỗng.", nameof(sessionId));

        _sessionDirectory = Path.Combine(baseDirectory, "data", "sessions", SafeName(sessionId));
        Directory.CreateDirectory(_sessionDirectory);
        _eventsPath = Path.Combine(_sessionDirectory, "events.jsonl");
        _processedPath = Path.Combine(_sessionDirectory, "processed-events.txt");
        LoadProcessedIds();
    }

    public string SessionDirectory => _sessionDirectory;

    public bool HasProcessed(string eventId)
        => !string.IsNullOrWhiteSpace(eventId) && _processed.ContainsKey(eventId);

    public async Task<bool> TryMarkProcessedAsync(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("eventId rỗng.", nameof(eventId));
        if (!_processed.TryAdd(eventId, 0)) return false;

        await _writeGate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_processedPath, eventId + Environment.NewLine, Encoding.UTF8, ct);
            return true;
        }
        catch
        {
            _processed.TryRemove(eventId, out _);
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task AppendAsync(LifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);
        var line = JsonSerializer.Serialize(lifecycleEvent, JsonOptions) + Environment.NewLine;

        await _writeGate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_eventsPath, line, Encoding.UTF8, ct);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public static LifecycleEvent Create(
        LifecycleEventType type,
        string sessionId,
        string symbol,
        string correlationId,
        int decisionVersion,
        string reasonCode,
        object? payload = null,
        DateTimeOffset? sourceTime = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new LifecycleEvent(
            EventId: $"evt-{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            EventType: type,
            EventTime: now,
            SourceTime: sourceTime ?? now,
            SessionId: sessionId,
            Symbol: symbol ?? string.Empty,
            CorrelationId: correlationId ?? string.Empty,
            DecisionVersion: decisionVersion,
            ReasonCode: string.IsNullOrWhiteSpace(reasonCode) ? "UNSPECIFIED" : reasonCode,
            PayloadJson: payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void LoadProcessedIds()
    {
        if (!File.Exists(_processedPath)) return;
        foreach (var line in File.ReadLines(_processedPath))
        {
            var id = line.Trim();
            if (!string.IsNullOrWhiteSpace(id)) _processed.TryAdd(id, 0);
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
