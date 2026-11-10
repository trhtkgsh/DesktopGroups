using System.Windows;
using System.Windows.Controls;

namespace DesktopGroups;

// Lays children out in a row, or in a column when Vertical is set.
// Horizontal: every child gets the height of the tallest one. Vertical: every child gets the width of the widest one.
public class EqualSizePanel : Panel
{
    public static readonly DependencyProperty VerticalProperty = DependencyProperty.Register(
        nameof(Vertical), typeof(bool), typeof(EqualSizePanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public bool Vertical
    {
        get => (bool)GetValue(VerticalProperty);
        set => SetValue(VerticalProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double main = 0, cross = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = child.DesiredSize;
            main += Vertical ? desired.Height : desired.Width;
            cross = Math.Max(cross, Vertical ? desired.Width : desired.Height);
        }
        return Vertical ? new Size(cross, main) : new Size(main, cross);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cross = 0;
        foreach (UIElement child in InternalChildren)
            cross = Math.Max(cross, Vertical ? child.DesiredSize.Width : child.DesiredSize.Height);

        double offset = 0;
        foreach (UIElement child in InternalChildren)
        {
            var desired = child.DesiredSize;
            if (Vertical)
            {
                child.Arrange(new Rect(0, offset, cross, desired.Height));
                offset += desired.Height;
            }
            else
            {
                child.Arrange(new Rect(offset, 0, desired.Width, cross));
                offset += desired.Width;
            }
        }
        return finalSize;
    }
}
