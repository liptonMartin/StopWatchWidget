using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using StopwatchWidget.Models;
using StopwatchWidget.Services;
using StopwatchWidget.ViewModels;
using WinUIEx;
using WinRT.Interop;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace StopwatchWidget;

/// <summary>
/// The application window. It hosts the stopwatch UI, binds it to a
/// <see cref="StopwatchViewModel"/>, drives the window presenter through WinUIEx's
/// <see cref="WindowManager"/>, and persists its own state via <see cref="SettingsService"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Size used on the very first run, before any settings exist.</summary>
    private static readonly SizeInt32 DefaultSize = new(360, 250);

    private readonly WindowManager _windowManager;
    private readonly SystemStateService _systemStateService;
    private readonly SettingsService _settingsService = new();
    private readonly IntPtr _windowHandle;

    /// <summary>
    /// The view model backing this window's UI.
    /// </summary>
    public StopwatchViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new StopwatchViewModel();

        InitializeComponent();

        // WindowManager wraps the window's OverlappedPresenter and exposes
        // convenient properties for size, resize/minimize/maximize and z-order.
        _windowManager = WindowManager.Get(this);

        // Let WinUIEx handle the window's position and size across runs. It restores
        // them on activation and skips restoring when the monitor layout changed, which
        // is far more robust than poking the placement APIs by hand. Must be set before
        // the window activates.
        _windowManager.PersistenceId = "StopwatchWidget.MainWindow";

        // Let the content fill the whole window, including the title bar area,
        // then use the top strip as the drag region.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(DefaultSize);

        _windowHandle = WindowNative.GetWindowHandle(this);

        // Presenter behaviour: the widget can be resized and minimized, but not maximized.
        _windowManager.IsResizable = true;
        _windowManager.IsMinimizable = true;
        _windowManager.IsMaximizable = false;

        // Window is not a FrameworkElement and has no DataContext of its own,
        // so the bindings resolve against the root content element instead.
        RootLayout.DataContext = ViewModel;

        // Keep the real window in sync with the toggle. Subscribed before restoring
        // settings so a saved IsAlwaysOnTop is pushed to the window as well.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Bring back the previous session, if one was saved.
        var settings = _settingsService.Load();
        if (settings is not null)
        {
            try
            {
                ApplySettings(settings);
            }
            catch (Exception ex)
            {
                // A bad saved state must never stop the widget from opening.
                AppLog.Write("Applying saved settings failed", ex);
            }
        }

        // Covers the very first run, when there are no settings to apply.
        ApplyAlwaysOnTop();

        // Pause the stopwatch while the session is locked or the display is off,
        // and resume it once the user is back.
        _systemStateService = new SystemStateService(_windowHandle, DispatcherQueue, ViewModel);

        // Save on close. Closing runs while the window still exists, so this is a safe
        // point to write the state and release the native hooks.
        AppWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        SaveSettings();
        _systemStateService.Dispose();
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Restores the saved application state onto the view model. Window geometry is
    /// handled by WinUIEx's persistence, not here.
    /// </summary>
    private void ApplySettings(WindowSettings settings)
    {
        ViewModel.RestoreElapsedTime(TimeSpan.FromTicks(settings.ElapsedTicks));
        ViewModel.IsAlwaysOnTop = settings.IsAlwaysOnTop;
    }

    private void SaveSettings()
    {
        _settingsService.Save(new WindowSettings
        {
            IsAlwaysOnTop = ViewModel.IsAlwaysOnTop,
            ElapsedTicks = ViewModel.ElapsedTime.Ticks,
        });
    }

    // ── Window wiring ────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StopwatchViewModel.IsAlwaysOnTop))
        {
            ApplyAlwaysOnTop();
        }
    }

    /// <summary>
    /// Pushes <see cref="StopwatchViewModel.IsAlwaysOnTop"/> to the window via
    /// <see cref="WindowManager"/>.
    /// </summary>
    private void ApplyAlwaysOnTop()
    {
        _windowManager.IsAlwaysOnTop = ViewModel.IsAlwaysOnTop;
    }
}
