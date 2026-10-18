using System.ComponentModel;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StopwatchWidget.Models;
using StopwatchWidget.Services;
using StopwatchWidget.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinUIEx;
using WinRT;
using WinRT.Interop;

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
    /// <summary>
    /// Size used on the very first run, in logical pixels. The window API works in physical
    /// pixels, so the current scale factor is applied when the size is used.
    /// </summary>
    private static readonly Size DefaultSize = new(340, 230);

    /// <summary>Chevron shown while the command buttons can be revealed.</summary>
    private const string ExpandGlyph = "\uE70D";

    /// <summary>Chevron shown while the command buttons can be tucked away.</summary>
    private const string CollapseGlyph = "\uE70E";

    /// <summary>
    /// Lowest tint the slider can reach. A fully clear material would leave the clock floating
    /// over the desktop with nothing behind it, which is neither readable nor what a slider
    /// labelled "background density" should offer.
    /// </summary>
    private const double MinimumTintOpacity = 0.3;

    /// <summary>
    /// Lowest opacity of the material's luminosity layer, which is what keeps the clock
    /// readable against whatever happens to be on the desktop.
    /// </summary>
    private const double MinimumLuminosityOpacity = 0.4;

    private readonly WindowManager _windowManager;
    private readonly SystemStateService _systemStateService;
    private readonly SettingsService _settingsService = new();
    private readonly IntPtr _windowHandle;

    /// <summary>
    /// Painted over the material to approximate it when the system has no acrylic support.
    /// When acrylic is available the material itself carries the density and this stays fully
    /// transparent, existing only so the widget keeps a background to hit-test against.
    /// </summary>
    private readonly SolidColorBrush _backgroundBrush = new();

    /// <summary>
    /// Drives the window's acrylic material. Unlike <c>DesktopAcrylicBackdrop</c>, this
    /// controller exposes the material's own opacity, which is what lets the slider take the
    /// background from "just the blurred desktop" to "a solid panel".
    /// </summary>
    private DesktopAcrylicController? _acrylicController;

    private SystemBackdropConfiguration? _backdropConfiguration;

    /// <summary>Guards the one-off sizing that happens on the first layout pass.</summary>
    private bool _isInitialLayoutApplied;

    /// <summary>
    /// Whether a settings file was found. A first run has no window size worth keeping, so the
    /// window is matched to its content instead of only being grown to fit it.
    /// </summary>
    private bool _hasSavedSettings;

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

        // Logical pixels: the window API wants physical ones.
        var initialScale = GetWindowScale();
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(DefaultSize.Width * initialScale),
            (int)Math.Ceiling(DefaultSize.Height * initialScale)));

        _windowHandle = WindowNative.GetWindowHandle(this);

        // Presenter behaviour: the widget can be resized and minimized, but not maximized.
        _windowManager.IsResizable = true;
        _windowManager.IsMinimizable = true;
        _windowManager.IsMaximizable = false;

        // Window is not a FrameworkElement and has no DataContext of its own,
        // so the bindings resolve against the root content element instead.
        RootLayout.DataContext = ViewModel;
        RootLayout.Background = _backgroundBrush;

        // Take over the window background with the acrylic material before the first frame is
        // drawn, so there is nothing to flicker between.
        SetUpBackdropMaterial();

        // The flyout is hosted in its own popup, outside the window's visual tree, so it
        // does not inherit that data context and has to be wired up explicitly.
        SettingsPanel.DataContext = ViewModel;

        // Bring back the previous session, if one was saved. This runs before anything
        // subscribes to the view model on purpose: resizing the window while it is still
        // being constructed is what used to take the whole app down at startup.
        var settings = _settingsService.Load();
        _hasSavedSettings = settings is not null;

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

        // The starting state is in place, so apply it to the window and only then start
        // following the view model.
        ApplyAlwaysOnTop();
        UpdateBackgroundAppearance();
        ApplyCommandsState(resizeWindow: false);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        RootLayout.Loaded += OnRootLayoutLoaded;
        RootLayout.ActualThemeChanged += OnRootLayoutActualThemeChanged;

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
        _acrylicController?.Dispose();
    }

    /// <summary>
    /// Raises the widget to the foreground, restoring it first if the user had minimized
    /// it. Used when a later launch hands its activation over to this instance.
    /// </summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();

        // Activate() alone doesn't always win the foreground against the process that
        // just launched us, so ask Windows for it explicitly.
        this.SetForegroundWindow();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fits the window to its content once the layout exists. The very first run has no saved
    /// geometry, so this is what turns the default size into whatever the layout needs.
    /// </summary>
    private void OnRootLayoutLoaded(object sender, RoutedEventArgs e)
    {
        // The effective theme is only known once the content is loaded.
        UpdateBackgroundAppearance();

        if (_isInitialLayoutApplied)
        {
            return;
        }

        _isInitialLayoutApplied = true;

        // A saved window keeps the width the user chose for it; the first run has nothing to
        // preserve and gets exactly the width the content asks for.
        EnsureContentWidth(matchContentExactly: !_hasSavedSettings);
        ApplyCommandsState(resizeWindow: true);
    }

    /// <summary>
    /// Repaints the panel when the system switches between the light and dark themes.
    /// </summary>
    private void OnRootLayoutActualThemeChanged(FrameworkElement sender, object args) => UpdateBackgroundAppearance();

    private void OnCommandsToggleClick(object sender, RoutedEventArgs e)
    {
        // The resulting property change drives the layout.
        ViewModel.IsCommandsCollapsed = !ViewModel.IsCommandsCollapsed;
    }

    /// <summary>
    /// Shows or hides the command buttons, and optionally shrinks or grows the window with
    /// them.
    /// </summary>
    private void ApplyCommandsState(bool resizeWindow)
    {
        var collapsed = ViewModel.IsCommandsCollapsed;

        CommandPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        CommandsToggleIcon.Glyph = collapsed ? ExpandGlyph : CollapseGlyph;
        ToolTipService.SetToolTip(CommandsToggleButton, collapsed ? "Показать кнопки" : "Свернуть кнопки");

        if (resizeWindow)
        {
            ResizeWindowToContent();
        }
    }

    /// <summary>
    /// Resizes the window to exactly fit its content. The bottom edge is kept in place, so a
    /// widget parked near the taskbar grows upwards instead of sliding off the screen.
    /// </summary>
    private void ResizeWindowToContent()
    {
        // The desired size of the root element is clamped to the space the window currently
        // gives it, so it can't be used directly. Measuring against an unlimited height
        // returns the height the content really needs — every row is content-sized, so that
        // is exactly what the widget should be.
        var availableWidth = RootLayout.ActualWidth > 0 ? RootLayout.ActualWidth : double.PositiveInfinity;
        RootLayout.Measure(new Size(availableWidth, double.PositiveInfinity));

        var contentHeight = RootLayout.DesiredSize.Height;
        if (contentHeight <= 1)
        {
            // Nothing has been measured yet; the next layout pass will get it right.
            return;
        }

        // Layout works in logical pixels, the window API in physical ones.
        var scale = GetRasterizationScale();

        // AppWindow.Size is the whole window, but the content only gets the client area,
        // so the window frame has to be added on top of the height the content asked for.
        var currentSize = AppWindow.Size;
        var frameHeight = Math.Max(0, currentSize.Height - AppWindow.ClientSize.Height);

        var height = (int)Math.Ceiling(contentHeight * scale) + frameHeight;
        if (height == currentSize.Height)
        {
            return;
        }

        // Keep the bottom edge in place, so a widget parked above the taskbar grows upwards
        // instead of extending off the bottom of the screen. The top edge is never allowed
        // to leave the display: that is where the buttons live.
        var position = AppWindow.Position;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var top = Math.Max(position.Y + currentSize.Height - height, workArea.Y);

        AppWindow.MoveAndResize(new RectInt32(position.X, top, currentSize.Width, height));
    }

    /// <summary>
    /// Gives the window at least the width its content needs. A window narrower than the clock
    /// would clip it, which is easy to end up with because the window API counts in physical
    /// pixels while the layout counts in logical ones.
    /// </summary>
    /// <param name="matchContentExactly">
    /// Used on the first run, where there is no width worth preserving yet.
    /// </param>
    private void EnsureContentWidth(bool matchContentExactly)
    {
        // Measuring against unlimited bounds reports the size the content would like to have.
        RootLayout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var contentWidth = RootLayout.DesiredSize.Width;
        if (contentWidth <= 1)
        {
            return;
        }

        var currentSize = AppWindow.Size;
        var frameWidth = Math.Max(0, currentSize.Width - AppWindow.ClientSize.Width);
        var width = (int)Math.Ceiling(contentWidth * GetRasterizationScale()) + frameWidth;

        if (!matchContentExactly && width <= currentSize.Width)
        {
            return;
        }

        AppWindow.Resize(new SizeInt32(width, currentSize.Height));
    }

    /// <summary>Scale factor between the layout's logical pixels and the window's physical
    /// ones (1.0 on a 100% display, 1.75 on a 175% one).</summary>
    private double GetRasterizationScale()
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 0;
        return scale > 0 ? scale : 1.0;
    }

    /// <summary>
    /// The scale factor of the window's monitor, read straight from the HWND. Needed before the
    /// visual tree exists, where there is no <c>XamlRoot</c> to ask.
    /// </summary>
    private double GetWindowScale()
    {
        var dpi = this.GetDpiForWindow();
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    // ── Appearance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Repaints the widget's background. With acrylic available the slider drives the
    /// material's own tint and luminosity layers, so the widget really does go from "the
    /// blurred desktop shows through" to "a solid panel". The clock sits above it and is never
    /// affected.
    /// </summary>
    private void UpdateBackgroundAppearance()
    {
        var opacity = Math.Clamp(ViewModel.BackgroundOpacity, 0, 1);

        // The clock follows the theme, so the panel moves with the theme to stay readable in
        // both of them. The app asks for the dark theme (see App.xaml), which is also what
        // makes the slider span a range you can actually see.
        var isLight = RootLayout.ActualTheme == ElementTheme.Light;
        var (r, g, b) = isLight
            ? ((byte)255, (byte)255, (byte)255)
            : ((byte)24, (byte)24, (byte)24);

        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.Theme = isLight ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        }

        if (_acrylicController is not null)
        {
            _acrylicController.TintColor = Color.FromArgb(255, r, g, b);
            _acrylicController.TintOpacity = (float)MapToRange(opacity, MinimumTintOpacity);
            _acrylicController.LuminosityOpacity = (float)MapToRange(opacity, MinimumLuminosityOpacity);

            // The material is doing the work, so nothing is painted on top of it.
            _backgroundBrush.Color = Color.FromArgb(0, r, g, b);
            return;
        }

        // No acrylic on this system, so approximate it with a plain translucent panel.
        _backgroundBrush.Color = Color.FromArgb(
            (byte)Math.Round(MapToRange(opacity, MinimumTintOpacity) * 255),
            r,
            g,
            b);
    }

    /// <summary>
    /// Stretches the slider's 0..1 onto <paramref name="minimum"/>..1, so that "as transparent
    /// as possible" still leaves a usable widget instead of an invisible one.
    /// </summary>
    private static double MapToRange(double opacity, double minimum) =>
        minimum + ((1 - minimum) * opacity);

    /// <summary>
    /// Hands the window's background over to an acrylic controller. The controller is kept
    /// alive for the lifetime of the window; without the field it would be collected and the
    /// material would disappear.
    /// </summary>
    private void SetUpBackdropMaterial()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        // The widget should look the same when it doesn't have focus, so input is always
        // reported as active instead of the material dimming whenever the user clicks away.
        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = RootLayout.ActualTheme == ElementTheme.Light
                ? SystemBackdropTheme.Light
                : SystemBackdropTheme.Dark,
        };

        _acrylicController = new DesktopAcrylicController();
        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
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
        ViewModel.IsCommandsCollapsed = settings.IsCommandsCollapsed;

        // Clamped because the file is editable by hand and a wild value would leave the
        // widget invisible or fully opaque.
        ViewModel.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 1);
    }

    private void SaveSettings()
    {
        _settingsService.Save(new WindowSettings
        {
            IsAlwaysOnTop = ViewModel.IsAlwaysOnTop,
            ElapsedTicks = ViewModel.ElapsedTime.Ticks,
            BackgroundOpacity = ViewModel.BackgroundOpacity,
            IsCommandsCollapsed = ViewModel.IsCommandsCollapsed,
        });
    }

    // ── Window wiring ────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StopwatchViewModel.IsAlwaysOnTop):
                ApplyAlwaysOnTop();
                break;

            case nameof(StopwatchViewModel.IsCommandsCollapsed):
                ApplyCommandsState(resizeWindow: true);
                break;

            case nameof(StopwatchViewModel.BackgroundOpacity):
                UpdateBackgroundAppearance();
                break;
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
