namespace StopwatchWidget.Models;

/// <summary>
/// The persisted application state: whether the widget floats above other windows
/// and how much time was on the clock. Serialized to JSON as-is, so keep the members
/// simple and public.
/// </summary>
/// <remarks>
/// The window's position and size are deliberately not part of this snapshot:
/// WinUIEx's <c>WindowManager.PersistenceId</c> persists those for us.
/// </remarks>
public sealed class WindowSettings
{
    /// <summary>Whether the window should stay above other windows.</summary>
    public bool IsAlwaysOnTop { get; set; }

    /// <summary>The elapsed time expressed in <see cref="TimeSpan"/> ticks (100 ns).</summary>
    public long ElapsedTicks { get; set; }
}
