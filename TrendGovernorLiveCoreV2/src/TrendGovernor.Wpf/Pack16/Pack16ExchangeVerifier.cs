using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed record BookTickerSnapshot(string Symbol, decimal BidPrice, decimal AskPrice, DateTimeOffset ReceivedAt)
{
    public decimal SpreadPercent => AskPrice > 0m && BidPrice > 0m
        ? Math.Max(0m, (AskPrice - BidPrice) / AskPrice * 100m)
        : decimal.MaxValue;
}

public sealed class Pack16ExchangeVerifier : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private string _apiKey = string.Empty;
    private string _apiSecret = string.Empty;

    public void SetCredentials(string apiKey, string apiSecret)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _apiSecret = apiSecret?.Trim() ?? string.Empty;
    }

    public async Task<BookTickerSnapshot> GetBookTickerAsync(string symbol, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"/fapi/v1/ticker/bookTicker?symbol={Uri.EscapeDataString(symbol)}", ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"BookTicker {(int)response.StatusCode}: {text}");
        using var document = JsonDocument.Parse(text);
        return new BookTickerSnapshot(
            symbol,
            Parse(document.RootElement, "bidPrice"),
            Parse(document.RootElement, "askPrice"),
            DateTimeOffset.UtcNow);
    }

    public async Task<bool> IsOneWayAsync(CancellationToken ct)
    {
        using var document = await SignedAsync(HttpMethod.Get, "/fapi/v1/positionSide/dual", new(), ct);
        return !document.RootElement.TryGetProperty("dualSidePosition", out var dual) || !dual.GetBoolean();
    }

    public async Task<bool> IsIsolatedAsync(string symbol, CancellationToken ct)
    {
        using var document = await SignedAsync(HttpMethod.Get, "/fapi/v3/positionRisk", new()
        {
            ["symbol"] = symbol
        }, ct);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var result = ReadIsolated(row, symbol);
                if (result.HasValue) return result.Value;
            }
            return false;
        }

        return ReadIsolated(document.RootElement, symbol) ?? false;
    }

    private static bool? ReadIsolated(JsonElement row, string symbol)
    {
        if (row.TryGetProperty("symbol", out var s) &&
            !string.Equals(s.GetString(), symbol, StringComparison.OrdinalIgnoreCase)) return null;

        if (row.TryGetProperty("isolated", out var isolated))
        {
            if (isolated.ValueKind is JsonValueKind.True or JsonValueKind.False) return isolated.GetBoolean();
            if (isolated.ValueKind == JsonValueKind.String && bool.TryParse(isolated.GetString(), out var parsed)) return parsed;
        }
        if (row.TryGetProperty("marginType", out var marginType))
            return string.Equals(marginType.GetString(), "isolated", StringComparison.OrdinalIgnoreCase);
        return null;
    }

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!CredentialRules.IsUsable(_apiKey) || !CredentialRules.IsUsable(_apiSecret))
            throw new InvalidOperationException("API Key/Secret chưa hợp lệ.");
        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        parameters["recvWindow"] = "5000";
        var query = string.Join("&", parameters.OrderBy(x => x.Key)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        using var request = new HttpRequestMessage(method, $"{path}?{query}&signature={signature}");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Binance {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static decimal Parse(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    public void Dispose() => _http.Dispose();
}
