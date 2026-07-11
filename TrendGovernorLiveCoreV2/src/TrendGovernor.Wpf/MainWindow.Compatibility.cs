using System.Windows.Controls;
using System.Windows.Media;

namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    // Internal read-model fields retained for logic compatibility.
    private readonly TextBlock _selectedSymbol = T("--", 22, Brushes.White, true);
    private readonly TextBlock _orderCount = T("0", 22, Brushes.White, true);

    // Performance canvas alias used by the shared runtime logic.
    private Canvas _equityCanvas => _equityCanvasFull;
}
