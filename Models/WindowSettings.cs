namespace StopwatchWidget.Models;

/// <summary>
/// The persisted application state: whether the widget floats above other windows,
/// how much time was on the clock, and how it is dressed. Serialized to JSON as-is,
/// so keep the members simple and public.
/// </summary>
/// <remarks>
/// The window's position and size are deliberately not part of this snapshot:
/// WinUIEx's <c>WindowManager.PersistenceId</c> persists those for us.
/// </remarks>
public sealed class WindowSettings
{
    /// <summary>Density used until the user moves the slider in the app menu.</summary>
    public const double DefaultBackgroundOpacity = 0.6;

    /// <summary>Whether the window should stay above other windows.</summary>
    public bool IsAlwaysOnTop { get; set; }

    /// <summary>The elapsed time expressed in <see cref="TimeSpan"/> ticks (100 ns).</summary>
    public long ElapsedTicks { get; set; }

    /// <summary>
    /// How dense the widget's panel is, from 0 (only the acrylic material) to 1 (a solid
    /// panel). Only the background is affected; the clock stays fully opaque.
    /// </summary>
    /// <remarks>
    /// The initializer also covers settings files written before this member existed.
    /// </remarks>
    public double BackgroundOpacity { get; set; } = DefaultBackgroundOpacity;

    /// <summary>
    /// Whether the command buttons are tucked away so that only the clock is on screen.
    /// </summary>
    public bool IsCommandsCollapsed { get; set; }
}
