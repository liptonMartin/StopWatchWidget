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
    /// Creates the view model and wires up the UI refresh timer.
    /// </summary>
    public StopwatchViewModel()
    {
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTimerTick;

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

    private bool CanStart() => !IsRunning;

    private bool CanPause() => IsRunning;

    private bool CanReset() => IsRunning || ElapsedTime > TimeSpan.Zero;

    private void OnTimerTick(object? sender, object e) => UpdateElapsedTime();

    private void UpdateElapsedTime()
    {
        ElapsedTime = _stopwatch.Elapsed;
        FormattedTime = StopwatchState.Format(ElapsedTime);
    }
}
