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

    internal void InitializePack16()
    {
        Loaded += (_, _) => InstallPack16SettingsAndDiagnostics();
        Closed += (_, _) => _pack16Exchange.Dispose();
    }

    private void InstallPack16SettingsAndDiagnostics()
    {
        var tabs = FindVisualChildren<TabControl>(this).FirstOrDefault();
        if (tabs is null) return;

        var settingsTab = tabs.Items.OfType<TabItem>().FirstOrDefault(x => HeaderText(x).Contains("CÀI ĐẶT", StringComparison.OrdinalIgnoreCase));
        if (settingsTab?.Content is Grid settingsRoot && settingsRoot.Children.OfType<Border>().All(x => !Equals(x.Tag, "PACK16_RISK")))
        {
            var panel = new StackPanel { Margin = new System.Windows.Thickness(12) };
            panel.Children.Add(Label("TỔNG MARGIN BOT TỐI ĐA (USDT)")); panel.Children.Add(_maxBotMargin);
            panel.Children.Add(Label("TỔNG NOTIONAL BOT TỐI ĐA (USDT)")); panel.Children.Add(_maxBotNotional);
            panel.Children.Add(Label("EXPOSURE BOT TỐI ĐA / EQUITY (%)")); panel.Children.Add(_maxExposurePercent);
            panel.Children.Add(Label("NOTIONAL TỐI ĐA MỖI CẶP (USDT)")); panel.Children.Add(_maxSymbolNotional);
            panel.Children.Add(Label("SPREAD TỐI ĐA (%)")); panel.Children.Add(_maxSpreadPercent);
            panel.Children.Add(Label("ĐỘ LỆCH ENTRY TỐI ĐA (%)")); panel.Children.Add(_maxEntryDriftPercent);
            panel.Children.Add(Label("KÍCH HOẠT PROFIT FLOOR (USDT)")); panel.Children.Add(_profitFloorActivation);
            panel.Children.Add(Label("GIVEBACK TỐI ĐA (%)")); panel.Children.Add(_maxProfitGiveback);
            var card = Card("PACK 16 • VỐN & GIỮ LỢI NHUẬN", panel);
            card.Tag = "PACK16_RISK";
            Grid.SetColumn(card, 1);
            settingsRoot.Children.Add(card);
        }

        var radarTab = tabs.Items.OfType<TabItem>().FirstOrDefault(x => HeaderText(x).Equals("RADAR", StringComparison.OrdinalIgnoreCase));
        if (radarTab?.Content is Grid radarRoot && radarRoot.Children.OfType<Border>().All(x => !Equals(x.Tag, "PACK16_FUNNEL")))
        {
            var card = Card("RADAR FUNNEL • FILTER PIPELINE", _radarFunnel);
            card.Tag = "PACK16_FUNNEL";
            Grid.SetRow(card, Math.Max(0, radarRoot.RowDefinitions.Count - 1));
            radarRoot.Children.Add(card);
        }
    }

    private static string HeaderText(TabItem item)
    {
        if (item.Header is StackPanel panel)
            return string.Join(" ", panel.Children.OfType<TextBlock>().Select(x => x.Text)).Trim();
        return item.Header?.ToString() ?? string.Empty;
    }

    private void ReadPack16Settings()
    {
        _pack16Config.MaxBotMarginUsdt = ParsePositive(_maxBotMargin, "Tổng margin bot", 1m, 10_000m);
        _pack16Config.MaxBotNotionalUsdt = ParsePositive(_maxBotNotional, "Tổng notional bot", 1m, 100_000m);
        _pack16Config.MaxExposurePercentOfEquity = ParsePositive(_maxExposurePercent, "Exposure / equity", 0.1m, 100m);
        _pack16Config.MaxSymbolNotionalUsdt = ParsePositive(_maxSymbolNotional, "Notional mỗi cặp", 1m, 100_000m);
        _pack16Config.MaxSpreadPercent = ParsePositive(_maxSpreadPercent, "Spread tối đa", 0.001m, 5m);
        _pack16Config.MaxEntryDriftPercent = ParsePositive(_maxEntryDriftPercent, "Độ lệch Entry", 0.001m, 5m);
        _pack16Config.ProfitFloorActivationUsdt = ParsePositive(_profitFloorActivation, "Profit floor", 0.01m, 10_000m);
        _pack16Config.MaxProfitGivebackPercent = ParsePositive(_maxProfitGiveback, "Giveback tối đa", 1m, 95m);
    }

    private static decimal ParsePositive(TextBox input, string label, decimal min, decimal max)
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

    private async Task<PreSubmitResult> FinalPreSubmitCheckAsync(MarketRow market, FrozenTradePlan plan, decimal orderNotional, CancellationToken ct)
    {
        var book = await _pack16Exchange.GetBookTickerAsync(market.Symbol, ct);
        var oneWay = await _pack16Exchange.IsOneWayAsync(ct);
        var isolated = await _pack16Exchange.IsIsolatedAsync(market.Symbol, ct);
        var capital = CurrentBotCapital(market.Symbol);
        var account = await _binance.GetAccountAsync(ct);
        var context = new PreSubmitContext(
            market.Symbol,
            market.Price,
            book.BidPrice,
            book.AskPrice,
            plan.EntryLow,
            plan.EntryHigh,
            orderNotional,
            capital.Margin,
            capital.Notional,
            capital.SymbolNotional,
            account.TotalWalletBalance,
            capital.SymbolPositions,
            oneWay,
            isolated,
            HasDuplicateIntent: _pack14.Intents.Any(x => string.Equals(x.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase) && x.PlanId == plan.PlanId),
            NewEntriesBlocked: _blockNewEntries);
        return _pack16RiskPolicy.Evaluate(context, _pack16Config);
    }

    private void UpdateRadarFunnel(string snapshotId, IReadOnlyCollection<MarketRow> rows)
    {
        var analyzed = rows.Count(x => x.LastAnalyzedUtc != default);
        var eligible = rows.Count(x => x.AutoEligible);
        var ready = rows.Count(x => x.CandidateStage == CandidateStage.PlanReady.ToString());
        var snapshot = new RadarFunnelSnapshot(snapshotId, DateTimeOffset.UtcNow,
            ExchangeSymbols: rows.Count,
            TradingPerpetualUsdt: rows.Count,
            PassedAge: rows.Count(x => x.ListingAgeDays == 0 || x.ListingAgeDays >= 7),
            PassedLiquidity: rows.Count(x => x.QuoteVolume >= 8_000_000m),
            PassedSpread: rows.Count,
            PassedVolatility: rows.Count(x => x.Range24hPercent > 0m),
            UniverseSelected: rows.Count,
            Analyzed: analyzed,
            AutoEligible: eligible,
            PlanReady: ready);
        _radarDiagnostics.AddSnapshot(snapshot);
        _radarFunnel.Text = $"UNIVERSE {snapshot.UniverseSelected} → PHÂN TÍCH {snapshot.Analyzed} → ĐỦ KỸ THUẬT {snapshot.AutoEligible} → PLAN READY {snapshot.PlanReady}\nSnapshot: {snapshot.SnapshotId} • {snapshot.Time.LocalDateTime:HH:mm:ss}";
        _radarFunnel.Foreground = ready > 0 ? Brushes.LimeGreen : Brushes.Gold;
    }
}
