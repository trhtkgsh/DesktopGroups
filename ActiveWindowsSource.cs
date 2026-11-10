using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;

namespace DesktopGroups;

// Runtime-only group that lists the open top-level windows in z-order. It is never saved with the settings.
public sealed class ActiveWindowsSource
{
    private const string GroupName = "חלונות פעילים";
    private const string GroupColor = "#546E7A";

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
    };

    private readonly ShortcutGroup group = new() { Name = GroupName, Color = GroupColor };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly uint ownProcessId = (uint)Environment.ProcessId;

    public ActiveWindowsSource()
    {
        Groups = [group];
        timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<ShortcutGroup> Groups { get; }

    public void Start()
    {
        Refresh();
        timer.Start();
    }

    public static void Activate(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    // Asks the window to close itself, so the program can still prompt to save its work.
    public static bool CloseWindow(IntPtr hwnd) =>
        NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);

    // Forcefully ends the process that owns the window, which closes all of its windows without saving.
    public static bool EndProcess(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessTerminate, false, processId);
        if (process == IntPtr.Zero) return false;

        try
        {
            return NativeMethods.TerminateProcess(process, 1);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    // Reconciles the group with the open windows, moving existing items instead of rebuilding them.
    private void Refresh()
    {
        var handles = EnumerateWindows();
        var items = group.Items;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!handles.Any(h => h.Handle == items[i].WindowHandle)) items.RemoveAt(i);
        }

        // Items before `target` already match the windows, so `items.Count >= target` holds for an insert.
        for (var target = 0; target < handles.Count; target++)
        {
            var (handle, title) = handles[target];
            var index = IndexOf(items, handle);
            if (index < 0)
            {
                var item = new ShortcutItem
                {
                    Name = title,
                    TargetPath = GetExePath(handle),
                    WindowHandle = handle
                };
                item.RefreshIcon();
                items.Insert(target, item);
                continue;
            }

            if (index != target) items.Move(index, target);
            if (items[target].Name != title) items[target].Name = title;
        }
    }

    private static int IndexOf(ObservableCollection<ShortcutItem> items, IntPtr handle)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].WindowHandle == handle) return i;
        }
        return -1;
    }

    private List<(IntPtr Handle, string Title)> EnumerateWindows()
    {
        var result = new List<(IntPtr Handle, string Title)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsListed(hwnd)) result.Add((hwnd, GetTitle(hwnd)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private bool IsListed(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;
        if (IsCloaked(hwnd)) return false;
        if (IgnoredClasses.Contains(GetClassName(hwnd))) return false;
        if (BrowserSource.BrowserExe(hwnd) is not null) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == ownProcessId) return false;

        // Tool windows and owned dialogs are hidden from the taskbar, unless they are flagged as app windows.
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        var isAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0;
        if (!isAppWindow)
        {
            if ((exStyle & NativeMethods.WsExToolWindow) != 0) return false;
            if (NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero) return false;
        }

        return true;
    }

    internal static bool IsCloaked(IntPtr hwnd) =>
        NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    internal static string GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    internal static string GetExePath(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return "";

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : "";
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
