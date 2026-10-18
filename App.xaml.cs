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
    private Window? _window;

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
            // Register before anything reads the activation arguments: that's what lets
            // a tap on a notification be recognized as an app-notification activation.
            Notifications.Register();
            Notifications.NotificationActivated += OnNotificationActivated;

            _window = new MainWindow();
            _window.Closed += OnWindowClosed;
            _window.Activate();

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
    /// Handles a notification the user interacted with, bringing the widget forward. Can
    /// be raised on a non-UI thread, so the window work is marshalled to the dispatcher.
    /// </summary>
    private void OnNotificationActivated(object? sender, AppNotificationActivatedEventArgs args)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() => window.Activate());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
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
