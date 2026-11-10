using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DesktopGroups;

// The widget runs in its own process, so it keeps running after the manager window is closed.
public static class WidgetProcess
{
    public const string Argument = "--widget";
    public const string ManagerArgument = "--manager";
    public const string MutexName = @"Local\DesktopGroups.Widget";
    public const string StopEventName = @"Local\DesktopGroups.Widget.Stop";

    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    public static void Start()
    {
        Process.Start(CreateStartInfo(widget: true));
    }

    // Opens the manager window from the widget's settings card.
    public static void StartManager()
    {
        Process.Start(CreateStartInfo(widget: false));
    }

    public static void RequestStop()
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out var stop)) return;
        stop.Set();
        stop.Dispose();
    }

    // Command line used by "start with Windows", for example: "C:\...\DesktopGroups.exe" --widget
    public static string CommandLine
    {
        get
        {
            var info = CreateStartInfo(widget: true);
            return string.Join(" ", info.ArgumentList.Prepend(info.FileName).Select(arg => $"\"{arg}\""));
        }
    }

    private static ProcessStartInfo CreateStartInfo(bool widget)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("לא ניתן לזהות את קובץ התוכנה.");
        var info = new ProcessStartInfo(processPath) { UseShellExecute = false };

        // During development the app runs as "dotnet <app>.dll", so the DLL must be passed on as well.
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);

        info.ArgumentList.Add(widget ? Argument : ManagerArgument);
        return info;
    }
}
