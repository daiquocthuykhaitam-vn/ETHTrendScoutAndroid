using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private readonly FundingIntelligenceService _funding = new();
    private string _apiKey = "";
    private string _apiSecret = "";

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_apiSecret);

    public void SetCredentials(string apiKey, string apiSecret)
    {
        _apiKey = apiKey.Trim();
        _apiSecret = apiSecret.Trim();
        _funding.SetCredentials(_apiKey, _apiSecret);
    }

    public async Task<List<MarketRow>> LoadTopMarketsAsync(CancellationToken ct)
    {
        using var res = await _http.GetAsync("/fapi/v1/ticker/24hr", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var rows = doc.RootElement.EnumerateArray()
            .Where(x => x.GetProperty("symbol").GetString()?.EndsWith("USDT", StringComparison.Ordinal) == true)
            .Select(x => new MarketRow
            {
                Symbol = x.GetProperty("symbol").GetString() ?? "",
                Price = Decimal(x, "lastPrice"),
                Change24h = Decimal(x, "priceChangePercent"),
                QuoteVolume = Decimal(x, "quoteVolume")
            })
            .Where(x => x.Price > 0 && x.QuoteVolume >= 15_000_000m)
            .OrderByDescending(x => x.QuoteVolume)
            .Take(40)
            .ToList();

        await _funding.EnrichMarketsAsync(rows, plannedNotional: 0m, ct);
        return rows;
    }

    public async Task AnalyzeAsync(MarketRow row, CancellationToken ct)
    {
        var h4 = await ClosesAsync(row.Symbol, "4h", 80, ct);
        var h1 = await ClosesAsync(row.Symbol, "1h", 100, ct);
        var m15 = await ClosesAsync(row.Symbol, "15m", 100, ct);
        var m5 = await ClosesAsync(row.Symbol, "5m", 100, ct);
        if (h4.Count < 55 || h1.Count < 55 || m15.Count < 55 || m5.Count < 55) return;

        decimal ema4Fast = Ema(h4, 20), ema4Slow = Ema(h4, 50);
        decimal ema1Fast = Ema(h1, 20), ema1Slow = Ema(h1, 50);
        decimal ema15 = Ema(m15, 20), ema5 = Ema(m5, 20);
        decimal last = row.Price;
        decimal low = h1.TakeLast(48).Min();
        decimal high = h1.TakeLast(48).Max();
        decimal range = Math.Max(high - low, last * 0.001m);
        decimal pos = (last - low) / range;
        bool longTrend = ema4Fast > ema4Slow && ema1Fast > ema1Slow;
        bool shortTrend = ema4Fast < ema4Slow && ema1Fast < ema1Slow;
        bool longTiming = m15[^1] > ema15 && m5[^1] > ema5;
        bool shortTiming = m15[^1] < ema15 && m5[^1] < ema5;
        int score = 35;
        if (longTrend || shortTrend) score += 30;
        if (longTiming || shortTiming) score += 20;
        if (row.QuoteVolume > 100_000_000m) score += 10;
        row.Score = Math.Min(score, 100);

        if (longTrend && longTiming && pos < 0.72m)
        {
            row.Direction = "LONG";
            row.Status = pos < 0.30m ? "CHỜ XÁC NHẬN ĐÁY" : "ĐỦ ĐIỀU KIỆN";
        }
        else if (shortTrend && shortTiming && pos > 0.28m)
        {
            row.Direction = "SHORT";
            row.Status = pos > 0.70m ? "CHỜ XÁC NHẬN ĐỈNH" : "ĐỦ ĐIỀU KIỆN";
        }
        else
        {
            row.Direction = "WAIT";
            row.Status = longTrend || shortTrend ? "CHỜ PULLBACK" : "XUNG ĐỘT XU HƯỚNG";
        }

        decimal atr = Atr(h1.TakeLast(20).ToList());
        if (row.Direction == "LONG")
        {
            row.EntryLow = Math.Max(ema1Fast - atr * 0.25m, low);
            row.EntryHigh = ema1Fast + atr * 0.10m;
            row.StopLoss = row.EntryLow - atr * 1.20m;
            row.TakeProfit = row.EntryHigh + atr * 2.40m;
        }
        else if (row.Direction == "SHORT")
        {
            row.EntryLow = ema1Fast - atr * 0.10m;
            row.EntryHigh = Math.Min(ema1Fast + atr * 0.25m, high);
            row.StopLoss = row.EntryHigh + atr * 1.20m;
            row.TakeProfit = row.EntryLow - atr * 2.40m;
        }
        if (row.StopLoss > 0)
        {
            var risk = row.Direction == "LONG" ? row.EntryHigh - row.StopLoss : row.StopLoss - row.EntryLow;
            var reward = row.Direction == "LONG" ? row.TakeProfit - row.EntryHigh : row.EntryLow - row.TakeProfit;
            row.RiskReward = risk > 0 ? reward / risk : 0;
        }
        FundingIntelligenceService.ApplyFundingDecision(row, 0m);
    }

    public async Task<AccountState> GetAccountAsync(CancellationToken ct)
    {
        using var doc = await SignedAsync(HttpMethod.Get, "/fapi/v3/account", new(), ct);
        return new AccountState
        {
            TotalWalletBalance = Decimal(doc.RootElement, "totalWalletBalance"),
            AvailableBalance = Decimal(doc.RootElement, "availableBalance"),
            TotalUnrealizedProfit = Decimal(doc.RootElement, "totalUnrealizedProfit")
        };
    }

    public async Task<List<PositionRow>> GetPositionsAsync(CancellationToken ct)
    {
        using var doc = await SignedAsync(HttpMethod.Get, "/fapi/v3/positionRisk", new(), ct);
        var rows = doc.RootElement.EnumerateArray()
            .Select(x => new PositionRow
            {
                Symbol = x.GetProperty("symbol").GetString() ?? "",
                Quantity = Decimal(x, "positionAmt"),
                EntryPrice = Decimal(x, "entryPrice"),
                MarkPrice = Decimal(x, "markPrice"),
                UnrealizedPnl = Decimal(x, "unRealizedProfit")
            })
            .Where(x => x.Quantity != 0)
            .Select(x => { x.Side = x.Quantity > 0 ? "LONG" : "SHORT"; x.Quantity = Math.Abs(x.Quantity); return x; })
            .ToList();

        await _funding.EnrichPositionsAsync(rows, ct);
        return rows;
    }

    public async Task<List<OrderRow>> GetOpenOrdersAsync(CancellationToken ct)
    {
        using var doc = await SignedAsync(HttpMethod.Get, "/fapi/v1/openOrders", new(), ct);
        return doc.RootElement.EnumerateArray().Select(x => new OrderRow
        {
            OrderId = x.GetProperty("orderId").GetInt64(),
            ClientOrderId = x.TryGetProperty("clientOrderId", out var clientId) ? clientId.GetString() ?? "" : "",
            Symbol = x.GetProperty("symbol").GetString() ?? "",
            Type = x.GetProperty("type").GetString() ?? "",
            Side = x.GetProperty("side").GetString() ?? "",
            Price = Decimal(x, "price"),
            StopPrice = Decimal(x, "stopPrice"),
            Quantity = Decimal(x, "origQty"),
            Status = x.GetProperty("status").GetString() ?? "",
            ReduceOnly = x.TryGetProperty("reduceOnly", out var r) && r.GetBoolean(),
            IsAlgo = false
        }).ToList();
    }

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> p, CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa nhập API Key/Secret.");
        p["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        p["recvWindow"] = "5000";
        string query = string.Join("&", p.OrderBy(x => x.Key).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        string sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        using var req = new HttpRequestMessage(method, $"{path}?{query}&signature={sig}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var res = await _http.SendAsync(req, ct);
        string text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Binance {(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private async Task<List<decimal>> ClosesAsync(string symbol, string interval, int limit, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"/fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray().Select(x => decimal.Parse(x[4].GetString()!, CultureInfo.InvariantCulture)).ToList();
    }

    private static decimal Ema(IReadOnlyList<decimal> v, int p)
    {
        decimal k = 2m / (p + 1), e = v.Take(p).Average();
        for (int i = p; i < v.Count; i++) e = v[i] * k + e * (1 - k);
        return e;
    }

    private static decimal Atr(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 2) return 0;
        return closes.Zip(closes.Skip(1), (a, b) => Math.Abs(b - a)).Average();
    }

    private static decimal Decimal(JsonElement x, string name)
    {
        if (!x.TryGetProperty(name, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
