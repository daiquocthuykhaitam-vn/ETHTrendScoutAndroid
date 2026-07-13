using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly BinanceExecutionGateway _execution = new();
    private readonly LargeWaveEngine _largeWave = new();
    private readonly FundingIntelligenceService _fundingIntel = new();
    private readonly SemaphoreSlim _autoCycleGate = new(1, 1);
    private readonly SemaphoreSlim _autoExecutionGate = new(1, 1);
    private readonly Dictionary<string, FrozenTradePlan> _activePlans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _peakNetPnl = new(StringComparer.OrdinalIgnoreCase);
    private readonly Pack14Runtime _pack14 = new(AppContext.BaseDirectory, SessionOwnershipRegistry.SessionId);
    private readonly TradingConfig _pack14Config = new() { MarginPerTrade = 2m, Leverage = 3, MaxPositions = 5 };
    private readonly AnalysisBundleExporter _bundleExporter = new();

    private readonly TextBlock _autoState = T("AUTO LIVE: DỪNG", 16, Brushes.Gold, true);
    private readonly Button _startAutoButton = Btn("BẮT ĐẦU AUTO LIVE", "#16784A");
    private readonly Button _emergencyStopButton = Btn("DỪNG KHẨN CẤP", "#9B1C31");

    private CancellationTokenSource? _autoCts;
    private Task? _autoLoopTask;
    private bool _blockNewEntries;

    private UIElement BuildAutoControlPanel()
    {
        _startAutoButton.Click -= StartAutoLiveClicked;
        _startAutoButton.Click += StartAutoLiveClicked;
        _emergencyStopButton.Click -= EmergencyStopClicked;
        _emergencyStopButton.Click += EmergencyStopClicked;
        _emergencyStopButton.IsEnabled = false;

        var panel = new StackPanel();
        panel.Children.Add(_autoState);
        panel.Children.Add(_startAutoButton);
        panel.Children.Add(_emergencyStopButton);
        panel.Children.Add(Btn("LÀM MỚI DỮ LIỆU", "#245EDB", (_, _) => _ = RefreshAllAsync()));
        panel.Children.Add(Btn("QUÉT THỦ CÔNG", "#275A8C", async (_, _) => await RunAutoCycleAsync(true, _cts.Token)));
        panel.Children.Add(Btn("TẠO ZIP PHÂN TÍCH", "#5B4BB7", async (_, _) => await ExportAnalysisBundleAsync()));
        panel.Children.Add(T("AUTO LIVE: Universe 40 → Lifecycle → Plan → VerifyGrant → PreSubmit → OrderIntent → Execute → Fill → Protect → Manage.", 11, B("#7F9AB5")));
        panel.Children.Add(T("SỞ HỮU: chỉ BOT PHIÊN HIỆN TẠI được quản lý/đóng. Vị thế có sẵn, mở tay và bot phiên cũ luôn chỉ đọc, không chiếm capacity bot.", 11, B("#66D49A"), true));
        return panel;
    }

    private void ReadLiveSettings()
    {
        if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0m || margin > 100m)
            throw new InvalidOperationException("Margin mỗi lệnh phải lớn hơn 0 và không vượt 100 USDT.");
        if (!int.TryParse(_leverage.Text, out var leverage) || leverage is < 1 or > 20)
            throw new InvalidOperationException("Đòn bẩy phải từ 1x đến 20x.");
        if (!int.TryParse(_maxBotPositions.Text, out var maxBotPositions) || maxBotPositions is < 1 or > 10)
            throw new InvalidOperationException("Số vị thế bot tối đa phải từ 1 đến 10.");

        _pack14Config.MarginPerTrade = margin;
        _pack14Config.Leverage = leverage;
        _pack14Config.MaxPositions = maxBotPositions;
        _pack14Config.AutoLiveEnabled = true;
        ReadPack16Settings();
    }

    private async void StartAutoLiveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_autoLoopTask is { IsCompleted: false }) return;
            if (!CredentialRules.IsUsable(_apiKey.Text) || !CredentialRules.IsUsable(_apiSecret.Password))
                throw new InvalidOperationException("API Key/Secret chưa hợp lệ hoặc vẫn là placeholder.");

            ReadLiveSettings();
            _binance.SetCredentials(_apiKey.Text, _apiSecret.Password);
            _execution.SetCredentials(_apiKey.Text, _apiSecret.Password);
            _fundingIntel.SetCredentials(_apiKey.Text, _apiSecret.Password);
            _pack16Exchange.SetCredentials(_apiKey.Text, _apiSecret.Password);

            if (await _execution.IsHedgeModeAsync(_cts.Token))
                throw new InvalidOperationException("Tài khoản đang ở Hedge Mode. AUTO LIVE yêu cầu One-way Mode.");

            await _execution.StartUserDataStreamAsync(_cts.Token);
            _execution.EventReceived -= ExecutionEventReceived;
            _execution.EventReceived += ExecutionEventReceived;
            await RefreshPrivateAsync(_cts.Token);

            _blockNewEntries = false;
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _autoLoopTask = RunAutoLoopAsync(_autoCts.Token);
            _autoState.Text = "AUTO LIVE: ĐANG CHẠY";
            _autoState.Foreground = Brushes.LimeGreen;
            _startAutoButton.IsEnabled = false;
            _emergencyStopButton.IsEnabled = true;
            SetEntryBlocker("ĐANG QUÉT", "Chờ Radar tìm ứng viên đạt soft-gate LIVE.", Brushes.LimeGreen);
            Log("AUTO", $"Bắt đầu Pack 16; session {_pack14.SessionId}; margin {_pack14Config.MarginPerTrade:0.##} USDT, leverage {_pack14Config.Leverage}x, max positions {_pack14Config.MaxPositions}, max bot margin {_pack16Config.MaxBotMarginUsdt:0.##}, max bot notional {_pack16Config.MaxBotNotionalUsdt:0.##}.");
        }
        catch (Exception ex)
        {
            _autoState.Text = "AUTO LIVE: KHÔNG THỂ KHỞI ĐỘNG";
            _autoState.Foreground = Brushes.OrangeRed;
            SetEntryBlocker("AUTO_START", ex.Message, Brushes.OrangeRed);
            AddAlert("AUTO START", ex.Message);
            MessageBox.Show(ex.Message, "AUTO LIVE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EmergencyStopClicked(object sender, RoutedEventArgs e)
    {
        _blockNewEntries = true;
        try { _autoCts?.Cancel(); } catch { }
        _autoState.Text = "AUTO LIVE: DỪNG KHẨN CẤP";
        _autoState.Foreground = Brushes.OrangeRed;
        _startAutoButton.IsEnabled = true;
        _emergencyStopButton.IsEnabled = false;
        SetEntryBlocker("EMERGENCY_STOP", "Đã khóa lệnh mới; chỉ đóng BOT PHIÊN HIỆN TẠI.", Brushes.OrangeRed);
        Log("EMERGENCY", "Đã khóa lệnh mới. Chỉ xử lý BOT PHIÊN HIỆN TẠI.");

        try
        {
            await RefreshPrivateAsync(_cts.Token);
            foreach (var position in _state.Positions.Where(p => SessionOwnershipRegistry.IsCurrentSessionBotOwned(p.Symbol)).ToList())
                await CloseOwnedPositionAndVerifyAsync(position, "EMERGENCY_STOP", _cts.Token);
            await RefreshPrivateAsync(_cts.Token);
        }
        catch (Exception ex) { AddAlert("EMERGENCY", ex.Message); }
    }

    private async Task RunAutoLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAutoCycleAsync(false, ct);
                await Task.Delay(TimeSpan.FromSeconds(_pack14Config.ScanIntervalSeconds), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SetEntryBlocker("AUTO_LOOP_ERROR", ex.Message, Brushes.OrangeRed);
                AddAlert("AUTO LOOP", ex.Message);
                Log("AUTO LOOP", ex.ToString());
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }

    private async Task RunAutoCycleAsync(bool manualOnly, CancellationToken ct)
    {
        if (!await _autoCycleGate.WaitAsync(0, ct)) return;
        try
        {
            ReadLiveSettings();
            SetStatus("AUTO: TẠO UNIVERSE 40 COIN", Brushes.Gold);
            var markets = await _binance.LoadTopMarketsAsync(ct);
            _state.Markets.Clear();
            foreach (var market in markets) _state.Markets.Add(market);
            _marketCount.Text = markets.Count.ToString(CultureInfo.InvariantCulture);

            SetStatus("AUTO: PHÂN TÍCH 40 COIN ĐA KHUNG", Brushes.Gold);
            var deep = _state.Markets.Take(_pack14Config.DeepScanCount).ToList();
            using var semaphore = new SemaphoreSlim(4);
            await Task.WhenAll(deep.Select(async row =>
            {
                await semaphore.WaitAsync(ct);
                try { await _largeWave.AnalyzeAsync(row, ct); }
                catch (Exception ex)
                {
                    row.Direction = "WAIT";
                    row.Status = "LỖI PHÂN TÍCH";
                    row.Reason = ex.Message;
                    row.AutoEligible = false;
                }
                finally { semaphore.Release(); }
            }));

            var plannedNotional = _pack14Config.MarginPerTrade * _pack14Config.Leverage;
            SetStatus("AUTO: FUNDING + CANDIDATE LIFECYCLE", Brushes.Gold);
            await _fundingIntel.EnrichMarketsAsync(deep, plannedNotional, ct);
            var snapshotId = $"snap-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            foreach (var row in deep)
            {
                var technicalScore = row.Score;
                row.Score = Math.Clamp((int)Math.Round(technicalScore * 0.90m + row.FundingScore * 0.10m), 0, 100);
                if (row.FundingWarning.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
                {
                    row.AutoEligible = false;
                    row.Status = "BLOCK FUNDING CỰC ĐOAN";
                    row.Reason = row.FundingWarning;
                }
                await _pack14.ObserveAsync(row, snapshotId, _pack14Config.RequiredStableCycles, ct);
            }

            var ordered = _state.Markets
                .OrderByDescending(x => x.CandidateStage == CandidateStage.PlanReady.ToString())
                .ThenByDescending(x => x.AutoEligible)
                .ThenByDescending(x => x.StableCycles)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.UniverseScore)
                .ThenByDescending(x => x.WaveScore)
                .ToList();

            _state.Markets.Clear();
            foreach (var row in ordered) _state.Markets.Add(row);
            UpdateRadarFunnel(snapshotId, ordered);
            SyncPack14UiState();
            _marketGrid.Items.Refresh();
            _radarGrid.Items.Refresh();

            var planReady = ordered.Count(x => x.CandidateStage == CandidateStage.PlanReady.ToString());
            _candidateCount.Text = planReady.ToString(CultureInfo.InvariantCulture);
            var selected = ordered.FirstOrDefault(x => x.CandidateStage == CandidateStage.PlanReady.ToString()) ?? ordered.FirstOrDefault();
            if (selected is not null) await SelectCandidateAsync(selected);
            Log("RADAR", $"Universe {ordered.Count}; PlanReady {planReady}; snapshot {snapshotId}.");

            if (_binance.HasCredentials) await RefreshPrivateAsync(ct);
            await MonitorOpenPositionsAsync(ct);

            if (manualOnly)
            {
                SetEntryBlocker("QUÉT THỦ CÔNG", "Đã cập nhật Radar; quét tay không gửi lệnh.");
                return;
            }
            if (_blockNewEntries)
            {
                SetEntryBlocker("SYSTEM_ENTRY_LOCK", "Hệ thống đang khóa lệnh mới.", Brushes.OrangeRed);
                return;
            }

            var botPositionCount = CurrentSessionBotPositionCount();
            if (botPositionCount >= _pack14Config.MaxPositions)
            {
                SetEntryBlocker("BOT_CAPACITY", $"Bot phiên hiện tại {botPositionCount}/{_pack14Config.MaxPositions}. Vị thế tay không được tính.", Brushes.LightSkyBlue);
                return;
            }

            var candidate = ordered.FirstOrDefault(x => x.CandidateStage == CandidateStage.PlanReady.ToString());
            if (candidate is null)
            {
                var closest = ordered.Where(x => x.LastAnalyzedUtc != default)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.TrendScore)
                    .ThenByDescending(x => x.WaveScore)
                    .FirstOrDefault();
                SetEntryBlocker(closest?.Status ?? "NO_PLAN_READY",
                    closest is null ? "Không có dữ liệu ứng viên hợp lệ." : $"{closest.Symbol}: {closest.Reason}");
                return;
            }

            await ExecuteAutoCandidateAsync(candidate, ct);
        }
        finally { _autoCycleGate.Release(); }
    }

    private async Task ExecuteAutoCandidateAsync(MarketRow market, CancellationToken ct)
    {
        if (!await _autoExecutionGate.WaitAsync(0, ct)) return;
        CandidateRecord? candidate = null;
        OrderIntent? intent = null;
        ExecutionFill? fill = null;
        FrozenTradePlan? plan = null;
        try
        {
            ReadLiveSettings();
            var botPositionCount = CurrentSessionBotPositionCount();
            if (_blockNewEntries) { SetEntryBlocker("SYSTEM_ENTRY_LOCK", "Hệ thống đang khóa lệnh mới.", Brushes.OrangeRed); return; }
            if (botPositionCount >= _pack14Config.MaxPositions)
            {
                SetEntryBlocker("BOT_CAPACITY", $"Bot phiên hiện tại {botPositionCount}/{_pack14Config.MaxPositions}. Vị thế tay không được tính.");
                return;
            }

            candidate = _pack14.FindCandidate(market.Symbol) ?? throw new InvalidOperationException("Candidate lifecycle không tồn tại.");
            if (candidate.Stage != CandidateStage.PlanReady)
                throw new InvalidOperationException($"Candidate chưa PLAN_READY: {candidate.Stage}.");

            plan = FrozenTradePlan.FromCandidate(market, _pack14Config.MarginPerTrade, _pack14Config.Leverage);
            _activePlans[market.Symbol] = plan;
            await _pack14.RecordPlanAsync(candidate, plan, ct);

            var rules = await _execution.GetRulesAsync(market.Symbol, ct);
            var quantity = _execution.NormalizeQuantity(plan.MarginUsdt * plan.Leverage / market.Price, rules);
            await _execution.EnsureIsolatedAsync(market.Symbol, ct);
            await _execution.SetLeverageAsync(market.Symbol, plan.Leverage, ct);

            var grant = await _pack14.VerifyAsync(candidate, plan, market, rules, quantity,
                accountIsOneWay: await _pack16Exchange.IsOneWayAsync(ct),
                isolatedReady: await _pack16Exchange.IsIsolatedAsync(market.Symbol, ct),
                botPositionCount, _pack14Config.MaxPositions, _blockNewEntries, ct);
            SyncPack14UiState();

            if (!grant.Passed)
            {
                var failed = grant.Gates.Where(x => !x.Passed).ToList();
                var finalGate = failed.LastOrDefault();
                _pack14.RecordMissed(market.Symbol, candidate.CandidateId, candidate.Stage.ToString(), "VERIFY_BLOCKED",
                    string.Join("; ", failed.Select(x => $"{x.GateCode}:{x.Reason}")));
                SetEntryBlocker(finalGate?.GateCode ?? "VERIFY_BLOCKED", $"{market.Symbol}: {finalGate?.Reason ?? "Không rõ lý do"}", Brushes.OrangeRed);
                Log("VERIFY", $"{market.Symbol}: BLOCK — {string.Join(", ", failed.Select(x => x.GateCode))}");
                return;
            }

            var preSubmit = await FinalPreSubmitCheckAsync(market, plan, quantity * market.Price, ct);
            if (!preSubmit.Passed)
            {
                _pack14.RecordMissed(market.Symbol, candidate.CandidateId, candidate.Stage.ToString(), preSubmit.GateCode, preSubmit.Reason);
                SetEntryBlocker(preSubmit.GateCode, $"{market.Symbol}: {preSubmit.Reason}", Brushes.OrangeRed);
                Log("PRE-SUBMIT", $"{market.Symbol}: BLOCK {preSubmit.GateCode}; spread={preSubmit.SpreadPercent:0.####}%; drift={preSubmit.EntryDriftPercent:0.####}%.");
                return;
            }

            SetEntryBlocker("PRE_SUBMIT_PASS", $"{market.Symbol}: spread {preSubmit.SpreadPercent:0.####}%, drift {preSubmit.EntryDriftPercent:0.####}%; đang gửi lệnh.", Brushes.LimeGreen);
            intent = await _pack14.CreateIntentAsync(grant, plan, quantity, candidate, ct);
            await _pack14.MarkOrderSubmittedAsync(candidate, intent, ct);
            fill = await _execution.PlaceMarketAndWaitFillAsync(intent, ct);
            await _pack14.MarkOrderTerminalAsync(candidate, intent, fill, ct);

            if (fill.Status != "FILLED" || fill.ExecutedQuantity <= 0m || fill.AveragePrice <= 0m)
                throw new InvalidOperationException($"Order kết thúc {fill.Status}, không có full fill.");

            SessionOwnershipRegistry.RegisterFilled(market.Symbol, fill.ClientOrderId, fill.OrderId);
            var stop = _execution.NormalizePrice(plan.StopLoss, rules);
            var takeProfit = _execution.NormalizePrice(plan.TakeProfit, rules);
            if (!ValidProtectionGeometry(plan.Direction, fill.AveragePrice, stop, takeProfit))
                throw new InvalidOperationException("Sau Fill, hình học SL/TP không còn hợp lệ.");

            var protection = await _execution.PlaceAndVerifyProtectionAsync(market.Symbol, plan.ExitSide, fill.ExecutedQuantity, stop, takeProfit, ct);
            await _pack14.MarkProtectionAsync(candidate, intent.OrderIntentId, protection, ct);
            if (!protection.IsProtected) throw new InvalidOperationException("Không xác minh đủ SL/TP sau Fill.");

            SetEntryBlocker("PROTECTED", $"{market.Symbol}: FILLED và đã xác minh SL/TP.", Brushes.LimeGreen);
            Log("PROTECT", $"{market.Symbol}: SL {protection.StopAlgoId}, TP {protection.TakeProfitAlgoId} đã xác minh.");
            await RefreshPrivateAsync(ct);
            SyncPack14UiState();
        }
        catch (Exception ex)
        {
            SetEntryBlocker("AUTO_EXECUTION_ERROR", ex.Message, Brushes.OrangeRed);
            AddAlert("AUTO EXECUTION", ex.Message);
            Log("AUTO EXECUTION", ex.ToString());
            if (fill is { ExecutedQuantity: > 0m } && SessionOwnershipRegistry.IsCurrentSessionBotOwned(fill.Symbol))
                await CompensateUnprotectedFillAsync(fill, plan, intent, ex, ct);
        }
        finally { _autoExecutionGate.Release(); }
    }

    private async Task CompensateUnprotectedFillAsync(ExecutionFill fill, FrozenTradePlan? plan, OrderIntent? intent, Exception cause, CancellationToken ct)
    {
        _blockNewEntries = true;
        try
        {
            await RefreshPrivateAsync(ct);
            var position = _state.Positions.FirstOrDefault(x => string.Equals(x.Symbol, fill.Symbol, StringComparison.OrdinalIgnoreCase));
            if (position is null) { SessionOwnershipRegistry.Release(fill.Symbol); return; }

            if (plan is not null)
            {
                try
                {
                    var rules = await _execution.GetRulesAsync(fill.Symbol, ct);
                    var stop = _execution.NormalizePrice(plan.StopLoss, rules);
                    var tp = _execution.NormalizePrice(plan.TakeProfit, rules);
                    var retry = await _execution.PlaceAndVerifyProtectionAsync(fill.Symbol, plan.ExitSide, position.Quantity, stop, tp, ct);
                    var candidate = _pack14.FindCandidate(fill.Symbol);
                    if (candidate is not null) await _pack14.MarkProtectionAsync(candidate, intent?.OrderIntentId ?? fill.ClientOrderId, retry, ct);
                    if (retry.IsProtected)
                    {
                        SetEntryBlocker("PROTECTION_RECOVERED", $"{fill.Symbol}: retry SL/TP thành công.", Brushes.LimeGreen);
                        return;
                    }
                }
                catch (Exception retryEx) { Log("PROTECTION RETRY", retryEx.Message); }
            }

            await CloseOwnedPositionAndVerifyAsync(position, "UNPROTECTED_COMPENSATION", ct);
            SetEntryBlocker("UNPROTECTED_CLOSED", $"{fill.Symbol}: đã đóng và xác minh position=0.", Brushes.OrangeRed);
            AddAlert("EMERGENCY_UNPROTECTED", $"{fill.Symbol}: đã đóng và xác minh sau lỗi protection: {cause.Message}");
        }
        catch (Exception closeEx)
        {
            SetEntryBlocker("CRITICAL_CLOSE_FAILURE", $"{fill.Symbol}: {closeEx.Message}", Brushes.Red);
            AddAlert("CRITICAL_CLOSE_FAILURE", $"{fill.Symbol}: không xác minh được emergency close: {closeEx.Message}");
        }
    }

    private async Task MonitorOpenPositionsAsync(CancellationToken ct)
    {
        foreach (var position in _state.Positions.ToList())
        {
            if (!SessionOwnershipRegistry.IsCurrentSessionBotOwned(position.Symbol))
            {
                position.Owner = "MỞ TAY / CÓ SẴN";
                position.Health = "CHỈ ĐỌC — KHÔNG CAN THIỆP";
                position.Recommendation = "ANH TỰ QUẢN LÝ";
                continue;
            }

            position.Owner = "BOT";
            var analysis = new MarketRow { Symbol = position.Symbol, Price = position.MarkPrice };
            try
            {
                await _largeWave.AnalyzeAsync(analysis, ct);
                await _fundingIntel.EnrichMarketsAsync([analysis], Math.Abs(position.MarkPrice * position.Quantity), ct);
            }
            catch (Exception ex) { Log("POSITION MONITOR", $"{position.Symbol}: {ex.Message}"); }

            var netPnl = position.NetPnlAfterFunding;
            if (!_peakNetPnl.TryGetValue(position.Symbol, out var peak) || netPnl > peak) _peakNetPnl[position.Symbol] = netPnl;
            peak = _peakNetPnl[position.Symbol];
            var decision = _profitRetention.Evaluate(position, analysis, peak, _pack16Config);
            position.PeakNetPnl = decision.PeakNetPnl;
            position.GivebackPercent = decision.GivebackPercent;

            await _pack14.MarkManagingAsync(position.Symbol, decision.ReasonCode, new
            {
                position.Side,
                analysis.Direction,
                analysis.TrendScore,
                decision.PeakNetPnl,
                decision.CurrentNetPnl,
                decision.GivebackPercent,
                decision.ProfitFloor,
                decision.Action
            }, ct);

            if (decision.Action == ProfitAction.CloseMissingProtection)
            {
                _blockNewEntries = true;
                await CloseOwnedPositionAndVerifyAsync(position, decision.ReasonCode, ct);
                continue;
            }
            if (decision.Action is ProfitAction.CloseWrongDirection or ProfitAction.CloseGiveback)
            {
                await CloseOwnedPositionAndVerifyAsync(position, decision.ReasonCode, ct);
                continue;
            }

            position.Health = decision.Action == ProfitAction.Protect
                ? $"BOT — PROFIT FLOOR {decision.ProfitFloor:0.####}"
                : "BOT PHIÊN HIỆN TẠI — ĐANG GIÁM SÁT RIÊNG";
            position.Recommendation = decision.Action == ProfitAction.Protect ? "GIỮ / KHÔNG CHO LÃI QUAY ĐẦU" : "GIỮ THEO TREND";
        }
        SyncPack14UiState();
    }

    private async Task CloseOwnedPositionAndVerifyAsync(PositionRow position, string reason, CancellationToken ct)
    {
        if (!SessionOwnershipRegistry.IsCurrentSessionBotOwned(position.Symbol)) return;
        var exitSide = position.Side == "LONG" ? "SELL" : "BUY";
        await _execution.EmergencyCloseAsync(position.Symbol, exitSide, position.Quantity, ct);

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            var positions = await _binance.GetPositionsAsync(ct);
            if (!positions.Any(x => string.Equals(x.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                await _pack14.MarkClosedAsync(position.Symbol, reason, new { position.Quantity, position.Side }, ct);
                SessionOwnershipRegistry.Release(position.Symbol);
                _activePlans.Remove(position.Symbol);
                _peakNetPnl.Remove(position.Symbol);
                Log("CLOSE", $"{position.Symbol}: {reason}; đã xác minh position=0.");
                return;
            }
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException($"{position.Symbol}: đã gửi reduce-only nhưng chưa xác minh position=0.");
    }

    private void SyncPack14UiState()
    {
        _state.Lifecycle.Clear();
        foreach (var row in _pack14.BuildLifecycleRows()) _state.Lifecycle.Add(row);
        _state.VerifyMatrix.Clear();
        foreach (var row in _pack14.BuildVerifyRows()) _state.VerifyMatrix.Add(row);
        RefreshPack14Ui();
    }

    private async Task ExportAnalysisBundleAsync()
    {
        try
        {
            var output = Path.Combine(AppContext.BaseDirectory, "analysis");
            var zip = await _bundleExporter.ExportPack14Async(output, _pack14, _pack14Config,
                _activePlans.Values.ToArray(), _state.Positions.ToArray(), _state.Orders.ToArray(), _cts.Token);
            MessageBox.Show($"Đã tạo:\n{zip}", "ANALYSIS BUNDLE", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { AddAlert("ANALYSIS BUNDLE", ex.Message); }
    }

    private static bool ValidProtectionGeometry(string direction, decimal price, decimal stop, decimal tp)
        => direction == "LONG" ? stop < price && tp > price : direction == "SHORT" && tp < price && stop > price;

    private void ExecutionEventReceived(string eventType, string payload) => Log("BINANCE USER STREAM", eventType);
}
