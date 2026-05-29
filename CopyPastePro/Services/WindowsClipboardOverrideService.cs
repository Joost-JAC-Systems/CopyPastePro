using System.Windows.Threading;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace CopyPastePro.Services;

/// <summary>
/// Requires Windows Win+V clipboard history to stay off while CopyPaste Pro runs.
/// Re-disables it if the user or another app turns it on again.
/// </summary>
public sealed class WindowsClipboardOverrideService : IDisposable
{
  private const string ClipboardRegPath = @"Software\Microsoft\Clipboard";
  private const string EnableHistoryValueName = "EnableClipboardHistory";
  private static readonly TimeSpan BasePollInterval = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan AlertCooldown = TimeSpan.FromSeconds(45);

  private readonly AppSettings _settings;
  private ResourceThrottleService? _throttle;
  private DispatcherTimer? _timer;
  private DateTime _lastAlertUtc = DateTime.MinValue;
  private Action? _onReEnabledByUser;

  public bool IsManagingWindowsHistory => _settings.WindowsClipboardHistoryDisabledByApp;

  public WindowsClipboardOverrideService(AppSettings settings) => _settings = settings;

  /// <summary>Forces clipboard history off at startup and marks the app as the manager.</summary>
  public bool ApplyAtStartup()
  {
    var wasEnabled = IsWindowsHistoryEnabledInRegistry();

    if (wasEnabled)
    {
      try { TryClearWindowsHistoryBeforeDisable(); } catch { }
      if (!_settings.WindowsClipboardHistoryWasEnabled)
        _settings.WindowsClipboardHistoryWasEnabled = true;
    }

    TrySetWindowsHistoryEnabled(false);

    _settings.WindowsClipboardHistoryDisabledByApp = true;
    _settings.Save();
    return wasEnabled;
  }

  /// <summary>Polls registry; if history is turned on, disables it and raises <paramref name="onReEnabledByUser"/>.</summary>
  public void BindThrottle(ResourceThrottleService throttle)
  {
    _throttle = throttle;
    throttle.ThrottleLevelChanged += (_, _) => RestartTimer();
  }

  public void StartEnforcement(Dispatcher dispatcher, Action onReEnabledByUser)
  {
    _onReEnabledByUser = onReEnabledByUser;
    _dispatcher = dispatcher;
    RestartTimer();
    Enforce(suppressAlert: true);
  }

  private Dispatcher? _dispatcher;

  private void RestartTimer()
  {
    if (_dispatcher == null) return;
    _timer?.Stop();
    var interval = _throttle?.GetScaledInterval(BasePollInterval) ?? BasePollInterval;
    _timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => Enforce(), _dispatcher);
    _timer.Start();
  }

  public void StopEnforcement()
  {
    _timer?.Stop();
    _timer = null;
  }

  public void RestoreOnExit()
  {
    StopEnforcement();

    if (!_settings.WindowsClipboardHistoryDisabledByApp)
      return;

    if (_settings.WindowsClipboardHistoryWasEnabled)
      TrySetWindowsHistoryEnabled(true);

    _settings.WindowsClipboardHistoryDisabledByApp = false;
    _settings.WindowsClipboardHistoryWasEnabled = false;
    _settings.Save();
  }

  public void Dispose() => StopEnforcement();

  private void Enforce(bool suppressAlert = false)
  {
    if (!IsWindowsHistoryEnabledInRegistry())
      return;

    try { TryClearWindowsHistoryBeforeDisable(); } catch { }
    TrySetWindowsHistoryEnabled(false);

    if (suppressAlert)
      return;

    if ((DateTime.UtcNow - _lastAlertUtc) < AlertCooldown)
      return;

    _lastAlertUtc = DateTime.UtcNow;
    _onReEnabledByUser?.Invoke();
  }

  public static bool IsWindowsHistoryEnabledInRegistry()
  {
    try
    {
      using var key = Registry.CurrentUser.OpenSubKey(ClipboardRegPath, writable: false);
      if (key?.GetValue(EnableHistoryValueName) is int v)
        return v != 0;
      if (key?.GetValue(EnableHistoryValueName) is long l)
        return l != 0;
    }
    catch { }
    return false;
  }

  private static bool TrySetWindowsHistoryEnabled(bool enabled)
  {
    try
    {
      using var key = Registry.CurrentUser.CreateSubKey(ClipboardRegPath, writable: true);
      key?.SetValue(EnableHistoryValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static void TryClearWindowsHistoryBeforeDisable()
  {
    try
    {
      if (WinClipboard.IsHistoryEnabled())
        WinClipboard.ClearHistory();
    }
    catch { }
  }
}
