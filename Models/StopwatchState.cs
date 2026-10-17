using System.Globalization;

namespace StopwatchWidget.Models;

/// <summary>
/// A lightweight, immutable snapshot of the stopwatch state at a moment in time.
/// The <see cref="ViewModels.StopwatchViewModel"/> produces these snapshots as the
/// elapsed time changes; they are handy for persistence, logging or unit tests
/// without pulling in any UI dependencies.
/// </summary>
public sealed record StopwatchState
{
    /// <summary>
    /// The total amount of time that has been measured.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// <see langword="true"/> while the stopwatch is actively counting.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// <see langword="true"/> when the widget window should stay above other windows.
    /// </summary>
    public bool IsAlwaysOnTop { get; init; }

    /// <summary>
    /// The elapsed time formatted as <c>HH:MM:SS</c>. Hours are not clamped to 24,
    /// so a long session keeps counting past <c>99</c> hours.
    /// </summary>
    public string FormattedTime => Format(ElapsedTime);

    /// <summary>
    /// Creates a copy of this state with a new <see cref="ElapsedTime"/>.
    /// </summary>
    public StopwatchState WithElapsedTime(TimeSpan elapsedTime) => this with { ElapsedTime = elapsedTime };

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as <c>HH:MM:SS</c>.
    /// </summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        var totalHours = (int)time.TotalHours;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}",
            totalHours,
            time.Minutes,
            time.Seconds);
    }
}
