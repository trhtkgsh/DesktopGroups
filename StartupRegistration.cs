using System.IO;
using Microsoft.Win32;

namespace DesktopGroups;

// "Start with Windows" starts the widget process for the current user.
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopGroups";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? throw new IOException("לא ניתן לגשת להגדרות ההפעלה של Windows.");

        if (enabled)
            key.SetValue(ValueName, WidgetProcess.CommandLine);
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }
}
