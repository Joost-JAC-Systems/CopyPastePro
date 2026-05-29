using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CopyPastePro.Models;
using Clipboard = System.Windows.Clipboard;

namespace CopyPastePro.Services;

public sealed class PrivacyService
{
  private readonly AppSettings _settings;
  private readonly SensitiveDataDetector _detector = new();
  private bool _monitoringPaused;
  private bool _privacyLocked;

  public PrivacyService(AppSettings settings) => _settings = settings;

  public bool IsMonitoringPaused => _monitoringPaused || _privacyLocked;
  public bool IsPrivacyLocked => _privacyLocked;

  public event EventHandler? PrivacyLockChanged;

  public void PauseMonitoring() => _monitoringPaused = true;
  public void ResumeMonitoring() => _monitoringPaused = false;

  public void SetPrivacyLock(bool locked)
  {
    _privacyLocked = locked;
    if (locked) _monitoringPaused = true;
    PrivacyLockChanged?.Invoke(this, EventArgs.Empty);
  }

  public bool TryUnlock(string pin) => !string.IsNullOrEmpty(_settings.PrivacyLockPin) && pin == _settings.PrivacyLockPin;

  public SensitiveDetectionResult Analyze(ClipboardEntry entry) =>
      _detector.Analyze(entry.TextContent ?? entry.Preview, _settings);

  public bool ShouldCapture(ClipboardEntry entry, string processName, string? windowTitle) =>
      ShouldCapture(entry.ContentType, processName, windowTitle);

  /// <summary>Check privacy rules before reading clipboard payloads (e.g. incognito prompt).</summary>
  public bool ShouldCapture(ClipboardContentType contentType, string processName, string? windowTitle)
  {
    if (IsMonitoringPaused) return false;

    if (_settings.AllowlistModeOnlyCaptureFromListed && _settings.TrustedProcessesOnly.Count > 0)
    {
      if (!_settings.TrustedProcessesOnly.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
        return false;
    }

    if (_settings.IgnoredProcesses.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
      return false;

    if (_settings.ExcludedApplications.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
      return false;

    if (_settings.NeverCaptureFromPasswordManagers &&
        _settings.PasswordManagerProcesses.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
      return false;

    if (_settings.NeverCaptureFromBankingApps &&
        _settings.BankingProcesses.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
      return false;

    if (!TryAllowIncognitoCapture(contentType, windowTitle))
      return false;

    if (_settings.NeverStoreImages && contentType == ClipboardContentType.Image)
      return false;

    return true;
  }

  public bool ShouldCaptureStoredEntry(ClipboardEntry entry)
  {
    var detection = Analyze(entry);
    if (detection.IsSensitive && ShouldBlockSensitive(detection.Kind))
      return false;
    return true;
  }

  private bool ShouldBlockSensitive(SensitiveDataKind kind) => kind switch
  {
    SensitiveDataKind.CreditCard => _settings.BlockCreditCards,
    SensitiveDataKind.SocialSecurityNumber => _settings.BlockSocialSecurityNumbers,
    SensitiveDataKind.Iban => _settings.BlockIbanAndBankNumbers,
    SensitiveDataKind.Email => _settings.BlockEmailAddresses,
    SensitiveDataKind.Phone => _settings.BlockPhoneNumbers,
    SensitiveDataKind.ApiKey => _settings.BlockApiKeysAndTokens,
    SensitiveDataKind.Jwt => _settings.BlockJwtTokens,
    SensitiveDataKind.PrivateKey => _settings.BlockPrivateKeys,
    SensitiveDataKind.PasswordLike => _settings.BlockPasswordLikeContent,
    SensitiveDataKind.CryptoWallet => _settings.BlockCryptoWalletAddresses,
    SensitiveDataKind.BlockedDomain or SensitiveDataKind.CustomPattern or SensitiveDataKind.Keyword =>
        _settings.BlockSensitiveKeywords || _settings.AutoDeleteSensitivePatterns,
    _ => false
  };

  public void PrepareForStorage(ClipboardEntry entry)
  {
    var detection = Analyze(entry);
    entry.IsSensitive = detection.IsSensitive;

    if (_settings.NeverStoreFilePaths && entry.ContentType == ClipboardContentType.Files)
    {
      entry.FilePathsJson = null;
      entry.Preview = $"{entry.FilePaths.Count} file(s) — paths hidden for privacy";
    }

    if (_settings.StoreOnlyPreviewNotFullText)
      entry.TextContent = null;

    if (_settings.StripHtmlWhenStoring && entry.ContentType == ClipboardContentType.Html && entry.TextContent != null)
      entry.TextContent = StripHtml(entry.TextContent);

    if (detection.IsSensitive && _settings.RedactSensitiveInStorage)
    {
      if (!string.IsNullOrEmpty(entry.TextContent))
        entry.TextContent = SensitiveDataDetector.Redact(entry.TextContent, detection.Kind);
      entry.Preview = SensitiveDataDetector.Redact(entry.Preview, detection.Kind);
      if (_settings.HashSensitivePreviews)
        entry.Preview = "[Sensitive] " + ClipboardHistoryRepository.ComputeHash(Encoding.UTF8.GetBytes(entry.Preview))[..12];
    }

    if (_settings.EncryptDatabase)
    {
      entry.TextContent = DataProtectionHelper.ProtectString(entry.TextContent);
      if (!_settings.HashSensitivePreviews || !detection.IsSensitive)
        entry.Preview = DataProtectionHelper.ProtectString(entry.Preview) ?? entry.Preview;
    }
  }

  public string GetDisplayPreview(ClipboardEntry entry)
  {
    var preview = _settings.EncryptDatabase
        ? DataProtectionHelper.UnprotectString(entry.Preview) ?? entry.Preview
        : entry.Preview;

    if (!entry.IsSensitive || !_settings.MaskSensitiveInUi)
      return preview;

    return _settings.DisablePreviewForSensitive
        ? "••• Sensitive clip (preview hidden) •••"
        : SensitiveDataDetector.Redact(preview, SensitiveDataKind.Keyword);
  }

  public string? GetDisplayText(ClipboardEntry entry)
  {
    var text = _settings.EncryptDatabase
        ? DataProtectionHelper.UnprotectString(entry.TextContent)
        : entry.TextContent;

    if (!entry.IsSensitive || !_settings.MaskSensitiveInUi)
      return text;

    return _settings.DisablePreviewForSensitive ? null : text;
  }

  private bool TryAllowIncognitoCapture(ClipboardContentType contentType, string? windowTitle)
  {
    if (!IsPrivateBrowsingWindow(windowTitle))
      return true;

    if (!_settings.IncognitoRestrictEnabled)
      return true;

    if (IsIncognitoTypeAllowed(contentType))
      return true;

    if (!_settings.IncognitoPromptWhenBlocked)
      return false;

    return PromptIncognitoAllowOnce(contentType, windowTitle);
  }

  private bool IsIncognitoTypeAllowed(ClipboardContentType type) => type switch
  {
    ClipboardContentType.Image => _settings.IncognitoSaveImages,
    ClipboardContentType.Files => _settings.IncognitoSaveFiles,
    ClipboardContentType.Text or ClipboardContentType.Html or ClipboardContentType.Rtf => _settings.IncognitoSaveText,
    _ => _settings.IncognitoSaveText
  };

  private static string IncognitoContentLabel(ClipboardContentType type) => type switch
  {
    ClipboardContentType.Image => "Image",
    ClipboardContentType.Files => "Files",
    ClipboardContentType.Html => "Text (HTML)",
    ClipboardContentType.Rtf => "Text (rich)",
    ClipboardContentType.Text => "Text",
    _ => type.ToString()
  };

  private bool PromptIncognitoAllowOnce(ClipboardContentType contentType, string? windowTitle)
  {
    var app = System.Windows.Application.Current;
    if (app == null)
      return false;

    if (app.Dispatcher.CheckAccess())
      return AppDialog.ConfirmIncognitoCapture(windowTitle ?? "Private window", IncognitoContentLabel(contentType));

    return app.Dispatcher.Invoke(() =>
        AppDialog.ConfirmIncognitoCapture(windowTitle ?? "Private window", IncognitoContentLabel(contentType)));
  }

  public static bool IsPrivateBrowsingWindow(string? title)
  {
    if (string.IsNullOrWhiteSpace(title)) return false;
    string[] markers =
    [
      "InPrivate", "Incognito", "Private Browsing", "Private Window", "Private Tab",
      "Navegación privada", "Navigation privée", "Privates Fenster", "GPrivate"
    ];
    return markers.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase));
  }

  public static (string ProcessName, string WindowTitle) GetForegroundContext()
  {
    try
    {
      var hwnd = NativeMethods.GetForegroundWindow();
      var title = GetWindowTitle(hwnd);
      NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
      var name = Process.GetProcessById((int)pid).ProcessName;
      return (name, title);
    }
    catch
    {
      return ("", "");
    }
  }

  public bool TryWipeSystemClipboard() => SystemClipboardHelper.TryClearWindowsFully().Success;

  public void WipeSystemClipboard()
  {
    _ = TryWipeSystemClipboard();
  }

  public int PurgeSensitive(ClipboardHistoryRepository repo)
  {
    var count = 0;
    foreach (var e in repo.Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }))
    {
      var text = _settings.EncryptDatabase
          ? DataProtectionHelper.UnprotectString(e.TextContent) ?? DataProtectionHelper.UnprotectString(e.Preview)
          : e.TextContent ?? e.Preview;
      if (Analyze(new ClipboardEntry { TextContent = text, Preview = text ?? "" }).IsSensitive)
      {
        repo.Delete(e.Id, _settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
        count++;
      }
    }
    return count;
  }

  public void PanicWipe(ClipboardHistoryRepository repo, ClipboardCaptureService? capture = null)
  {
    PauseMonitoring();
    var clearedClipboard = false;
    if (_settings.PanicClearsHistory)
      repo.ClearAll(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
    if (_settings.PanicClearsClipboard)
      clearedClipboard = TryWipeSystemClipboard();
    capture?.OnHistoryCleared(clearedClipboard);
  }

  public void OnSessionLock(ClipboardHistoryRepository repo)
  {
    if (_settings.PauseCaptureOnLock) PauseMonitoring();
    if (_settings.ClearUnpinnedOnLock) repo.ClearUnpinned(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
    if (_settings.ClearHistoryOnLock) repo.ClearAll(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
    if (_settings.ClearClipboardOnLock) WipeSystemClipboard();
  }

  public void OnSessionUnlock()
  {
    if (_settings.PauseCaptureOnLock && !_privacyLocked) ResumeMonitoring();
  }

  public void OnExit(ClipboardHistoryRepository repo)
  {
    if (_settings.ClearHistoryOnExit)
      repo.ClearAll(_settings.SecureDeletePasses, _settings.SecureDeletePayloadFiles);
    if (_settings.ClearClipboardOnExit)
      WipeSystemClipboard();
    if (_settings.SecureDeletePasses > 0)
      SecureDeleteHelper.WipeDirectoryContents(repo.DataDirectory, _settings.SecureDeletePasses);
  }

  private static string GetWindowTitle(IntPtr hwnd)
  {
    var sb = new StringBuilder(512);
    NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
    return sb.ToString();
  }

  private static string StripHtml(string html) =>
      Regex.Replace(html, "<[^>]+>", " ").Trim();

  private static class NativeMethods
  {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  }
}
