using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopyPastePro.Models;

// CategoryRetentionRule, CategoryItemLimit in SmartOrganizerService.cs

namespace CopyPastePro.Services;

public sealed class AppSettings
{
  // ── General ──
  public bool StartMinimized { get; set; } = false;
  public bool RunAtWindowsStartup { get; set; } = false;
  public bool ShowTrayIcon { get; set; } = true;
  public bool ConfirmOnExit { get; set; } = false;
  public bool ConfirmClearHistory { get; set; } = true;
  public string Language { get; set; } = "en";

  // ── Copy / capture ──
  public bool MonitoringEnabled { get; set; } = true;
  public bool IgnoreDuplicates { get; set; } = true;
  public bool CaptureText { get; set; } = true;
  public bool CaptureImages { get; set; } = true;
  public bool CaptureFiles { get; set; } = true;
  public bool CaptureHtml { get; set; } = true;
  public bool CaptureRtf { get; set; } = true;
  public bool CaptureOtherFormats { get; set; } = true;
  public int MaxTextPreviewLength { get; set; } = 500;
  public int MaxSingleItemBytes { get; set; } = 50_000_000;
  public bool IgnoreBlankClips { get; set; } = true;
  public bool IgnorePasswordFields { get; set; } = false;
  public int ClipboardPollFallbackMs { get; set; } = 0;
  public List<string> IgnoredProcesses { get; set; } = new();
  public List<string> IgnoredFormats { get; set; } = new();

  // ── Paste ──
  public bool PasteAndHideQuickAccess { get; set; } = true;
  public bool PasteAndHideMainWindow { get; set; } = true;
  public int PasteDelayMs { get; set; } = 80;
  public bool RestoreFocusAfterPaste { get; set; } = true;
  public bool PasteAsPlainTextByDefault { get; set; } = false;
  public bool StripFormattingOnPlainPaste { get; set; } = true;
  public bool SequentialPasteMode { get; set; } = false;
  public int SequentialPasteDelayMs { get; set; } = 500;

  // ── Hotkeys ──
  public string QuickAccessModifiers { get; set; } = "Control,Shift";
  public string QuickAccessKey { get; set; } = "V";
  public bool QuickAccessHotkeyEnabled { get; set; } = true;
  public string MainWindowModifiers { get; set; } = "Control,Shift";
  public string MainWindowKey { get; set; } = "M";
  public bool MainWindowHotkeyEnabled { get; set; } = false;
  public string PastePlainModifiers { get; set; } = "Control,Shift";
  public string PastePlainKey { get; set; } = "X";
  public bool PastePlainHotkeyEnabled { get; set; } = false;

  // ── Screenshot capture ──
  public bool ScreenshotEnabled { get; set; } = true;
  public bool ScreenshotHotkeyEnabled { get; set; } = true;
  public string ScreenshotModifiers { get; set; } = "Control,Shift";
  public string ScreenshotKey { get; set; } = "S";
  /// <summary>Snip = Win+Shift+S overlay; FullScreen = Print Screen.</summary>
  public string ScreenshotMode { get; set; } = "Snip";
  public int ScreenshotHideDelayMs { get; set; } = 350;
  public int ScreenshotWaitTimeoutSeconds { get; set; } = 60;
  public bool ScreenshotRestoreWindows { get; set; } = true;
  public bool ScreenshotOpenImageLibraryAfterCapture { get; set; } = true;

  // ── Theme / appearance ──
  public string Theme { get; set; } = "Dark"; // Dark, Light
  public double UiFontSize { get; set; } = 13;
  public bool CompactHistoryRows { get; set; } = false;
  public double QuickAccessWidth { get; set; } = 300;
  public double QuickAccessHeight { get; set; } = 560;
  public bool QuickAccessAlwaysOnTop { get; set; } = true;
  public bool QuickAccessShowPreview { get; set; } = true;
  public int QuickAccessItemCount { get; set; } = 25;
  public string QuickAccessPosition { get; set; } = "NearCursor"; // NearCursor, CenterScreen, Remember
  public bool QuickAccessCloseOnFocusLoss { get; set; } = true;

  // ── Organization / smart features ──
  public HistorySortMode DefaultSort { get; set; } = HistorySortMode.PinnedThenNewest;
  public bool AutoCategorizeEnabled { get; set; } = true;
  public bool AutoTagFromCategory { get; set; } = true;
  public bool ShowCategoryBadges { get; set; } = true;
  public bool GroupByCategoryInList { get; set; } = false;
  public bool FavoritesOnlyFilter { get; set; } = false;
  public List<string> AutoPinCategories { get; set; } = new();
  public List<string> AutoFavoriteCategories { get; set; } = new();
  public bool AutoPinLargeClips { get; set; } = false;
  public long AutoPinMinBytes { get; set; } = 1_000_000;
  public List<CategoryRetentionRule> CategoryRetentionDays { get; set; } = new();
  public List<CategoryItemLimit> CategoryMaxItems { get; set; } = new();
  public List<ExtensionCategoryRule> CustomExtensionCategories { get; set; } = new();
  public bool RecategorizeOnSaveSettings { get; set; } = false;
  public bool AutoSaveToDatabase { get; set; } = true;
  public bool InstantPersistOnCapture { get; set; } = true;

  // ── Auto backup ──
  public bool AutoBackupEnabled { get; set; } = true;
  public int AutoBackupIntervalMinutes { get; set; } = 60;
  public int AutoBackupKeepCount { get; set; } = 10;
  public bool AutoBackupOnStartup { get; set; } = true;

  // ── History / storage ──
  public int MaxHistoryItems { get; set; } = 500;
  public int MaxPinnedItems { get; set; } = 100;
  public int AutoDeleteAfterDays { get; set; } = 0;
  public bool EncryptDatabase { get; set; } = false;
  public bool StoreImagesOnDisk { get; set; } = true;
  public bool CompressImageStorage { get; set; } = false;
  public string HistoryDatabasePath { get; set; } = "";

  // ── Notifications ──
  public bool PlaySoundOnCapture { get; set; } = false;
  public bool BalloonOnCapture { get; set; } = false;
  public bool BalloonOnPaste { get; set; } = false;
  public bool FlashTrayOnCapture { get; set; } = false;

  // ── Automation / rules ──
  public bool RulesEnabled { get; set; } = true;
  public List<ClipboardRule> Rules { get; set; } = new();
  public bool AutoPinPasswordManagerClips { get; set; } = false;
  public bool AutoDeleteSensitivePatterns { get; set; } = false;
  public string SensitivePatterns { get; set; } = "password,credit card,ssn";
  public bool AutoClearOnLogout { get; set; } = false;
  public bool IgnoreCopiesFromSelf { get; set; } = true;

  // ── Privacy: sensitive data blocking ──
  public bool BlockCreditCards { get; set; } = true;
  public bool BlockSocialSecurityNumbers { get; set; } = true;
  public bool BlockIbanAndBankNumbers { get; set; } = true;
  public bool BlockEmailAddresses { get; set; } = false;
  public bool BlockPhoneNumbers { get; set; } = false;
  public bool BlockApiKeysAndTokens { get; set; } = true;
  public bool BlockJwtTokens { get; set; } = true;
  public bool BlockPrivateKeys { get; set; } = true;
  public bool BlockPasswordLikeContent { get; set; } = true;
  public bool BlockCryptoWalletAddresses { get; set; } = false;
  public bool BlockSensitiveKeywords { get; set; } = true;
  public string CustomBlockedRegex { get; set; } = "";
  public List<string> BlockedDomains { get; set; } = new();

  // ── Privacy: app / browser ──
  /// <summary>Legacy; migrated to <see cref="IncognitoRestrictEnabled"/> on load.</summary>
  public bool ExcludeIncognitoBrowsers { get; set; } = true;
  /// <summary>When true, copies from InPrivate/Incognito windows follow the per-type rules below.</summary>
  public bool IncognitoRestrictEnabled { get; set; } = true;
  public bool IncognitoSaveText { get; set; } = false;
  public bool IncognitoSaveImages { get; set; } = false;
  public bool IncognitoSaveFiles { get; set; } = false;
  /// <summary>When a type is blocked, show a prompt with &quot;Allow only this time&quot;.</summary>
  public bool IncognitoPromptWhenBlocked { get; set; } = true;
  public List<string> ExcludedApplications { get; set; } = new();
  public List<string> PasswordManagerProcesses { get; set; } = ["1password", "1Password", "keepass", "KeePass", "bitwarden", "lastpass", "dashlane", "nordpass", "enpass", "roboform"];
  public List<string> BankingProcesses { get; set; } = new();
  public bool NeverCaptureFromPasswordManagers { get; set; } = true;
  public bool NeverCaptureFromBankingApps { get; set; } = false;
  public bool AllowlistModeOnlyCaptureFromListed { get; set; } = false;
  public List<string> TrustedProcessesOnly { get; set; } = new();

  // ── Privacy: storage ──
  public bool NeverStoreFilePaths { get; set; } = false;
  public bool StoreOnlyPreviewNotFullText { get; set; } = false;
  public bool RedactSensitiveInStorage { get; set; } = true;
  public bool HashSensitivePreviews { get; set; } = false;
  public bool NeverStoreImages { get; set; } = false;
  public bool StripHtmlWhenStoring { get; set; } = false;
  public int AutoDeleteSensitiveAfterMinutes { get; set; } = 0;

  // ── Privacy: display ──
  public bool MaskSensitiveInUi { get; set; } = true;
  public bool HideSourceAppInUi { get; set; } = false;
  public bool DisablePreviewForSensitive { get; set; } = true;

  // ── Privacy: session / lifecycle ──
  public bool ClearClipboardOnExit { get; set; } = false;
  public bool ClearHistoryOnExit { get; set; } = false;
  public bool ClearUnpinnedOnLock { get; set; } = false;
  public bool ClearHistoryOnLock { get; set; } = false;
  public bool ClearClipboardOnLock { get; set; } = false;
  public bool PauseCaptureOnLock { get; set; } = true;
  public int SecureDeletePasses { get; set; } = 0;
  public bool SecureDeletePayloadFiles { get; set; } = true;

  // ── Privacy: encryption (Windows DPAPI, current user) ──
  public bool EncryptPayloadFiles { get; set; } = false;

  // ── Privacy: lock & panic ──
  public string PrivacyLockPin { get; set; } = "";
  public bool RequirePinToOpenManager { get; set; } = false;
  public bool PanicHotkeyEnabled { get; set; } = false;
  public string PanicHotkeyModifiers { get; set; } = "Control,Shift";
  public string PanicHotkeyKey { get; set; } = "Delete";
  public bool PanicClearsClipboard { get; set; } = true;
  public bool PanicClearsHistory { get; set; } = true;

  // ── Image library ──
  public bool ImageLibraryAutoSaveEnabled { get; set; } = false;
  public string ImageLibraryAutoSaveFolder { get; set; } = "";
  public bool ImageLibrarySyncFolderEnabled { get; set; } = false;
  public string ImageLibrarySyncFolderPath { get; set; } = "";
  /// <summary>When true, images in subfolders of the sync folder appear in the library.</summary>
  public bool ImageLibrarySyncScanSubfolders { get; set; } = true;
  /// <summary>When true, new clipboard images are written into the sync folder.</summary>
  public bool ImageLibrarySyncSaveNewCaptures { get; set; } = true;
  public string ImageLibraryFileNamePattern { get; set; } = "clipboard_{yyyy-MM-dd}_{HH-mm-ss}";
  public string ImageLibrarySaveFormat { get; set; } = "png"; // png, jpg
  public bool ImageLibraryOrganizeByDate { get; set; } = true;
  public bool ImageLibraryOpenFolderAfterSave { get; set; } = false;
  public string ImageLibraryDuplicateHandling { get; set; } = "rename"; // rename, skip, overwrite
  public int ImageLibraryThumbnailSize { get; set; } = 140;
  public bool ImageLibraryShowDimensionsOnThumb { get; set; } = true;
  public bool ImageLibraryFocusPreview { get; set; } = false;

  /// <summary>UI scale for the main manager window (0.75–1.5). Quick access and tray are not scaled.</summary>
  public double MainWindowUiScale { get; set; } = 1.0;

  // ── Advanced ──
  public bool DebugLogging { get; set; } = false;
  public bool ExportHistoryOnCrash { get; set; } = true;
  public int DatabaseVacuumEveryDays { get; set; } = 7;

  // Legacy (migrated on load)
  [JsonIgnore] public string HotkeyModifiers { get => QuickAccessModifiers; set => QuickAccessModifiers = value; }
  [JsonIgnore] public string HotkeyKey { get => QuickAccessKey; set => QuickAccessKey = value; }

  private static string SettingsPath =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyPastePro", "settings.json");

  public static AppSettings Load()
  {
    try
    {
      if (!File.Exists(SettingsPath)) return new AppSettings();
      var json = File.ReadAllText(SettingsPath);
      var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
      MigrateLegacy(json, settings);
      return settings;
    }
    catch
    {
      return new AppSettings();
    }
  }

  private static void MigrateLegacy(string json, AppSettings s)
  {
    if (json.Contains("\"HotkeyModifiers\"") && !json.Contains("\"QuickAccessModifiers\""))
    {
      try
      {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("HotkeyModifiers", out var m)) s.QuickAccessModifiers = m.GetString() ?? s.QuickAccessModifiers;
        if (root.TryGetProperty("HotkeyKey", out var k)) s.QuickAccessKey = k.GetString() ?? s.QuickAccessKey;
      }
      catch { }
    }

    if (json.Contains("\"ExcludeIncognitoBrowsers\"", StringComparison.Ordinal) &&
        !json.Contains("\"IncognitoSaveText\"", StringComparison.Ordinal))
    {
      try
      {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ExcludeIncognitoBrowsers", out var legacy))
          return;
        if (!legacy.GetBoolean())
        {
          s.IncognitoRestrictEnabled = false;
          return;
        }

        s.IncognitoRestrictEnabled = true;
        s.IncognitoSaveText = false;
        s.IncognitoSaveImages = false;
        s.IncognitoSaveFiles = false;
      }
      catch { }
    }
  }

  public void Save()
  {
    var dir = Path.GetDirectoryName(SettingsPath)!;
    Directory.CreateDirectory(dir);
    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
  }

  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

  public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

  public void ResetToDefaults()
  {
    var theme = Theme;
    var qaW = QuickAccessWidth;
    var qaH = QuickAccessHeight;
    var defaults = new AppSettings();
    foreach (var p in typeof(AppSettings).GetProperties())
    {
      if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
      if (p.Name is nameof(Theme) or nameof(QuickAccessWidth) or nameof(QuickAccessHeight)) continue;
      p.SetValue(this, p.GetValue(defaults));
    }
    Theme = theme;
    QuickAccessWidth = qaW;
    QuickAccessHeight = qaH;
  }
}
