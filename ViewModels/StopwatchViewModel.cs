using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using StopwatchWidget.Models;

namespace StopwatchWidget.ViewModels;

/// <summary>
/// Drives the stopwatch: keeps the elapsed time, exposes bindable properties and
/// the Start / Pause / Reset commands, and refreshes the UI on a <see cref="DispatcherTimer"/>.
/// </summary>
public partial class StopwatchViewModel : ObservableObject
{
    /// <summary>
    /// How often the displayed time is refreshed. The actual elapsed time is read from
    /// <see cref="System.Diagnostics.Stopwatch"/>, so a slow tick never causes drift.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();

    /// <summary>
    /// Time carried over from earlier runs (or restored from disk). The live counter
    /// can't hold it across a restart, so it lives here and is added to the running
    /// <see cref="_stopwatch"/> whenever the elapsed time is read.
    /// </summary>
    private TimeSpan _accumulatedElapsed;

    /// <summary>
    /// Creates the view model and wires up the UI refresh timer.
    /// </summary>
    public StopwatchViewModel()
    {
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTimerTick;

        BackgroundOpacity = WindowSettings.DefaultBackgroundOpacity;
        FormattedTime = StopwatchState.Format(ElapsedTime);
    }

    /// <summary>
    /// The total time measured so far.
    /// </summary>
    [ObservableProperty]
    public partial TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// The elapsed time rendered as <c>HH:MM:SS</c> for binding in the view.
    /// </summary>
    [ObservableProperty]
    public partial string FormattedTime { get; set; }

    /// <summary>
    /// <see langword="true"/> while the stopwatch is counting.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// <see langword="true"/> when the window should stay above other windows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAlwaysOnTop { get; set; }

    /// <summary>
    /// How dense the widget's background panel is: 0 leaves the acrylic material at its most
    /// see-through, 1 turns it into a solid panel. The clock is never affected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundOpacityLabel))]
    public partial double BackgroundOpacity { get; set; }

    /// <summary>
    /// The background density rendered for the app menu, e.g. <c>60%</c>.
    /// </summary>
    public string BackgroundOpacityLabel => $"{BackgroundOpacity * 100:0}%";

    /// <summary>
    /// <see langword="true"/> when the command buttons are hidden, leaving only the clock
    /// on screen. The window itself shrinks to match.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCommandsCollapsed { get; set; }

    /// <summary>
    /// Starts (or resumes) the stopwatch. Disabled while it is already running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    public void Start() => Resume();

    /// <summary>
    /// Pauses the stopwatch, keeping the elapsed time. Disabled while it is not running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _stopwatch.Stop();
        _accumulatedElapsed += _stopwatch.Elapsed;
        _stopwatch.Reset();
        _timer.Stop();
        IsRunning = false;

        // Make sure the last fraction of a second is reflected on screen.
        UpdateElapsedTime();
    }

    /// <summary>
    /// Stops the stopwatch and resets the elapsed time back to zero.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    public void Reset()
    {
        _stopwatch.Reset();
        _accumulatedElapsed = TimeSpan.Zero;
        _timer.Stop();
        IsRunning = false;

        ElapsedTime = TimeSpan.Zero;
        FormattedTime = StopwatchState.Format(TimeSpan.Zero);
    }

    /// <summary>
    /// Resumes counting from the current elapsed time without clearing it.
    /// Safe to call when the stopwatch is already running (it is a no-op).
    /// </summary>
    public void Resume()
    {
        if (IsRunning)
        {
            return;
        }

        _stopwatch.Start();
        _timer.Start();
        IsRunning = true;
    }

    /// <summary>
    /// Returns a snapshot of the current state, useful for persistence or logging.
    /// </summary>
    public StopwatchState GetState() => new()
    {
        ElapsedTime = ElapsedTime,
        IsRunning = IsRunning,
        IsAlwaysOnTop = IsAlwaysOnTop,
    };

    /// <summary>
    /// Restores a previously saved elapsed time without starting the clock.
    /// Pressing Start afterwards keeps counting on top of the restored value.
    /// </summary>
    public void RestoreElapsedTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        _stopwatch.Reset();
        _accumulatedElapsed = elapsed;

        ElapsedTime = elapsed;
        FormattedTime = StopwatchState.Format(elapsed);

        // ElapsedTime isn't watched by the generated command notifications, so a
        // restored value wouldn't otherwise enable the Reset button.
        ResetCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsRunning;

    private bool CanPause() => IsRunning;

    private bool CanReset() => IsRunning || ElapsedTime > TimeSpan.Zero;

    private void OnTimerTick(object? sender, object e) => UpdateElapsedTime();

    private void UpdateElapsedTime()
    {
        ElapsedTime = _accumulatedElapsed + _stopwatch.Elapsed;
        FormattedTime = StopwatchState.Format(ElapsedTime);
    }
}
