using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CopyPastePro.Services;

/// <summary>
/// Detects user idle, sleep, and optional media playback; scales background work intervals smoothly.
/// </summary>
public sealed class ResourceThrottleService : IDisposable
{
  private readonly AppSettings _settings;
  private System.Timers.Timer? _timer;
  private DateTime _lastInputUtc = DateTime.UtcNow;
  private bool _isSuspended;
  private bool _isMediaPlaying;
  private double _level; // 0 = active, 1 = fully throttled

  public ResourceThrottleService(AppSettings settings)
  {
    _settings = settings;
    SystemEvents.PowerModeChanged += OnPowerModeChanged;
    SystemEvents.SessionSwitch += OnSessionSwitch;
    _timer = new System.Timers.Timer(4000) { AutoReset = true };
    _timer.Elapsed += (_, _) => Tick();
    _timer.Start();
    Tick();
  }

  public bool IsLowPower => _level >= 0.85;
  public bool IsFormattingAllowed => !IsLowPower || !_settings.PauseFormattingWhenIdle;

  /// <summary>Multiplier for timer intervals (1 = normal, higher = slower).</summary>
  public double IntervalMultiplier => 1.0 + _level * 14.0;

  public TimeSpan GetScaledInterval(TimeSpan normal) =>
      TimeSpan.FromMilliseconds(Math.Max(normal.TotalMilliseconds * IntervalMultiplier, normal.TotalMilliseconds));

  public event EventHandler? ThrottleLevelChanged;

  private void Tick()
  {
    if (!_settings.PowerSavingEnabled)
    {
      SetLevel(0);
      return;
    }

    var idleMs = GetIdleMilliseconds();
    var idleLimit = TimeSpan.FromMinutes(Math.Max(1, _settings.IdleMinutesBeforeThrottle));
    var idleRatio = Math.Clamp(idleMs / idleLimit.TotalMilliseconds, 0, 1);

    if (_isSuspended)
      idleRatio = 1;

    if (_settings.ThrottleWhenMediaPlaying && _isMediaPlaying)
      idleRatio = Math.Min(idleRatio, 0.35);

    if (_settings.ThrottleWhenMediaPlaying && !_isMediaPlaying)
      _ = RefreshMediaStateAsync();

    // Smooth transitions so timers do not jump abruptly.
    var target = idleRatio;
    var next = _level + (target - _level) * 0.35;
    SetLevel(next);
  }

  private void SetLevel(double level)
  {
    level = Math.Clamp(level, 0, 1);
    if (Math.Abs(level - _level) < 0.02) return;
    _level = level;
    ThrottleLevelChanged?.Invoke(this, EventArgs.Empty);
  }

  private double GetIdleMilliseconds()
  {
    if (GetLastInputInfo(out var info))
    {
      var idle = (uint)Environment.TickCount - info.dwTime;
      _lastInputUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(idle);
      return idle;
    }
    return (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds;
  }

  private async Task RefreshMediaStateAsync()
  {
    try
    {
      var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
      _isMediaPlaying = manager.GetSessions()
          .Any(s => s.GetPlaybackInfo().PlaybackStatus
              == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }
    catch
    {
      _isMediaPlaying = false;
    }
  }

  private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) =>
      _isSuspended = e.Mode == PowerModes.Suspend;

  private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) =>
      _isSuspended = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect;

  public void Dispose()
  {
    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    SystemEvents.SessionSwitch -= OnSessionSwitch;
    _timer?.Dispose();
  }

  [DllImport("user32.dll")]
  private static extern bool GetLastInputInfo(out LastInputInfo info);

  [StructLayout(LayoutKind.Sequential)]
  private struct LastInputInfo
  {
    public uint cbSize;
    public uint dwTime;
  }
}
