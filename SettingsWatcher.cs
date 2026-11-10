using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DesktopGroups;

// Reloads the widget board when the manager saves the settings file.
public sealed class SettingsWatcher : IDisposable
{
    private readonly SettingsStore store;
    private readonly BoardViewModel board;
    private readonly string fileName;
    private readonly FileSystemWatcher watcher;
    private readonly DispatcherTimer reloadTimer;
    private int failedReads;

    public SettingsWatcher(SettingsStore store, BoardViewModel board)
    {
        this.store = store;
        this.board = board;
        fileName = Path.GetFileName(store.FilePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(store.FilePath))!;
        Directory.CreateDirectory(directory);

        // Reloads at most every 50 ms while changes keep coming, so a slider moves the widget live.
        reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        reloadTimer.Tick += OnReloadTick;

        watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Changed += (_, e) => OnFileEvent(e.Name);
        watcher.Created += (_, e) => OnFileEvent(e.Name);
        watcher.Renamed += (_, e) => OnFileEvent(e.Name);
        watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        watcher.Dispose();
        reloadTimer.Stop();
    }

    private void OnFileEvent(string? name)
    {
        if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!reloadTimer.IsEnabled) reloadTimer.Start();
        });
    }

    private void OnReloadTick(object? sender, EventArgs e)
    {
        reloadTimer.Stop();
        try
        {
            board.ReloadFrom(store.Load());
            failedReads = 0;
        }
        catch (IOException) when (failedReads++ < 5)
        {
            // The manager is still writing the file. Try again shortly.
            reloadTimer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // The file is invalid or locked. Keep the current board, the next save will reload it.
        }
    }
}
