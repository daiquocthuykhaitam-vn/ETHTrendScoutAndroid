namespace TrendGovernor.Wpf;

public sealed class LargeWaveEngine
{
    private readonly BinanceCandleService _candles = new();

    public async Task AnalyzeAsync(MarketRow row, CancellationToken ct)
    {
        var d1Task = _candles.GetCandlesAsync(row.Symbol, "1d", 220, ct);
        var h4Task = _candles.GetCandlesAsync(row.Symbol, "4h", 220, ct);
        var h1Task = _candles.GetCandlesAsync(row.Symbol, "1h", 220, ct);
        var m15Task = _candles.GetCandlesAsync(row.Symbol, "15m", 160, ct);
        var m5Task = _candles.GetCandlesAsync(row.Symbol, "5m", 160, ct);
        await Task.WhenAll(d1Task, h4Task, h1Task, m15Task, m5Task);

        var d1 = d1Task.Result;
        var h4 = h4Task.Result;
        var h1 = h1Task.Result;
        var m15 = m15Task.Result;
        var m5 = m5Task.Result;
        if (d1.Count < 210 || h4.Count < 210 || h1.Count < 210 || m15.Count < 80 || m5.Count < 80)
        {
            row.Direction = "WAIT";
            row.Status = "CHỜ ĐỦ DỮ LIỆU";
            row.Reason = "Chưa đủ nến đa khung để xác nhận trend dài và sóng lớn.";
            row.AutoEligible = false;
            return;
        }

        var last = row.Price > 0 ? row.Price : h1[^1].Close;
        var d1Fast = Ema(d1, 20); var d1Mid = Ema(d1, 50); var d1Slow = Ema(d1, 200);
        var h4Fast = Ema(h4, 20); var h4Mid = Ema(h4, 50); var h4Slow = Ema(h4, 200);
        var h1Fast = Ema(h1, 20); var h1Mid = Ema(h1, 50); var h1Slow = Ema(h1, 200);

        row.Trend1D = TrendName(d1Fast, d1Mid, d1Slow);
        row.Trend4H = TrendName(h4Fast, h4Mid, h4Slow);
        row.Trend1H = TrendName(h1Fast, h1Mid, h1Slow);

        var longAligned = row.Trend1D == "TĂNG" && row.Trend4H == "TĂNG" && row.Trend1H == "TĂNG";
        var shortAligned = row.Trend1D == "GIẢM" && row.Trend4H == "GIẢM" && row.Trend1H == "GIẢM";

        var atr1h = Atr(h1, 14);
        row.AtrPercent = last > 0 ? atr1h / last * 100m : 0m;
        row.Adx1H = Adx(h1, 14);

        var rangeBars = h1.TakeLast(96).ToList();
        var rangeLow = rangeBars.Min(x => x.Low);
        var rangeHigh = rangeBars.Max(x => x.High);
        var range = Math.Max(rangeHigh - rangeLow, last * 0.0001m);
        row.PositionPercent = (last - rangeLow) / range * 100m;
        row.DistanceToEmaPercent = h1Fast > 0 ? (last - h1Fast) / h1Fast * 100m : 0m;

        var impulse12 = Math.Abs(h1[^1].Close - h1[^13].Close);
        var impulse36 = Math.Abs(h1[^1].Close - h1[^37].Close);
        var impulseAtr = atr1h > 0 ? Math.Max(impulse12 / atr1h, impulse36 / atr1h / 2m) : 0m;
        var volumeNow = h1.TakeLast(6).Average(x => x.Volume);
        var volumeBase = h1.Skip(Math.Max(0, h1.Count - 42)).Take(30).Average(x => x.Volume);
        var volumeRatio = volumeBase > 0 ? volumeNow / volumeBase : 1m;
        row.ExpectedMovePercent = row.AtrPercent * Math.Max(1.5m, Math.Min(3.5m, impulseAtr / 2m));

        var structureLong = IsHigherHighHigherLow(h1.TakeLast(48).ToList());
        var structureShort = IsLowerHighLowerLow(h1.TakeLast(48).ToList());
        row.WaveState = structureLong ? "HH/HL" : structureShort ? "LH/LL" : "CHƯA RÕ";

        var trendScore = 0;
        if (longAligned || shortAligned) trendScore += 55;
        if (row.Adx1H >= 22m) trendScore += 20;
        if ((longAligned && structureLong) || (shortAligned && structureShort)) trendScore += 20;
        if (Math.Abs((double)(h4Fast - h4Mid)) / Math.Max((double)last, 0.0000001d) >= 0.003d) trendScore += 5;
        row.TrendScore = Math.Min(100, trendScore);

        var waveScore = 20;
        if (impulseAtr >= 3m) waveScore += 30;
        else if (impulseAtr >= 2m) waveScore += 20;
        if (volumeRatio >= 1.25m) waveScore += 20;
        if (row.AtrPercent >= 0.45m) waveScore += 15;
        if ((longAligned && structureLong) || (shortAligned && structureShort)) waveScore += 15;
        row.WaveScore = Math.Min(100, waveScore);

        var m15Fast = Ema(m15, 20); var m15Slow = Ema(m15, 50);
        var m5Fast = Ema(m5, 20); var m5Slow = Ema(m5, 50);
        var longTiming = m15[^1].Close > m15Fast && m15Fast > m15Slow && m5[^1].Close > m5Fast && m5Fast > m5Slow;
        var shortTiming = m15[^1].Close < m15Fast && m15Fast < m15Slow && m5[^1].Close < m5Fast && m5Fast < m5Slow;
        row.TimingScore = (longTiming || shortTiming) ? 85 : 35;

        row.Direction = longAligned ? "LONG" : shortAligned ? "SHORT" : "WAIT";
        row.Setup = "PULLBACK CONTINUATION";

        var tooHighForLong = row.PositionPercent >= 78m || row.DistanceToEmaPercent > Math.Max(1.2m, row.AtrPercent * 2.2m);
        var tooLowForShort = row.PositionPercent <= 22m || row.DistanceToEmaPercent < -Math.Max(1.2m, row.AtrPercent * 2.2m);
        var pullbackLong = last >= h1Fast - atr1h * 0.35m && last <= h1Fast + atr1h * 0.35m;
        var pullbackShort = last >= h1Fast - atr1h * 0.35m && last <= h1Fast + atr1h * 0.35m;

        var swingLow = h1.TakeLast(24).Min(x => x.Low);
        var swingHigh = h1.TakeLast(24).Max(x => x.High);
        if (row.Direction == "LONG")
        {
            row.EntryLow = Math.Max(swingLow + atr1h * 0.15m, h1Fast - atr1h * 0.30m);
            row.EntryHigh = h1Fast + atr1h * 0.20m;
            row.StopLoss = Math.Min(swingLow - atr1h * 0.35m, row.EntryLow - atr1h * 0.90m);
            var risk = Math.Max(row.EntryHigh - row.StopLoss, atr1h * 0.8m);
            row.TakeProfit = Math.Max(swingHigh, row.EntryHigh + risk * 2.2m);
        }
        else if (row.Direction == "SHORT")
        {
            row.EntryLow = h1Fast - atr1h * 0.20m;
            row.EntryHigh = Math.Min(swingHigh - atr1h * 0.15m, h1Fast + atr1h * 0.30m);
            row.StopLoss = Math.Max(swingHigh + atr1h * 0.35m, row.EntryHigh + atr1h * 0.90m);
            var risk = Math.Max(row.StopLoss - row.EntryLow, atr1h * 0.8m);
            row.TakeProfit = Math.Min(swingLow, row.EntryLow - risk * 2.2m);
        }
        else
        {
            row.EntryLow = row.EntryHigh = row.StopLoss = row.TakeProfit = row.RiskReward = 0m;
        }

        if (row.StopLoss > 0 && row.TakeProfit > 0)
        {
            var risk = row.Direction == "LONG" ? row.EntryHigh - row.StopLoss : row.StopLoss - row.EntryLow;
            var reward = row.Direction == "LONG" ? row.TakeProfit - row.EntryHigh : row.EntryLow - row.TakeProfit;
            row.RiskReward = risk > 0 ? reward / risk : 0m;
        }

        row.Score = Math.Min(100, (int)Math.Round(row.TrendScore * 0.45m + row.WaveScore * 0.35m + row.TimingScore * 0.20m));
        row.LastAnalyzedUtc = DateTime.UtcNow;

        if (!longAligned && !shortAligned)
        {
            row.Status = "XUNG ĐỘT XU HƯỚNG";
            row.Reason = $"Trend 1D/4H/1H chưa đồng thuận: {row.Trend1D}/{row.Trend4H}/{row.Trend1H}.";
        }
        else if (row.Direction == "LONG" && tooHighForLong)
        {
            row.Status = "KHÔNG ĐUỔI GIÁ";
            row.Reason = $"LONG bị chặn vì giá ở {row.PositionPercent:0}% biên 1H hoặc cách EMA20 {row.DistanceToEmaPercent:0.00}%.";
        }
        else if (row.Direction == "SHORT" && tooLowForShort)
        {
            row.Status = "KHÔNG BÁN ĐÁY";
            row.Reason = $"SHORT bị chặn vì giá ở {row.PositionPercent:0}% biên 1H hoặc cách EMA20 {row.DistanceToEmaPercent:0.00}%.";
        }
        else if ((row.Direction == "LONG" && !longTiming) || (row.Direction == "SHORT" && !shortTiming))
        {
            row.Status = "CHỜ XÁC NHẬN 15M/5M";
            row.Reason = "Trend dài đúng nhưng timing 15m/5m chưa đồng thuận.";
        }
        else if ((row.Direction == "LONG" && !pullbackLong) || (row.Direction == "SHORT" && !pullbackShort))
        {
            row.Status = "CHỜ PULLBACK";
            row.Reason = "Chưa về vùng vào quanh EMA20 1H/swing hợp lệ.";
        }
        else if (row.Adx1H < 20m || row.WaveScore < 60)
        {
            row.Status = "SÓNG CHƯA ĐỦ MẠNH";
            row.Reason = $"ADX {row.Adx1H:0.0}, WaveScore {row.WaveScore}; chưa đạt tiêu chuẩn sóng lớn.";
        }
        else if (row.RiskReward < 2m)
        {
            row.Status = "RR CHƯA ĐẠT";
            row.Reason = $"RR {row.RiskReward:0.00} dưới 2.00.";
        }
        else
        {
            row.Status = "ĐỦ ĐIỀU KIỆN";
            row.Reason = $"Trend {row.Trend1D}/{row.Trend4H}/{row.Trend1H}, {row.WaveState}, ADX {row.Adx1H:0.0}, RR {row.RiskReward:0.00}.";
        }

        row.AutoEligible = row.Status == "ĐỦ ĐIỀU KIỆN" && row.Score >= 80 && row.TrendScore >= 80 && row.WaveScore >= 60 && row.RiskReward >= 2m;
    }

    private static string TrendName(decimal fast, decimal mid, decimal slow)
        => fast > mid && mid > slow ? "TĂNG" : fast < mid && mid < slow ? "GIẢM" : "ĐI NGANG";

    private static decimal Ema(IReadOnlyList<CandlePoint> candles, int period)
    {
        var values = candles.Select(x => x.Close).ToList();
        var ema = values.Take(period).Average();
        var k = 2m / (period + 1m);
        for (var i = period; i < values.Count; i++) ema = values[i] * k + ema * (1m - k);
        return ema;
    }

    private static decimal Atr(IReadOnlyList<CandlePoint> candles, int period)
    {
        var trs = new List<decimal>();
        for (var i = 1; i < candles.Count; i++)
        {
            var highLow = candles[i].High - candles[i].Low;
            var highClose = Math.Abs(candles[i].High - candles[i - 1].Close);
            var lowClose = Math.Abs(candles[i].Low - candles[i - 1].Close);
            trs.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
        }
        return trs.TakeLast(period).DefaultIfEmpty(0m).Average();
    }

    private static decimal Adx(IReadOnlyList<CandlePoint> candles, int period)
    {
        if (candles.Count < period * 3) return 0m;
        var tr = new List<decimal>(); var plus = new List<decimal>(); var minus = new List<decimal>();
        for (var i = 1; i < candles.Count; i++)
        {
            var up = candles[i].High - candles[i - 1].High;
            var down = candles[i - 1].Low - candles[i].Low;
            plus.Add(up > down && up > 0 ? up : 0m);
            minus.Add(down > up && down > 0 ? down : 0m);
            tr.Add(Math.Max(candles[i].High - candles[i].Low, Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close), Math.Abs(candles[i].Low - candles[i - 1].Close))));
        }
        var dx = new List<decimal>();
        for (var i = period; i < tr.Count; i++)
        {
            var atr = tr.Skip(i - period).Take(period).Sum();
            if (atr <= 0) continue;
            var pdi = plus.Skip(i - period).Take(period).Sum() / atr * 100m;
            var mdi = minus.Skip(i - period).Take(period).Sum() / atr * 100m;
            var denominator = pdi + mdi;
            if (denominator > 0) dx.Add(Math.Abs(pdi - mdi) / denominator * 100m);
        }
        return dx.TakeLast(period).DefaultIfEmpty(0m).Average();
    }

    private static bool IsHigherHighHigherLow(IReadOnlyList<CandlePoint> candles)
    {
        var a = candles.Take(candles.Count / 2).ToList();
        var b = candles.Skip(candles.Count / 2).ToList();
        return b.Max(x => x.High) > a.Max(x => x.High) && b.Min(x => x.Low) > a.Min(x => x.Low);
    }

    private static bool IsLowerHighLowerLow(IReadOnlyList<CandlePoint> candles)
    {
        var a = candles.Take(candles.Count / 2).ToList();
        var b = candles.Skip(candles.Count / 2).ToList();
        return b.Max(x => x.High) < a.Max(x => x.High) && b.Min(x => x.Low) < a.Min(x => x.Low);
    }
}
