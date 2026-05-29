using System.IO;

namespace CopyPastePro.Services;

public sealed class AutoBackupService : IDisposable
{
  private readonly AppSettings _settings;
  private System.Timers.Timer? _timer;

  public AutoBackupService(AppSettings settings) => _settings = settings;

  public void Start()
  {
    if (!_settings.AutoBackupEnabled) return;
    var interval = Math.Max(5, _settings.AutoBackupIntervalMinutes);
    _timer = new System.Timers.Timer(interval * 60_000) { AutoReset = true };
    _timer.Elapsed += (_, _) => RunBackup();
    _timer.Start();
    if (_settings.AutoBackupOnStartup) RunBackup();
  }

  /// <summary>Creates a new timestamped backup file (never overwrites an existing backup).</summary>
  public string RunBackup()
  {
    var srcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyPastePro");
    var db = Path.Combine(srcDir, "history.db");
    if (!File.Exists(db)) return "";

    var backupDir = Path.Combine(srcDir, "backups");
    Directory.CreateDirectory(backupDir);
    var name = $"history-{DateTime.Now:yyyyMMdd-HHmmss}.db";
    var dest = Path.Combine(backupDir, name);
    File.Copy(db, dest, overwrite: false);

    TrimOldBackups(backupDir);
    return dest;
  }

  /// <summary>Optional snapshot before a wipe (for manual recovery only — app never auto-restores).</summary>
  public string? CreatePreWipeSnapshot()
  {
    if (!_settings.AutoBackupEnabled) return null;
    var srcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyPastePro");
    var db = Path.Combine(srcDir, "history.db");
    if (!File.Exists(db)) return null;

    var backupDir = Path.Combine(srcDir, "backups");
    Directory.CreateDirectory(backupDir);
    var name = $"history-before-wipe-{DateTime.Now:yyyyMMdd-HHmmss}.db";
    var dest = Path.Combine(backupDir, name);
    try
    {
      File.Copy(db, dest, overwrite: false);
      TrimOldBackups(backupDir);
      return dest;
    }
    catch
    {
      return null;
    }
  }

  /// <summary>After a clear/wipe, always write a fresh backup of the current (empty) database.</summary>
  public string CreatePostWipeBackup()
  {
    var path = RunBackup();
    return path;
  }

  private void TrimOldBackups(string backupDir)
  {
    var files = Directory.GetFiles(backupDir, "history-*.db")
        .OrderByDescending(File.GetCreationTimeUtc)
        .Skip(Math.Max(1, _settings.AutoBackupKeepCount))
        .ToList();
    foreach (var f in files)
    {
      try { File.Delete(f); } catch { }
    }
  }

  public void Dispose() => _timer?.Dispose();
}
