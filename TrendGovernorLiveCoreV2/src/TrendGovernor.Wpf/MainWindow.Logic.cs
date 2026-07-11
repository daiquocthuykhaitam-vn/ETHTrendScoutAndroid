using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _marketGrid.ItemsSource = _state.Markets;
        _positionGrid.ItemsSource = _state.Positions;
        _orderGrid.ItemsSource = _state.Orders;
        _marketGrid.SelectionChanged += MarketSelected;
        SetStatus("BINANCE PUBLIC: ĐANG KẾT NỐI", Brushes.Gold);
        SeedTruthOnlyPanels();
        await RefreshAllAsync();
        _ = RunLoopAsync(_cts.Token);
    }

    private void SeedTruthOnlyPanels()
    {
        _alerts.Text = "Chưa có cảnh báo nghiêm trọng.\nChỉ hiển thị dữ liệu thật từ runtime.";
        _recentTrades.Text = "Chưa có giao dịch đã xác nhận trong phiên.";
        _riskSummary.Text = "Tài khoản: chưa kết nối\nExposure: --\nRủi ro phiên: --\nBảo vệ vị thế: chưa xác minh";
        RenderEmptyEquity("Chưa có dữ liệu tài khoản để vẽ đường cong tài sản");
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (_binance.HasCredentials) await RefreshPrivateAsync(ct);
                if (_autoLive.IsChecked == true && _liveArmed) await AutoExecuteAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("LOOP", ex.Message);
                AddAlert("LỖI VÒNG LẶP", ex.Message);
            }
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            SetStatus("ĐANG ĐỒNG BỘ BINANCE", Brushes.Gold);
            var markets = await _binance.LoadTopMarketsAsync(_cts.Token);
            _state.Markets.Clear();
            foreach (var market in markets) _state.Markets.Add(market);
            _marketCount.Text = markets.Count.ToString(CultureInfo.InvariantCulture);
            Log("MARKET", $"Đã nhận {markets.Count} cặp USDT-M.");
            await ScanAsync();
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

    private async Task ScanAsync()
    {
        if (_state.Markets.Count == 0) return;
        SetStatus("RADAR ĐANG PHÂN TÍCH", Brushes.Gold);
        var list = _state.Markets.Take(20).ToList();
        using var semaphore = new SemaphoreSlim(3);
        var tasks = list.Select(async row =>
        {
            await semaphore.WaitAsync(_cts.Token);
            try { await _binance.AnalyzeAsync(row, _cts.Token); }
            catch (Exception ex) { row.Status = "LỖI: " + ex.Message; }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);

        var ordered = _state.Markets
            .OrderByDescending(x => x.Status == "ĐỦ ĐIỀU KIỆN")
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.QuoteVolume)
            .ToList();

        _state.Markets.Clear();
        foreach (var row in ordered) _state.Markets.Add(row);
        _marketGrid.Items.Refresh();
        _radarGrid.Items.Refresh();

        var eligible = ordered.Count(x => x.Status == "ĐỦ ĐIỀU KIỆN");
        _candidateCount.Text = eligible.ToString(CultureInfo.InvariantCulture);
        SetStatus("RADAR HOÀN TẤT", Brushes.LimeGreen);
        Log("RADAR", $"{eligible} ứng viên đủ điều kiện trên {ordered.Count} cặp.");

        if (eligible == 0)
            AddAlert("RADAR", "Chưa có ứng viên đủ điều kiện; hệ thống tiếp tục quan sát, không ép lệnh.");

        if (_selected == null && ordered.Count > 0)
            await SelectCandidateAsync(ordered[0]);
    }

    private void MarketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_marketGrid.SelectedItem is MarketRow row)
            _ = SelectCandidateAsync(row);
    }

    private async Task SelectCandidateAsync(MarketRow row)
    {
        _selected = row;
        _selectedSymbol.Text = row.Symbol;
        _chartTitle.Text = $"{row.Symbol} • {row.Direction} • {row.Status}";
        _decision.Text = $"{row.Direction}\n{row.Status}\n{row.Score}%";
        _decision.Foreground = row.Direction switch
        {
            "LONG" => Brushes.LimeGreen,
            "SHORT" => Brushes.OrangeRed,
            _ => Brushes.Gold
        };

        _plan.Text =
            $"Cặp: {row.Symbol}\n" +
            $"Giá hiện tại: {row.Price:0.########}\n" +
            $"Vùng vào: {row.EntryLow:0.########} — {row.EntryHigh:0.########}\n" +
            $"Stop Loss: {row.StopLoss:0.########}\n" +
            $"Take Profit: {row.TakeProfit:0.########}\n" +
            $"RR: {row.RiskReward:0.00}\n" +
            $"Trạng thái: {row.Status}";

        await RenderRealCandleChartAsync(row);
    }

    private async Task RenderRealCandleChartAsync(MarketRow row)
    {
        try
        {
            var candles = await _candles.GetCandlesAsync(row.Symbol, "1h", 80, _cts.Token);
            RenderCandles(candles, row);
        }
        catch (Exception ex)
        {
            _chartCanvas.Children.Clear();
            AddChartText("Không tải được nến Binance: " + ex.Message, 18, 18, Brushes.OrangeRed);
            Log("CHART", ex.Message);
        }
    }

    private void RenderCandles(IReadOnlyList<CandlePoint> candles, MarketRow row)
    {
        _chartCanvas.Children.Clear();
        var width = Math.Max(_chartCanvas.ActualWidth, 680);
        var height = Math.Max(_chartCanvas.ActualHeight, 320);
        if (candles.Count < 10)
        {
            AddChartText("Chưa đủ dữ liệu nến 1H", 18, 18, Brushes.Gold);
            return;
        }

        var visible = candles.TakeLast(60).ToList();
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
            _chartCanvas.Children.Add(new Line { X1 = 14, X2 = width - 14, Y1 = y, Y2 = y, Stroke = B("#17344D"), StrokeThickness = 1 });
            var price = max - (max - min) * (decimal)i / 5m;
            AddChartText(price.ToString("0.########", CultureInfo.InvariantCulture), width - 100, y - 16, B("#6E8BA8"));
        }

        var candleWidth = Math.Max(3.5, (width - 130) / visible.Count * 0.58);
        var step = (width - 130) / visible.Count;
        for (var i = 0; i < visible.Count; i++)
        {
            var candle = visible[i];
            var x = 18 + i * step;
            var highY = PriceY(candle.High, height, min, max);
            var lowY = PriceY(candle.Low, height, min, max);
            var openY = PriceY(candle.Open, height, min, max);
            var closeY = PriceY(candle.Close, height, min, max);
            var up = candle.Close >= candle.Open;
            var color = up ? B("#35D39A") : B("#EF5B6C");
            _chartCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = highY, Y2 = lowY, Stroke = color, StrokeThickness = 1 });
            var body = new Rectangle { Width = candleWidth, Height = Math.Max(2, Math.Abs(closeY - openY)), Fill = color, Stroke = color };
            Canvas.SetLeft(body, x - candleWidth / 2);
            Canvas.SetTop(body, Math.Min(openY, closeY));
            _chartCanvas.Children.Add(body);
        }

        DrawEma(visible, 20, width, height, min, max, B("#F4B942"));
        DrawEma(visible, 50, width, height, min, max, B("#4EA1FF"));
        DrawZone(row.EntryLow, row.EntryHigh, "VÙNG VÀO", B("#E9B949"), width, height, min, max);
        DrawLevel(row.StopLoss, "SL", B("#F05D6F"), width, height, min, max);
        DrawLevel(row.TakeProfit, "TP", B("#45D483"), width, height, min, max);
        DrawLevel(row.Price, "GIÁ", Brushes.White, width, height, min, max);
    }

    private void DrawEma(IReadOnlyList<CandlePoint> candles, int period, double width, double height, decimal min, decimal max, Brush color)
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
                _chartCanvas.Children.Add(new Line { X1 = previous.Value.X, Y1 = previous.Value.Y, X2 = point.X, Y2 = point.Y, Stroke = color, StrokeThickness = 1.4 });
            previous = point;
        }
    }

    private void DrawZone(decimal low, decimal high, string label, Brush color, double width, double height, decimal min, decimal max)
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
        _chartCanvas.Children.Add(rectangle);
        AddChartText($"{label} {low:0.########} — {high:0.########}", 20, y1 + 2, color);
    }

    private void DrawLevel(decimal value, string label, Brush color, double width, double height, decimal min, decimal max)
    {
        if (value <= 0) return;
        var y = PriceY(value, height, min, max);
        _chartCanvas.Children.Add(new Line
        {
            X1 = 14,
            X2 = width - 115,
            Y1 = y,
            Y2 = y,
            Stroke = color,
            StrokeThickness = label == "GIÁ" ? 2 : 1.5,
            StrokeDashArray = label == "GIÁ" ? null : new DoubleCollection { 5, 4 }
        });
        AddChartText($"{label} {value:0.########}", width - 110, y - 16, color);
    }

    private static double PriceY(decimal value, double height, decimal min, decimal max)
        => 20 + (double)((max - value) / Math.Max(max - min, 0.00000001m)) * (height - 50);

    private void AddChartText(string text, double left, double top, Brush color)
    {
        var block = T(text, 11, color, true);
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        _chartCanvas.Children.Add(block);
    }

    private void RenderEmptyEquity(string message)
    {
        _equityCanvas.Children.Clear();
        var block = T(message, 13, B("#6E8BA8"), true);
        Canvas.SetLeft(block, 16);
        Canvas.SetTop(block, 16);
        _equityCanvas.Children.Add(block);
    }

    private async Task ConnectAccountAsync()
    {
        try
        {
            _binance.SetCredentials(_apiKey.Text, _apiSecret.Password);
            await RefreshPrivateAsync(_cts.Token);
            Log("ACCOUNT", "Kết nối tài khoản Binance thành công.");
        }
        catch (Exception ex)
        {
            _account.Text = "KẾT NỐI THẤT BẠI";
            Log("ACCOUNT", ex.Message);
            AddAlert("TÀI KHOẢN", ex.Message);
        }
    }

    private async Task RefreshPrivateAsync(CancellationToken ct)
    {
        var account = await _binance.GetAccountAsync(ct);
        var positions = await _binance.GetPositionsAsync(ct);
        var orders = await _binance.GetOpenOrdersAsync(ct);

        _account.Text = $"Ví: {account.TotalWalletBalance:0.00} USDT\nKhả dụng: {account.AvailableBalance:0.00} USDT\nPnL mở: {account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT";
        _equityMetric.Text = $"{account.TotalWalletBalance:0.00} USDT";
        _dailyPnlMetric.Text = $"{account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT";
        _dailyPnlMetric.Foreground = account.TotalUnrealizedProfit >= 0 ? Brushes.LimeGreen : Brushes.OrangeRed;

        _state.Positions.Clear(); foreach (var position in positions) _state.Positions.Add(position);
        _state.Orders.Clear(); foreach (var order in orders) _state.Orders.Add(order);
        foreach (var position in _state.Positions)
            position.Protection = _state.Orders.Any(o => o.Symbol == position.Symbol && o.ReduceOnly && (o.Type.Contains("STOP") || o.Type.Contains("TAKE_PROFIT"))) ? "ĐÃ BẢO VỆ" : "THIẾU SL/TP";

        _positionCount.Text = _state.Positions.Count.ToString(CultureInfo.InvariantCulture);
        _orderCount.Text = _state.Orders.Count.ToString(CultureInfo.InvariantCulture);
        var exposure = _state.Positions.Sum(p => p.MarkPrice * p.Quantity);
        var exposurePercent = account.TotalWalletBalance > 0 ? exposure / account.TotalWalletBalance * 100m : 0m;
        _sessionRiskMetric.Text = $"{exposurePercent:0.00}%";
        _riskSummary.Text =
            $"Equity: {account.TotalWalletBalance:0.00} USDT\n" +
            $"Khả dụng: {account.AvailableBalance:0.00} USDT\n" +
            $"Exposure: {exposure:0.00} USDT ({exposurePercent:0.00}%)\n" +
            $"Vị thế: {_state.Positions.Count}\n" +
            $"Thiếu SL/TP: {_state.Positions.Count(p => p.Protection == "THIẾU SL/TP")}";

        if (_state.Positions.Any(p => p.Protection == "THIẾU SL/TP"))
            AddAlert("BẢO VỆ", "Có vị thế chưa xác minh đủ SL/TP.");

        _positionGrid.Items.Refresh();
        _positionsTabGrid.Items.Refresh();
        _orderGrid.Items.Refresh();
        _ordersTabGrid.Items.Refresh();
    }

    private void ArmLive(object sender, RoutedEventArgs e)
    {
        if (!_binance.HasCredentials) { MessageBox.Show("Kết nối API tài khoản trước."); return; }
        var confirm = MessageBox.Show("BẬT QUYỀN GỬI LỆNH THẬT TRÊN BINANCE FUTURES?", "XÁC NHẬN LIVE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        _liveArmed = confirm == MessageBoxResult.Yes;
        _armButton.Content = _liveArmed ? "LIVE ĐÃ MỞ" : "BẬT QUYỀN LIVE";
        _armButton.Background = _liveArmed ? Brushes.DarkGreen : Brushes.DarkRed;
        Log("LIVE", _liveArmed ? "Đã mở quyền live." : "Quyền live đang khóa.");
    }

    private async Task ExecuteSelectedAsync()
    {
        if (!_liveArmed) { MessageBox.Show("AUTO/LIVE chưa được mở."); return; }
        if (_selected == null || _selected.Status != "ĐỦ ĐIỀU KIỆN" || (_selected.Direction != "LONG" && _selected.Direction != "SHORT"))
        {
            MessageBox.Show("Ứng viên chưa đủ điều kiện.");
            return;
        }
        await ExecuteCandidateAsync(_selected, _cts.Token);
    }

    private async Task AutoExecuteAsync(CancellationToken ct)
    {
        if (_state.Positions.Count >= 1) return;
        var candidate = _state.Markets.FirstOrDefault(x => x.Status == "ĐỦ ĐIỀU KIỆN" && x.Score >= 80 && x.RiskReward >= 1.8m);
        if (candidate != null) await ExecuteCandidateAsync(candidate, ct);
    }

    private async Task ExecuteCandidateAsync(MarketRow candidate, CancellationToken ct)
    {
        if (_state.Positions.Count >= 1) { Log("VERIFY", "Đã đủ 1 vị thế, không mở thêm."); return; }
        if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0)
            throw new InvalidOperationException("Margin không hợp lệ.");
        if (!int.TryParse(_leverage.Text, out var leverage) || leverage is < 1 or > 20)
            throw new InvalidOperationException("Đòn bẩy không hợp lệ.");
        if (candidate.Price <= 0 || candidate.StopLoss <= 0 || candidate.TakeProfit <= 0)
            throw new InvalidOperationException("Kế hoạch SL/TP chưa hợp lệ.");

        var quantity = Math.Floor((margin * leverage / candidate.Price) * 1000m) / 1000m;
        if (quantity <= 0) throw new InvalidOperationException("Khối lượng sau chuẩn hóa bằng 0.");
        var entrySide = candidate.Direction == "LONG" ? "BUY" : "SELL";
        var exitSide = candidate.Direction == "LONG" ? "SELL" : "BUY";
        var ok = MessageBox.Show($"GỬI LỆNH THẬT {entrySide} {quantity} {candidate.Symbol}?\nSL {candidate.StopLoss}\nTP {candidate.TakeProfit}", "XÁC NHẬN LỆNH LIVE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            Log("VERIFY", $"{candidate.Symbol}: kế hoạch hợp lệ, bắt đầu gửi lệnh.");
            await _binance.SetLeverageAsync(candidate.Symbol, leverage, ct);
            var orderId = await _binance.PlaceMarketAsync(candidate.Symbol, entrySide, quantity, ct);
            Log("EXECUTE", $"Đã gửi market order {orderId}.");
            await Task.Delay(1200, ct);
            var positions = await _binance.GetPositionsAsync(ct);
            var position = positions.FirstOrDefault(x => x.Symbol == candidate.Symbol);
            if (position == null) throw new InvalidOperationException("Không xác nhận được FILL/vị thế sau lệnh.");
            Log("FILL", $"Đã xác nhận vị thế {position.Symbol} {position.Side} {position.Quantity}.");
            await _binance.PlaceProtectionAsync(candidate.Symbol, exitSide, position.Quantity, candidate.StopLoss, candidate.TakeProfit, ct);
            Log("PROTECT", "Đã gửi SL và TP reduce-only.");
            _recentTrades.AppendText($"{DateTime.Now:HH:mm:ss} {candidate.Symbol} {candidate.Direction} QTY {position.Quantity} — đã xác nhận fill và bảo vệ{Environment.NewLine}");
            await RefreshPrivateAsync(ct);
        }
        catch (Exception ex)
        {
            Log("EXECUTION ERROR", ex.Message);
            AddAlert("EXECUTION", ex.Message);
            MessageBox.Show(ex.Message, "LỖI LIVE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetStatus(string text, Brush color)
    {
        _status.Text = text;
        _status.Foreground = color;
        _statusMetric.Text = text;
        _statusMetric.Foreground = color;
    }

    private void AddAlert(string area, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _alerts.AppendText($"{DateTime.Now:HH:mm:ss} [{area}] {message}{Environment.NewLine}");
            _alerts.ScrollToEnd();
        });
    }

    private void Log(string area, string message)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss} [{area}] {message}{Environment.NewLine}";
            _logs.AppendText(line); _logs.ScrollToEnd();
            _logPreview.AppendText(line); _logPreview.ScrollToEnd();
        });
    }
}
