using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

public static class IconHelper
{
    public static ImageSource? Load(ShortcutItem item)
    {
        if (item.IsRecycleBin) return LoadBinIcon(item, IsRecycleBinEmpty());
        if (item.PowerAction != PowerAction.None && string.IsNullOrEmpty(item.IconPath)) return LoadPowerIcon(item.PowerAction);
        return string.IsNullOrEmpty(item.IconPath) ? LoadShellIcon(item.TargetPath) : LoadCustomIcon(item.IconPath);
    }

    // Draws a power symbol into a 32 px bitmap, since the system has no matching icon for each action.
    private static ImageSource LoadPowerIcon(PowerAction action)
    {
        var glyph = action switch
        {
            PowerAction.Restart => "\u21BB",
            PowerAction.Sleep => "\u263E",
            PowerAction.Hibernate => "\u2744",
            PowerAction.Submenu => "\u2630",
            _ => "\u23FB"
        };
        var brush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        brush.Freeze();
        var text = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            22, brush, 1.0);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawText(text, new Point((32 - text.Width) / 2, (32 - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // The bin's chosen symbol for the given state, or the stock Windows symbol when none was chosen.
    public static ImageSource? LoadBinIcon(ShortcutItem item, bool empty)
    {
        var path = empty ? item.EmptyBinIconPath : item.FullBinIconPath;
        return (string.IsNullOrEmpty(path) ? null : LoadCustomIcon(path)) ?? GetRecycleBinIcon(full: !empty);
    }

    public static bool CanLoadCustomIcon(string path) => LoadCustomIcon(path) != null;

    private static ImageSource? LoadCustomIcon(string path)
    {
        if (!File.Exists(path)) return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".exe" or ".dll") return LoadShellIcon(path);
        if (extension is not (".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp")) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static ImageSource? LoadShellIcon(string path)
    {
        if (SpecialItems.GetSpecialFolderId(path) is { } folderId) return LoadSpecialFolderIcon(folderId);
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        var result = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON);
        return result == IntPtr.Zero ? null : ToImageSource(info.hIcon);
    }

    // Full bin icon when the Recycle Bin holds items, empty bin icon otherwise.
    public static bool IsRecycleBinEmpty()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) != 0 || info.i64NumItems == 0;
    }

    // Returns false if the user cancelled the Windows confirmation or the call failed.
    public static bool EmptyRecycleBin(IntPtr ownerHwnd) => SHEmptyRecycleBin(ownerHwnd, null, 0) == 0;

    public static ImageSource? GetRecycleBinIcon(bool full) =>
        LoadStockIcon(full ? SIID_RECYCLERFULL : SIID_RECYCLER);

    private static ImageSource? LoadStockIcon(int stockId)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(stockId, SHGSI_ICON, ref info) != 0) return null;
        return ToImageSource(info.hIcon);
    }

    private static ImageSource? LoadSpecialFolderIcon(int folderId)
    {
        if (SHGetSpecialFolderLocation(IntPtr.Zero, folderId, out var pidl) != 0 || pidl == IntPtr.Zero) return null;
        try
        {
            var result = SHGetFileInfoByPidl(pidl, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_PIDL);
            return result == IntPtr.Zero ? null : ToImageSource(info.hIcon);
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static ImageSource? ToImageSource(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        finally { DestroyIcon(hIcon); }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_PIDL = 0x8;
    private const uint SHGSI_ICON = 0x100;
    private const int SIID_RECYCLER = 31;
    private const int SIID_RECYCLERFULL = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);
    // Without SHERB_NOCONFIRMATION, Windows shows its own "delete permanently" confirmation.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
    [DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(int siid, uint flags, ref SHSTOCKICONINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfo")]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO fileInfo, uint size, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfo")]
    private static extern IntPtr SHGetFileInfoByPidl(IntPtr pidl, uint attributes, out SHFILEINFO fileInfo, uint size, uint flags);
    [DllImport("shell32.dll")]
    private static extern int SHGetSpecialFolderLocation(IntPtr hwndOwner, int folder, out IntPtr pidl);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

