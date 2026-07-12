using System.Collections.Concurrent;

namespace TrendGovernor.Wpf;

/// <summary>
/// Process-local ownership truth. Only fills created by this running process are BOT-owned.
/// Any position that existed before startup, was opened manually, or belongs to an older bot session remains read-only.
/// </summary>
public static class SessionOwnershipRegistry
{
    private static readonly ConcurrentDictionary<string, OwnershipRecord> CurrentSessionPositions = new(StringComparer.OrdinalIgnoreCase);

    public static string SessionId { get; } = $"TGSESSION-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    public static void RegisterFilled(string symbol, string clientOrderId, long orderId)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        if (string.IsNullOrWhiteSpace(clientOrderId)) return;
        if (!clientOrderId.StartsWith("tg-", StringComparison.OrdinalIgnoreCase)) return;
        if (clientOrderId.StartsWith("tg-emergency-", StringComparison.OrdinalIgnoreCase)) return;

        CurrentSessionPositions[symbol] = new OwnershipRecord
        {
            SessionId = SessionId,
            Symbol = symbol,
            EntryClientOrderId = clientOrderId,
            EntryOrderId = orderId,
            RegisteredUtc = DateTime.UtcNow
        };
    }

    public static bool IsCurrentSessionBotOwned(string symbol)
        => !string.IsNullOrWhiteSpace(symbol) && CurrentSessionPositions.ContainsKey(symbol);

    public static bool TryGet(string symbol, out OwnershipRecord record)
        => CurrentSessionPositions.TryGetValue(symbol, out record!);

    public static void Release(string symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol)) CurrentSessionPositions.TryRemove(symbol, out _);
    }

    public static string ResolveOwner(string symbol)
        => IsCurrentSessionBotOwned(symbol) ? "BOT" : "MỞ TAY / CÓ SẴN";
}

public sealed class OwnershipRecord
{
    public string SessionId { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string EntryClientOrderId { get; init; } = "";
    public long EntryOrderId { get; init; }
    public DateTime RegisteredUtc { get; init; }
}
