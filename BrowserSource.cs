using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Automation;
using System.Windows.Threading;

namespace DesktopGroups;

// Runtime-only groups: one per open Edge or Chrome window, listing that window's tabs. They are never saved.
public sealed class BrowserSource
{
    private const string BrowserWindowClass = "Chrome_WidgetWin_1";
    private const int MaxTabSearchDepth = 14;

    private static readonly Dictionary<string, (string Name, string Color)> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msedge.exe"] = ("Microsoft Edge", "#0078D4"),
        ["chrome.exe"] = ("Google Chrome", "#34A853")
    };

    private readonly ObservableCollection<ShortcutGroup> groups;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public BrowserSource(ObservableCollection<ShortcutGroup> groups)
    {
        this.groups = groups;
        timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        timer.Start();
    }

    // Returns the full path of the browser executable when the window belongs to Edge or Chrome, otherwise null.
    internal static string? BrowserExe(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindowTextLength(hwnd) == 0) return null;
        if (ActiveWindowsSource.GetClassName(hwnd) != BrowserWindowClass) return null;
        if (ActiveWindowsSource.IsCloaked(hwnd)) return null;

        var path = ActiveWindowsSource.GetExePath(hwnd);
        return Browsers.ContainsKey(Path.GetFileName(path)) ? path : null;
    }

    // Activates the tab with this name inside the browser window. Returns false if the tab is no longer open.
    public static bool SelectTab(IntPtr hwnd, string name)
    {
        try
        {
            var tab = FindTabs(hwnd).FirstOrDefault(t => t.Current.Name == name);
            if (tab is null) return false;

            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
                ((SelectionItemPattern)pattern).Select();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private void Refresh()
    {
        var browsers = EnumerateBrowsers();

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].IsBrowser && !browsers.Any(b => b.Handle == groups[i].BrowserHandle)) groups.RemoveAt(i);
        }

        foreach (var (handle, path) in browsers)
        {
            var (name, color) = Browsers[Path.GetFileName(path)];
            var group = groups.FirstOrDefault(g => g.BrowserHandle == handle);
            if (group is null)
            {
                group = new ShortcutGroup { BrowserHandle = handle, Name = name, Color = color };
                groups.Add(group);
            }

            SyncTabs(group, handle, path, ReadTabs(handle));
        }
    }

    private static List<(IntPtr Handle, string Path)> EnumerateBrowsers()
    {
        var result = new List<(IntPtr Handle, string Path)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var path = BrowserExe(hwnd);
            if (path is not null) result.Add((hwnd, path));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // Rebuilds the items only when the tab list changes; otherwise only the selection marker is updated.
    private static void SyncTabs(ShortcutGroup group, IntPtr handle, string path, IReadOnlyList<(string Name, bool Selected)> tabs)
    {
        if (group.Items.Select(i => i.Name).SequenceEqual(tabs.Select(t => t.Name)))
        {
            for (var i = 0; i < tabs.Count; i++) group.Items[i].IsActiveTab = tabs[i].Selected;
            return;
        }

        group.Items.Clear();
        var favicons = BrowserFavicons.Get(Path.GetFileName(path), tabs.Select(t => t.Name));
        foreach (var tab in tabs)
        {
            var item = new ShortcutItem
            {
                Name = tab.Name,
                TargetPath = path,
                BrowserHandle = handle,
                IsActiveTab = tab.Selected
            };
            item.RefreshIcon();
            if (favicons.TryGetValue(tab.Name, out var favicon)) item.SetRuntimeIcon(favicon);
            group.Items.Add(item);
        }
    }

    private static List<(string Name, bool Selected)> ReadTabs(IntPtr hwnd)
    {
        try
        {
            return FindTabs(hwnd)
                .Select(t => (t.Current.Name, IsSelected(t)))
                .ToList();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    private static bool IsSelected(AutomationElement tab) =>
        tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;

    // Walks the raw view and skips web page content (Document), where sites such as Gmail expose their own tab lists.
    // The default UI Automation search does not see Edge's nested TabItems, so the raw view is walked by hand.
    private static List<AutomationElement> FindTabs(IntPtr hwnd)
    {
        var tabs = new List<AutomationElement>();
        CollectTabItems(AutomationElement.FromHandle(hwnd), 0, tabs);
        return tabs;
    }

    private static void CollectTabItems(AutomationElement parent, int depth, List<AutomationElement> tabs)
    {
        if (depth > MaxTabSearchDepth) return;

        for (var child = TreeWalker.RawViewWalker.GetFirstChild(parent); child is not null; child = TreeWalker.RawViewWalker.GetNextSibling(child))
        {
            var type = child.Current.ControlType;
            if (type == ControlType.TabItem) tabs.Add(child);
            else if (type != ControlType.Document) CollectTabItems(child, depth + 1, tabs);
        }
    }
}
