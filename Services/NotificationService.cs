using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace StopwatchWidget.Services;

/// <summary>
/// Wraps <see cref="AppNotificationManager"/> so the rest of the app can raise app
/// notifications without touching the Windows App SDK directly.
/// </summary>
/// <remarks>
/// This is the packaged-app setup: notification activation is routed back to this
/// process through the COM server declared in <c>Package.appxmanifest</c>.
/// </remarks>
public sealed class NotificationService : IDisposable
{
    private readonly AppNotificationManager _manager = AppNotificationManager.Default;

    private bool _isRegistered;
    private bool _isDisposed;

    /// <summary>
    /// Raised when the user interacts with one of this app's notifications — both when
    /// the app was already running and when the notification launched it.
    /// </summary>
    public event EventHandler<AppNotificationActivatedEventArgs>? NotificationActivated;

    /// <summary>Whether this machine and session can show app notifications at all.</summary>
    public static bool IsSupported => AppNotificationManager.IsSupported();

    /// <summary>
    /// Registers this process to receive app notifications and activation callbacks.
    /// Call it once at startup, before inspecting the launch activation arguments —
    /// otherwise a tap on a notification can't be recognized as such.
    /// </summary>
    public void Register()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isRegistered)
        {
            return;
        }

        // Attach the handler first: if Register runs without one, Windows starts a
        // second process to service the activation instead of raising the event here.
        _manager.NotificationInvoked += OnNotificationInvoked;
        _manager.Register();
        _isRegistered = true;
    }

    /// <summary>
    /// Shows a notification whose first line is <paramref name="title"/> and whose
    /// second line is <paramref name="message"/>. Requires <see cref="Register"/>.
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(message);

        // The toast schema has no dedicated title field: the first text element is
        // rendered as the heading and the rest as the body.
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .BuildNotification();

        _manager.Show(notification);
    }

    /// <summary>
    /// Unregisters the process. Required before terminating: without it later
    /// notifications may no longer be able to launch the app.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (!_isRegistered)
        {
            return;
        }

        _isRegistered = false;
        _manager.NotificationInvoked -= OnNotificationInvoked;

        try
        {
            _manager.Unregister();
        }
        catch (Exception ex)
        {
            AppLog.Write("Unregistering app notifications failed", ex);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationActivated?.Invoke(this, args);
    }
}
