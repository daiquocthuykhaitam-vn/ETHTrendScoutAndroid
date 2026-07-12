using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly Queue<(DateTime Time, decimal Equity)> _equityHistory = new();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _marketGrid.ItemsSource = _state.Markets;
        _positionGrid.ItemsSource = _state.Positions;
        _orderGrid.ItemsSource = _state.Orders;
        _marketGrid.SelectionChanged += MarketSelected;

        SetStatus("BINANCE PUBLIC: ĐANG KẾT NỐI", Brushes.Gold);
        SeedTruthOnlyPanels();
        await RefreshAllAsync();
        _ = RunTruthLoopAsync(_cts.Token);
    }

    private void SeedTruthOnlyPanels()
    {
        const string noAlert = "Chưa có cảnh báo nghiêm trọng.\nChỉ hiển thị dữ liệu thật từ runtime.";
        const string noTrade = "Chưa có giao dịch đã xác nhận trong phiên.";
        _alerts.Text = noAlert;
        _alertsFull.Text = noAlert;
        _recentTrades.Text = noTrade;
        _recentTradesFull.Text = noTrade;
        _performanceLog.Text = "Chưa có đủ dữ liệu tài khoản để tính hiệu suất.";
        _riskSummary.Text = "Tài khoản: chưa kết nối\nExposure: --\nRủi ro phiên: --\nBảo vệ vị thế: chưa xác minh";
        _riskSummaryFull.Text = _riskSummary.Text;
        RenderEquityHistory("Chưa có dữ liệu tài khoản để vẽ đường cong tài sản");
    }

    private async Task RunTruthLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (_binance.HasCredentials) await RefreshPrivateAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log("TRUTH LOOP", ex.Message);
                AddAlert("ĐỒNG BỘ TÀI KHOẢN", ex.Message);
            }
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            SetStatus("ĐANG ĐỒNG BỘ BINANCE", Brushes.Gold);
            await RunAutoCycleAsync(manualOnly: true, _cts.Token);
            if (_binance.HasCredentials) await RefreshPrivateAsync(_cts.Token);
            SetStatus("HỆ THỐNG ĐANG CHẠY", Brushes.LimeGreen);
        }
        catch (Exception ex)
        {
            SetStatus("LỖI DỮ LIỆU", Brushes.OrangeRed);
            Log("ERROR", ex.Message);
            AddAlert("LỖI DỮ LIỆU", ex.Message);
        }
    }

    private void MarketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_marketGrid.SelectedItem is MarketRow row)
            _ = SelectCandidateAsync(row);
    }

    private async Task SelectCandidateAsync(MarketRow row)
    {
        _selected = row;
        var title = $"{row.Symbol} • {row.Direction} • {row.Status}";
        _chartTitle.Text = title;
        _chartTitleFull.Text = title;

        var decisionText = $"{row.Direction}\n{row.Status}\n{row.Score}%";
        var decisionColor = row.Direction switch
        {
            "LONG" => Brushes.LimeGreen,
            "SHORT" => Brushes.OrangeRed,
            _ => Brushes.Gold
        };
        _decision.Text = decisionText;
        _decision.Foreground = decisionColor;
        _decisionFull.Text = decisionText;
        _decisionFull.Foreground = decisionColor;

        var nextFunding = row.NextFundingTime.HasValue
            ? row.NextFundingTime.Value.LocalDateTime.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
            : "--";
        var planText =
            $"Cặp: {row.Symbol}\n" +
            $"Giá hiện tại: {row.Price:0.########}\n" +
            $"Setup: {row.Setup}\n" +
            $"Trend 1D/4H/1H: {row.Trend1D}/{row.Trend4H}/{row.Trend1H}\n" +
            $"Sóng: {row.WaveState} | Trend {row.TrendScore} | Wave {row.WaveScore} | Timing {row.TimingScore}\n" +
            $"Vùng vào: {row.EntryLow:0.########} — {row.EntryHigh:0.########}\n" +
            $"Stop Loss: {row.StopLoss:0.########}\n" +
            $"Take Profit: {row.TakeProfit:0.########}\n" +
            $"RR: {row.RiskReward:0.00}\n" +
            $"Funding: {row.FundingRatePercent:+0.####;-0.####;0}%/{row.FundingIntervalHours}H • {row.FundingFlow}\n" +
            $"Funding/giờ: {row.FundingPerHourPercent:+0.####;-0.####;0}% • {row.FundingLevel} • {row.FundingBias}\n" +
            $"Kỳ kế tiếp: {nextFunding} ({row.FundingMinutesRemaining} phút)\n" +
            $"Ổn định: {row.StableCycles} chu kỳ • Funding {row.FundingStability}\n" +
            $"Lý do: {row.Reason}" +
            (string.IsNullOrWhiteSpace(row.FundingWarning) ? "" : $"\nCảnh báo funding: {row.FundingWarning}");

        _plan.Text = planText;
        _planFull.Text = planText;
        await RenderRealCandleChartAsync(row);
    }

    private async Task RenderRealCandleChartAsync(MarketRow row)
    {
        try
        {
            var candles = await _candles.GetCandlesAsync(row.Symbol, "1h", 100, _cts.Token);
            RenderCandlesOn(_chartCanvas, candles, row, 320);
            RenderCandlesOn(_chartCanvasFull, candles, row, 600);
        }
        catch (Exception ex)
        {
            RenderChartError(_chartCanvas, "Không tải được nến Binance: " + ex.Message);
            RenderChartError(_chartCanvasFull, "Không tải được nến Binance: " + ex.Message);
            Log("CHART", ex.Message);
        }
    }

    private static void RenderChartError(Canvas canvas, string message)
    {
        canvas.Children.Clear();
        AddCanvasText(canvas, message, 18, 18, Brushes.OrangeRed);
    }

    private static void RenderCandlesOn(Canvas canvas, IReadOnlyList<CandlePoint> candles, MarketRow row, double minimumHeight)
    {
        canvas.Children.Clear();
        var width = Math.Max(canvas.ActualWidth, 680);
        var height = Math.Max(canvas.ActualHeight, minimumHeight);
        if (candles.Count < 10)
        {
            AddCanvasText(canvas, "Chưa đủ dữ liệu nến 1H", 18, 18, Brushes.Gold);
            return;
        }

        var visible = candles.TakeLast(80).ToList();
        var priceValues = visible.SelectMany(c => new[] { c.High, c.Low }).ToList();
        if (row.EntryLow > 0) priceValues.Add(row.EntryLow);
        if (row.EntryHigh > 0) priceValues.Add(row.EntryHigh);
        if (row.StopLoss > 0) priceValues.Add(row.StopLoss);
        if (row.TakeProfit > 0) priceValues.Add(row.TakeProfit);

        var min = priceValues.Min();
        var max = priceValues.Max();
        var span = Math.Max(max - min, Math.Max(row.Price * 0.002m, 0.00000001m));
        min -= span * 0.08m;
        max += span * 0.08m;

        for (var i = 0; i <= 5; i++)
        {
            var y = 20 + (height - 50) * i / 5d;
            canvas.Children.Add(new Line { X1 = 14, X2 = width - 14, Y1 = y, Y2 = y, Stroke = B("#17344D"), StrokeThickness = 1 });
            var price = max - (max - min) * (decimal)i / 5m;
            AddCanvasText(canvas, price.ToString("0.########", CultureInfo.InvariantCulture), width - 100, y - 16, B("#6E8BA8"));
        }

        var candleWidth = Math.Max(3.0, (width - 130) / visible.Count * 0.58);
        var step = (width - 130) / visible.Count;
        for (var i = 0; i < visible.Count; i++)
        {
            var candle = visible[i];
            var x = 18 + i * step;
            var highY = PriceY(candle.High, height, min, max);
            var lowY = PriceY(candle.Low, height, min, max);
            var openY = PriceY(candle.Open, height, min, max);
            var closeY = PriceY(candle.Close, height, min, max);
            var color = candle.Close >= candle.Open ? B("#35D39A") : B("#EF5B6C");
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = highY, Y2 = lowY, Stroke = color, StrokeThickness = 1 });
            var body = new Rectangle { Width = candleWidth, Height = Math.Max(2, Math.Abs(closeY - openY)), Fill = color, Stroke = color };
            Canvas.SetLeft(body, x - candleWidth / 2);
            Canvas.SetTop(body, Math.Min(openY, closeY));
            canvas.Children.Add(body);
        }

        DrawEmaOn(canvas, visible, 20, width, height, min, max, B("#F4B942"));
        DrawEmaOn(canvas, visible, 50, width, height, min, max, B("#4EA1FF"));
        DrawZoneOn(canvas, row.EntryLow, row.EntryHigh, "VÙNG VÀO", B("#E9B949"), width, height, min, max);
        DrawLevelOn(canvas, row.StopLoss, "SL", B("#F05D6F"), width, height, min, max);
        DrawLevelOn(canvas, row.TakeProfit, "TP", B("#45D483"), width, height, min, max);
        DrawLevelOn(canvas, row.Price, "GIÁ", Brushes.White, width, height, min, max);
    }

    private static void DrawEmaOn(Canvas canvas, IReadOnlyList<CandlePoint> candles, int period, double width, double height, decimal min, decimal max, Brush color)
    {
        if (candles.Count < period) return;
        var closes = candles.Select(c => c.Close).ToList();
        var values = new List<decimal>();
        var ema = closes.Take(period).Average();
        var k = 2m / (period + 1m);
        for (var i = 0; i < closes.Count; i++)
        {
            if (i < period - 1) values.Add(decimal.MinValue);
            else if (i == period - 1) values.Add(ema);
            else { ema = closes[i] * k + ema * (1 - k); values.Add(ema); }
        }

        var step = (width - 130) / candles.Count;
        Point? previous = null;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == decimal.MinValue) continue;
            var point = new Point(18 + i * step, PriceY(values[i], height, min, max));
            if (previous.HasValue)
                canvas.Children.Add(new Line { X1 = previous.Value.X, Y1 = previous.Value.Y, X2 = point.X, Y2 = point.Y, Stroke = color, StrokeThickness = 1.4 });
            previous = point;
        }
    }

    private static void DrawZoneOn(Canvas canvas, decimal low, decimal high, string label, Brush color, double width, double height, decimal min, decimal max)
    {
        if (low <= 0 || high <= 0) return;
        var y1 = PriceY(high, height, min, max);
        var y2 = PriceY(low, height, min, max);
        var solid = (SolidColorBrush)color;
        var rectangle = new Rectangle
        {
            Width = width - 130,
            Height = Math.Max(6, y2 - y1),
            Fill = new SolidColorBrush(Color.FromArgb(34, solid.Color.R, solid.Color.G, solid.Color.B)),
            Stroke = color,
            StrokeThickness = 1
        };
        Canvas.SetLeft(rectangle, 14);
        Canvas.SetTop(rectangle, y1);
        canvas.Children.Add(rectangle);
        AddCanvasText(canvas, $"{label} {low:0.########} — {high:0.########}", 20, y1 + 2, color);
    }

    private static void DrawLevelOn(Canvas canvas, decimal value, string label, Brush color, double width, double height, decimal min, decimal max)
    {
        if (value <= 0) return;
        var y = PriceY(value, height, min, max);
        canvas.Children.Add(new Line
        {
            X1 = 14,
            X2 = width - 115,
            Y1 = y,
            Y2 = y,
            Stroke = color,
            StrokeThickness = label == "GIÁ" ? 2 : 1.5,
            StrokeDashArray = label == "GIÁ" ? null : new DoubleCollection { 5, 4 }
        });
        AddCanvasText(canvas, $"{label} {value:0.########}", width - 110, y - 16, color);
    }

    private static double PriceY(decimal value, double height, decimal min, decimal max)
        => 20 + (double)((max - value) / Math.Max(max - min, 0.00000001m)) * (height - 50);

    private static void AddCanvasText(Canvas canvas, string text, double left, double top, Brush color)
    {
        var block = T(text, 11, color, true);
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        canvas.Children.Add(block);
    }

    private void RenderEquityHistory(string emptyMessage)
    {
        _equityCanvasFull.Children.Clear();
        var width = Math.Max(_equityCanvasFull.ActualWidth, 900);
        var height = Math.Max(_equityCanvasFull.ActualHeight, 300);
        if (_equityHistory.Count < 2)
        {
            AddCanvasText(_equityCanvasFull, emptyMessage, 16, 16, B("#6E8BA8"));
            return;
        }

        var points = _equityHistory.ToList();
        var min = points.Min(x => x.Equity);
        var max = points.Max(x => x.Equity);
        var span = Math.Max(max - min, Math.Max(max * 0.001m, 0.01m));
        Point? previous = null;
        for (var i = 0; i < points.Count; i++)
        {
            var x = 20 + i * (width - 60) / Math.Max(1, points.Count - 1);
            var y = 20 + (double)((max - points[i].Equity) / span) * (height - 55);
            var point = new Point(x, y);
            if (previous.HasValue)
                _equityCanvasFull.Children.Add(new Line { X1 = previous.Value.X, Y1 = previous.Value.Y, X2 = x, Y2 = y, Stroke = B("#45D483"), StrokeThickness = 2 });
            previous = point;
        }
        AddCanvasText(_equityCanvasFull, $"Equity hiện tại: {points[^1].Equity:0.00} USDT", 18, height - 30, Brushes.White);
    }

    private async Task RefreshPrivateAsync(CancellationToken ct)
    {
        var accountTask = _binance.GetAccountAsync(ct);
        var positionsTask = _binance.GetPositionsAsync(ct);
        var regularOrdersTask = _binance.GetOpenOrdersAsync(ct);
        await Task.WhenAll(accountTask, positionsTask, regularOrdersTask);

        var account = accountTask.Result;
        var positions = positionsTask.Result;
        var allOrders = regularOrdersTask.Result;

        foreach (var position in positions)
        {
            var algoOrders = await _execution.GetOpenAlgoOrdersAsync(position.Symbol, ct);
            foreach (var algo in algoOrders)
            {
                allOrders.Add(new OrderRow
                {
                    OrderId = algo.AlgoId,
                    ClientOrderId = algo.ClientAlgoId,
                    Symbol = position.Symbol,
                    Type = algo.OrderType,
                    Side = position.Side == "LONG" ? "SELL" : "BUY",
                    StopPrice = algo.TriggerPrice,
                    Quantity = position.Quantity,
                    Status = algo.Status,
                    ReduceOnly = true,
                    IsAlgo = true
                });
            }

            var symbolOrders = allOrders.Where(o => string.Equals(o.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var owned = _botOwnedSymbols.Contains(position.Symbol)
                        || _activePlans.ContainsKey(position.Symbol)
                        || symbolOrders.Any(o => o.ClientOrderId.StartsWith("tg-", StringComparison.OrdinalIgnoreCase));
            if (owned) _botOwnedSymbols.Add(position.Symbol);
            position.Owner = owned ? "BOT" : "MỞ TAY";
            position.StopLossConfirmed = symbolOrders.Any(o => o.ReduceOnly && o.Status == "NEW" && o.Type.Contains("STOP_MARKET", StringComparison.OrdinalIgnoreCase) && !o.Type.Contains("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase));
            position.TakeProfitConfirmed = symbolOrders.Any(o => o.ReduceOnly && o.Status == "NEW" && o.Type.Contains("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase));
            position.Protection = position.StopLossConfirmed && position.TakeProfitConfirmed
                ? "ĐÃ BẢO VỆ"
                : position.StopLossConfirmed
                    ? "CÓ SL / THIẾU TP"
                    : position.TakeProfitConfirmed
                        ? "THIẾU SL / CÓ TP"
                        : "THIẾU SL/TP";
        }

        _state.Positions.Clear();
        foreach (var position in positions) _state.Positions.Add(position);
        _state.Orders.Clear();
        foreach (var order in allOrders.OrderBy(x => x.Symbol).ThenBy(x => x.Type)) _state.Orders.Add(order);

        var accountText =
            $"Ví: {account.TotalWalletBalance:0.00} USDT\n" +
            $"Khả dụng: {account.AvailableBalance:0.00} USDT\n" +
            $"PnL giá: {account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT\n" +
            $"Funding 24H: {_state.Positions.Sum(x => x.RealizedFunding):+0.00;-0.00;0.00} USDT\n" +
            $"Net PnL: {_state.Positions.Sum(x => x.NetPnlAfterFunding):+0.00;-0.00;0.00} USDT";
        _account.Text = accountText;
        _accountFull.Text = accountText;
        _equityMetric.Text = $"{account.TotalWalletBalance:0.00} USDT";
        _dailyPnlMetric.Text = $"{_state.Positions.Sum(x => x.NetPnlAfterFunding):+0.00;-0.00;0.00} USDT";
        _dailyPnlMetric.Foreground = _state.Positions.Sum(x => x.NetPnlAfterFunding) >= 0 ? Brushes.LimeGreen : Brushes.OrangeRed;
        _positionCount.Text = _state.Positions.Count.ToString(CultureInfo.InvariantCulture);

        var exposure = _state.Positions.Sum(p => Math.Abs(p.MarkPrice * p.Quantity));
        var exposurePercent = account.TotalWalletBalance > 0 ? exposure / account.TotalWalletBalance * 100m : 0m;
        _sessionRiskMetric.Text = $"{exposurePercent:0.00}%";
        var riskText =
            $"Equity: {account.TotalWalletBalance:0.00} USDT\n" +
            $"Khả dụng: {account.AvailableBalance:0.00} USDT\n" +
            $"Exposure: {exposure:0.00} USDT ({exposurePercent:0.00}%)\n" +
            $"Vị thế: {_state.Positions.Count}\n" +
            $"Bot / mở tay: {_state.Positions.Count(x => x.Owner == "BOT")} / {_state.Positions.Count(x => x.Owner != "BOT")}\n" +
            $"Đủ SL+TP: {_state.Positions.Count(x => x.StopLossConfirmed && x.TakeProfitConfirmed)}\n" +
            $"Thiếu hard SL: {_state.Positions.Count(x => !x.StopLossConfirmed)}";
        _riskSummary.Text = riskText;
        _riskSummaryFull.Text = riskText;

        foreach (var unprotected in _state.Positions.Where(p => !p.StopLossConfirmed))
            AddAlert("BẢO VỆ", $"{unprotected.Symbol} ({unprotected.Owner}) chưa xác minh hard SL trên Binance.");

        _equityHistory.Enqueue((DateTime.Now, account.TotalWalletBalance));
        while (_equityHistory.Count > 240) _equityHistory.Dequeue();
        RenderEquityHistory("Chưa đủ hai mẫu equity thật để vẽ đường cong");
        _performanceLog.Text =
            $"Mẫu equity thật: {_equityHistory.Count}\n" +
            $"PnL giá đang mở: {account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT\n" +
            $"Funding 24H: {_state.Positions.Sum(x => x.RealizedFunding):+0.00;-0.00;0.00} USDT\n" +
            $"Net PnL: {_state.Positions.Sum(x => x.NetPnlAfterFunding):+0.00;-0.00;0.00} USDT\n" +
            $"Projected funding kỳ tới: {_state.Positions.Sum(x => x.EstimatedNextFunding):+0.00;-0.00;0.00} USDT";

        _positionGrid.Items.Refresh();
        _positionsTabGrid.Items.Refresh();
        _orderGrid.Items.Refresh();
        _ordersTabGrid.Items.Refresh();
    }

    private void SetStatus(string text, Brush color)
    {
        _status.Text = text;
        _status.Foreground = color;
        _statusMetric.Text = text;
        _statusMetric.Foreground = color;
        _statusFull.Text = text;
        _statusFull.Foreground = color;
    }

    private void AddAlert(string area, string message)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss} [{area}] {message}{Environment.NewLine}";
            _alerts.AppendText(line);
            _alerts.ScrollToEnd();
            _alertsFull.AppendText(line);
            _alertsFull.ScrollToEnd();
        });
    }

    private void Log(string area, string message)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss} [{area}] {message}{Environment.NewLine}";
            _logs.AppendText(line);
            _logs.ScrollToEnd();
            _logPreview.AppendText(line);
            _logPreview.ScrollToEnd();
        });
    }
}
