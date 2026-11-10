using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopGroups;

public static class AppInfo
{
    public const string Title = "מרחב הקיצורים";
}

// Owns the shared board state, shared by the control window and the widget. Any edit is saved automatically.
public partial class BoardViewModel : ObservableObject
{
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly bool canSave;
    private bool reloading;

    public BoardViewModel(SettingsStore store, AppSettings settings, bool canSave)
    {
        this.store = store;
        this.settings = settings;
        this.canSave = canSave;
        horizontalPosition = settings.HorizontalPosition;
        verticalPosition = settings.VerticalPosition;
        screenEdge = settings.ScreenEdge;
        groupLayout = settings.GroupLayout;
        itemDisplayMode = settings.ItemDisplayMode;
        displayScale = settings.DisplayScale;
        textScale = settings.TextScale;
        groupOpacity = settings.GroupOpacity;
        powerIconScale = settings.PowerIconScale;

        // Older versions kept a standalone power group; it is replaced by the power submenu item.
        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].IsLegacyPowerGroup) Groups.RemoveAt(i);
        }

        Groups.CollectionChanged += OnGroupsChanged;
        foreach (var group in Groups) Attach(group);
    }

    public event Action<string>? SaveFailed;

    public ObservableCollection<ShortcutGroup> Groups => settings.Groups;

    // 0 = left/top, 100 = right/bottom.
    [ObservableProperty] private double horizontalPosition;
    [ObservableProperty] private double verticalPosition;
    [ObservableProperty] private ScreenEdge screenEdge;
    [ObservableProperty] private GroupLayout groupLayout;
    [ObservableProperty] private ItemDisplayMode itemDisplayMode;
    [ObservableProperty] private double displayScale;
    [ObservableProperty] private double textScale;
    [ObservableProperty] private double groupOpacity;
    [ObservableProperty] private double powerIconScale;

    // The sliders that have no effect for the chosen edge are disabled in the manager.
    public bool CanMoveHorizontally => ScreenEdge is ScreenEdge.Free or ScreenEdge.Top or ScreenEdge.Bottom;
    public bool CanMoveVertically => ScreenEdge is ScreenEdge.Free or ScreenEdge.Left or ScreenEdge.Right;

    partial void OnHorizontalPositionChanged(double value)
    {
        settings.HorizontalPosition = value;
        Save();
    }

    partial void OnVerticalPositionChanged(double value)
    {
        settings.VerticalPosition = value;
        Save();
    }

    partial void OnScreenEdgeChanged(ScreenEdge value)
    {
        settings.ScreenEdge = value;
        OnPropertyChanged(nameof(CanMoveHorizontally));
        OnPropertyChanged(nameof(CanMoveVertically));
        Save();
    }

    partial void OnGroupLayoutChanged(GroupLayout value)
    {
        settings.GroupLayout = value;
        Save();
    }

    partial void OnItemDisplayModeChanged(ItemDisplayMode value)
    {
        settings.ItemDisplayMode = value;
        Save();
    }

    partial void OnDisplayScaleChanged(double value)
    {
        settings.DisplayScale = value;
        Save();
    }

    partial void OnTextScaleChanged(double value)
    {
        settings.TextScale = value;
        Save();
    }

    partial void OnGroupOpacityChanged(double value)
    {
        settings.GroupOpacity = value;
        Save();
    }

    partial void OnPowerIconScaleChanged(double value)
    {
        settings.PowerIconScale = value;
        Save();
    }

    // Replaces the board with settings read from disk, used by the widget when the manager saves.
    public void ReloadFrom(AppSettings fresh)
    {
        reloading = true;
        try
        {
            foreach (var group in Groups.ToList()) Detach(group);
            Groups.Clear();
            foreach (var group in fresh.Groups)
            {
                Groups.Add(group);
                Attach(group);
            }
            HorizontalPosition = fresh.HorizontalPosition;
            VerticalPosition = fresh.VerticalPosition;
            ScreenEdge = fresh.ScreenEdge;
            GroupLayout = fresh.GroupLayout;
            ItemDisplayMode = fresh.ItemDisplayMode;
            DisplayScale = fresh.DisplayScale;
            TextScale = fresh.TextScale;
            GroupOpacity = fresh.GroupOpacity;
            PowerIconScale = fresh.PowerIconScale;
        }
        finally
        {
            reloading = false;
        }
    }

    public void Save()
    {
        // Saving is disabled when the settings file could not be read, so the broken file is not overwritten.
        // It is also disabled while reloading from disk, since the file is already the source of truth.
        if (!canSave || reloading) return;
        try
        {
            store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveFailed?.Invoke($"לא ניתן לשמור את ההגדרות:\n{ex.Message}");
        }
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ShortcutGroup group in e.OldItems) Detach(group);
        if (e.NewItems is not null)
            foreach (ShortcutGroup group in e.NewItems) Attach(group);
        Save();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ShortcutItem item in e.OldItems) item.PropertyChanged -= OnNodeChanged;
        if (e.NewItems is not null)
            foreach (ShortcutItem item in e.NewItems) item.PropertyChanged += OnNodeChanged;
        Save();
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Derived visuals are not persisted, so they should not trigger a save.
        if (e.PropertyName is nameof(ShortcutGroup.AccentBrush) or nameof(ShortcutGroup.SoftBrush) or nameof(ShortcutItem.Icon) or nameof(ShortcutItem.IsSubmenuOpen))
            return;
        Save();
    }

    private void Attach(ShortcutGroup group)
    {
        group.PropertyChanged += OnNodeChanged;
        group.Items.CollectionChanged += OnItemsChanged;
        foreach (var item in group.Items) item.PropertyChanged += OnNodeChanged;
    }

    private void Detach(ShortcutGroup group)
    {
        group.PropertyChanged -= OnNodeChanged;
        group.Items.CollectionChanged -= OnItemsChanged;
        foreach (var item in group.Items) item.PropertyChanged -= OnNodeChanged;
    }
}
