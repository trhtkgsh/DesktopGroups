using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopGroups;

// The widget is sized to its content and placed by the position sliders. It has no title bar, so it cannot be dragged.
public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel;
    private readonly ValueAnimator _leftAnimator;
    private readonly ValueAnimator _topAnimator;
    private readonly DispatcherTimer _lowerTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _placed;
    // Toggled by Left Alt + ~: false keeps the groups behind every window, true keeps them above every window.
    private bool _alwaysOnTop;
    private readonly KeyboardHook _stackHotkey = new();

    public WidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // CollectionContainer.Collection is not a dependency property, so the composite is set up here instead of in XAML.
        GroupsList.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = viewModel.RegularGroupsView },
            new CollectionContainer { Collection = viewModel.ActiveGroups }
        };
        _leftAnimator = new ValueAnimator(v => Left = v);
        _topAnimator = new ValueAnimator(v => Top = v);

        _viewModel.Board.PropertyChanged += OnBoardPropertyChanged;
        SizeChanged += (_, _) => Reposition();
        ContentRendered += (_, _) => Reposition();

        // The timer re-applies the stacking position if the shell recreates the desktop window behind us.
        SourceInitialized += (_, _) =>
        {
            HideFromAltTab();
            ApplyStacking();
        };
        Activated += (_, _) => ApplyStacking();
        _lowerTimer.Tick += (_, _) => ApplyStacking();
        _lowerTimer.Start();
        _stackHotkey.Pressed += ToggleStacking;
        _viewModel.ProgramStarted += SendToBackground;
        Closed += (_, _) =>
        {
            _lowerTimer.Stop();
            _stackHotkey.Dispose();
        };
    }

    // Tool windows are left out of Alt+Tab, so the groups are not listed there.
    private void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle,
            (exStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow);
    }

    // Keeps the groups behind every other window, or above them when "always on top" is on.
    // Activation is not blocked, so clicking a group still works.
    private void ApplyStacking()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, _alwaysOnTop ? NativeMethods.HwndTopmost : NativeMethods.HwndBottom, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private void ToggleStacking()
    {
        _alwaysOnTop = !_alwaysOnTop;
        ApplyStacking();
    }

    // Opening a program moves the whole widget to the background. The bottom mode is kept afterwards,
    // so the timer does not bring the widget back above the program.
    private void SendToBackground()
    {
        _alwaysOnTop = false;
        ApplyStacking();

        // The click and the window activation of the launched program finish after this handler,
        // so the position is applied again once they settle.
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            ApplyStacking();
        };
        delay.Start();
    }

    private void OpenManagerMenu_Click(object sender, RoutedEventArgs e) => _viewModel.OpenManagerCommand.Execute(null);

    private void CloseAppMenu_Click(object sender, RoutedEventArgs e) => _viewModel.CloseAppCommand.Execute(null);

    // The menu is shown from an item, so its placement target carries that item as DataContext.
    private void EmptyRecycleBinMenu_Click(object sender, RoutedEventArgs e) =>
        _viewModel.EmptyRecycleBinCommand.Execute(MenuTargetItem(sender));

    private void CloseWindowMenu_Click(object sender, RoutedEventArgs e) =>
        _viewModel.CloseWindowCommand.Execute(MenuTargetItem(sender));

    private void EndProcessMenu_Click(object sender, RoutedEventArgs e) =>
        _viewModel.EndProcessCommand.Execute(MenuTargetItem(sender));

    // The actions sit inside the item button, so the press is handled here to keep the item from launching.
    private void CloseWindowAction_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.CloseWindowCommand.Execute((sender as FrameworkElement)?.DataContext as ShortcutItem);
    }

    private void EndProcessAction_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.EndProcessCommand.Execute((sender as FrameworkElement)?.DataContext as ShortcutItem);
    }

    private void EmptyRecycleBinAction_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.EmptyRecycleBinCommand.Execute((sender as FrameworkElement)?.DataContext as ShortcutItem);
    }

    private static ShortcutItem? MenuTargetItem(object sender)
    {
        var target = (sender as FrameworkElement)?.Parent is ContextMenu menu ? menu.PlacementTarget as FrameworkElement : null;
        return target?.DataContext as ShortcutItem;
    }

    private void OnBoardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardViewModel.HorizontalPosition) or nameof(BoardViewModel.VerticalPosition)
            or nameof(BoardViewModel.ScreenEdge) or nameof(BoardViewModel.GroupLayout))
            Reposition();
    }

    // Places the widget according to the screen edge and the sliders. Moves are animated after the first placement.
    private void Reposition()
    {
        if (!IsLoaded) return;

        var area = SystemParameters.WorkArea;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var board = _viewModel.Board;
        var h = board.HorizontalPosition / 100.0;
        var v = board.VerticalPosition / 100.0;
        var freeLeft = area.Left + (area.Width - width) * h;
        var freeTop = area.Top + (area.Height - height) * v;

        double targetLeft, targetTop;
        switch (board.ScreenEdge)
        {
            case ScreenEdge.Left:
                targetLeft = area.Left;
                targetTop = freeTop;
                break;
            case ScreenEdge.Right:
                targetLeft = area.Right - width;
                targetTop = freeTop;
                break;
            case ScreenEdge.Top:
                targetLeft = freeLeft;
                targetTop = area.Top;
                break;
            case ScreenEdge.Bottom:
                targetLeft = freeLeft;
                targetTop = area.Bottom - height;
                break;
            default:
                targetLeft = freeLeft;
                targetTop = freeTop;
                break;
        }

        if (!_placed)
        {
            _placed = true;
            _leftAnimator.Snap(targetLeft);
            _topAnimator.Snap(targetTop);
            return;
        }

        _leftAnimator.AnimateTo(targetLeft);
        _topAnimator.AnimateTo(targetTop);
    }
}
