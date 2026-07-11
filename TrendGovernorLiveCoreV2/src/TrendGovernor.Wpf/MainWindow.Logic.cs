using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _marketGrid.ItemsSource = _state.Markets;
        _positionGrid.ItemsSource = _state.Positions;
        _orderGrid.ItemsSource = _state.Orders;
        _marketGrid.SelectionChanged += MarketSelected;
        _status.Text = "BINANCE PUBLIC: ĐANG KẾT NỐI";
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
            _status.Text = "ĐANG ĐỒNG BỘ BINANCE";
            var markets = await _binance.LoadTopMarketsAsync(_cts.Token);
            _state.Markets.Clear(); foreach (var m in markets) _state.Markets.Add(m);
            Log("MARKET", $"Đã nhận {markets.Count} cặp USDT-M.");
            await ScanAsync();
            if (_binance.HasCredentials) await RefreshPrivateAsync(_cts.Token);
            _status.Text = "HỆ THỐNG ĐANG CHẠY";
            _status.Foreground = Brushes.LimeGreen;
        }
        catch (Exception ex)
        {
            _status.Text = "LỖI DỮ LIỆU"; _status.Foreground = Brushes.OrangeRed; Log("ERROR", ex.Message);
        }
    }

    private async Task ScanAsync()
    {
        if (_state.Markets.Count == 0) return;
        _status.Text = "RADAR ĐANG PHÂN TÍCH";
        var list = _state.Markets.Take(12).ToList();
        using var sem = new SemaphoreSlim(3);
        var tasks = list.Select(async row =>
        {
            await sem.WaitAsync(_cts.Token);
            try { await _binance.AnalyzeAsync(row, _cts.Token); }
            catch (Exception ex) { row.Status = "LỖI: " + ex.Message; }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
        var ordered = _state.Markets.OrderByDescending(x => x.Status == "ĐỦ ĐIỀU KIỆN").ThenByDescending(x => x.Score).ToList();
        _state.Markets.Clear(); foreach (var row in ordered) _state.Markets.Add(row);
        _marketGrid.Items.Refresh();
        _status.Text = "RADAR HOÀN TẤT";
        Log("RADAR", $"{ordered.Count(x => x.Status == "ĐỦ ĐIỀU KIỆN")} ứng viên đủ điều kiện.");
        if (_selected == null && ordered.Count > 0) _marketGrid.SelectedItem = ordered[0];
    }

    private void MarketSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = _marketGrid.SelectedItem as MarketRow;
        if (_selected == null) return;
        _decision.Text = $"{_selected.Direction} — {_selected.Status} — {_selected.Score}%";
        _decision.Foreground = _selected.Direction switch { "LONG" => Brushes.LimeGreen, "SHORT" => Brushes.OrangeRed, _ => Brushes.Gold };
        _plan.Text = $"Cặp: {_selected.Symbol}\nGiá: {_selected.Price}\nVùng vào: {_selected.EntryLow:0.########} — {_selected.EntryHigh:0.########}\nSL: {_selected.StopLoss:0.########}\nTP: {_selected.TakeProfit:0.########}\nRR: {_selected.RiskReward:0.00}";
    }

    private async Task ConnectAccountAsync()
    {
        try
        {
            _binance.SetCredentials(_apiKey.Text, _apiSecret.Password);
            await RefreshPrivateAsync(_cts.Token);
            Log("ACCOUNT", "Kết nối tài khoản Binance thành công.");
        }
        catch (Exception ex) { _account.Text = "KẾT NỐI THẤT BẠI"; Log("ACCOUNT", ex.Message); }
    }

    private async Task RefreshPrivateAsync(CancellationToken ct)
    {
        var account = await _binance.GetAccountAsync(ct);
        var positions = await _binance.GetPositionsAsync(ct);
        var orders = await _binance.GetOpenOrdersAsync(ct);
        _account.Text = $"Ví: {account.TotalWalletBalance:0.00} USDT\nKhả dụng: {account.AvailableBalance:0.00} USDT\nPnL mở: {account.TotalUnrealizedProfit:+0.00;-0.00;0.00} USDT";
        _state.Positions.Clear(); foreach (var p in positions) _state.Positions.Add(p);
        _state.Orders.Clear(); foreach (var o in orders) _state.Orders.Add(o);
        foreach (var p in _state.Positions)
            p.Protection = _state.Orders.Any(o => o.Symbol == p.Symbol && o.ReduceOnly && (o.Type.Contains("STOP") || o.Type.Contains("TAKE_PROFIT"))) ? "ĐÃ BẢO VỆ" : "THIẾU SL/TP";
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

    private async Task ExecuteCandidateAsync(MarketRow c, CancellationToken ct)
    {
        if (_state.Positions.Count >= 1) { Log("VERIFY", "Đã đủ 1 vị thế, không mở thêm."); return; }
        if (!decimal.TryParse(_margin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin) || margin <= 0) throw new InvalidOperationException("Margin không hợp lệ.");
        if (!int.TryParse(_leverage.Text, out var leverage) || leverage is < 1 or > 20) throw new InvalidOperationException("Đòn bẩy không hợp lệ.");
        if (c.Price <= 0 || c.StopLoss <= 0 || c.TakeProfit <= 0) throw new InvalidOperationException("Kế hoạch SL/TP chưa hợp lệ.");

        decimal quantity = Math.Floor((margin * leverage / c.Price) * 1000m) / 1000m;
        if (quantity <= 0) throw new InvalidOperationException("Khối lượng sau chuẩn hóa bằng 0.");
        string entrySide = c.Direction == "LONG" ? "BUY" : "SELL";
        string exitSide = c.Direction == "LONG" ? "SELL" : "BUY";
        var ok = MessageBox.Show($"GỬI LỆNH THẬT {entrySide} {quantity} {c.Symbol}?\nSL {c.StopLoss}\nTP {c.TakeProfit}", "XÁC NHẬN LỆNH LIVE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            Log("VERIFY", $"{c.Symbol}: kế hoạch hợp lệ, bắt đầu gửi lệnh.");
            await _binance.SetLeverageAsync(c.Symbol, leverage, ct);
            long orderId = await _binance.PlaceMarketAsync(c.Symbol, entrySide, quantity, ct);
            Log("EXECUTE", $"Đã gửi market order {orderId}.");
            await Task.Delay(1200, ct);
            var positions = await _binance.GetPositionsAsync(ct);
            var pos = positions.FirstOrDefault(x => x.Symbol == c.Symbol);
            if (pos == null) throw new InvalidOperationException("Không xác nhận được FILL/vị thế sau lệnh.");
            Log("FILL", $"Đã xác nhận vị thế {pos.Symbol} {pos.Side} {pos.Quantity}.");
            await _binance.PlaceProtectionAsync(c.Symbol, exitSide, pos.Quantity, c.StopLoss, c.TakeProfit, ct);
            Log("PROTECT", "Đã gửi SL và TP reduce-only.");
            await RefreshPrivateAsync(ct);
        }
        catch (Exception ex)
        {
            Log("EXECUTION ERROR", ex.Message);
            MessageBox.Show(ex.Message, "LỖI LIVE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Log(string area, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _logs.AppendText($"{DateTime.Now:HH:mm:ss} [{area}] {message}{Environment.NewLine}");
            _logs.ScrollToEnd();
        });
    }
}
