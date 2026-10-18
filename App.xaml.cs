using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using StopwatchWidget.Services;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace StopwatchWidget;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Identity of the single running copy of the widget. Every launch of this app resolves
    /// to the same <see cref="AppInstance"/> through this key, so a second launch can find
    /// the copy that is already running instead of starting its own.
    /// </summary>
    private const string SingleInstanceKey = "StopwatchWidget";

    private MainWindow? _window;
    private AppInstance? _instance;

    /// <summary>
    /// App-level notifications, reachable from anywhere as
    /// <c>((App)Application.Current).Notifications</c>.
    /// </summary>
    public NotificationService Notifications { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // A packaged WinUI app has no console and shows no dialog: an exception during
        // startup would just kill the process silently. Route unhandled exceptions to
        // AppLog so the real cause stays recoverable.
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

            if (!instance.IsCurrent)
            {
                // The widget is already running. Hand this launch's activation over to that
                // copy - it is the running instance that brings itself to the front - and
                // quit this process without creating a window of its own.
                AppLog.Write("Another instance is already running; redirecting this launch to it.");
                RedirectActivationTo(instance, AppInstance.GetCurrent().GetActivatedEventArgs());
                Exit();
                return;
            }

            _instance = instance;

            // Raised in this process when a later launch redirects its activation here.
            _instance.Activated += OnInstanceActivated;

            // Only the single instance registers for notifications: registering from a
            // process that is about to exit would fight over the same notification identity.
            // It must also happen before the activation arguments are read below.
            Notifications.Register();
            Notifications.NotificationActivated += OnNotificationActivated;

            _window = new MainWindow();
            _window.Closed += OnWindowClosed;
            _window.Activate();

            // A launch caused by interacting with a notification arrives as activation
            // arguments rather than as a plain launch.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification &&
                activation.Data is AppNotificationActivatedEventArgs notificationArgs)
            {
                OnNotificationActivated(this, notificationArgs);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Launching the main window failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Hands this launch's activation over to the instance that is already running.
    /// </summary>
    private static void RedirectActivationTo(AppInstance instance, AppActivationArguments activationArgs)
    {
        // RedirectActivationToAsync only completes once the target instance has processed
        // the activation, so it must not be awaited on this app's UI thread: blocking the
        // dispatcher here can deadlock. Wait for it on a pool thread instead.
        using var redirected = new ManualResetEventSlim(false);

        _ = Task.Run(async () =>
        {
            try
            {
                await instance.RedirectActivationToAsync(activationArgs);
            }
            catch (Exception ex)
            {
                AppLog.Write("Redirecting the activation to the running instance failed", ex);
            }
            finally
            {
                redirected.Set();
            }
        });

        redirected.Wait();
    }

    /// <summary>
    /// Raised in the running instance when another launch redirected its activation here,
    /// which means the user asked for the widget that is already on screen.
    /// </summary>
    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        AppLog.Write($"Activation of a second launch was redirected here ({args.Kind}).");
        BringWindowToFront();
    }

    /// <summary>
    /// Handles a notification the user interacted with.
    /// </summary>
    private void OnNotificationActivated(object? sender, AppNotificationActivatedEventArgs args)
    {
        BringWindowToFront();
    }

    /// <summary>
    /// Brings the widget to the front. Both callers above can run on a non-UI thread, so
    /// the window work is marshalled to the dispatcher.
    /// </summary>
    private void BringWindowToFront()
    {
        var window = _window;

        if (window is null)
        {
            AppLog.Write("The main window is not up yet; dropping the request to raise it.");
            return;
        }

        window.DispatcherQueue.TryEnqueue(() => _window?.BringToFront());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_instance is not null)
        {
            _instance.Activated -= OnInstanceActivated;
        }

        Notifications.NotificationActivated -= OnNotificationActivated;
        Notifications.Dispose();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Write("Unhandled XAML exception", e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        AppLog.Write("Unhandled AppDomain exception", e.ExceptionObject as Exception);
    }
}
