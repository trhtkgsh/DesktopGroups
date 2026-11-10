using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace DesktopGroups;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer widgetStateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime paletteClosedAt = DateTime.MinValue;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;

        widgetStateTimer.Tick += (_, _) => this.viewModel.RefreshWidgetState();
        Closed += (_, _) => widgetStateTimer.Stop();
        widgetStateTimer.Start();
    }

    private void NewGroupColorButton_Click(object sender, RoutedEventArgs e) =>
        OpenPalette(NewGroupPalette, NewGroupPicker, viewModel.NewGroupColor, hex => viewModel.NewGroupColor = hex);

    private void GroupColorButton_Click(object sender, RoutedEventArgs e)
    {
        var group = viewModel.SelectedGroup;
        if (group is null) return;
        OpenPalette(GroupPalette, GroupPicker, group.Color, hex => group.Color = hex);
    }

    // The palette closes when the user clicks outside it, and that same click lands on the dot.
    // Ignore the dot click shortly after a close so the palette does not reopen immediately.
    private void OpenPalette(Popup popup, ColorPickerPanel panel, string currentHex, Action<string> apply)
    {
        if (popup.IsOpen || DateTime.UtcNow - paletteClosedAt < TimeSpan.FromMilliseconds(250)) return;

        panel.Show(currentHex, apply);
        popup.IsOpen = true;
    }

    private void Palette_Closed(object sender, EventArgs e) => paletteClosedAt = DateTime.UtcNow;
}
