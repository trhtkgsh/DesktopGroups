using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace DesktopGroups;

// Reads each tab's own icon from the browser's default profile: History maps a tab title to its page URL,
// and Favicons maps that URL to its icon. The profile files are locked while the browser runs, so they are copied first.
internal static class BrowserFavicons
{
    private const int IconSize = 32;
    private static readonly TimeSpan MinimumReloadInterval = TimeSpan.FromSeconds(15);

    private static readonly Dictionary<(string Browser, string Title), ImageSource> Cache = new();
    private static readonly Dictionary<string, DateTime> LastLoad = new(StringComparer.OrdinalIgnoreCase);

    // Returns icons for the titles that have one. Titles that are not cached are looked up at most once per reload interval per browser.
    public static Dictionary<string, ImageSource> Get(string exeName, IEnumerable<string> titles)
    {
        var browser = exeName.ToLowerInvariant();
        var profile = ProfileFolder(browser);
        var result = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        if (profile is null) return result;

        var missing = new List<string>();
        foreach (var title in titles)
        {
            if (Cache.TryGetValue((browser, title), out var cached)) result[title] = cached;
            else missing.Add(title);
        }

        if (missing.Count == 0) return result;
        if (LastLoad.TryGetValue(browser, out var last) && DateTime.UtcNow - last < MinimumReloadInterval) return result;
        LastLoad[browser] = DateTime.UtcNow;

        foreach (var (title, icon) in Lookup(profile, missing))
        {
            Cache[(browser, title)] = icon;
            result[title] = icon;
        }
        return result;
    }

    private static string? ProfileFolder(string exeName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = exeName.ToLowerInvariant() switch
        {
            "msedge.exe" => Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default"),
            "chrome.exe" => Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default"),
            _ => null
        };
        return folder is not null && Directory.Exists(folder) ? folder : null;
    }

    private static List<(string Title, ImageSource Icon)> Lookup(string profile, List<string> titles)
    {
        var found = new List<(string, ImageSource)>();
        var copyDir = Path.Combine(Path.GetTempPath(), "DesktopGroups", "favicons-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(copyDir);
            var history = CopyProfileFile(profile, copyDir, "History");
            var favicons = CopyProfileFile(profile, copyDir, "Favicons");
            if (history is null || favicons is null) return found;

            using var historyDb = Open(history);
            using var faviconDb = Open(favicons);
            foreach (var title in titles)
            {
                var url = QueryUrl(historyDb, title);
                if (url is null) continue;
                var bytes = QueryIcon(faviconDb, url);
                var icon = bytes is null ? null : Decode(bytes);
                if (icon is not null) found.Add((title, icon));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            // Browser data is optional: without it the tab keeps the browser icon.
        }
        finally
        {
            try { Directory.Delete(copyDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    // Copies the file (and its WAL file, when present) with shared access, since the browser keeps it open.
    private static string? CopyProfileFile(string profile, string copyDir, string name)
    {
        var source = Path.Combine(profile, name);
        if (!File.Exists(source)) return null;
        var target = Path.Combine(copyDir, name);
        CopyShared(source, target);
        var wal = source + "-wal";
        if (File.Exists(wal)) CopyShared(wal, target + "-wal");
        return target;
    }

    private static void CopyShared(string source, string target)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    private static string? QueryUrl(SqliteConnection history, string title)
    {
        using var command = history.CreateCommand();
        command.CommandText = "SELECT url FROM urls WHERE title = $title ORDER BY last_visit_time DESC LIMIT 1";
        command.Parameters.AddWithValue("$title", title);
        return command.ExecuteScalar() as string;
    }

    private static byte[]? QueryIcon(SqliteConnection favicons, string pageUrl)
    {
        using var command = favicons.CreateCommand();
        command.CommandText = """
            SELECT fb.image_data FROM icon_mapping im
            JOIN favicon_bitmaps fb ON fb.icon_id = im.icon_id
            WHERE im.page_url = $url AND fb.image_data IS NOT NULL
            ORDER BY fb.width DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$url", pageUrl);
        return command.ExecuteScalar() as byte[];
    }

    // Formats such as WebP are not supported by WPF, so those icons are skipped.
    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = IconSize;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
