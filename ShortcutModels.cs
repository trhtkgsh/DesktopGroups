using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopGroups;

// Where the widget sticks to the screen. Free uses both sliders; an edge uses only the slider along that edge.
public enum ScreenEdge
{
    Free,
    Left,
    Right,
    Top,
    Bottom
}

// Horizontal puts the groups in a row with equal heights; Vertical puts them in a column with equal widths.
public enum GroupLayout
{
    Horizontal,
    Vertical
}

// The system action a power item performs when it is clicked; None for ordinary shortcuts.
public enum PowerAction
{
    None,
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    // Opens the power actions inline below the item instead of performing an action.
    Submenu
}

// Names, colors and tooltips of the built-in power group, kept together so they stay consistent.
public static class PowerActions
{
    public const string GroupColor = "#D13438";

    // The actions shown inside a power submenu, in display order.
    public static readonly PowerAction[] Actions =
    {
        PowerAction.Shutdown, PowerAction.Restart, PowerAction.Sleep, PowerAction.Hibernate
    };

    public static string Name(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "כיבוי",
        PowerAction.Restart => "הפעלה מחדש",
        PowerAction.Sleep => "שינה",
        PowerAction.Hibernate => "Hibernate",
        PowerAction.Submenu => "קבוצת כיבוי",
        _ => string.Empty
    };

    public static string Description(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "כיבוי המחשב",
        PowerAction.Restart => "הפעלה מחדש של המחשב",
        PowerAction.Sleep => "מעבר למצב שינה",
        PowerAction.Hibernate => "מעבר למצב Hibernate",
        PowerAction.Submenu => "פתיחה וסגירה של אפשרויות הכיבוי",
        _ => string.Empty
    };

    public static string HexOf(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "#D13438",
        PowerAction.Restart => "#FFB900",
        PowerAction.Sleep => "#4CC2FF",
        PowerAction.Hibernate => "#000080",
        _ => GroupColor
    };

    public static Brush HoverBrush(PowerAction action) => Solid(HexOf(action), 0x50);

    public static Brush StrongBrush(PowerAction action) => Solid(HexOf(action), 0xFF);

    private static SolidColorBrush Solid(string hex, byte alpha)
    {
        var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}

// Row shows the icon and name side by side; IconWithCaption shows the name under the icon.
public enum ItemDisplayMode
{
    Row,
    IconWithCaption
}

public class AppSettings
{
    public const int CurrentVersion = 2;

    // Version 1 stored positions as indexes 0-2; version 2 stores percentages (0 = left/top, 100 = right/bottom).
    public int Version { get; set; } = 1;
    public ObservableCollection<ShortcutGroup> Groups { get; set; } = [];
    public double HorizontalPosition { get; set; } = 50;
    public double VerticalPosition { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScreenEdge ScreenEdge { get; set; } = ScreenEdge.Free;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GroupLayout GroupLayout { get; set; } = GroupLayout.Horizontal;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemDisplayMode ItemDisplayMode { get; set; } = ItemDisplayMode.Row;
    // Percentage applied to the base icon size, set by the icon size slider in the manager.
    public double DisplayScale { get; set; } = 100;
    // Percentage applied to the base text size, set by the text size slider in the manager.
    public double TextScale { get; set; } = 100;
    // Percentage opacity applied to every group card on the widget.
    public double GroupOpacity { get; set; } = 100;
    // Percentage applied to the base power icon size, set by the power icon size slider in the manager.
    public double PowerIconScale { get; set; } = 100;
}

public partial class ShortcutGroup : ObservableObject
{
    private Brush accentBrush = Brushes.Transparent;
    private Brush softBrush = Brushes.Transparent;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string color = ColorMath.DefaultHex;

    // Only read from older settings files, to remove the standalone power group they contain.
    [JsonPropertyName("isPowerGroup"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsLegacyPowerGroup { get; set; }

    public ObservableCollection<ShortcutItem> Items { get; set; } = [];

    // Set only for the runtime groups of open browser windows; such groups are never saved.
    [JsonIgnore]
    public IntPtr BrowserHandle { get; set; }

    [JsonIgnore]
    public bool IsBrowser => BrowserHandle != IntPtr.Zero;

    // Solid color used for headers, borders and pressed state.
    [JsonIgnore]
    public Brush AccentBrush { get => accentBrush; private set => SetProperty(ref accentBrush, value); }

    // Translucent tint of the same color used for the hover state.
    [JsonIgnore]
    public Brush SoftBrush { get => softBrush; private set => SetProperty(ref softBrush, value); }

    public ShortcutGroup() => UpdateBrushes();

    partial void OnColorChanged(string value) => UpdateBrushes();

    private void UpdateBrushes()
    {
        if (ColorConverter.ConvertFromString(Color) is not System.Windows.Media.Color color) return;

        var accent = new SolidColorBrush(color);
        accent.Freeze();
        AccentBrush = accent;

        var soft = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, color.R, color.G, color.B));
        soft.Freeze();
        SoftBrush = soft;
    }
}

public partial class ShortcutItem : ObservableObject
{
    private ImageSource? icon;
    private IntPtr windowHandle;
    private ImageSource? emptyBinPreview;
    private ImageSource? fullBinPreview;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string targetPath = "";
    [ObservableProperty] private string iconPath = "";

    // Custom icon files for the Recycle Bin's two states; an empty value uses the stock Windows icon.
    [ObservableProperty] private string emptyBinIconPath = "";
    [ObservableProperty] private string fullBinIconPath = "";

    [JsonIgnore]
    public bool IsRecycleBin => TargetPath == SpecialItems.RecycleBinTarget;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PowerAction PowerAction { get; set; }

    [JsonIgnore]
    public string PowerDescription => PowerActions.Description(PowerAction);

    [JsonIgnore]
    public Brush PowerHoverBrush => PowerActions.HoverBrush(PowerAction);

    [JsonIgnore]
    public Brush PowerStrongBrush => PowerActions.StrongBrush(PowerAction);

    // Runtime state of a Submenu item; it is not saved.
    [ObservableProperty, JsonIgnore]
    private bool isSubmenuOpen;

    // Runtime state of a browser tab item: true for the tab currently shown in its window; never saved.
    [ObservableProperty, JsonIgnore]
    private bool isActiveTab;

    partial void OnTargetPathChanged(string value) => OnPropertyChanged(nameof(IsRecycleBin));

    private const int MaxBrowserTabTitleLength = 40;

    // Long browser tab titles are cut for display only; the full name is kept for matching the tab on click.
    [JsonIgnore]
    public string DisplayName => BrowserHandle != IntPtr.Zero && Name.Length > MaxBrowserTabTitleLength
        ? Name[..MaxBrowserTabTitleLength] + "…"
        : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    // Set only for items of the runtime "active windows" group; such items are never saved.
    [JsonIgnore]
    public IntPtr WindowHandle
    {
        get => windowHandle;
        set
        {
            if (SetProperty(ref windowHandle, value)) OnPropertyChanged(nameof(IsWindow));
        }
    }

    [JsonIgnore]
    public bool IsWindow => WindowHandle != IntPtr.Zero;

    // Set only for tab items of a browser group, holding the browser window that owns the tab; never saved.
    [JsonIgnore]
    public IntPtr BrowserHandle { get; set; }

    [JsonIgnore]
    public ImageSource? Icon { get => icon; private set => SetProperty(ref icon, value); }

    // Previews of both Recycle Bin symbols, shown in the manager so both are visible at all times.
    [JsonIgnore]
    public ImageSource? EmptyBinPreview { get => emptyBinPreview; private set => SetProperty(ref emptyBinPreview, value); }

    [JsonIgnore]
    public ImageSource? FullBinPreview { get => fullBinPreview; private set => SetProperty(ref fullBinPreview, value); }

    public void RefreshIcon()
    {
        if (IsRecycleBin)
        {
            EmptyBinPreview = IconHelper.LoadBinIcon(this, empty: true);
            FullBinPreview = IconHelper.LoadBinIcon(this, empty: false);
        }
        Icon = IconHelper.Load(this);
    }

    // Sets an icon that does not come from a file, such as a browser tab's favicon; the icon is not saved.
    public void SetRuntimeIcon(ImageSource? image) => Icon = image;
}
