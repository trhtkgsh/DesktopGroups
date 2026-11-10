using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopGroups;

public partial class MainViewModel : ObservableObject
{
    private readonly IDialogService dialogs;

    public MainViewModel(BoardViewModel board, IDialogService dialogs)
    {
        Board = board;
        this.dialogs = dialogs;
        Board.SaveFailed += message => dialogs.ShowError(message, AppInfo.Title);
    }

    public BoardViewModel Board { get; }

    public ObservableCollection<ShortcutGroup> Groups => Board.Groups;

    [ObservableProperty] private bool widgetRunning;

    public string WidgetButtonText => WidgetRunning ? "הסתר את הקבוצות" : "הצג את הקבוצות";

    private bool startWithWindows;

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (!SetProperty(ref startWithWindows, value)) return;
            try
            {
                StartupRegistration.SetEnabled(value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                dialogs.ShowError($"לא ניתן לעדכן את הפעלה עם Windows.\n{ex.Message}", AppInfo.Title);
                // Revert the toggle to the actual state, without writing the registry again.
                startWithWindows = !value;
                OnPropertyChanged(nameof(StartWithWindows));
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveGroupUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveGroupDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddThisPcCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRecycleBinCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPowerSubmenuCommand))]
    private ShortcutGroup? selectedGroup;

    [ObservableProperty] private string newGroupName = "";
    [ObservableProperty] private string newGroupColor = ColorMath.DefaultHex;

    partial void OnWidgetRunningChanged(bool value) => OnPropertyChanged(nameof(WidgetButtonText));

    // Called once at startup: the manager keeps the groups visible, so it starts the widget when it is not running.
    public void EnsureWidgetRunning()
    {
        startWithWindows = StartupRegistration.IsEnabled();
        OnPropertyChanged(nameof(StartWithWindows));
        if (!WidgetProcess.IsRunning()) StartWidget();
        RefreshWidgetState();
        SelectedGroup ??= Groups.FirstOrDefault();
    }

    public void RefreshWidgetState()
    {
        WidgetRunning = WidgetProcess.IsRunning();
    }

    [RelayCommand]
    private void ToggleWidget()
    {
        if (WidgetRunning)
            WidgetProcess.RequestStop();
        else
            StartWidget();
        RefreshWidgetState();
    }

    private void StartWidget()
    {
        try
        {
            WidgetProcess.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            dialogs.ShowError($"לא ניתן להפעיל את הקבוצות.\n{ex.Message}", AppInfo.Title);
        }
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0)
        {
            dialogs.ShowWarning("יש להזין שם לקבוצה.", AppInfo.Title);
            return;
        }

        var group = new ShortcutGroup { Name = name, Color = NewGroupColor };
        Groups.Add(group);
        SelectedGroup = group;
        NewGroupName = "";
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void RemoveGroup()
    {
        var group = SelectedGroup;
        if (group is null) return;
        if (!dialogs.Confirm($"למחוק את הקבוצה \"{group.Name}\" וכל פריטיה?", AppInfo.Title)) return;

        Groups.Remove(group);
        SelectedGroup = Groups.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void AddFiles()
    {
        if (dialogs.PickFiles() is { } paths) AddTargets(paths);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void AddFolders()
    {
        if (dialogs.PickFolders() is { } paths) AddTargets(paths);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void AddThisPc() => AddSpecialItem(SpecialItems.ThisPcName, SpecialItems.ThisPcTarget);

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void AddRecycleBin() => AddSpecialItem(SpecialItems.RecycleBinName, SpecialItems.RecycleBinTarget);

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void AddPowerSubmenu()
    {
        var group = SelectedGroup;
        if (group is null) return;

        var item = new ShortcutItem { Name = PowerActions.Name(PowerAction.Submenu), PowerAction = PowerAction.Submenu };
        item.RefreshIcon();
        group.Items.Add(item);
    }

    [RelayCommand]
    private void RemoveItem(ShortcutItem? item)
    {
        if (item is not null) SelectedGroup?.Items.Remove(item);
    }

    [RelayCommand]
    private void MoveItemUp(ShortcutItem? item) => MoveItem(item, -1);

    [RelayCommand]
    private void MoveItemDown(ShortcutItem? item) => MoveItem(item, 1);

    [RelayCommand]
    private void ChangeIcon(ShortcutItem? item)
    {
        if (item is null) return;

        var path = dialogs.PickIconFile();
        if (path is null) return;

        if (!IconHelper.CanLoadCustomIcon(path))
        {
            dialogs.ShowWarning("לא ניתן לטעון את הסמל שנבחר. יש לבחור קובץ ICO, PNG, JPG, BMP, EXE או DLL.", AppInfo.Title);
            return;
        }

        item.IconPath = path;
        item.RefreshIcon();
    }

    [RelayCommand]
    private void ChooseEmptyBinIcon(ShortcutItem? item) => ChooseBinIcon(item, empty: true);

    [RelayCommand]
    private void ChooseFullBinIcon(ShortcutItem? item) => ChooseBinIcon(item, empty: false);

    private void ChooseBinIcon(ShortcutItem? item, bool empty)
    {
        if (item is null || !item.IsRecycleBin) return;

        var path = dialogs.PickIconFile();
        if (path is null) return;

        if (!IconHelper.CanLoadCustomIcon(path))
        {
            dialogs.ShowWarning("לא ניתן לטעון את הסמל שנבחר. יש לבחור קובץ ICO, PNG, JPG, BMP, EXE או DLL.", AppInfo.Title);
            return;
        }

        if (empty) item.EmptyBinIconPath = path;
        else item.FullBinIconPath = path;
        item.RefreshIcon();
    }

    // Closes the widget and the manager together.
    [RelayCommand]
    private void CloseApp()
    {
        WidgetProcess.RequestStop();
        System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void MoveGroupUp() => MoveGroup(-1);

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void MoveGroupDown() => MoveGroup(1);

    private bool CanModifySelectedGroup() => SelectedGroup is not null;

    private void MoveGroup(int offset)
    {
        var group = SelectedGroup;
        if (group is null) return;

        var index = Groups.IndexOf(group);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Groups.Count) return;
        Groups.Move(index, target);
    }

    private void MoveItem(ShortcutItem? item, int offset)
    {
        var items = SelectedGroup?.Items;
        if (item is null || items is null) return;

        var index = items.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= items.Count) return;
        items.Move(index, target);
    }

    private void AddSpecialItem(string name, string target)
    {
        var group = SelectedGroup;
        if (group is null) return;

        var item = new ShortcutItem { Name = name, TargetPath = target };
        item.RefreshIcon();
        group.Items.Add(item);
    }

    private void AddTargets(IEnumerable<string> paths)
    {
        var group = SelectedGroup;
        if (group is null) return;

        foreach (var path in paths)
        {
            var name = Directory.Exists(path)
                ? new DirectoryInfo(path).Name
                : Path.GetFileNameWithoutExtension(path);

            var item = new ShortcutItem { Name = string.IsNullOrEmpty(name) ? path : name, TargetPath = path };
            item.RefreshIcon();
            group.Items.Add(item);
        }
    }
}
