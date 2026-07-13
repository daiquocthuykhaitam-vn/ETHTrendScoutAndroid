using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly Pack16Config _pack16Config = new();
    private readonly Pack16RiskPolicy _pack16RiskPolicy = new();
    private readonly ProfitRetentionEngine _profitRetention = new();
    private readonly RadarDiagnosticsStore _radarDiagnostics = new();
    private readonly Pack16ExchangeVerifier _pack16Exchange = new();

    private readonly TextBox _maxBotMargin = Input("10");
    private readonly TextBox _maxBotNotional = Input("50");
    private readonly TextBox _maxExposurePercent = Input("15");
    private readonly TextBox _maxSymbolNotional = Input("15");
    private readonly TextBox _maxSpreadPercent = Input("0.20");
    private readonly TextBox _maxEntryDriftPercent = Input("0.20");
    private readonly TextBox _profitFloorActivation = Input("0.50");
    private readonly TextBox _maxProfitGiveback = Input("30");
    private readonly TextBlock _radarFunnel = T("RADAR FUNNEL: CHƯA QUÉT", 11.5, B("#9AB4C9"), true);

    internal void InitializePack16() => Closed += (_, _) => _pack16Exchange.Dispose();

    private void ReadPack16Settings()
    {
        _pack16Config.MaxBotMarginUsdt = ParsePack16(_maxBotMargin, "Tổng margin bot", 1m, 10_000m);
        _pack16Config.MaxBotNotionalUsdt = ParsePack16(_maxBotNotional, "Tổng notional bot", 1m, 100_000m);
        _pack16Config.MaxExposurePercentOfEquity = ParsePack16(_maxExposurePercent, "Exposure / equity", 0.1m, 100m);
        _pack16Config.MaxSymbolNotionalUsdt = ParsePack16(_maxSymbolNotional, "Notional mỗi cặp", 1m, 100_000m);
        _pack16Config.MaxSpreadPercent = ParsePack16(_maxSpreadPercent, "Spread tối đa", 0.001m, 5m);
        _pack16Config.MaxEntryDriftPercent = ParsePack16(_maxEntryDriftPercent, "Độ lệch Entry", 0.001m, 5m);
        _pack16Config.ProfitFloorActivationUsdt = ParsePack16(_profitFloorActivation, "Profit floor", 0.01m, 10_000m);
        _pack16Config.MaxProfitGivebackPercent = ParsePack16(_maxProfitGiveback, "Giveback tối đa", 1m, 95m);
    }

    private static decimal ParsePack16(TextBox input, string label, decimal min, decimal max)
    {
        if (!decimal.TryParse(input.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new InvalidOperationException($"{label} phải trong khoảng {min} đến {max}.");
        return value;
    }

    private (decimal Margin, decimal Notional, decimal SymbolNotional, int SymbolPositions) CurrentBotCapital(string symbol)
    {
        var owned = _state.Positions.Where(x => SessionOwnershipRegistry.IsCurrentSessionBotOwned(x.Symbol)).ToList();
        var notional = owned.Sum(x => Math.Abs(x.MarkPrice * x.Quantity));
        var margin = _pack14Config.Leverage > 0 ? notional / _pack14Config.Leverage : notional;
        var symbolRows = owned.Where(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList();
        return (margin, notional, symbolRows.Sum(x => Math.Abs(x.MarkPrice * x.Quantity)), symbolRows.Count);
    }

    private async Task<PreSubmitResult> FinalPreSubmitCheckAsync(
        MarketRow market, FrozenTradePlan plan, decimal orderNotional, CancellationToken ct)
    {
        var book = await _pack16Exchange.GetBookTickerAsync(market.Symbol, ct);
        var capital = CurrentBotCapital(market.Symbol);
        var account = await _binance.GetAccountAsync(ct);
        return _pack16RiskPolicy.Evaluate(new PreSubmitContext(
            market.Symbol, market.Price, book.BidPrice, book.AskPrice,
            plan.EntryLow, plan.EntryHigh, plan.MarginUsdt, orderNotional,
            capital.Margin, capital.Notional, capital.SymbolNotional,
            account.TotalWalletBalance, capital.SymbolPositions,
            await _pack16Exchange.IsOneWayAsync(ct),
            await _pack16Exchange.IsIsolatedAsync(market.Symbol, ct),
            _pack14.Intents.Any(x => string.Equals(x.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase) && x.PlanId == plan.PlanId),
            _blockNewEntries), _pack16Config);
    }

    private void UpdateRadarFunnel(string snapshotId, IReadOnlyCollection<MarketRow> rows)
    {
        var snapshot = new RadarFunnelSnapshot(snapshotId, DateTimeOffset.UtcNow,
            rows.Count, rows.Count,
            rows.Count(x => x.ListingAgeDays == 0 || x.ListingAgeDays >= 7),
            rows.Count(x => x.QuoteVolume >= 8_000_000m), rows.Count,
            rows.Count(x => x.Range24hPercent > 0m), rows.Count,
            rows.Count(x => x.LastAnalyzedUtc != default),
            rows.Count(x => x.AutoEligible),
            rows.Count(x => x.CandidateStage == CandidateStage.PlanReady.ToString()));
        _radarDiagnostics.AddSnapshot(snapshot);
        _radarFunnel.Text = $"UNIVERSE {snapshot.UniverseSelected} → PHÂN TÍCH {snapshot.Analyzed} → ĐỦ KỸ THUẬT {snapshot.AutoEligible} → PLAN READY {snapshot.PlanReady}";
        _radarFunnel.Foreground = snapshot.PlanReady > 0 ? Brushes.LimeGreen : Brushes.Gold;
    }
}
