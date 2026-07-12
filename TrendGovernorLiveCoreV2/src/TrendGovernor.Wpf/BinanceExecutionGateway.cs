using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceExecutionGateway : IAsyncDisposable
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FILLED", "CANCELED", "EXPIRED", "REJECTED"
    };

    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private readonly ConcurrentDictionary<string, SymbolTradingRules> _ruleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ExecutionFill>> _terminalWaiters = new(StringComparer.Ordinal);
    private string _apiKey = "";
    private string _apiSecret = "";
    private ClientWebSocket? _userSocket;
    private CancellationTokenSource? _userStreamCts;
    private Task? _userStreamTask;

    public bool HasCredentials => CredentialRules.IsUsable(_apiKey) && CredentialRules.IsUsable(_apiSecret);
    public event Action<string, string>? EventReceived;

    public void SetCredentials(string apiKey, string apiSecret)
    {
        _apiKey = apiKey.Trim();
        _apiSecret = apiSecret.Trim();
    }

    public async Task StartUserDataStreamAsync(CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa có API Key/Secret hợp lệ.");
        if (_userStreamTask is { IsCompleted: false }) return;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/listenKey");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Không tạo được listenKey: {json}");
        using var document = JsonDocument.Parse(json);
        var listenKey = document.RootElement.GetProperty("listenKey").GetString()
            ?? throw new InvalidOperationException("listenKey rỗng.");

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
            try
            {
                while (await keepAlive.WaitForNextTickAsync(ct))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Put, $"/fapi/v1/listenKey?listenKey={Uri.EscapeDataString(listenKey)}");
                    request.Headers.Add("X-MBX-APIKEY", _apiKey);
                    using var response = await _http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode) EventReceived?.Invoke("USER_STREAM", "Gia hạn listenKey thất bại.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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
            if (status == "PARTIALLY_FILLED")
            {
                EventReceived?.Invoke("ORDER_PARTIALLY_FILLED", payload);
                return;
            }
            if (!TerminalStatuses.Contains(status)) return;
            if (!_terminalWaiters.TryGetValue(clientOrderId, out var waiter)) return;

            waiter.TrySetResult(new ExecutionFill
            {
                OrderId = order.TryGetProperty("i", out var id) ? id.GetInt64() : 0L,
                ClientOrderId = clientOrderId,
                Symbol = order.TryGetProperty("s", out var s) ? s.GetString() ?? "" : "",
                Side = order.TryGetProperty("S", out var side) ? side.GetString() ?? "" : "",
                Status = status,
                ExecutedQuantity = Parse(order, "z"),
                AveragePrice = Parse(order, "ap")
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
                    minNotional = filter.TryGetProperty("notional", out _) ? Parse(filter, "notional") : Parse(filter, "minNotional");
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
        return Math.Round(normalized, rules.QuantityPrecision, MidpointRounding.ToZero);
    }

    public decimal NormalizePrice(decimal price, SymbolTradingRules rules)
    {
        if (rules.TickSize <= 0) return price;
        return Math.Round(Math.Floor(price / rules.TickSize) * rules.TickSize, rules.PricePrecision, MidpointRounding.ToZero);
    }

    public Task<ExecutionFill> PlaceMarketAndWaitFillAsync(string symbol, string side, decimal quantity, CancellationToken ct)
        => PlaceMarketAndWaitFillAsync(new OrderIntent(
            $"legacy-{Guid.NewGuid():N}", "legacy", "legacy", "legacy", symbol, side, "MARKET", quantity, null, false, DateTimeOffset.UtcNow), ct);

    public async Task<ExecutionFill> PlaceMarketAndWaitFillAsync(OrderIntent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.ReduceOnly) throw new InvalidOperationException("Entry OrderIntent không được reduce-only.");
        if (intent.Type != "MARKET") throw new InvalidOperationException("Pack 14 hiện chỉ cho phép MARKET OrderIntent.");

        await _sendGate.WaitAsync(ct);
        try
        {
            var clientOrderId = BuildClientOrderId(intent.OrderIntentId);
            var waiter = new TaskCompletionSource<ExecutionFill>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_terminalWaiters.TryAdd(clientOrderId, waiter))
                throw new InvalidOperationException("clientOrderId bị trùng.");

            try
            {
                using var document = await SignedAsync(HttpMethod.Post, "/fapi/v1/order", new()
                {
                    ["symbol"] = intent.Symbol,
                    ["side"] = intent.Side,
                    ["type"] = "MARKET",
                    ["quantity"] = F(intent.Quantity),
                    ["newClientOrderId"] = clientOrderId,
                    ["newOrderRespType"] = "RESULT"
                }, ct);

                var immediate = ParseFill(document.RootElement, clientOrderId, intent.Symbol, intent.Side);
                if (TerminalStatuses.Contains(immediate.Status)) return immediate;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    return await waiter.Task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return await QueryOrderUntilTerminalAsync(intent.Symbol, clientOrderId, TimeSpan.FromSeconds(12), ct);
                }
            }
            finally
            {
                _terminalWaiters.TryRemove(clientOrderId, out _);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<ExecutionFill> QueryOrderUntilTerminalAsync(string symbol, string clientOrderId, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        ExecutionFill? last = null;
        while (DateTime.UtcNow < until)
        {
            using var document = await SignedAsync(HttpMethod.Get, "/fapi/v1/order", new()
            {
                ["symbol"] = symbol,
                ["origClientOrderId"] = clientOrderId
            }, ct);
            last = ParseFill(document.RootElement, clientOrderId, symbol, document.RootElement.GetProperty("side").GetString() ?? "");
            if (TerminalStatuses.Contains(last.Status)) return last;
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException($"Order {symbol}/{clientOrderId} chưa tới trạng thái terminal. Last={last?.Status ?? "UNKNOWN"}.");
    }

    public async Task<ProtectionVerification> PlaceAndVerifyProtectionAsync(
        string symbol,
        string exitSide,
        decimal quantity,
        decimal stopPrice,
        decimal takeProfitPrice,
        CancellationToken ct)
    {
        var baseId = $"tg14p-{Guid.NewGuid():N}"[..30];
        var stopId = await PlaceAlgoAsync(symbol, exitSide, "STOP_MARKET", quantity, stopPrice, baseId + "-s", ct);
        long tpId = 0;
        try
        {
            tpId = await PlaceAlgoAsync(symbol, exitSide, "TAKE_PROFIT_MARKET", quantity, takeProfitPrice, baseId + "-t", ct);
        }
        catch (Exception ex)
        {
            EventReceived?.Invoke("PROTECT", $"{symbol}: TP lỗi sau khi SL đã gửi: {ex.Message}");
        }

        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var open = await GetOpenAlgoOrdersAsync(symbol, ct);
            var stopOk = open.Any(x => x.AlgoId == stopId && x.OrderType == "STOP_MARKET" && x.Status == "NEW");
            var tpOk = tpId > 0 && open.Any(x => x.AlgoId == tpId && x.OrderType == "TAKE_PROFIT_MARKET" && x.Status == "NEW");
            if (stopOk && tpOk)
                return new ProtectionVerification { StopLossConfirmed = true, TakeProfitConfirmed = true, StopAlgoId = stopId, TakeProfitAlgoId = tpId };
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
        using var document = await SignedAsync(HttpMethod.Post, "/fapi/v1/order", new()
        {
            ["symbol"] = symbol,
            ["side"] = exitSide,
            ["type"] = "MARKET",
            ["quantity"] = F(quantity),
            ["reduceOnly"] = "true",
            ["newClientOrderId"] = $"tg-emergency-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["newOrderRespType"] = "RESULT"
        }, ct);
        var status = document.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        if (status is "REJECTED" or "EXPIRED" or "CANCELED")
            throw new InvalidOperationException($"Emergency close kết thúc {status}.");
    }

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Chưa nhập API Key/Secret hợp lệ.");
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

    private static string BuildClientOrderId(string intentId)
    {
        var suffix = new string(intentId.Where(char.IsLetterOrDigit).TakeLast(26).ToArray());
        return $"tg14-{suffix}"[..Math.Min(36, 5 + suffix.Length)];
    }

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
