using System.Windows;

namespace TrendGovernor.Wpf;

public static class AppEntry
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow());
    }
}
