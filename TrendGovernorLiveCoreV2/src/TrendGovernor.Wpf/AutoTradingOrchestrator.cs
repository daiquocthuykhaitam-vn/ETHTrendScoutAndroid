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
    private readonly Dictionary<string, int> _stableCandidateCycles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrozenTradePlan> _activePlans = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _botOwnedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _peakNetPnl = new(StringComparer.OrdinalIgnoreCase);

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
        panel.Children.Add(Btn("QUÉT THỦ CÔNG", "#275A8C", async (_, _) => await RunAutoCycleAsync(manualOnly: true, _cts.Token)));
        panel.Children.Add(T("AUTO LIVE tự chạy: đồng bộ → radar → plan → verify → execute → fill → protect → manage → radar.", 11, B("#7F9AB5")));
        return panel;
    }

    private async void StartAutoLiveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_autoLoopTask is { IsCompleted: false }) return;
            if (string.IsNullOrWhiteSpace(_apiKey.Text) || string.IsNullOrWhiteSpace(_apiSecret.Password))
                throw new InvalidOperationException("Nhập API Key và API Secret trước khi bắt đầu AUTO LIVE.");
            if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0m || margin > 5m)
                throw new InvalidOperationException("Margin mỗi lệnh phải lớn hơn 0 và không vượt 5 USDT ở giai đoạn hiện tại.");
            if (!int.TryParse(_leverage.Text, out var leverage) || leverage is < 1 or > 5)
                throw new InvalidOperationException("Đòn bẩy giai đoạn hiện tại chỉ cho phép 1x–5x.");

            _binance.SetCredentials(_apiKey.Text, _apiSecret.Password);
            _execution.SetCredentials(_apiKey.Text, _apiSecret.Password);
            _fundingIntel.SetCredentials(_apiKey.Text, _apiSecret.Password);

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
            Log("AUTO", $"Bắt đầu AUTO LIVE; margin {margin:0.##} USDT, leverage {leverage}x.");
        }
        catch (Exception ex)
        {
            _autoState.Text = "AUTO LIVE: KHÔNG THỂ KHỞI ĐỘNG";
            _autoState.Foreground = Brushes.OrangeRed;
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
        Log("EMERGENCY", "Đã khóa lệnh mới và dừng vòng AUTO.");

        try
        {
            await RefreshPrivateAsync(_cts.Token);
            foreach (var position in _state.Positions.Where(p => p.Owner == "BOT").ToList())
            {
                var exitSide = position.Side == "LONG" ? "SELL" : "BUY";
                await _execution.EmergencyCloseAsync(position.Symbol, exitSide, position.Quantity, _cts.Token);
                Log("EMERGENCY", $"Đã gửi đóng reduce-only vị thế BOT {position.Symbol}.");
            }
            await RefreshPrivateAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            AddAlert("EMERGENCY", ex.Message);
        }
    }

    private async Task RunAutoLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAutoCycleAsync(manualOnly: false, ct);
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
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
            SetStatus("AUTO: ĐỒNG BỘ BINANCE", Brushes.Gold);
            var markets = await _binance.LoadTopMarketsAsync(ct);
            var previous = _state.Markets.ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
            _state.Markets.Clear();
            foreach (var market in markets)
            {
                if (previous.TryGetValue(market.Symbol, out var old)) market.StableCycles = old.StableCycles;
                _state.Markets.Add(market);
            }
            _marketCount.Text = markets.Count.ToString(CultureInfo.InvariantCulture);

            SetStatus("AUTO: PHÂN TÍCH TREND DÀI / SÓNG LỚN", Brushes.Gold);
            var deep = _state.Markets.Take(24).ToList();
            using var semaphore = new SemaphoreSlim(4);
            var tasks = deep.Select(async row =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await _largeWave.AnalyzeAsync(row, ct);
                }
                catch (Exception ex)
                {
                    row.Direction = "WAIT";
                    row.Status = "LỖI PHÂN TÍCH";
                    row.Reason = ex.Message;
                    row.AutoEligible = false;
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);

            decimal plannedNotional = 0m;
            if (decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) &&
                int.TryParse(_leverage.Text, out var leverage))
                plannedNotional = Math.Max(0m, margin * leverage);

            SetStatus("AUTO: PHÂN TÍCH FUNDING", Brushes.Gold);
            await _fundingIntel.EnrichMarketsAsync(deep, plannedNotional, ct);
            foreach (var row in deep)
            {
                var technicalScore = row.Score;
                row.Score = Math.Clamp((int)Math.Round(technicalScore * 0.90m + row.FundingScore * 0.10m), 0, 100);
                var fundingBlocked = row.FundingWarning.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase);
                if (fundingBlocked)
                {
                    row.AutoEligible = false;
                    row.Status = "BLOCK FUNDING CỰC ĐOAN";
                    row.Reason = row.FundingWarning;
                }

                if (row.AutoEligible)
                {
                    _stableCandidateCycles.TryGetValue(row.Symbol, out var cycles);
                    row.StableCycles = cycles + 1;
                    _stableCandidateCycles[row.Symbol] = row.StableCycles;
                }
                else
                {
                    row.StableCycles = 0;
                    _stableCandidateCycles.Remove(row.Symbol);
                }
            }

            var ordered = _state.Markets
                .OrderByDescending(x => x.AutoEligible)
                .ThenByDescending(x => x.StableCycles)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.FundingFavorsDirection)
                .ThenByDescending(x => x.WaveScore)
                .ThenByDescending(x => x.QuoteVolume)
                .ToList();
            _state.Markets.Clear();
            foreach (var row in ordered) _state.Markets.Add(row);
            _marketGrid.Items.Refresh();
            _radarGrid.Items.Refresh();

            var eligible = ordered.Count(x => x.AutoEligible);
            _candidateCount.Text = eligible.ToString(CultureInfo.InvariantCulture);
            var selected = ordered.FirstOrDefault(x => x.AutoEligible && x.StableCycles >= 2) ?? ordered.FirstOrDefault();
            if (selected != null) await SelectCandidateAsync(selected);
            Log("RADAR", $"Phân tích sâu {deep.Count} cặp; {eligible} ứng viên; {ordered.Count(x => x.StableCycles >= 2)} ổn định >=2 chu kỳ.");

            if (_binance.HasCredentials) await RefreshPrivateAsync(ct);
            await ManageOpenPositionsAsync(ct);

            if (manualOnly || _blockNewEntries) return;
            if (_state.Positions.Count >= 1)
            {
                SetStatus("AUTO: ĐANG QUẢN LÝ VỊ THẾ", Brushes.LightSkyBlue);
                return;
            }

            var candidate = ordered.FirstOrDefault(x => x.AutoEligible && x.StableCycles >= 2);
            if (candidate == null)
            {
                SetStatus("AUTO: CHƯA CÓ ỨNG VIÊN ĐỦ CHUẨN", Brushes.Gold);
                return;
            }

            await ExecuteAutoCandidateAsync(candidate, ct);
            SetStatus("AUTO: HOÀN TẤT CHU KỲ", Brushes.LimeGreen);
        }
        finally
        {
            _autoCycleGate.Release();
        }
    }

    private async Task ExecuteAutoCandidateAsync(MarketRow candidate, CancellationToken ct)
    {
        if (!await _autoExecutionGate.WaitAsync(0, ct)) return;
        try
        {
            if (_blockNewEntries || _state.Positions.Count >= 1) return;
            if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0m)
                throw new InvalidOperationException("Margin không hợp lệ.");
            if (!int.TryParse(_leverage.Text, out var leverage))
                throw new InvalidOperationException("Đòn bẩy không hợp lệ.");

            var plan = FrozenTradePlan.FromCandidate(candidate, margin, leverage);
            _activePlans[candidate.Symbol] = plan;
            Log("PLAN", $"{plan.PlanId}: {plan.Direction} {plan.Symbol}, entry {plan.EntryLow:0.########}-{plan.EntryHigh:0.########}, SL {plan.StopLoss:0.########}, TP {plan.TakeProfit:0.########}, RR {plan.RiskReward:0.00}.");

            if (plan.IsExpired || plan.Score < 80 || plan.StableCycles < 2 || plan.RiskReward < 2m)
                throw new InvalidOperationException("Kế hoạch hết hạn hoặc chưa đạt VERIFY.");
            if (candidate.FundingWarning.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(candidate.FundingWarning);
            if (candidate.Price < plan.EntryLow || candidate.Price > plan.EntryHigh)
            {
                Log("VERIFY", $"{candidate.Symbol}: giá {candidate.Price:0.########} chưa nằm trong vùng vào; tiếp tục chờ, không đuổi giá.");
                return;
            }
            if (candidate.Direction == "LONG" && !(plan.StopLoss < candidate.Price && plan.TakeProfit > candidate.Price))
                throw new InvalidOperationException("SL/TP LONG không nằm đúng phía giá hiện tại.");
            if (candidate.Direction == "SHORT" && !(plan.TakeProfit < candidate.Price && plan.StopLoss > candidate.Price))
                throw new InvalidOperationException("SL/TP SHORT không nằm đúng phía giá hiện tại.");

            var rules = await _execution.GetRulesAsync(candidate.Symbol, ct);
            var quantity = _execution.NormalizeQuantity(margin * leverage / candidate.Price, rules);
            if (quantity <= 0m) throw new InvalidOperationException("Khối lượng sau chuẩn hóa bằng 0.");
            if (rules.MinNotional > 0m && quantity * candidate.Price < rules.MinNotional)
                throw new InvalidOperationException($"Notional {quantity * candidate.Price:0.########} dưới minimum {rules.MinNotional:0.########} của Binance.");

            var stop = _execution.NormalizePrice(plan.StopLoss, rules);
            var takeProfit = _execution.NormalizePrice(plan.TakeProfit, rules);
            await _execution.EnsureIsolatedAsync(candidate.Symbol, ct);
            await _execution.SetLeverageAsync(candidate.Symbol, leverage, ct);
            Log("VERIFY", $"{candidate.Symbol}: ISOLATED, leverage {leverage}x, qty {quantity}, tick {rules.TickSize}, step {rules.StepSize}.");

            var fill = await _execution.PlaceMarketAndWaitFillAsync(candidate.Symbol, plan.EntrySide, quantity, ct);
            if (fill.Status != "FILLED" || fill.ExecutedQuantity <= 0m || fill.AveragePrice <= 0m)
                throw new InvalidOperationException("Không có FILL thật đầy đủ; không đặt protection theo phỏng đoán.");
            Log("FILL", $"{candidate.Symbol}: FILLED {fill.ExecutedQuantity} @ {fill.AveragePrice:0.########}, order {fill.OrderId}.");

            if (candidate.Direction == "LONG" && !(stop < fill.AveragePrice && takeProfit > fill.AveragePrice))
                throw new InvalidOperationException("Sau fill, SL/TP LONG không còn hợp lệ.");
            if (candidate.Direction == "SHORT" && !(takeProfit < fill.AveragePrice && stop > fill.AveragePrice))
                throw new InvalidOperationException("Sau fill, SL/TP SHORT không còn hợp lệ.");

            var protection = await _execution.PlaceAndVerifyProtectionAsync(candidate.Symbol, plan.ExitSide, fill.ExecutedQuantity, stop, takeProfit, ct);
            if (!protection.IsProtected)
            {
                _blockNewEntries = true;
                await _execution.EmergencyCloseAsync(candidate.Symbol, plan.ExitSide, fill.ExecutedQuantity, ct);
                AddAlert("EMERGENCY_UNPROTECTED", $"{candidate.Symbol}: không xác minh đủ SL/TP, đã gửi đóng reduce-only và khóa lệnh mới.");
                throw new InvalidOperationException("Protection không đầy đủ; đã kích hoạt emergency close.");
            }

            _botOwnedSymbols.Add(candidate.Symbol);
            Log("PROTECT", $"{candidate.Symbol}: SL algo {protection.StopAlgoId}, TP algo {protection.TakeProfitAlgoId} đã xác minh NEW.");
            var tradeLine = $"{DateTime.Now:HH:mm:ss} {candidate.Symbol} {candidate.Direction} {fill.ExecutedQuantity} @ {fill.AveragePrice:0.########} — PROTECTED{Environment.NewLine}";
            _recentTrades.AppendText(tradeLine);
            _recentTradesFull.AppendText(tradeLine);
            await RefreshPrivateAsync(ct);
        }
        catch (Exception ex)
        {
            AddAlert("AUTO EXECUTION", ex.Message);
            Log("AUTO EXECUTION", ex.ToString());
        }
        finally
        {
            _autoExecutionGate.Release();
        }
    }

    private async Task ManageOpenPositionsAsync(CancellationToken ct)
    {
        foreach (var position in _state.Positions.ToList())
        {
            var netPnl = position.NetPnlAfterFunding;
            if (!_peakNetPnl.TryGetValue(position.Symbol, out var peak) || netPnl > peak)
                _peakNetPnl[position.Symbol] = netPnl;
            peak = _peakNetPnl[position.Symbol];
            position.PeakNetPnl = peak;
            position.PeakUnrealizedPnl = Math.Max(position.PeakUnrealizedPnl, position.UnrealizedPnl);
            position.GivebackPercent = peak > 0m ? Math.Max(0m, (peak - netPnl) / peak * 100m) : 0m;

            if (position.Owner != "BOT")
            {
                position.Recommendation = position.StopLossConfirmed ? "THEO DÕI VỊ THẾ TAY" : "VỊ THẾ TAY THIẾU HARD SL";
                continue;
            }

            var market = _state.Markets.FirstOrDefault(x => string.Equals(x.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase));
            var wrongDirection = market != null && market.TrendScore >= 80 &&
                                 ((position.Side == "LONG" && market.Direction == "SHORT") ||
                                  (position.Side == "SHORT" && market.Direction == "LONG"));
            var protectProfit = peak >= 0.5m && position.GivebackPercent >= 35m;

            if (!position.StopLossConfirmed)
            {
                _blockNewEntries = true;
                var exitSide = position.Side == "LONG" ? "SELL" : "BUY";
                await _execution.EmergencyCloseAsync(position.Symbol, exitSide, position.Quantity, ct);
                position.Recommendation = "ĐÓNG KHẨN CẤP — THIẾU HARD SL";
                AddAlert("EMERGENCY_UNPROTECTED", $"{position.Symbol}: vị thế BOT thiếu hard SL, đã gửi đóng reduce-only.");
                continue;
            }

            if (wrongDirection || protectProfit)
            {
                var exitSide = position.Side == "LONG" ? "SELL" : "BUY";
                await _execution.EmergencyCloseAsync(position.Symbol, exitSide, position.Quantity, ct);
                position.Recommendation = wrongDirection ? "ĐÓNG — SAI XU HƯỚNG" : "ĐÓNG — BẢO VỆ NET PNL";
                Log("MANAGE", $"{position.Symbol}: {position.Recommendation}; peak net {peak:0.00}, current net {netPnl:0.00}, giveback {position.GivebackPercent:0.0}%.");
                _botOwnedSymbols.Remove(position.Symbol);
                _activePlans.Remove(position.Symbol);
                _peakNetPnl.Remove(position.Symbol);
            }
            else
            {
                position.Health = "XU HƯỚNG CÒN HỢP LỆ";
                position.Recommendation = position.TakeProfitConfirmed ? "GIỮ / TIẾP TỤC BẢO VỆ" : "GIỮ CÓ SL / RETRY TP";
            }
        }
    }

    private void ExecutionEventReceived(string eventType, string payload)
    {
        Log("BINANCE USER STREAM", eventType);
    }
}
