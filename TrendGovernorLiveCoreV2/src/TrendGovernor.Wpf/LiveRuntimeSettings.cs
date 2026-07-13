using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly TextBox _maxBotPositions = Input("5");
    private readonly List<TextBlock> _entryBlockerViews = [];

    private TextBlock _lastEntryBlocker
    {
        get
        {
            var view = T("GATE CUỐI: CHƯA QUÉT", 12, Brushes.Gold, true);
            _entryBlockerViews.Add(view);
            return view;
        }
    }

    public void InitializeLiveRuntimeSettingsUi() { }

    private int CurrentSessionBotPositionCount()
        => _state.Positions.Count(position => SessionOwnershipRegistry.IsCurrentSessionBotOwned(position.Symbol));

    private void SetEntryBlocker(string code, string detail, Brush? color = null)
    {
        var text = $"GATE CUỐI: {code} — {detail}";
        foreach (var view in _entryBlockerViews)
        {
            view.Text = text;
            view.Foreground = color ?? Brushes.Gold;
        }
        SetStatus(text, color ?? Brushes.Gold);
    }
}