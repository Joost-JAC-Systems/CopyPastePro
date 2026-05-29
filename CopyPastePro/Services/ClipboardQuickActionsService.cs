using System.Windows;
using System.Windows.Threading;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

/// <summary>Common clipboard and history actions (clear, copy latest, etc.).</summary>
public sealed class ClipboardQuickActionsService
{
  private readonly AppSettings _settings;
  private readonly ClipboardHistoryRepository _repository;
  private readonly ClipboardCaptureService _capture;
  private readonly ClipboardPasteService _paste;
  private readonly PrivacyService _privacy;
  private readonly Action? _refreshUi;
  private ClipboardMonitor? _monitor;
  private AutoBackupService? _backup;

  public ClipboardQuickActionsService(
      AppSettings settings,
      ClipboardHistoryRepository repository,
      ClipboardCaptureService capture,
      ClipboardPasteService paste,
      PrivacyService privacy,
      Action? refreshUi = null)
  {
    _settings = settings;
    _repository = repository;
    _capture = capture;
    _paste = paste;
    _privacy = privacy;
    _refreshUi = refreshUi;
  }

  public void ConfigureWipeDependencies(ClipboardMonitor monitor, AutoBackupService backup)
  {
    _monitor = monitor;
    _backup = backup;
  }

  public void ClearSystemClipboard(Window? owner = null) => RunOnUi(owner, () =>
  {
    _monitor?.SetPaused(true);
    SystemClipboardHelper.ClearResult cleared;
    using (_capture.EnterWipeMode())
    using (_capture.SuppressCapture())
    {
      cleared = PerformWindowsClipboardWipe();
      _capture.OnHistoryCleared(cleared.Success);
    }
    _monitor?.SetPaused(false);

    if (!cleared.Success)
    {
      AppDialog.Warning(
          BuildClipboardClearFailureMessage(cleared),
          "Clear Windows clipboard", owner);
      return;
    }

    AppDialog.Info(
        "Windows clipboard cleared (active clipboard and Win+V history).\n\nCopyPaste Pro history was not changed.",
        "Windows clipboard cleared", owner);
  });

  public void CopyLatestToClipboard(Window? owner = null) => RunOnUi(owner, () =>
  {
    var latest = GetLatestEntry();
    if (latest == null)
    {
      AppDialog.Info("No items in clipboard history yet.", "Copy latest", owner);
      return;
    }

    if (!TryMutateSystemClipboard(() => _paste.TrySetClipboard(latest)))
    {
      AppDialog.Warning("Could not copy this item to the system clipboard.", "Copy latest", owner);
      return;
    }

    AppDialog.Info("Latest history item copied to the system clipboard.", "Copied", owner);
  });

  public void PasteLatestToApp(Window? owner = null) => RunOnUi(owner, () =>
  {
    var latest = GetLatestEntry();
    if (latest == null)
    {
      AppDialog.Info("No items in clipboard history yet.", "Paste latest", owner);
      return;
    }

    if (!TryMutateSystemClipboard(() => _paste.TrySetClipboard(latest)))
    {
      AppDialog.Warning("Could not place the latest item on the system clipboard.", "Paste latest", owner);
      return;
    }

    ClipboardPasteService.SendPasteKeys();
  });

  public void ClearUnpinnedHistory(Window? owner = null) => RunOnUi(owner, () =>
  {
    var count = _repository.GetCount();
    var unpinned = _repository.GetAll().Count(e => !e.IsPinned);
    if (unpinned == 0)
    {
      AppDialog.Info("There are no unpinned items to remove.", "Clear unpinned", owner);
      return;
    }

    if (_settings.ConfirmClearHistory &&
        !AppDialog.Confirm($"Remove {unpinned} unpinned item(s) from history?", "Clear unpinned", owner))
      return;

    var wiped = PerformHistoryWipe(clearHistory: true, unpinnedOnly: true, clearSystemClipboard: false, pauseCapture: false);
    if (wiped.RemainingHistoryCount > 0)
    {
      AppDialog.Error($"Could not clear history ({wiped.RemainingHistoryCount} item(s) remain).", "Clear unpinned", owner);
      return;
    }

    AppDialog.Info($"Removed {unpinned} unpinned item(s).", "Clear unpinned", owner);
  });

  /// <summary>Empties the application SQLite database (all history rows). Windows clipboard is unchanged.</summary>
  public void EmptyDatabase(Window? owner = null) => RunOnUi(owner, () =>
  {
    var count = _repository.GetCount();
    if (count == 0)
    {
      AppDialog.Info("The database is already empty.", "Empty database", owner);
      return;
    }

    if (!AppDialog.ConfirmWarning(
            $"Delete all {count} item(s) from the CopyPaste Pro database?\n\nThis cannot be undone. Windows clipboard will not be changed.",
            "Empty database", owner))
      return;

    var wiped = PerformHistoryWipe(clearHistory: true, unpinnedOnly: false, clearSystemClipboard: false, pauseCapture: false);
    if (wiped.RemainingHistoryCount > 0)
    {
      AppDialog.Error($"Could not empty database ({wiped.RemainingHistoryCount} item(s) remain).", "Empty database", owner);
      return;
    }

    var backupNote = string.IsNullOrEmpty(wiped.PostWipeBackupPath)
        ? ""
        : $"\n\nNew backup saved:\n{wiped.PostWipeBackupPath}";
    AppDialog.Info($"Database emptied ({count} item(s) removed).{backupNote}", "Empty database", owner);
  });

  /// <summary>Clears CopyPaste Pro history only. Windows clipboard (Ctrl+V / Win+V) is unchanged.</summary>
  public void ClearAppHistory(Window? owner = null) => RunOnUi(owner, () =>
  {
    var count = _repository.GetCount();
    if (count == 0)
    {
      AppDialog.Info("CopyPaste Pro history is already empty.", "Clear app history", owner);
      return;
    }

    if (_settings.ConfirmClearHistory &&
        !AppDialog.ConfirmWarning(
            $"Delete all {count} item(s) from CopyPaste Pro history?\n\nPinned items are included. Windows clipboard will not be changed.",
            "Clear app history", owner))
      return;

    var wiped = PerformHistoryWipe(clearHistory: true, unpinnedOnly: false, clearSystemClipboard: false, pauseCapture: false);
    if (wiped.RemainingHistoryCount > 0)
    {
      AppDialog.Error($"Could not clear app history ({wiped.RemainingHistoryCount} item(s) remain).", "Clear app history", owner);
      return;
    }

    var backupNote = string.IsNullOrEmpty(wiped.PostWipeBackupPath)
        ? ""
        : $"\n\nNew backup saved:\n{wiped.PostWipeBackupPath}";
    AppDialog.Info(
        $"CopyPaste Pro history cleared ({count} item(s) removed).{backupNote}\n\nWindows clipboard was not changed.",
        "Clear app history", owner);
  });

  /// <summary>Clears CopyPaste Pro history and Windows clipboard (Ctrl+V + Win+V).</summary>
  public void ClearAllHistory(Window? owner = null) => RunOnUi(owner, () =>
  {
    var count = _repository.GetCount();
    if (count == 0)
    {
      AppDialog.Info("CopyPaste Pro history is already empty.", "Clear all", owner);
      return;
    }

    if (!AppDialog.ConfirmWarning(
            $"Delete all {count} CopyPaste Pro item(s) and clear Windows clipboard + Win+V history?",
            "Clear all", owner))
      return;

    var wiped = PerformHistoryWipe(clearHistory: true, unpinnedOnly: false, clearSystemClipboard: true, pauseCapture: false);
    ReportWipeResult(wiped, owner, "Clear all");
  });

  public void PanicWipe(Window? owner = null, bool respectSettings = false) => RunOnUi(owner, () =>
  {
    if (!AppDialog.ConfirmWarning(
            "Delete ALL CopyPaste Pro history and clear Windows clipboard + Win+V history?",
            "Panic privacy wipe", owner))
      return;

    var clearHistory = !respectSettings || _settings.PanicClearsHistory;
    var clearClipboard = !respectSettings || _settings.PanicClearsClipboard;

    if (!clearHistory && !clearClipboard)
    {
      AppDialog.Warning(
          "Panic wipe is disabled in Settings → Privacy.",
          "Panic privacy wipe", owner);
      return;
    }

    var wiped = PerformHistoryWipe(clearHistory, unpinnedOnly: false, clearClipboard, pauseCapture: true);
    ReportWipeResult(wiped, owner, "Panic privacy wipe", requireClipboard: clearClipboard, requireHistory: clearHistory);
  });

  public void ToggleCapturePaused(Window? owner = null) => RunOnUi(owner, () =>
  {
    if (_privacy.IsMonitoringPaused)
    {
      _privacy.ResumeMonitoring();
      AppDialog.Info("Clipboard capture resumed.", "Capture", owner);
    }
    else
    {
      _privacy.PauseMonitoring();
      AppDialog.Info("Clipboard capture paused.", "Capture", owner);
    }
  });

  public bool IsCapturePaused => _privacy.IsMonitoringPaused;

  private WipeOutcome PerformHistoryWipe(
      bool clearHistory,
      bool unpinnedOnly,
      bool clearSystemClipboard,
      bool pauseCapture)
  {
    if (pauseCapture)
      _privacy.PauseMonitoring();

    _backup?.CreatePreWipeSnapshot();
    _monitor?.SetPaused(true);

    var outcome = new WipeOutcome();
    using (_capture.EnterWipeMode())
    using (_capture.SuppressCapture())
    {
      if (clearHistory)
      {
        outcome.DeletedHistoryCount = unpinnedOnly
            ? _repository.ClearUnpinned(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles)
            : _repository.ClearAll(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
        outcome.RemainingHistoryCount = _repository.GetCount();
      }

      if (clearSystemClipboard)
        outcome.ClipboardResult = PerformWindowsClipboardWipe();

      var clipboardWasCleared = clearSystemClipboard && (outcome.ClipboardResult?.Success ?? false);
      if (clearHistory)
        _capture.OnHistoryCleared(clipboardWasCleared);
    }

    _monitor?.SetPaused(false);

    if (clearHistory)
      outcome.PostWipeBackupPath = _backup?.CreatePostWipeBackup();

    ForceRefreshUi();
    return outcome;
  }

  private SystemClipboardHelper.ClearResult PerformWindowsClipboardWipe() =>
      SystemClipboardHelper.TryClearWindowsFully();

  private void ReportWipeResult(
      WipeOutcome wiped,
      Window? owner,
      string title,
      bool requireClipboard = true,
      bool requireHistory = true)
  {
    var historyOk = !requireHistory || wiped.RemainingHistoryCount == 0;
    var clipboardOk = !requireClipboard || (wiped.ClipboardResult?.Success ?? false);

    if (historyOk && clipboardOk)
    {
      var backupNote = string.IsNullOrEmpty(wiped.PostWipeBackupPath)
          ? ""
          : $"\n\nNew backup saved:\n{wiped.PostWipeBackupPath}";
      AppDialog.Info(
          "CopyPaste Pro history and Windows clipboard (including Win+V history) were cleared." + backupNote,
          title, owner);
      return;
    }

    var msg = "";
    if (!historyOk)
      msg += $"CopyPaste Pro still has {wiped.RemainingHistoryCount} item(s) in history.\n";
    if (!clipboardOk && wiped.ClipboardResult != null)
      msg += BuildClipboardClearFailureMessage(wiped.ClipboardResult);
  AppDialog.Warning(msg.Trim(), title, owner);
  }

  private static string BuildClipboardClearFailureMessage(SystemClipboardHelper.ClearResult cleared)
  {
    var parts = new List<string>
    {
      "Could not fully clear the Windows clipboard."
    };
    if (!cleared.ActiveClipboardCleared)
      parts.Add("• Active clipboard (Ctrl+V) is still in use or locked.");
    if (!cleared.HistoryCleared)
      parts.Add("• Win+V history may still have items (turn on Clipboard history in Windows Settings → System → Clipboard).");
    if (!string.IsNullOrWhiteSpace(cleared.Error))
      parts.Add(cleared.Error);
    return string.Join("\n", parts);
  }

  private void ForceRefreshUi()
  {
    var dispatcher = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher == null)
    {
      _refreshUi?.Invoke();
      return;
    }

    dispatcher.Invoke(() => _refreshUi?.Invoke(), DispatcherPriority.Send);
    dispatcher.BeginInvoke(() => _refreshUi?.Invoke(), DispatcherPriority.ApplicationIdle);
  }

  private bool TryMutateSystemClipboard(Func<bool> mutate)
  {
    using (_capture.SuppressCapture())
    {
      if (!mutate())
        return false;
      _capture.IgnoreClipboardChanges(1500);
      return true;
    }
  }

  private static void RunOnUi(Window? owner, Action action)
  {
    var dispatcher = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher == null)
    {
      action();
      return;
    }

    if (dispatcher.CheckAccess())
      action();
    else
      dispatcher.Invoke(action);
  }

  private ClipboardEntry? GetLatestEntry() =>
      _repository.Query(new HistoryQuery { Sort = HistorySortMode.NewestFirst, Take = 1 }).FirstOrDefault();

  private sealed class WipeOutcome
  {
    public int DeletedHistoryCount { get; set; }
    public int RemainingHistoryCount { get; set; }
    public SystemClipboardHelper.ClearResult? ClipboardResult { get; set; }
    public string? PostWipeBackupPath { get; set; }
  }
}
