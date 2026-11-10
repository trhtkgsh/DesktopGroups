using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopGroups;

public partial class App : Application
{
    private Mutex? widgetMutex;
    private EventWaitHandle? stopEvent;
    private SettingsWatcher? settingsWatcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dialogs = new DialogService();
        var store = new SettingsStore();
        var (settings, canSave) = LoadSettings(store, dialogs);

        if (e.Args.Contains(WidgetProcess.ManagerArgument))
            StartManager(store, settings, canSave, dialogs);
        else
            StartWidget(store, settings, canSave, dialogs);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        settingsWatcher?.Dispose();
        stopEvent?.Dispose();
        widgetMutex?.Dispose();
        base.OnExit(e);
    }

    private static (AppSettings Settings, bool CanSave) LoadSettings(SettingsStore store, DialogService dialogs)
    {
        try
        {
            return (store.Load(), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            dialogs.ShowError(
                $"לא ניתן לקרוא את קובץ ההגדרות:\n{store.FilePath}\n\n{ex.Message}\n\n" +
                "האפליקציה תפעל עם הגדרות ריקות, והשינויים לא יישמרו כדי לא לדרוס את הקובץ.",
                AppInfo.Title);
            return (new AppSettings { Version = AppSettings.CurrentVersion }, false);
        }
    }

    private void StartManager(SettingsStore store, AppSettings settings, bool canSave, DialogService dialogs)
    {
        var board = new BoardViewModel(store, settings, canSave);
        var viewModel = new MainViewModel(board, dialogs);
        var controlWindow = new MainWindow(viewModel);

        MainWindow = controlWindow;
        controlWindow.Show();
        viewModel.EnsureWidgetRunning();
    }

    private void StartWidget(SettingsStore store, AppSettings settings, bool canSave, DialogService dialogs)
    {
        // Only one widget process may run. A second instance exits immediately.
        widgetMutex = new Mutex(true, WidgetProcess.MutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // The widget keeps running after its window is closed, until the manager asks it to stop.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var board = new BoardViewModel(store, settings, canSave);
        var widget = new WidgetWindow(new WidgetViewModel(board, dialogs));
        settingsWatcher = new SettingsWatcher(store, board);

        stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WidgetProcess.StopEventName);
        var stop = stopEvent;
        _ = Task.Run(() =>
        {
            stop.WaitOne();
            Dispatcher.InvokeAsync(() => Shutdown());
        });

        widget.Show();
    }
}
