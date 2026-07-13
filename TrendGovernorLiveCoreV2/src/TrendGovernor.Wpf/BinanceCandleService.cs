using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceCandleService
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<CandlePoint>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return Array.Empty<CandlePoint>();
        if (string.IsNullOrWhiteSpace(interval)) throw new ArgumentException("interval rỗng.", nameof(interval));
        limit = Math.Clamp(limit, 20, 500);

        var cacheKey = $"{symbol.ToUpperInvariant()}|{interval}|{limit}";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Candles;

        using var response = await Http.GetAsync($"/fapi/v1/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={limit}", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var candles = document.RootElement.EnumerateArray()
            .Where(item => item.GetArrayLength() > 6 && item[6].GetInt64() < nowMs)
            .Select(item => new CandlePoint
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).UtcDateTime,
                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64()).UtcDateTime,
                IsClosed = true,
                Open = Parse(item[1]),
                High = Parse(item[2]),
                Low = Parse(item[3]),
                Close = Parse(item[4]),
                Volume = Parse(item[5])
            })
            .ToList();

        Cache[cacheKey] = new CacheEntry(DateTime.UtcNow.Add(CacheLifetime(interval)), candles);
        return candles;
    }

    private static TimeSpan CacheLifetime(string interval) => interval.ToLowerInvariant() switch
    {
        "1m" => TimeSpan.FromSeconds(8),
        "3m" or "5m" => TimeSpan.FromSeconds(15),
        "15m" => TimeSpan.FromSeconds(30),
        "1h" => TimeSpan.FromSeconds(45),
        "4h" or "1d" => TimeSpan.FromMinutes(2),
        _ => TimeSpan.FromSeconds(30)
    };

    private static decimal Parse(JsonElement value)
        => decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private sealed record CacheEntry(DateTime ExpiresUtc, IReadOnlyList<CandlePoint> Candles);
}
