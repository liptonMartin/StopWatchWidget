using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using StopwatchWidget.ViewModels;

namespace StopwatchWidget.Services;

/// <summary>
/// Watches OS-level system state so the stopwatch stops counting time while the
/// user is away, then continues once they're back.
/// <para>
/// Two independent signals are combined, both delivered to this window's message
/// queue and handled in the subclassed window procedure:
/// </para>
/// <list type="bullet">
///   <item>
///     Session lock / unlock (<c>WM_WTSSESSION_CHANGE</c>) registered with
///     <c>WTSRegisterSessionNotification</c>.
///   </item>
///   <item>
///     Console display on / off (<c>WM_POWERBROADCAST</c>) registered with
///     <c>RegisterPowerSettingNotification</c> for <c>GUID_CONSOLE_DISPLAY_STATE</c>.
///   </item>
/// </list>
/// <para>
/// Both signals are marshalled onto the UI thread through a
/// <see cref="DispatcherQueue"/> before touching the view model.
/// </para>
/// </summary>
public sealed class SystemStateService : IDisposable
{
    // ── Window messages and their payload values ─────────────────────────────

    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_POWERBROADCAST = 0x0218;

    private const long PBT_POWERSETTINGCHANGE = 0x8013;

    // wParam values for WM_WTSSESSION_CHANGE.
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;

    private const int GWLP_WNDPROC = -4;

    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x0000;

    /// <summary>Only get notifications for the session this process runs in.</summary>
    private const uint NOTIFY_FOR_THIS_SESSION = 0;

    // Values carried in the GUID_CONSOLE_DISPLAY_STATE payload.
    private const uint ConsoleDisplayOff = 0;
    private const uint ConsoleDisplayOn = 1;

    /// <summary>GUID_CONSOLE_DISPLAY_STATE - the console display on/off power setting.</summary>
    private static readonly Guid GuidConsoleDisplayState =
        new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private readonly IntPtr _windowHandle;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly StopwatchViewModel _viewModel;

    // Rooted so the GC doesn't collect the native callback while it's installed.
    private readonly WndProcDelegate _wndProc;

    private IntPtr _previousWndProc;
    private IntPtr _powerNotification;
    private bool _sessionNotificationRegistered;

    /// <summary>
    /// <see langword="true"/> when this service was the one that paused the stopwatch,
    /// so an unlock / display-on only resumes what we stopped (never starts a fresh run).
    /// </summary>
    private bool _suspendedBySystem;

    private bool _disposed;

    /// <summary>
    /// Installs the hooks. Must be created on the UI thread once the window exists.
    /// </summary>
    /// <param name="windowHandle">The top-level HWND of the application window.</param>
    /// <param name="dispatcherQueue">The UI thread queue used to touch the view model safely.</param>
    /// <param name="viewModel">The stopwatch to pause and resume.</param>
    public SystemStateService(IntPtr windowHandle, DispatcherQueue dispatcherQueue, StopwatchViewModel viewModel)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        _wndProc = WindowProc;

        // The subclassed procedure must be in place before anything that relies on
        // receiving window messages is registered.
        HookWindowProcedure();
        RegisterSessionNotification();
        RegisterDisplayStateNotification();
    }

    /// <summary>
    /// Unsubscribes from the system notifications and restores the original window procedure.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_sessionNotificationRegistered)
        {
            WTSUnRegisterSessionNotification(_windowHandle);
            _sessionNotificationRegistered = false;
        }

        if (_powerNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotification);
            _powerNotification = IntPtr.Zero;
        }

        if (_previousWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _previousWndProc);
            _previousWndProc = IntPtr.Zero;
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────

    private void HookWindowProcedure()
    {
        var newProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _previousWndProc = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, newProc);
    }

    private void RegisterSessionNotification()
    {
        _sessionNotificationRegistered =
            WTSRegisterSessionNotification(_windowHandle, NOTIFY_FOR_THIS_SESSION);
    }

    private void RegisterDisplayStateNotification()
    {
        var guid = GuidConsoleDisplayState;
        _powerNotification = RegisterPowerSettingNotification(
            _windowHandle,
            ref guid,
            DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    // ── Window procedure ─────────────────────────────────────────────────────

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_WTSSESSION_CHANGE:
                OnSessionChange(wParam.ToInt32());
                break;

            case WM_POWERBROADCAST when wParam.ToInt64() == PBT_POWERSETTINGCHANGE:
                OnPowerSettingChange(lParam);
                break;
        }

        // Always hand the message back to WinUI's own procedure.
        return CallWindowProcW(_previousWndProc, hWnd, msg, wParam, lParam);
    }

    // ── Session lock / unlock ────────────────────────────────────────────────

    private void OnSessionChange(int reason)
    {
        switch (reason)
        {
            case WTS_SESSION_LOCK:
                RequestSuspend();
                break;

            case WTS_SESSION_UNLOCK:
                RequestResume();
                break;
        }
    }

    // ── Console display on / off ─────────────────────────────────────────────

    private void OnPowerSettingChange(IntPtr lParam)
    {
        var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);

        if (setting.PowerSetting != GuidConsoleDisplayState)
        {
            return;
        }

        switch (setting.Data)
        {
            case ConsoleDisplayOff:
                RequestSuspend();
                break;

            case ConsoleDisplayOn:
                RequestResume();
                break;

            // ConsoleDisplayDimmed (2): the screen is still on, so keep counting.
        }
    }

    // ── Suspend / resume marshalling ─────────────────────────────────────────

    private void RequestSuspend() => _dispatcherQueue.TryEnqueue(() =>
    {
        // Nothing to do if the stopwatch isn't counting, and don't overwrite the
        // flag on the second of two suspend signals (both lock and display-off can fire).
        if (!_viewModel.IsRunning)
        {
            return;
        }

        _suspendedBySystem = true;
        _viewModel.Pause();
    });

    private void RequestResume() => _dispatcherQueue.TryEnqueue(() =>
    {
        // Only continue something we paused ourselves.
        if (!_suspendedBySystem)
        {
            return;
        }

        _suspendedBySystem = false;
        _viewModel.Resume();
    });

    // ── Interop ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrNative(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLongNative(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    /// <summary>
    /// <c>SetWindowLongPtr</c> only exists on 64-bit Windows, so fall back to the
    /// 32-bit <c>SetWindowLong</c> when running as a 32-bit process.
    /// </summary>
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtrNative(hWnd, nIndex, dwNewLong)
            : SetWindowLongNative(hWnd, nIndex, dwNewLong);
}
