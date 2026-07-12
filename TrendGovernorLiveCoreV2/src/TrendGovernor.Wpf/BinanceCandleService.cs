using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceCandleService
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };

    public async Task<IReadOnlyList<CandlePoint>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return Array.Empty<CandlePoint>();
        limit = Math.Clamp(limit, 20, 500);
        using var response = await _http.GetAsync($"/fapi/v1/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={limit}", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        return document.RootElement.EnumerateArray()
            .Select(item => new CandlePoint
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).LocalDateTime,
                Open = Parse(item[1]),
                High = Parse(item[2]),
                Low = Parse(item[3]),
                Close = Parse(item[4]),
                Volume = Parse(item[5])
            })
            .ToList();
    }

    private static decimal Parse(JsonElement value)
        => decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
}
