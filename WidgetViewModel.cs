using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopGroups;

public partial class WidgetViewModel : ObservableObject
{
    // Sizes at 100 % display scale.
    private const double BaseIconSize = 32;
    // The power icons are drawn at 32 px and shown at this size at 100 %.
    private const double BasePowerIconSize = 20;
    private const double BaseTextSize = 14;

    private readonly IDialogService dialogs;
    private readonly ValueAnimator iconSizeAnimator;
    private readonly ValueAnimator textSizeAnimator;
    private readonly ValueAnimator opacityAnimator;
    private readonly ActiveWindowsSource activeWindows = new();
    private readonly BrowserSource browsers;
    private readonly DispatcherTimer recycleBinTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool? recycleBinEmpty;

    public WidgetViewModel(BoardViewModel board, IDialogService dialogs)
    {
        Board = board;
        this.dialogs = dialogs;
        RegularGroupsView = new ListCollectionView(Board.Groups);
        iconSizeAnimator = new ValueAnimator(v => IconSize = v);
        textSizeAnimator = new ValueAnimator(v => TextSize = v);
        opacityAnimator = new ValueAnimator(v => GroupOpacity = v);
        browsers = new BrowserSource(activeWindows.Groups);

        Board.PropertyChanged += OnBoardPropertyChanged;
        UpdateSizes(snap: true);
        UpdateOpacity(snap: true);
        IsVertical = Board.GroupLayout == GroupLayout.Vertical;
        activeWindows.Start();
        browsers.Start();

        recycleBinTimer.Tick += (_, _) => RefreshRecycleBinIcons();
        recycleBinTimer.Start();
        RefreshRecycleBinIcons();
    }

    // The bin icon shows full or empty depending on its contents, so it is refreshed when that state changes.
    private void RefreshRecycleBinIcons()
    {
        var empty = IconHelper.IsRecycleBinEmpty();
        if (empty == recycleBinEmpty) return;
        recycleBinEmpty = empty;

        foreach (var item in Groups.SelectMany(group => group.Items))
        {
            if (item.TargetPath == SpecialItems.RecycleBinTarget) item.RefreshIcon();
        }
    }

    public BoardViewModel Board { get; }

    public ItemDisplayMode ItemDisplayMode => Board.ItemDisplayMode;

    public ObservableCollection<ShortcutGroup> Groups => Board.Groups;

    // The groups shown on the widget, in the order they were saved.
    public ICollectionView RegularGroupsView { get; }

    // Shown after the saved groups; it is computed at runtime and is not part of the settings.
    public ObservableCollection<ShortcutGroup> ActiveGroups => activeWindows.Groups;

    [ObservableProperty] private double iconSize;
    [ObservableProperty] private double powerIconSize = BasePowerIconSize;

    // The power actions shown inside a Submenu item; these are display-only copies and are never saved.
    public IReadOnlyList<ShortcutItem> PowerSubmenuItems { get; } = BuildPowerSubmenuItems();

    private static List<ShortcutItem> BuildPowerSubmenuItems()
    {
        var items = new List<ShortcutItem>();
        foreach (var action in PowerActions.Actions)
        {
            var item = new ShortcutItem { Name = PowerActions.Name(action), PowerAction = action };
            item.RefreshIcon();
            items.Add(item);
        }
        return items;
    }
    [ObservableProperty] private double textSize;
    [ObservableProperty] private double groupOpacity;
    [ObservableProperty] private bool isVertical;

    // Raised after a program or folder is opened from an item, so the window can step back behind it.
    public event Action? ProgramStarted;

    [RelayCommand]
    private void OpenManager()
    {
        try
        {
            WidgetProcess.StartManager();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            dialogs.ShowError($"לא ניתן לפתוח את ההגדרות.\n{ex.Message}", AppInfo.Title);
        }
    }

    [RelayCommand]
    private void CloseApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand]
    private void Launch(ShortcutItem? item)
    {
        if (item is null) return;

        if (item.PowerAction == PowerAction.Submenu)
        {
            item.IsSubmenuOpen = !item.IsSubmenuOpen;
            return;
        }

        if (item.PowerAction != PowerAction.None)
        {
            RunPowerAction(item);
            return;
        }

        if (item.BrowserHandle != IntPtr.Zero)
        {
            BrowserSource.SelectTab(item.BrowserHandle, item.Name);
            ActiveWindowsSource.Activate(item.BrowserHandle);
            ProgramStarted?.Invoke();
            return;
        }

        if (item.WindowHandle != IntPtr.Zero)
        {
            ActiveWindowsSource.Activate(item.WindowHandle);
            ProgramStarted?.Invoke();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true });
            ProgramStarted?.Invoke();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            dialogs.ShowError($"לא ניתן לפתוח את \"{item.Name}\".\n{ex.Message}", AppInfo.Title);
        }
    }

    // Power actions shut the PC down or restart it immediately, so they always ask first.
    private void RunPowerAction(ShortcutItem item)
    {
        if (item.PowerAction == PowerAction.Hibernate && !IsHibernateEnabled())
        {
            if (dialogs.Confirm("מצב Hibernate אינו פעיל במחשב.\nלפתוח את אפשרויות החשמל כדי להפעיל אותו?", AppInfo.Title))
                OpenPowerOptions();
            return;
        }

        var (question, fileName, arguments) = item.PowerAction switch
        {
            PowerAction.Restart => ("להפעיל את המחשב מחדש עכשיו?", "shutdown.exe", "/r /t 0"),
            PowerAction.Sleep => ("להכניס את המחשב למצב שינה?", "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
            PowerAction.Hibernate => ("להכניס את המחשב למצב Hibernate?", "shutdown.exe", "/h"),
            _ => ("לכבות את המחשב עכשיו?", "shutdown.exe", "/s /t 0")
        };
        if (!dialogs.Confirm($"{question}\nעבודה שלא נשמרה תאבד.", AppInfo.Title)) return;

        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            dialogs.ShowError($"לא ניתן לבצע את הפעולה.\n{ex.Message}", AppInfo.Title);
        }
    }

    // HibernateEnabled is only written after the user changes the setting; HibernateEnabledDefault holds the system default.
    private static bool IsHibernateEnabled()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power";
        var value = Microsoft.Win32.Registry.GetValue(key, "HibernateEnabled", null)
                    ?? Microsoft.Win32.Registry.GetValue(key, "HibernateEnabledDefault", 0);
        return value is int enabled && enabled != 0;
    }

    // The Hibernate switch lives on the System Settings tab of Power Options in Control Panel.
    private void OpenPowerOptions()
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "powercfg.cpl,,1") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            dialogs.ShowError($"לא ניתן לפתוח את אפשרויות החשמל.\n{ex.Message}", AppInfo.Title);
        }
    }

    [RelayCommand]
    private void EmptyRecycleBin(ShortcutItem? item)
    {
        if (item is null || !item.IsRecycleBin) return;

        if (IconHelper.EmptyRecycleBin(IntPtr.Zero)) item.RefreshIcon();
    }

    [RelayCommand]
    private void CloseWindow(ShortcutItem? item)
    {
        if (item is null || !item.IsWindow) return;

        ActiveWindowsSource.CloseWindow(item.WindowHandle);
    }

    [RelayCommand]
    private void CloseBrowser(ShortcutGroup? group)
    {
        if (group is null || !group.IsBrowser) return;

        ActiveWindowsSource.CloseWindow(group.BrowserHandle);
    }

    [RelayCommand]
    private void EndProcess(ShortcutItem? item)
    {
        if (item is null || !item.IsWindow) return;

        var program = string.IsNullOrEmpty(item.TargetPath) ? item.Name : Path.GetFileName(item.TargetPath);
        if (!dialogs.Confirm($"לסיים את התהליך \"{program}\"?\nכל החלונות שלו ייסגרו, ועבודה שלא נשמרה תאבד.", AppInfo.Title)) return;

        if (!ActiveWindowsSource.EndProcess(item.WindowHandle))
        {
            dialogs.ShowError($"לא ניתן לסיים את התהליך \"{program}\".", AppInfo.Title);
        }
    }

    private void OnBoardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BoardViewModel.DisplayScale):
            case nameof(BoardViewModel.TextScale):
            case nameof(BoardViewModel.PowerIconScale):
                UpdateSizes();
                break;
            case nameof(BoardViewModel.GroupOpacity):
                UpdateOpacity();
                break;
            case nameof(BoardViewModel.GroupLayout):
                IsVertical = Board.GroupLayout == GroupLayout.Vertical;
                break;
            case nameof(BoardViewModel.ItemDisplayMode):
                OnPropertyChanged(nameof(ItemDisplayMode));
                break;
        }
    }

    private void UpdateOpacity(bool snap = false)
    {
        var target = Math.Clamp(Board.GroupOpacity, 20, 100) / 100;
        if (snap) opacityAnimator.Snap(target);
        else opacityAnimator.AnimateTo(target);
    }

    private void UpdateSizes(bool snap = false)
    {
        var iconScale = Math.Clamp(Board.DisplayScale, 50, 500) / 100;
        var textScale = Math.Clamp(Board.TextScale, 50, 200) / 100;
        var powerIconScale = Math.Clamp(Board.PowerIconScale, 50, 300) / 100;
        PowerIconSize = Math.Round(BasePowerIconSize * powerIconScale);
        var icon = Math.Round(BaseIconSize * iconScale);
        var text = Math.Round(BaseTextSize * textScale, 1);
        if (snap)
        {
            iconSizeAnimator.Snap(icon);
            textSizeAnimator.Snap(text);
        }
        else
        {
            iconSizeAnimator.AnimateTo(icon);
            textSizeAnimator.AnimateTo(text);
        }
    }
}

