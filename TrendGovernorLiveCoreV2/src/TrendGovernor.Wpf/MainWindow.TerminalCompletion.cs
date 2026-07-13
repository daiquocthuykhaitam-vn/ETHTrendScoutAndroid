namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    internal void InitializeTerminalCompletionUi()
    {
        Title = "TRENDGOVERNOR PRO - PACK 15 PROFESSIONAL TERMINAL";
        // Exit control, tab styling and grid styling are now declared once in MainWindow.Ui.cs.
        // Legacy visual-tree patching was removed to prevent duplicated buttons and competing themes.
    }
}