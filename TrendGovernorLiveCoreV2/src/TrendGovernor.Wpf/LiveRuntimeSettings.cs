using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly TextBox _maxBotPositions = Input("5");
    private readonly TextBlock _lastEntryBlocker = T("GATE CUỐI: CHƯA QUÉT", 12, Brushes.Gold, true);

    public void InitializeLiveRuntimeSettingsUi()
    {
        Loaded += (_, _) =>
        {
            var form = FindParent<StackPanel>(_leverage);
            if (form is null || form.Children.Contains(_maxBotPositions)) return;

            var leverageIndex = form.Children.IndexOf(_leverage);
            var insertIndex = Math.Min(form.Children.Count, leverageIndex + 1);
            form.Children.Insert(insertIndex++, Label("SỐ VỊ THẾ BOT TỐI ĐA"));
            form.Children.Insert(insertIndex++, _maxBotPositions);

            var blockerCard = Card("GATE CHẶN LỆNH GẦN NHẤT", _lastEntryBlocker);
            form.Children.Insert(Math.Min(form.Children.Count, insertIndex), blockerCard);
        };
    }

    private int CurrentSessionBotPositionCount()
        => _state.Positions.Count(position => SessionOwnershipRegistry.IsCurrentSessionBotOwned(position.Symbol));

    private void SetEntryBlocker(string code, string detail, Brush? color = null)
    {
        var text = $"GATE CUỐI: {code} — {detail}";
        _lastEntryBlocker.Text = text;
        _lastEntryBlocker.Foreground = color ?? Brushes.Gold;
        SetStatus(text, color ?? Brushes.Gold);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T match) return match;
        }
        return null;
    }
}
