using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    // Internal read-model fields retained for logic compatibility.
    private readonly System.Windows.Controls.TextBlock _selectedSymbol = T("--", 22, Brushes.White, true);
    private readonly System.Windows.Controls.TextBlock _orderCount = T("0", 22, Brushes.White, true);
}
