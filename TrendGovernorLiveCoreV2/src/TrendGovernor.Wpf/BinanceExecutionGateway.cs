using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceExecutionGateway : IAsyncDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private readonly Dictionary<string, SymbolTradingRules> _ruleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private string _apiKey = "";
    private string _apiSecret = "";
    private ClientWebSocket? _userSocket;
    private CancellationTokenSource? _userStreamCts;
    private Task? _userStreamTask;
    private readonly Dictionary<string, TaskCompletionSource<ExecutionFill>> _fillWaiters = new(StringComparer.Ordinal);

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_apiSecret);
    public event Action<string, string>? EventReceived;

    public void SetCredentials(string apiKey, string apiSecret)
    {
        _apiKey = apiKey.Trim();
        _apiSecret = apiSecret.Trim();
    }

    public async Task StartUserDataStreamAsync(CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa có API Key/Secret.");
        if (_userStreamTask is { IsCompleted: false }) return;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/listenKey");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Không tạo được listenKey: {json}");
        using var document = JsonDocument.Parse(json);
        var listenKey = document.RootElement.GetProperty("listenKey").GetString() ?? throw new InvalidOperationException("listenKey rỗng.");

        _userStreamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _userSocket = new ClientWebSocket();
        await _userSocket.ConnectAsync(new Uri($"wss://fstream.binance.com/ws/{listenKey}"), _userStreamCts.Token);
        _userStreamTask = ReceiveUserStreamAsync(listenKey, _userStreamCts.Token);
    }

    private async Task ReceiveUserStreamAsync(string listenKey, CancellationToken ct)
    {
        using var keepAlive = new PeriodicTimer(TimeSpan.FromMinutes(45));
        var keepAliveTask = Task.Run(async () =>
        {
            while (await keepAlive.WaitForNextTickAsync(ct))
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"/fapi/v1/listenKey?listenKey={Uri.EscapeDataString(listenKey)}");
                request.Headers.Add("X-MBX-APIKEY", _apiKey);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) EventReceived?.Invoke("USER_STREAM", "Gia hạn listenKey thất bại.");
            }
        }, ct);

        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _userSocket?.State == WebSocketState.Open)
            {
                var result = await _userSocket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;
                var payload = builder.ToString();
                builder.Clear();
                ProcessUserEvent(payload);
            }
        }
        finally
        {
            try { await keepAliveTask; } catch { }
        }
    }

    private void ProcessUserEvent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("e", out var e) ? e.GetString() ?? "UNKNOWN" : "UNKNOWN";
            EventReceived?.Invoke(eventType, payload);
            if (eventType != "ORDER_TRADE_UPDATE" || !root.TryGetProperty("o", out var order)) return;

            var clientOrderId = order.TryGetProperty("c", out var c) ? c.GetString() ?? "" : "";
            var status = order.TryGetProperty("X", out var x) ? x.GetString() ?? "" : "";
            if (!_fillWaiters.TryGetValue(clientOrderId, out var waiter)) return;
            if (status is not ("FILLED" or "PARTIALLY_FILLED")) return;

            var executed = Parse(order, "z");
            var average = Parse(order, "ap");
            var orderId = order.TryGetProperty("i", out var id) ? id.GetInt64() : 0L;
            waiter.TrySetResult(new ExecutionFill
            {
                OrderId = orderId,
                ClientOrderId = clientOrderId,
                Symbol = order.TryGetProperty("s", out var s) ? s.GetString() ?? "" : "",
                Side = order.TryGetProperty("S", out var side) ? side.GetString() ?? "" : "",
                Status = status,
                ExecutedQuantity = executed,
                AveragePrice = average
            });
        }
        catch (Exception ex)
        {
            EventReceived?.Invoke("USER_STREAM_ERROR", ex.Message);
        }
    }

    public async Task<SymbolTradingRules> GetRulesAsync(string symbol, CancellationToken ct)
    {
        if (_ruleCache.TryGetValue(symbol, out var cached)) return cached;
        using var response = await _http.GetAsync("/fapi/v1/exchangeInfo", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var item in document.RootElement.GetProperty("symbols").EnumerateArray())
        {
            var itemSymbol = item.GetProperty("symbol").GetString() ?? "";
            if (!string.Equals(itemSymbol, symbol, StringComparison.OrdinalIgnoreCase)) continue;
            decimal tick = 0m, step = 0m, minQty = 0m, maxQty = 0m, minNotional = 0m;
            foreach (var filter in item.GetProperty("filters").EnumerateArray())
            {
                var type = filter.GetProperty("filterType").GetString();
                if (type == "PRICE_FILTER") tick = Parse(filter, "tickSize");
                else if (type == "LOT_SIZE")
                {
                    step = Parse(filter, "stepSize");
                    minQty = Parse(filter, "minQty");
                    maxQty = Parse(filter, "maxQty");
                }
                else if (type is "MIN_NOTIONAL" or "NOTIONAL")
                {
                    if (filter.TryGetProperty("notional", out _)) minNotional = Parse(filter, "notional");
                    else if (filter.TryGetProperty("minNotional", out _)) minNotional = Parse(filter, "minNotional");
                }
            }
            var rules = new SymbolTradingRules
            {
                Symbol = symbol,
                TickSize = tick,
                StepSize = step,
                MinQuantity = minQty,
                MaxQuantity = maxQty,
                MinNotional = minNotional,
                PricePrecision = item.TryGetProperty("pricePrecision", out var pp) ? pp.GetInt32() : 8,
                QuantityPrecision = item.TryGetProperty("quantityPrecision", out var qp) ? qp.GetInt32() : 8
            };
            _ruleCache[symbol] = rules;
            return rules;
        }
        throw new InvalidOperationException($"Không tìm thấy quy tắc symbol {symbol}.");
    }

    public async Task<bool> IsHedgeModeAsync(CancellationToken ct)
    {
        using var document = await SignedAsync(HttpMethod.Get, "/fapi/v1/positionSide/dual", new(), ct);
        return document.RootElement.TryGetProperty("dualSidePosition", out var value) && value.GetBoolean();
    }

    public async Task EnsureIsolatedAsync(string symbol, CancellationToken ct)
    {
        try
        {
            using var _ = await SignedAsync(HttpMethod.Post, "/fapi/v1/marginType", new()
            {
                ["symbol"] = symbol,
                ["marginType"] = "ISOLATED"
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("-4046", StringComparison.OrdinalIgnoreCase))
        {
            EventReceived?.Invoke("VERIFY", $"{symbol}: đã ở ISOLATED.");
        }
    }

    public async Task SetLeverageAsync(string symbol, int leverage, CancellationToken ct)
    {
        using var _ = await SignedAsync(HttpMethod.Post, "/fapi/v1/leverage", new()
        {
            ["symbol"] = symbol,
            ["leverage"] = leverage.ToString(CultureInfo.InvariantCulture)
        }, ct);
    }

    public decimal NormalizeQuantity(decimal quantity, SymbolTradingRules rules)
    {
        if (rules.StepSize <= 0) return quantity;
        var normalized = Math.Floor(quantity / rules.StepSize) * rules.StepSize;
        if (normalized < rules.MinQuantity) return 0m;
        if (rules.MaxQuantity > 0 && normalized > rules.MaxQuantity) normalized = rules.MaxQuantity;
        return normalized;
    }

    public decimal NormalizePrice(decimal price, SymbolTradingRules rules)
    {
        if (rules.TickSize <= 0) return price;
        return Math.Round(Math.Floor(price / rules.TickSize) * rules.TickSize, rules.PricePrecision, MidpointRounding.ToZero);
    }

    public async Task<ExecutionFill> PlaceMarketAndWaitFillAsync(string symbol, string side, decimal quantity, CancellationToken ct)
    {
        await _sendGate.WaitAsync(ct);
        try
        {
            var clientOrderId = $"tg-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000, 9999)}";
            var waiter = new TaskCompletionSource<ExecutionFill>(TaskCreationOptions.RunContinuationsAsynchronously);
            _fillWaiters[clientOrderId] = waiter;
            try
            {
                using var document = await SignedAsync(HttpMethod.Post, "/fapi/v1/order", new()
                {
                    ["symbol"] = symbol,
                    ["side"] = side,
                    ["type"] = "MARKET",
                    ["quantity"] = F(quantity),
                    ["newClientOrderId"] = clientOrderId,
                    ["newOrderRespType"] = "RESULT"
                }, ct);

                var immediate = ParseFill(document.RootElement, clientOrderId, symbol, side);
                if (immediate.Status == "FILLED" && immediate.ExecutedQuantity > 0) return immediate;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try { return await waiter.Task.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return await QueryOrderUntilFilledAsync(symbol, clientOrderId, TimeSpan.FromSeconds(8), ct);
                }
            }
            finally
            {
                _fillWaiters.Remove(clientOrderId);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<ExecutionFill> QueryOrderUntilFilledAsync(string symbol, string clientOrderId, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            using var document = await SignedAsync(HttpMethod.Get, "/fapi/v1/order", new()
            {
                ["symbol"] = symbol,
                ["origClientOrderId"] = clientOrderId
            }, ct);
            var fill = ParseFill(document.RootElement, clientOrderId, symbol, document.RootElement.GetProperty("side").GetString() ?? "");
            if (fill.Status == "FILLED" && fill.ExecutedQuantity > 0) return fill;
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException($"Không xác nhận được FILL thật cho {symbol} ({clientOrderId}).");
    }

    public async Task<ProtectionVerification> PlaceAndVerifyProtectionAsync(string symbol, string exitSide, decimal quantity, decimal stopPrice, decimal takeProfitPrice, CancellationToken ct)
    {
        var baseId = $"tg-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(100, 999)}";
        var stopId = await PlaceAlgoAsync(symbol, exitSide, "STOP_MARKET", quantity, stopPrice, baseId + "-sl", ct);
        long tpId;
        try
        {
            tpId = await PlaceAlgoAsync(symbol, exitSide, "TAKE_PROFIT_MARKET", quantity, takeProfitPrice, baseId + "-tp", ct);
        }
        catch
        {
            EventReceived?.Invoke("PROTECT", $"{symbol}: TP lỗi, SL đã gửi; tiếp tục reconcile TP.");
            tpId = 0;
        }

        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var open = await GetOpenAlgoOrdersAsync(symbol, ct);
            var stopOk = open.Any(x => x.AlgoId == stopId && x.OrderType == "STOP_MARKET" && x.Status == "NEW");
            var tpOk = tpId > 0 && open.Any(x => x.AlgoId == tpId && x.OrderType == "TAKE_PROFIT_MARKET" && x.Status == "NEW");
            if (stopOk && tpOk) return new ProtectionVerification { StopLossConfirmed = true, TakeProfitConfirmed = true, StopAlgoId = stopId, TakeProfitAlgoId = tpId };
            await Task.Delay(500, ct);
        }

        var final = await GetOpenAlgoOrdersAsync(symbol, ct);
        return new ProtectionVerification
        {
            StopLossConfirmed = final.Any(x => x.AlgoId == stopId && x.Status == "NEW"),
            TakeProfitConfirmed = tpId > 0 && final.Any(x => x.AlgoId == tpId && x.Status == "NEW"),
            StopAlgoId = stopId,
            TakeProfitAlgoId = tpId
        };
    }

    private async Task<long> PlaceAlgoAsync(string symbol, string side, string type, decimal quantity, decimal triggerPrice, string clientAlgoId, CancellationToken ct)
    {
        using var document = await SignedAsync(HttpMethod.Post, "/fapi/v1/algoOrder", new()
        {
            ["algoType"] = "CONDITIONAL",
            ["symbol"] = symbol,
            ["side"] = side,
            ["type"] = type,
            ["quantity"] = F(quantity),
            ["triggerPrice"] = F(triggerPrice),
            ["workingType"] = "MARK_PRICE",
            ["reduceOnly"] = "true",
            ["priceProtect"] = "false",
            ["clientAlgoId"] = clientAlgoId,
            ["newOrderRespType"] = "RESULT"
        }, ct);
        if (document.RootElement.TryGetProperty("algoId", out var algoId)) return algoId.GetInt64();
        throw new InvalidOperationException($"Binance không trả algoId cho {type}.");
    }

    public async Task<IReadOnlyList<AlgoOrderState>> GetOpenAlgoOrdersAsync(string symbol, CancellationToken ct)
    {
        using var document = await SignedAsync(HttpMethod.Get, "/fapi/v1/openAlgoOrders", new()
        {
            ["symbol"] = symbol,
            ["algoType"] = "CONDITIONAL"
        }, ct);
        return document.RootElement.EnumerateArray().Select(x => new AlgoOrderState
        {
            AlgoId = x.GetProperty("algoId").GetInt64(),
            ClientAlgoId = x.TryGetProperty("clientAlgoId", out var c) ? c.GetString() ?? "" : "",
            OrderType = x.TryGetProperty("orderType", out var type) ? type.GetString() ?? "" : "",
            Status = x.TryGetProperty("algoStatus", out var status) ? status.GetString() ?? "" : "",
            TriggerPrice = Parse(x, "triggerPrice")
        }).ToList();
    }

    public async Task EmergencyCloseAsync(string symbol, string exitSide, decimal quantity, CancellationToken ct)
    {
        using var _ = await SignedAsync(HttpMethod.Post, "/fapi/v1/order", new()
        {
            ["symbol"] = symbol,
            ["side"] = exitSide,
            ["type"] = "MARKET",
            ["quantity"] = F(quantity),
            ["reduceOnly"] = "true",
            ["newClientOrderId"] = $"tg-emergency-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["newOrderRespType"] = "RESULT"
        }, ct);
    }

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa nhập API Key/Secret.");
        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        parameters["recvWindow"] = "5000";
        var query = string.Join("&", parameters.OrderBy(x => x.Key).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        using var request = new HttpRequestMessage(method, $"{path}?{query}&signature={signature}");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Binance {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static ExecutionFill ParseFill(JsonElement root, string clientOrderId, string symbol, string side)
        => new()
        {
            OrderId = root.TryGetProperty("orderId", out var id) ? id.GetInt64() : 0,
            ClientOrderId = root.TryGetProperty("clientOrderId", out var c) ? c.GetString() ?? clientOrderId : clientOrderId,
            Symbol = root.TryGetProperty("symbol", out var s) ? s.GetString() ?? symbol : symbol,
            Side = root.TryGetProperty("side", out var sd) ? sd.GetString() ?? side : side,
            Status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            ExecutedQuantity = Parse(root, "executedQty"),
            AveragePrice = Parse(root, "avgPrice")
        };

    private static decimal Parse(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static string F(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        try { _userStreamCts?.Cancel(); } catch { }
        if (_userSocket is not null)
        {
            try { await _userSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None); } catch { }
            _userSocket.Dispose();
        }
        _http.Dispose();
        _sendGate.Dispose();
        _userStreamCts?.Dispose();
    }
}

public sealed class AlgoOrderState
{
    public long AlgoId { get; init; }
    public string ClientAlgoId { get; init; } = "";
    public string OrderType { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal TriggerPrice { get; init; }
}
