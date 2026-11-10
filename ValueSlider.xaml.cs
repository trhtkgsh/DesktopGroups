using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopGroups;

// A slider with a text box that shows the same value. Both stay in sync, and typed values are clamped to the range.
public partial class ValueSlider : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ValueSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ValueSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ValueSlider), new PropertyMetadata(100.0));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ValueSlider), new PropertyMetadata("", OnLabelChanged));

    public ValueSlider()
    {
        InitializeComponent();
        UpdateText();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ValueSlider)d).UpdateText();

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ValueSlider)d).LabelText.Text = (string)e.NewValue;

    // Updates the box while the value changes, but not while the user is typing in it.
    private void UpdateText()
    {
        if (ValueBox is null || ValueBox.IsKeyboardFocusWithin) return;
        ShowValue();
    }

    private void ShowValue() => ValueBox.Text = Math.Round(Value).ToString("F0", CultureInfo.CurrentCulture);

    private void SliderControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateText();

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
            e.Handled = true;
        }
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitText();

    private void CommitText()
    {
        if (double.TryParse(ValueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed))
            Value = Math.Clamp(typed, Minimum, Maximum);
        ShowValue();
    }
}
