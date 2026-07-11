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
        await RefreshAllAsync();
        _ = RunLoopAsync(_cts.Token);
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
            catch (Exception ex) { Log("LOOP", ex.Message); }
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
        }
    }

    private async Task ScanAsync()
    {
        if (_state.Markets.Count == 0) return;
        SetStatus("RADAR ĐANG PHÂN TÍCH", Brushes.Gold);
        var list = _state.Markets.Take(12).ToList();
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
            .ToList();
        _state.Markets.Clear();
        foreach (var row in ordered) _state.Markets.Add(row);
        _marketGrid.Items.Refresh();
        _radarGrid.Items.Refresh();
        var eligible = ordered.Count(x => x.Status == "ĐỦ ĐIỀU KIỆN");
        _candidateCount.Text = eligible.ToString(CultureInfo.InvariantCulture);
        SetStatus("RADAR HOÀN TẤT", Brushes.LimeGreen);
        Log("RADAR", $"{eligible} ứng viên đủ điều kiện.");
        if (_selected == null && ordered.Count > 0) SelectCandidate(ordered[0]);
    }

    private void MarketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_marketGrid.SelectedItem is MarketRow row) SelectCandidate(row);
    }

    private void SelectCandidate(MarketRow row)
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
            $"RR: {row.RiskReward:0.00}";
        RenderPlanChart(row);
    }

    private void RenderPlanChart(MarketRow row)
    {
        _chartCanvas.Children.Clear();
        var width = Math.Max(_chartCanvas.ActualWidth, 520);
        var height = Math.Max(_chartCanvas.ActualHeight, 280);
        var levels = new[] { row.Price, row.EntryLow, row.EntryHigh, row.StopLoss, row.TakeProfit }.Where(x => x > 0).ToList();
        if (levels.Count < 2)
        {
            AddChartText("Chưa có kế hoạch giá hợp lệ", 18, 18, Brushes.Gold);
            return;
        }

        var min = levels.Min();
        var max = levels.Max();
        var span = Math.Max(max - min, Math.Max(row.Price * 0.002m, 0.00000001m));
        min -= span * 0.15m;
        max += span * 0.15m;

        for (var i = 0; i <= 5; i++)
        {
            var y = 24 + (height - 48) * i / 5d;
            var grid = new Line { X1 = 14, X2 = width - 14, Y1 = y, Y2 = y, Stroke = B("#17344D"), StrokeThickness = 1 };
            _chartCanvas.Children.Add(grid);
        }

        DrawLevel(row.TakeProfit, "TP", B("#45D483"), width, height, min, max);
        DrawZone(row.EntryLow, row.EntryHigh, "VÙNG VÀO", B("#E9B949"), width, height, min, max);
        DrawLevel(row.Price, "GIÁ", Brushes.White, width, height, min, max);
        DrawLevel(row.StopLoss, "SL", B("#F05D6F"), width, height, min, max);
    }

    private void DrawZone(decimal low, decimal high, string label, Brush color, double width, double height, decimal min, decimal max)
    {
        if (low <= 0 || high <= 0) return;
        var y1 = PriceY(high, height, min, max);
        var y2 = PriceY(low, height, min, max);
        var rect = new Rectangle { Width = width - 28, Height = Math.Max(6, y2 - y1), Fill = new SolidColorBrush(Color.FromArgb(38, ((SolidColorBrush)color).Color.R, ((SolidColorBrush)color).Color.G, ((SolidColorBrush)color).Color.B)), Stroke = color, StrokeThickness = 1 };
        Canvas.SetLeft(rect, 14); Canvas.SetTop(rect, y1); _chartCanvas.Children.Add(rect);
        AddChartText($"{label}  {low:0.########} — {high:0.########}", 20, y1 + 2, color);
    }

    private void DrawLevel(decimal value, string label, Brush color, double width, double height, decimal min, decimal max)
    {
        if (value <= 0) return;
        var y = PriceY(value, height, min, max);
        var line = new Line { X1 = 14, X2 = width - 14, Y1 = y, Y2 = y, Stroke = color, StrokeThickness = label == "GIÁ" ? 2 : 1.5, StrokeDashArray = label == "GIÁ" ? null : new DoubleCollection { 5, 4 } };
        _chartCanvas.Children.Add(line);
        AddChartText($"{label}  {value:0.########}", width - 170, y - 18, color);
    }

    private static double PriceY(decimal value, double height, decimal min, decimal max)
        => 24 + (double)((max - value) / Math.Max(max - min, 0.00000001m)) * (height - 48);

    private void AddChartText(string text, double left, double top, Brush color)
    {
        var block = T(text, 12, color, true);
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        _chartCanvas.Children.Add(block);
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
        }
    }

    private async Task RefreshPrivateAsync(CancellationToken ct)
    {
        var account = await _binance.GetAccountAsync(ct);
        var positions = await _binance.GetPositionsAsync(ct);
        var orders = await _binance.GetOpenOrdersAsync(ct);
        _account.Text = $"Ví: {account.TotalWalletBalance:0.00} USDT\nKhả dụng: {account.AvailableBalance:0.00} USDT\nPnL mở: {account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT";
        _state.Positions.Clear(); foreach (var position in positions) _state.Positions.Add(position);
        _state.Orders.Clear(); foreach (var order in orders) _state.Orders.Add(order);
        foreach (var position in _state.Positions)
            position.Protection = _state.Orders.Any(o => o.Symbol == position.Symbol && o.ReduceOnly && (o.Type.Contains("STOP") || o.Type.Contains("TAKE_PROFIT"))) ? "ĐÃ BẢO VỆ" : "THIẾU SL/TP";
        _positionCount.Text = _state.Positions.Count.ToString(CultureInfo.InvariantCulture);
        _orderCount.Text = _state.Orders.Count.ToString(CultureInfo.InvariantCulture);
        _positionGrid.Items.Refresh(); _positionsTabGrid.Items.Refresh();
        _orderGrid.Items.Refresh(); _ordersTabGrid.Items.Refresh();
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
        if (_selected == null || _selected.Status != "ĐỦ ĐIỀU KIỆN" || (_selected.Direction != "LONG" && _selected.Direction != "SHORT")) { MessageBox.Show("Ứng viên chưa đủ điều kiện."); return; }
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
        if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0) throw new InvalidOperationException("Margin không hợp lệ.");
        if (!int.TryParse(_leverage.Text, out var leverage) || leverage is < 1 or > 20) throw new InvalidOperationException("Đòn bẩy không hợp lệ.");
        if (candidate.Price <= 0 || candidate.StopLoss <= 0 || candidate.TakeProfit <= 0) throw new InvalidOperationException("Kế hoạch SL/TP chưa hợp lệ.");

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
            await RefreshPrivateAsync(ct);
        }
        catch (Exception ex)
        {
            Log("EXECUTION ERROR", ex.Message);
            MessageBox.Show(ex.Message, "LỖI LIVE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetStatus(string text, Brush color)
    {
        _status.Text = text; _status.Foreground = color;
        _statusMetric.Text = text; _statusMetric.Foreground = color;
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
