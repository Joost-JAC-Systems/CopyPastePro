namespace CopyPastePro.Services;

public sealed class SensitiveRetentionService : IDisposable
{
  private readonly AppSettings _settings;
  private readonly ClipboardHistoryRepository _repository;
  private readonly PrivacyService _privacy;
  private System.Timers.Timer? _timer;

  public SensitiveRetentionService(AppSettings settings, ClipboardHistoryRepository repository, PrivacyService privacy)
  {
    _settings = settings;
    _repository = repository;
    _privacy = privacy;
    ResetTimer();
  }

  public void ResetTimer()
  {
    _timer?.Stop();
    _timer?.Dispose();
    if (_settings.AutoDeleteSensitiveAfterMinutes <= 0) return;

    _timer = new System.Timers.Timer(TimeSpan.FromMinutes(Math.Max(1, _settings.AutoDeleteSensitiveAfterMinutes)).TotalMilliseconds)
    {
      AutoReset = true
    };
    _timer.Elapsed += (_, _) => _privacy.PurgeSensitive(_repository);
    _timer.Start();
  }

  public void Dispose() => _timer?.Dispose();
}
