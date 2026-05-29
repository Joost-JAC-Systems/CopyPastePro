using Microsoft.Win32;

namespace CopyPastePro.Services;

public sealed class SessionPrivacyMonitor : IDisposable
{
  private readonly PrivacyService _privacy;
  private readonly ClipboardHistoryRepository _repository;
  private readonly AppSettings _settings;

  public SessionPrivacyMonitor(PrivacyService privacy, ClipboardHistoryRepository repository, AppSettings settings)
  {
    _privacy = privacy;
    _repository = repository;
    _settings = settings;
    SystemEvents.SessionSwitch += OnSessionSwitch;
  }

  private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
  {
    switch (e.Reason)
    {
      case SessionSwitchReason.SessionLock:
      case SessionSwitchReason.ConsoleDisconnect:
      case SessionSwitchReason.RemoteDisconnect:
        _privacy.OnSessionLock(_repository);
        break;
      case SessionSwitchReason.SessionUnlock:
      case SessionSwitchReason.ConsoleConnect:
      case SessionSwitchReason.RemoteConnect:
        _privacy.OnSessionUnlock();
        break;
      case SessionSwitchReason.SessionLogoff:
        if (_settings.AutoClearOnLogout)
          _repository.ClearAll(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
        break;
    }
  }

  public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
