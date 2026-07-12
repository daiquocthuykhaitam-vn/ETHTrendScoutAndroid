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
    private readonly RadarUniverseSelector _radarUniverse = new();
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
        var tickerTask = _http.GetAsync("/fapi/v1/ticker/24hr", ct);
        var exchangeTask = _http.GetAsync("/fapi/v1/exchangeInfo", ct);
        await Task.WhenAll(tickerTask, exchangeTask);

        using var tickerResponse = tickerTask.Result;
        using var exchangeResponse = exchangeTask.Result;
        tickerResponse.EnsureSuccessStatusCode();
        exchangeResponse.EnsureSuccessStatusCode();

        using var tickerDocument = JsonDocument.Parse(await tickerResponse.Content.ReadAsStringAsync(ct));
        using var exchangeDocument = JsonDocument.Parse(await exchangeResponse.Content.ReadAsStringAsync(ct));

        var metadata = exchangeDocument.RootElement.GetProperty("symbols")
            .EnumerateArray()
            .Select(x => new
            {
                Symbol = x.GetProperty("symbol").GetString() ?? "",
                BaseAsset = x.GetProperty("baseAsset").GetString() ?? "",
                QuoteAsset = x.GetProperty("quoteAsset").GetString() ?? "",
                Status = x.GetProperty("status").GetString() ?? "",
                ContractType = x.TryGetProperty("contractType", out var contractType) ? contractType.GetString() ?? "" : "",
                OnboardDate = x.TryGetProperty("onboardDate", out var onboard) && onboard.TryGetInt64(out var value) ? value : 0L
            })
            .ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var inputs = new List<RadarTickerInput>();
        foreach (var ticker in tickerDocument.RootElement.EnumerateArray())
        {
            var symbol = ticker.GetProperty("symbol").GetString() ?? "";
            if (!metadata.TryGetValue(symbol, out var meta)) continue;
            var listingAgeDays = meta.OnboardDate > 0
                ? Math.Max(0, (int)(now - DateTimeOffset.FromUnixTimeMilliseconds(meta.OnboardDate)).TotalDays)
                : 0;

            inputs.Add(new RadarTickerInput(
                Symbol: symbol,
                BaseAsset: meta.BaseAsset,
                QuoteAsset: meta.QuoteAsset,
                IsTrading: string.Equals(meta.Status, "TRADING", StringComparison.OrdinalIgnoreCase),
                IsPerpetual: string.Equals(meta.ContractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase),
                ListingAgeDays: listingAgeDays,
                Price: Decimal(ticker, "lastPrice"),
                ChangePercent: Decimal(ticker, "priceChangePercent"),
                HighPrice: Decimal(ticker, "highPrice"),
                LowPrice: Decimal(ticker, "lowPrice"),
                QuoteVolume: Decimal(ticker, "quoteVolume"),
                TradeCount: ticker.TryGetProperty("count", out var count) && count.TryGetInt64(out var trades) ? trades : 0L));
        }

        return _radarUniverse.Select(inputs, 40).ToList();
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
            .Select(x =>
            {
                x.Side = x.Quantity > 0 ? "LONG" : "SHORT";
                x.Quantity = Math.Abs(x.Quantity);
                return x;
            })
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

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa nhập API Key/Secret.");
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

    private static decimal Decimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }
}
