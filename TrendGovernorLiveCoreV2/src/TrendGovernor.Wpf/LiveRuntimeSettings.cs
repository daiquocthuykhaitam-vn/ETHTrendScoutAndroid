using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly TextBox _maxBotPositions = Input("5");
    private readonly TextBlock _lastEntryBlocker = T("GATE CUỐI: CHƯA QUÉT", 12, Brushes.Gold, true);

    private int CurrentSessionBotPositionCount()
        => _state.Positions.Count(position => SessionOwnershipRegistry.IsCurrentSessionBotOwned(position.Symbol));

    private void SetEntryBlocker(string code, string detail, Brush? color = null)
    {
        var text = $"GATE CUỐI: {code} — {detail}";
        _lastEntryBlocker.Text = text;
        _lastEntryBlocker.Foreground = color ?? Brushes.Gold;
        SetStatus(text, color ?? Brushes.Gold);
    }
}
