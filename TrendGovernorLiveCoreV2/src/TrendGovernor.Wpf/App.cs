using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TrendGovernor.Wpf;

public static class AppEntry
{
    [STAThread]
    public static void Main()
    {
        ConfigureGlobalCrashLogging();
        WriteLog("START", $"Khởi động {DateTime.Now:O}");

        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            var window = new MainWindow();
            window.InitializeFundingUi();
            app.Run(window);
            WriteLog("EXIT", "Ứng dụng đã đóng bình thường.");
        }
        catch (Exception ex)
        {
            ReportFatal("STARTUP_FATAL", ex);
            Environment.ExitCode = 1;
        }
    }

    private static void ConfigureGlobalCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteLog("UNHANDLED", ex.ToString());
            else
                WriteLog("UNHANDLED", args.ExceptionObject?.ToString() ?? "Unknown fatal error");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteLog("TASK", args.Exception.ToString());
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal("UI_FATAL", e.Exception);
        e.Handled = true;
        Application.Current?.Shutdown(1);
    }

    private static void ReportFatal(string area, Exception ex)
    {
        WriteLog(area, ex.ToString());
        try
        {
            MessageBox.Show(
                $"Tool gặp lỗi khởi động. Chi tiết đã lưu tại:\n{CrashLogPath}\n\n{ex.Message}",
                "TRENDGOVERNOR - LỖI KHỞI ĐỘNG",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static string CrashLogPath => Path.Combine(AppContext.BaseDirectory, "logs", "startup-crash.log");

    private static void WriteLog(string area, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(CrashLogPath, $"{DateTime.Now:O} [{area}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
