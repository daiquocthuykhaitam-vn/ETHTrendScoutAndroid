using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class FundingIntelligenceService : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://fapi.binance.com") };
    private readonly Dictionary<string, (DateTime expiresUtc, FundingSnapshot snapshot)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string _apiKey = "";
    private string _apiSecret = "";

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_apiSecret);

    public void SetCredentials(string apiKey, string apiSecret)
    {
        _apiKey = apiKey.Trim();
        _apiSecret = apiSecret.Trim();
    }

    public async Task EnrichMarketsAsync(IReadOnlyList<MarketRow> rows, decimal plannedNotional, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var premiumTask = LoadPremiumIndexAsync(ct);
        var infoTask = LoadFundingInfoAsync(ct);
        await Task.WhenAll(premiumTask, infoTask);
        var premium = premiumTask.Result;
        var info = infoTask.Result;

        using var gate = new SemaphoreSlim(4);
        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync(ct);
            try
            {
                premium.TryGetValue(row.Symbol, out var live);
                info.TryGetValue(row.Symbol, out var interval);
                var history = await LoadFundingHistoryAsync(row.Symbol, 12, ct);
                var snapshot = BuildSnapshot(row.Symbol, live, interval, history, plannedNotional);
                Apply(row, snapshot, plannedNotional);
                _cache[row.Symbol] = (DateTime.UtcNow.AddSeconds(45), snapshot);
            }
            catch
            {
                row.FundingWarning = "Không tải được funding; không dùng funding để cộng điểm.";
                row.FundingScore = 0;
                row.FundingBias = "CHƯA XÁC MINH";
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    public async Task<FundingSnapshot> GetSnapshotAsync(string symbol, decimal plannedNotional, CancellationToken ct)
    {
        if (_cache.TryGetValue(symbol, out var cached) && cached.expiresUtc > DateTime.UtcNow)
            return Reprice(cached.snapshot, plannedNotional);

        var premiumTask = LoadPremiumIndexAsync(ct);
        var infoTask = LoadFundingInfoAsync(ct);
        var historyTask = LoadFundingHistoryAsync(symbol, 12, ct);
        await Task.WhenAll(premiumTask, infoTask, historyTask);
        premiumTask.Result.TryGetValue(symbol, out var live);
        infoTask.Result.TryGetValue(symbol, out var interval);
        var snapshot = BuildSnapshot(symbol, live, interval, historyTask.Result, plannedNotional);
        _cache[symbol] = (DateTime.UtcNow.AddSeconds(45), snapshot);
        return snapshot;
    }

    public async Task<Dictionary<string, decimal>> GetFundingIncomeBySymbolAsync(DateTimeOffset from, CancellationToken ct)
    {
        if (!HasCredentials) return new(StringComparer.OrdinalIgnoreCase);
        using var doc = await SignedAsync(HttpMethod.Get, "/fapi/v1/income", new()
        {
            ["incomeType"] = "FUNDING_FEE",
            ["startTime"] = from.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["limit"] = "1000"
        }, ct);

        return doc.RootElement.EnumerateArray()
            .GroupBy(x => x.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.Sum(x => Parse(x, "income")), StringComparer.OrdinalIgnoreCase);
    }

    public async Task EnrichPositionsAsync(IReadOnlyList<PositionRow> positions, CancellationToken ct)
    {
        if (positions.Count == 0) return;
        var income = await GetFundingIncomeBySymbolAsync(DateTimeOffset.UtcNow.AddDays(-1), ct);
        foreach (var position in positions)
        {
            var notional = Math.Abs(position.MarkPrice * position.Quantity);
            var snapshot = await GetSnapshotAsync(position.Symbol, notional, ct);
            position.RealizedFunding = income.TryGetValue(position.Symbol, out var value) ? value : 0m;
            position.EstimatedNextFunding = position.Side == "LONG" ? snapshot.EstimatedLongFunding : snapshot.EstimatedShortFunding;
            position.NetPnlAfterFunding = position.UnrealizedPnl + position.RealizedFunding;
            position.ProjectedNetPnl = position.NetPnlAfterFunding + position.EstimatedNextFunding;
            position.FundingRatePercent = snapshot.FundingRatePercent;
            position.FundingIntervalHours = snapshot.FundingIntervalHours;
            position.NextFundingTime = snapshot.NextFundingTime;
            position.FundingFlow = snapshot.Flow;
            position.FundingLevel = snapshot.Level;
        }
    }

    public static void ApplyFundingDecision(MarketRow row, decimal plannedNotional)
    {
        row.EstimatedFundingForTrade = row.Direction == "LONG"
            ? -plannedNotional * row.FundingRate
            : plannedNotional * row.FundingRate;

        var receives = row.EstimatedFundingForTrade > 0m;
        var pays = row.EstimatedFundingForTrade < 0m;
        row.FundingFavorsDirection = receives;
        row.FundingBias = receives ? "CÙNG LỢI ÍCH" : pays ? "CHI PHÍ GIỮ LỆNH" : "TRUNG LẬP";

        var absoluteHourly = Math.Abs(row.FundingPerHourPercent);
        var score = 50;
        if (receives) score += absoluteHourly switch { >= 0.25m => 15, >= 0.05m => 12, >= 0.01m => 8, _ => 3 };
        if (pays) score -= absoluteHourly switch { >= 0.25m => 35, >= 0.05m => 22, >= 0.01m => 10, _ => 3 };
        if (row.FundingSameSignPeriods >= 6) score += receives ? 5 : -5;
        row.FundingScore = Math.Clamp(score, 0, 100);

        var extremeCost = pays && absoluteHourly >= 0.25m;
        if (extremeCost)
            row.FundingWarning = $"BLOCK: hướng {row.Direction} phải trả funding cực đoan {row.FundingRatePercent:+0.####;-0.####;0}%/{row.FundingIntervalHours}H.";
        else if (receives && absoluteHourly >= 0.25m)
            row.FundingWarning = "Funding nhận cực đoan: chỉ ưu tiên khi trend/entry vẫn hợp lệ; không đi ngược xu hướng để săn phí.";
        else
            row.FundingWarning = "";
    }

    private async Task<Dictionary<string, PremiumState>> LoadPremiumIndexAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/fapi/v1/premiumIndex", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray().ToDictionary(
            x => x.GetProperty("symbol").GetString() ?? "",
            x => new PremiumState(
                Parse(x, "markPrice"), Parse(x, "indexPrice"), Parse(x, "lastFundingRate"),
                x.TryGetProperty("nextFundingTime", out var nft) && nft.GetInt64() > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(nft.GetInt64()) : null),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, FundingInfoState>> LoadFundingInfoAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/fapi/v1/fundingInfo", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = new Dictionary<string, FundingInfoState>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in doc.RootElement.EnumerateArray())
        {
            var symbol = x.GetProperty("symbol").GetString() ?? "";
            var hours = x.TryGetProperty("fundingIntervalHours", out var h) ? h.GetInt32() : 8;
            result[symbol] = new FundingInfoState(Math.Max(1, hours));
        }
        return result;
    }

    private async Task<List<decimal>> LoadFundingHistoryAsync(string symbol, int limit, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"/fapi/v1/fundingRate?symbol={Uri.EscapeDataString(symbol)}&limit={Math.Clamp(limit, 1, 1000)}", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray().Select(x => Parse(x, "fundingRate")).ToList();
    }

    private static FundingSnapshot BuildSnapshot(string symbol, PremiumState? live, FundingInfoState? info, IReadOnlyList<decimal> history, decimal notional)
    {
        var rate = live?.FundingRate ?? history.LastOrDefault();
        var hours = info?.IntervalHours ?? InferInterval(live?.NextFundingTime);
        var percent = rate * 100m;
        var perHour = hours > 0 ? percent / hours : percent;
        var next = live?.NextFundingTime;
        var minutes = next.HasValue ? Math.Max(0, (int)Math.Ceiling((next.Value - DateTimeOffset.UtcNow).TotalMinutes)) : 0;
        var signs = history.Where(x => x != 0m).Select(Math.Sign).ToList();
        var signChanges = signs.Zip(signs.Skip(1), (a, b) => a != b ? 1 : 0).Sum();
        var currentSign = Math.Sign(rate);
        var sameSign = history.Reverse().TakeWhile(x => Math.Sign(x) == currentSign && currentSign != 0).Count();
        var average = history.Count > 0 ? history.Average() * 100m : percent;
        var level = Classify(Math.Abs(perHour));
        var flow = rate > 0 ? "LONG → SHORT" : rate < 0 ? "SHORT → LONG" : "KHÔNG CHUYỂN";
        var mark = live?.MarkPrice ?? 0m;
        var index = live?.IndexPrice ?? 0m;
        var premium = index > 0 ? (mark - index) / index * 100m : 0m;
        return new FundingSnapshot
        {
            Symbol = symbol,
            MarkPrice = mark,
            IndexPrice = index,
            PremiumPercent = premium,
            FundingRate = rate,
            FundingRatePercent = percent,
            FundingIntervalHours = hours,
            FundingPerHourPercent = perHour,
            Funding24hPercent = perHour * 24m,
            NextFundingTime = next,
            MinutesRemaining = minutes,
            Flow = flow,
            Level = level,
            AverageFundingPercent = average,
            SameSignPeriods = sameSign,
            SignChanges = signChanges,
            Stability = sameSign >= 6 && signChanges <= 1 ? "ỔN ĐỊNH" : signChanges >= 4 ? "DAO ĐỘNG" : "TRUNG BÌNH",
            EstimatedLongFunding = -notional * rate,
            EstimatedShortFunding = notional * rate
        };
    }

    private static FundingSnapshot Reprice(FundingSnapshot s, decimal notional) => new()
    {
        Symbol = s.Symbol, MarkPrice = s.MarkPrice, IndexPrice = s.IndexPrice, PremiumPercent = s.PremiumPercent,
        FundingRate = s.FundingRate, FundingRatePercent = s.FundingRatePercent, FundingIntervalHours = s.FundingIntervalHours,
        FundingPerHourPercent = s.FundingPerHourPercent, Funding24hPercent = s.Funding24hPercent,
        NextFundingTime = s.NextFundingTime, MinutesRemaining = s.NextFundingTime.HasValue ? Math.Max(0, (int)Math.Ceiling((s.NextFundingTime.Value - DateTimeOffset.UtcNow).TotalMinutes)) : 0,
        Flow = s.Flow, Level = s.Level, AverageFundingPercent = s.AverageFundingPercent, SameSignPeriods = s.SameSignPeriods,
        SignChanges = s.SignChanges, Stability = s.Stability, EstimatedLongFunding = -notional * s.FundingRate, EstimatedShortFunding = notional * s.FundingRate
    };

    private static void Apply(MarketRow row, FundingSnapshot s, decimal plannedNotional)
    {
        row.MarkPrice = s.MarkPrice; row.IndexPrice = s.IndexPrice; row.PremiumPercent = s.PremiumPercent;
        row.FundingRate = s.FundingRate; row.FundingRatePercent = s.FundingRatePercent; row.FundingIntervalHours = s.FundingIntervalHours;
        row.FundingPerHourPercent = s.FundingPerHourPercent; row.Funding24hPercent = s.Funding24hPercent;
        row.NextFundingTime = s.NextFundingTime; row.FundingMinutesRemaining = s.MinutesRemaining;
        row.FundingFlow = s.Flow; row.FundingLevel = s.Level; row.FundingAveragePercent = s.AverageFundingPercent;
        row.FundingSameSignPeriods = s.SameSignPeriods; row.FundingSignChanges = s.SignChanges; row.FundingStability = s.Stability;
        ApplyFundingDecision(row, plannedNotional);
    }

    private static string Classify(decimal absHourly) => absHourly switch
    {
        < 0.002m => "THẤP",
        < 0.01m => "BÌNH THƯỜNG",
        < 0.05m => "CAO",
        < 0.25m => "RẤT CAO",
        _ => "CỰC ĐOAN"
    };

    private static int InferInterval(DateTimeOffset? next)
    {
        if (!next.HasValue) return 8;
        var hours = Math.Max(1, (int)Math.Ceiling((next.Value - DateTimeOffset.UtcNow).TotalHours));
        return hours <= 1 ? 1 : hours <= 2 ? 2 : hours <= 4 ? 4 : 8;
    }

    private async Task<JsonDocument> SignedAsync(HttpMethod method, string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        parameters["recvWindow"] = "5000";
        var query = string.Join("&", parameters.OrderBy(x => x.Key).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        using var request = new HttpRequestMessage(method, $"{path}?{query}&signature={signature}");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Binance funding {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static decimal Parse(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    public void Dispose() => _http.Dispose();

    private sealed record PremiumState(decimal MarkPrice, decimal IndexPrice, decimal FundingRate, DateTimeOffset? NextFundingTime);
    private sealed record FundingInfoState(int IntervalHours);
}
