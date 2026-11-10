using System.Windows.Threading;

namespace DesktopGroups;

// Moves a value smoothly toward a target and reports each step. A new target starts from the current value,
// so changes made in the middle of an animation stay smooth.
public sealed class ValueAnimator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Action<double> apply;
    private double start;
    private double target;
    private DateTime startedAt;

    public ValueAnimator(Action<double> apply)
    {
        this.apply = apply;
        timer.Tick += (_, _) => Step();
    }

    public double Current { get; private set; }

    // Sets the value at once, without animating. Used for the first value.
    public void Snap(double value)
    {
        timer.Stop();
        Current = value;
        apply(value);
    }

    public void AnimateTo(double value)
    {
        if (timer.IsEnabled && Math.Abs(value - target) < 0.01) return;

        if (Math.Abs(value - Current) < 0.01)
        {
            Snap(value);
            return;
        }

        start = Current;
        target = value;
        startedAt = DateTime.UtcNow;
        timer.Start();
    }

    private void Step()
    {
        var t = Math.Clamp((DateTime.UtcNow - startedAt) / Duration, 0, 1);
        if (t >= 1)
        {
            Snap(target);
            return;
        }

        // Ease-out cubic: fast at first, then slowing down into the target.
        var eased = 1 - Math.Pow(1 - t, 3);
        Current = start + (target - start) * eased;
        apply(Current);
    }
}
