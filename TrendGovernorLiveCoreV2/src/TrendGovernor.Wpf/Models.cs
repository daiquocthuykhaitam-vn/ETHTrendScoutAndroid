using System.Collections.ObjectModel;

namespace TrendGovernor.Wpf;

public sealed class MarketRow
{
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Change24h { get; set; }
    public decimal QuoteVolume { get; set; }
    public string Direction { get; set; } = "WAIT";
    public int Score { get; set; }
    public string Status { get; set; } = "CHỜ";
    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }
}

public sealed class PositionRow
{
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public string Protection { get; set; } = "CHƯA XÁC MINH";
}

public sealed class OrderRow
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = "";
    public string Type { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Price { get; set; }
    public decimal StopPrice { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = "";
    public bool ReduceOnly { get; set; }
}

public sealed class AccountState
{
    public decimal TotalWalletBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal TotalUnrealizedProfit { get; set; }
}

public sealed class TradingConfig
{
    public decimal MarginPerTrade { get; set; } = 2m;
    public int Leverage { get; set; } = 3;
    public decimal StopLossPercent { get; set; } = 1.2m;
    public decimal TakeProfitPercent { get; set; } = 2.8m;
    public int MaxPositions { get; set; } = 1;
    public bool AutoLiveEnabled { get; set; }
}

public sealed class DashboardState
{
    public ObservableCollection<MarketRow> Markets { get; } = [];
    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<OrderRow> Orders { get; } = [];
}
