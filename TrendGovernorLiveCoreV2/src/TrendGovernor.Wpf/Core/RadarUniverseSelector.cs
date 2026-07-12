using System.Collections.Concurrent;

namespace TrendGovernor.Wpf;

/// <summary>
/// Builds a balanced Binance USD-M perpetual radar universe. The 40 rows are not simply the
/// highest-volume coins: they combine liquid majors, strongest upside/downside movers,
/// high-range contracts and continuity candidates from the previous cycle.
/// </summary>
public sealed class RadarUniverseSelector
{
    private readonly ConcurrentDictionary<string, int> _continuity = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MarketRow> Select(IReadOnlyCollection<RadarTickerInput> inputs, int limit = 40)
    {
        limit = Math.Clamp(limit, 10, 80);
        var eligible = inputs
            .Where(IsEligible)
            .Select(ToMarketRow)
            .ToList();

        if (eligible.Count == 0) return Array.Empty<MarketRow>();

        var selected = new Dictionary<string, MarketRow>(StringComparer.OrdinalIgnoreCase);
        AddBucket(selected, eligible.OrderByDescending(x => x.QuoteVolume), 12, "THANH KHOẢN LÕI");
        AddBucket(selected, eligible.Where(x => x.Change24h > 0).OrderByDescending(x => x.Change24h), 7, "TĂNG MẠNH 24H");
        AddBucket(selected, eligible.Where(x => x.Change24h < 0).OrderBy(x => x.Change24h), 7, "GIẢM MẠNH 24H");
        AddBucket(selected, eligible.OrderByDescending(x => x.Range24hPercent), 8, "BIÊN ĐỘ LỚN");
        AddBucket(selected, eligible.Where(x => _continuity.ContainsKey(x.Symbol))
            .OrderByDescending(x => _continuity.TryGetValue(x.Symbol, out var age) ? age : 0)
            .ThenByDescending(x => x.UniverseScore), 6, "DUY TRÌ THEO DÕI");

        foreach (var row in eligible.OrderByDescending(x => x.UniverseScore))
        {
            if (selected.Count >= limit) break;
            selected.TryAdd(row.Symbol, row);
        }

        var result = selected.Values
            .OrderByDescending(x => x.UniverseScore)
            .ThenByDescending(x => x.QuoteVolume)
            .Take(limit)
            .ToList();

        var current = result.Select(x => x.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in _continuity.Keys)
        {
            if (!current.Contains(symbol)) _continuity.TryRemove(symbol, out _);
        }
        foreach (var row in result)
        {
            _continuity.AddOrUpdate(row.Symbol, 1, (_, value) => Math.Min(value + 1, 1000));
            row.RetainedByMemory = _continuity[row.Symbol] > 1;
        }

        for (var i = 0; i < result.Count; i++) result[i].RadarRank = i + 1;
        return result;
    }

    private static bool IsEligible(RadarTickerInput x)
    {
        if (!x.IsTrading || !x.IsPerpetual || !string.Equals(x.QuoteAsset, "USDT", StringComparison.OrdinalIgnoreCase)) return false;
        if (x.Price <= 0m || x.QuoteVolume < 8_000_000m || x.TradeCount < 1000) return false;
        if (x.ListingAgeDays is > 0 and < 7) return false;

        var baseAsset = x.BaseAsset.ToUpperInvariant();
        string[] blocked = ["USDC", "FDUSD", "TUSD", "USDP", "DAI", "BUSD", "USDE", "USD1"];
        if (blocked.Contains(baseAsset)) return false;
        if (baseAsset.EndsWith("UP", StringComparison.Ordinal) || baseAsset.EndsWith("DOWN", StringComparison.Ordinal) ||
            baseAsset.EndsWith("BULL", StringComparison.Ordinal) || baseAsset.EndsWith("BEAR", StringComparison.Ordinal)) return false;
        return true;
    }

    private static MarketRow ToMarketRow(RadarTickerInput x)
    {
        var range = x.LowPrice > 0m ? Math.Max(0m, (x.HighPrice - x.LowPrice) / x.LowPrice * 100m) : 0m;
        var liquidity = ScoreLog(x.QuoteVolume, 8_000_000m, 1_000_000_000m, 35);
        var movement = Math.Min(20, (int)Math.Round(Math.Abs(x.ChangePercent) * 1.6m));
        var rangeScore = Math.Min(25, (int)Math.Round(range * 2.2m));
        var trades = ScoreLog(x.TradeCount, 1000m, 2_000_000m, 10);
        var maturity = x.ListingAgeDays >= 30 ? 10 : x.ListingAgeDays >= 14 ? 6 : 2;

        return new MarketRow
        {
            Symbol = x.Symbol,
            Price = x.Price,
            Change24h = x.ChangePercent,
            Range24hPercent = range,
            QuoteVolume = x.QuoteVolume,
            ListingAgeDays = x.ListingAgeDays,
            LiquidityTier = x.QuoteVolume >= 500_000_000m ? "A" : x.QuoteVolume >= 100_000_000m ? "B" : "C",
            UniverseScore = Math.Clamp(liquidity + movement + rangeScore + trades + maturity, 0, 100),
            UniverseSource = "ĐIỂM TỔNG HỢP"
        };
    }

    private static void AddBucket(Dictionary<string, MarketRow> selected, IEnumerable<MarketRow> source, int count, string label)
    {
        var added = 0;
        foreach (var row in source)
        {
            if (added >= count) break;
            if (selected.TryAdd(row.Symbol, row))
            {
                row.UniverseSource = label;
                added++;
            }
        }
    }

    private static int ScoreLog(decimal value, decimal min, decimal max, int points)
    {
        if (value <= min) return 0;
        if (value >= max) return points;
        var ratio = (Math.Log((double)value) - Math.Log((double)min)) /
                    (Math.Log((double)max) - Math.Log((double)min));
        return Math.Clamp((int)Math.Round(ratio * points), 0, points);
    }
}

public sealed record RadarTickerInput(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    bool IsTrading,
    bool IsPerpetual,
    int ListingAgeDays,
    decimal Price,
    decimal ChangePercent,
    decimal HighPrice,
    decimal LowPrice,
    decimal QuoteVolume,
    long TradeCount);
