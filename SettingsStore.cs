using System.IO;
using System.Text.Json;

namespace DesktopGroups;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore()
    {
        var directory = Environment.GetEnvironmentVariable("DESKTOPGROUPS_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopGroups");
        FilePath = Path.Combine(directory, "settings.json");
    }

    public string FilePath { get; }

    /// <summary>Loads the settings. Throws if the file exists but is invalid, so it is never overwritten by defaults.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings { Version = AppSettings.CurrentVersion, HorizontalPosition = 50, VerticalPosition = 0 };

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                       ?? throw new JsonException("קובץ ההגדרות ריק או אינו תקין.");

        if (settings.Version < AppSettings.CurrentVersion)
        {
            // Version 1 stored positions as indexes: horizontal 0=right, 1=center, 2=left; vertical 0=top, 1=center, 2=bottom.
            settings.HorizontalPosition = settings.HorizontalPosition switch { 0 => 100, 2 => 0, _ => 50 };
            settings.VerticalPosition = settings.VerticalPosition switch { 1 => 50, 2 => 100, _ => 0 };
            settings.Version = AppSettings.CurrentVersion;
        }

        foreach (var item in settings.Groups.SelectMany(group => group.Items))
            item.RefreshIcon();

        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
