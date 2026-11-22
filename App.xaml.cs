using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AjazzManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("UNHANDLED: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("TASK: " + args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log("DISPATCHER: " + args.Exception);
            // A UI event failure (e.g. HID timeout) should not terminate the app.
            args.Handled = true;
        };
        base.OnStartup(e);
    }

    private static void Log(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ajazz_error.log"),
                DateTime.Now + " " + text + Environment.NewLine);
        }
        catch { }
    }
}
