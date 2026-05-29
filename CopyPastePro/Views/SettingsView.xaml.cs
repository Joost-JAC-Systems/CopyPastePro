using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CopyPastePro.Models;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using CopyPastePro.Services;
using Forms = System.Windows.Forms;

namespace CopyPastePro.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
  private readonly AppSettings _settings;
  private readonly CategoryClassifier _classifier;
  private readonly ClipboardHistoryRepository _repository;
  private readonly PrivacyService _privacy;
  private readonly SensitiveRetentionService? _sensitiveRetention;
  private readonly ClipboardQuickActionsService? _quickActions;
  private readonly Dictionary<string, StackPanel> _panels = new();
  private string _currentCategory = "General";

  public event EventHandler? SettingsSaved;
  public event EventHandler? ThemeChanged;
  public event EventHandler? HotkeysChanged;

  public SettingsView(AppSettings settings, CategoryClassifier classifier, ClipboardHistoryRepository repository, PrivacyService privacy, SensitiveRetentionService? sensitiveRetention = null, ClipboardQuickActionsService? quickActions = null)
  {
    _settings = settings;
    _classifier = classifier;
    _repository = repository;
    _privacy = privacy;
    _sensitiveRetention = sensitiveRetention;
    _quickActions = quickActions;
    InitializeComponent();
    BuildCategories();
    BuildAllPanels();
    CategoryList.SelectedIndex = 0;
  }

  private void BuildCategories()
  {
    string[] cats = ["General", "Organization", "Copy", "Paste", "Hotkeys", "Theme", "History", "Image library", "Notifications", "Automation", "Privacy", "Advanced"];
    CategoryList.ItemsSource = cats;
  }

  private void BuildAllPanels()
  {
    AddPanel("General", BuildGeneral());
    AddPanel("Organization", BuildOrganization());
    AddPanel("Copy", BuildCopy());
    AddPanel("Paste", BuildPaste());
    AddPanel("Hotkeys", BuildHotkeys());
    AddPanel("Theme", BuildTheme());
    AddPanel("History", BuildHistory());
    AddPanel("Image library", BuildImageLibrary());
    AddPanel("Notifications", BuildNotifications());
    AddPanel("Automation", BuildAutomation());
    AddPanel("Privacy", BuildPrivacy());
    AddPanel("Advanced", BuildAdvanced());
    ShowCategory("General");
  }

  private void AddPanel(string name, StackPanel panel) => _panels[name] = panel;

  private void ShowCategory(string cat)
  {
    _currentCategory = cat;
    SettingsContent.Children.Clear();
    if (_panels.TryGetValue(cat, out var panel))
      SettingsContent.Children.Add(panel);
  }

  private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (CategoryList.SelectedItem is string cat) ShowCategory(cat);
  }

  private StackPanel BuildGeneral()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("General"));
    p.Children.Add(Check("Start minimized to tray", () => _settings.StartMinimized, v => _settings.StartMinimized = v));
    p.Children.Add(Check("Run at Windows startup", () => _settings.RunAtWindowsStartup, v => _settings.RunAtWindowsStartup = v));
    p.Children.Add(Check("Show tray icon", () => _settings.ShowTrayIcon, v => _settings.ShowTrayIcon = v));
    p.Children.Add(Check("Confirm before exit", () => _settings.ConfirmOnExit, v => _settings.ConfirmOnExit = v));
    p.Children.Add(Check("Confirm before clearing history", () => _settings.ConfirmClearHistory, v => _settings.ConfirmClearHistory = v));
    p.Children.Add(TextField("Interface language (code)", () => _settings.Language, v => _settings.Language = v));
    p.Children.Add(Note("The full manager window keeps its size. Use the quick popup (hotkey) for a small sticky-note picker."));
    return p;
  }

  private StackPanel BuildOrganization()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Smart organization"));
    p.Children.Add(Note("Automatic categories by file extension, content type, and smart text detection."));
    p.Children.Add(Check("Auto-categorize new clips", () => _settings.AutoCategorizeEnabled, v => _settings.AutoCategorizeEnabled = v));
    p.Children.Add(Check("Auto-tag from category name", () => _settings.AutoTagFromCategory, v => _settings.AutoTagFromCategory = v));
    p.Children.Add(Check("Show category badges in lists", () => _settings.ShowCategoryBadges, v => _settings.ShowCategoryBadges = v));
    p.Children.Add(Check("Instant save on every capture", () => _settings.InstantPersistOnCapture, v => _settings.InstantPersistOnCapture = v));
    p.Children.Add(Check("Auto-save to database", () => _settings.AutoSaveToDatabase, v => _settings.AutoSaveToDatabase = v));
    var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
    sortRow.Children.Add(new TextBlock { Text = "Default sort", Width = 200, VerticalAlignment = VerticalAlignment.Center });
    var sortCombo = new ComboBox { Width = 200 };
    sortCombo.ItemsSource = Enum.GetNames<HistorySortMode>();
    sortCombo.SelectedItem = _settings.DefaultSort.ToString();
    sortCombo.SelectionChanged += (_, _) =>
    {
      if (Enum.TryParse<HistorySortMode>(sortCombo.SelectedItem as string, out var m)) _settings.DefaultSort = m;
    };
    sortRow.Children.Add(sortCombo);
    p.Children.Add(sortRow);
    p.Children.Add(SubHeading("Auto backup"));
    p.Children.Add(Check("Enable scheduled backups", () => _settings.AutoBackupEnabled, v => _settings.AutoBackupEnabled = v));
    p.Children.Add(NumberField("Backup every (minutes)", () => _settings.AutoBackupIntervalMinutes, v => _settings.AutoBackupIntervalMinutes = v));
    p.Children.Add(NumberField("Keep backup files", () => _settings.AutoBackupKeepCount, v => _settings.AutoBackupKeepCount = v));
    p.Children.Add(Check("Backup on startup", () => _settings.AutoBackupOnStartup, v => _settings.AutoBackupOnStartup = v));
    p.Children.Add(Note("Backups are never restored automatically. After you clear history, a new timestamped backup is saved. Old backup files are kept until the keep limit."));
    p.Children.Add(SubHeading("Auto pin / favorite by category"));
    p.Children.Add(TextField("Auto-pin categories (comma)", () => string.Join(", ", _settings.AutoPinCategories),
        v => _settings.AutoPinCategories = SplitList(v)));
    p.Children.Add(TextField("Auto-favorite categories (comma)", () => string.Join(", ", _settings.AutoFavoriteCategories),
        v => _settings.AutoFavoriteCategories = SplitList(v)));
    p.Children.Add(Check("Auto-pin large clips", () => _settings.AutoPinLargeClips, v => _settings.AutoPinLargeClips = v));
    p.Children.Add(NumberField("Large clip threshold (bytes)", () => (int)_settings.AutoPinMinBytes, v => _settings.AutoPinMinBytes = v));
    p.Children.Add(SubHeading("Per-category limits (examples)"));
    p.Children.Add(Note("Code: 30 days · Image: 200 items — edit settings.json for full control, or use defaults below."));
    if (_settings.CategoryRetentionDays.Count == 0)
    {
      _settings.CategoryRetentionDays.Add(new CategoryRetentionRule { Category = "Code", Days = 30 });
      _settings.CategoryRetentionDays.Add(new CategoryRetentionRule { Category = "Image", Days = 0 });
    }
    if (_settings.CategoryMaxItems.Count == 0)
      _settings.CategoryMaxItems.Add(new CategoryItemLimit { Category = "Image", Max = 200 });
    p.Children.Add(TextField("Retention: Category:Days (e.g. Code:30,Text:14)", () => string.Join(",", _settings.CategoryRetentionDays.Select(r => $"{r.Category}:{r.Days}")),
        v => _settings.CategoryRetentionDays = ParseRetention(v)));
    p.Children.Add(TextField("Max items: Category:Max (e.g. Image:200)", () => string.Join(",", _settings.CategoryMaxItems.Select(r => $"{r.Category}:{r.Max}")),
        v => _settings.CategoryMaxItems = ParseLimits(v)));
    p.Children.Add(SubHeading("Custom extension → category"));
    p.Children.Add(Note("Example: Category=Design Extensions=.psd,.ai,.fig"));
    var addCustom = new Button { Content = "Add custom mapping", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 8, 0, 8) };
    addCustom.Click += (_, _) => _settings.CustomExtensionCategories.Add(new ExtensionCategoryRule { Category = "Custom", Extensions = ".ext" });
    p.Children.Add(addCustom);
    foreach (var rule in _settings.CustomExtensionCategories.ToList())
      p.Children.Add(TextField($"[{rule.Category}] extensions", () => rule.Extensions, v => rule.Extensions = v));
    if (_settings.CustomExtensionCategories.Count == 0)
      _settings.CustomExtensionCategories.Add(new ExtensionCategoryRule { Category = "Design", Extensions = ".psd,.ai,.fig,.sketch" });
    var recat = new Button { Content = "Recategorize entire history now", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 12, 0, 0) };
    recat.Click += (_, _) =>
    {
      _repository.RecategorizeAll(_classifier.Classify);
      AppDialog.Info("All items recategorized.", "Done", Window.GetWindow(this));
    };
    p.Children.Add(recat);
    return p;
  }

  private static List<string> SplitList(string v) =>
      v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

  private static List<CategoryRetentionRule> ParseRetention(string v)
  {
    var list = new List<CategoryRetentionRule>();
    foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var bits = part.Split(':', 2);
      if (bits.Length == 2 && int.TryParse(bits[1], out var days))
        list.Add(new CategoryRetentionRule { Category = bits[0].Trim(), Days = days });
    }
    return list;
  }

  private static List<CategoryItemLimit> ParseLimits(string v)
  {
    var list = new List<CategoryItemLimit>();
    foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var bits = part.Split(':', 2);
      if (bits.Length == 2 && int.TryParse(bits[1], out var max))
        list.Add(new CategoryItemLimit { Category = bits[0].Trim(), Max = max });
    }
    return list;
  }

  private StackPanel BuildCopy()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Copy & capture"));
    p.Children.Add(Check("Enable clipboard monitoring", () => _settings.MonitoringEnabled, v => _settings.MonitoringEnabled = v));
    p.Children.Add(Check("Ignore duplicate copies", () => _settings.IgnoreDuplicates, v => _settings.IgnoreDuplicates = v));
    p.Children.Add(Check("Capture text", () => _settings.CaptureText, v => _settings.CaptureText = v));
    p.Children.Add(Check("Capture images", () => _settings.CaptureImages, v => _settings.CaptureImages = v));
    p.Children.Add(Check("Capture files", () => _settings.CaptureFiles, v => _settings.CaptureFiles = v));
    p.Children.Add(Check("Capture HTML", () => _settings.CaptureHtml, v => _settings.CaptureHtml = v));
    p.Children.Add(Check("Capture RTF / rich text", () => _settings.CaptureRtf, v => _settings.CaptureRtf = v));
    p.Children.Add(Check("Capture other formats", () => _settings.CaptureOtherFormats, v => _settings.CaptureOtherFormats = v));
    p.Children.Add(Check("Ignore blank clips", () => _settings.IgnoreBlankClips, v => _settings.IgnoreBlankClips = v));
    p.Children.Add(Check("Ignore copies from this app", () => _settings.IgnoreCopiesFromSelf, v => _settings.IgnoreCopiesFromSelf = v));
    p.Children.Add(NumberField("Max preview length (chars)", () => _settings.MaxTextPreviewLength, v => _settings.MaxTextPreviewLength = v));
    p.Children.Add(NumberField("Max single item size (bytes)", () => _settings.MaxSingleItemBytes, v => _settings.MaxSingleItemBytes = v));
    p.Children.Add(TextField("Ignored processes (comma-separated)", () => string.Join(", ", _settings.IgnoredProcesses),
        v => _settings.IgnoredProcesses = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()));
    return p;
  }

  private StackPanel BuildPaste()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Paste"));
    p.Children.Add(Check("Hide quick access after paste", () => _settings.PasteAndHideQuickAccess, v => _settings.PasteAndHideQuickAccess = v));
    p.Children.Add(Note("Full manager: Paste to app, Enter, or double-click hides the window and pastes into the app you were using before."));
    p.Children.Add(NumberField("Paste delay (ms)", () => _settings.PasteDelayMs, v => _settings.PasteDelayMs = v));
    p.Children.Add(Check("Restore focus after paste", () => _settings.RestoreFocusAfterPaste, v => _settings.RestoreFocusAfterPaste = v));
    p.Children.Add(Check("Paste as plain text by default", () => _settings.PasteAsPlainTextByDefault, v => _settings.PasteAsPlainTextByDefault = v));
    p.Children.Add(Check("Strip formatting on plain paste", () => _settings.StripFormattingOnPlainPaste, v => _settings.StripFormattingOnPlainPaste = v));
    p.Children.Add(Check("Sequential paste mode", () => _settings.SequentialPasteMode, v => _settings.SequentialPasteMode = v));
    p.Children.Add(NumberField("Delay between sequential pastes (ms)", () => _settings.SequentialPasteDelayMs, v => _settings.SequentialPasteDelayMs = v));
    return p;
  }

  private StackPanel BuildHotkeys()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Hotkeys"));
    p.Children.Add(Note("Restart may be required for hotkey changes to apply fully."));
    p.Children.Add(SubHeading("Quick access (sticky popup)"));
    p.Children.Add(Check("Enabled", () => _settings.QuickAccessHotkeyEnabled, v => _settings.QuickAccessHotkeyEnabled = v));
    p.Children.Add(TextField("Modifiers (e.g. Control,Shift)", () => _settings.QuickAccessModifiers, v => _settings.QuickAccessModifiers = v));
    p.Children.Add(TextField("Key (e.g. V)", () => _settings.QuickAccessKey, v => _settings.QuickAccessKey = v));
    p.Children.Add(SubHeading("Full manager window"));
    p.Children.Add(Check("Enabled", () => _settings.MainWindowHotkeyEnabled, v => _settings.MainWindowHotkeyEnabled = v));
    p.Children.Add(TextField("Modifiers", () => _settings.MainWindowModifiers, v => _settings.MainWindowModifiers = v));
    p.Children.Add(TextField("Key", () => _settings.MainWindowKey, v => _settings.MainWindowKey = v));
    p.Children.Add(SubHeading("Paste plain text"));
    p.Children.Add(Check("Enabled", () => _settings.PastePlainHotkeyEnabled, v => _settings.PastePlainHotkeyEnabled = v));
    p.Children.Add(TextField("Modifiers", () => _settings.PastePlainModifiers, v => _settings.PastePlainModifiers = v));
    p.Children.Add(TextField("Key", () => _settings.PastePlainKey, v => _settings.PastePlainKey = v));
    p.Children.Add(SubHeading("Screenshot capture"));
    p.Children.Add(Note("Hides CopyPaste Pro, starts Windows capture, then saves the image to history when done."));
    p.Children.Add(Check("Enabled", () => _settings.ScreenshotEnabled, v => _settings.ScreenshotEnabled = v));
    p.Children.Add(Check("Hotkey enabled", () => _settings.ScreenshotHotkeyEnabled, v => _settings.ScreenshotHotkeyEnabled = v));
    p.Children.Add(TextField("Modifiers", () => _settings.ScreenshotModifiers, v => _settings.ScreenshotModifiers = v));
    p.Children.Add(TextField("Key", () => _settings.ScreenshotKey, v => _settings.ScreenshotKey = v));
    p.Children.Add(TextField("Mode (Snip or FullScreen)", () => _settings.ScreenshotMode, v => _settings.ScreenshotMode = v));
    p.Children.Add(NumberField("Hide delay before capture (ms)", () => _settings.ScreenshotHideDelayMs, v => _settings.ScreenshotHideDelayMs = v));
    p.Children.Add(NumberField("Wait for capture timeout (sec)", () => _settings.ScreenshotWaitTimeoutSeconds, v => _settings.ScreenshotWaitTimeoutSeconds = v));
    p.Children.Add(Check("Restore windows after capture", () => _settings.ScreenshotRestoreWindows, v => _settings.ScreenshotRestoreWindows = v));
    p.Children.Add(Check("Open image library after capture", () => _settings.ScreenshotOpenImageLibraryAfterCapture, v => _settings.ScreenshotOpenImageLibraryAfterCapture = v));
    return p;
  }

  private StackPanel BuildTheme()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Theme & appearance"));
    var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
    themeRow.Children.Add(new TextBlock { Text = "Color theme", Width = 200, VerticalAlignment = VerticalAlignment.Center });
    var combo = new ComboBox { Width = 160 };
    combo.Items.Add("Dark");
    combo.Items.Add("Light");
    combo.SelectedItem = _settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
    combo.SelectionChanged += (_, _) =>
    {
      if (combo.SelectedItem is string t)
      {
        _settings.Theme = t;
        ThemeManager.Apply(_settings);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
      }
    };
    themeRow.Children.Add(combo);
    p.Children.Add(themeRow);
    p.Children.Add(NumberField("UI font size", () => (int)_settings.UiFontSize, v => _settings.UiFontSize = v));
    p.Children.Add(Check("Compact history rows", () => _settings.CompactHistoryRows, v => _settings.CompactHistoryRows = v));
    p.Children.Add(SubHeading("Quick access popup (sticky note)"));
    p.Children.Add(NumberField("Width", () => (int)_settings.QuickAccessWidth, v => _settings.QuickAccessWidth = v));
    p.Children.Add(NumberField("Height", () => (int)_settings.QuickAccessHeight, v => _settings.QuickAccessHeight = v));
    p.Children.Add(Check("Always on top", () => _settings.QuickAccessAlwaysOnTop, v => _settings.QuickAccessAlwaysOnTop = v));
    p.Children.Add(Check("Close when clicking outside", () => _settings.QuickAccessCloseOnFocusLoss, v => _settings.QuickAccessCloseOnFocusLoss = v));
    p.Children.Add(NumberField("Items shown", () => _settings.QuickAccessItemCount, v => _settings.QuickAccessItemCount = v));
    var posRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    posRow.Children.Add(new TextBlock { Text = "Open position", Width = 200, VerticalAlignment = VerticalAlignment.Center });
    var pos = new ComboBox { Width = 160 };
    pos.Items.Add("NearCursor");
    pos.Items.Add("CenterScreen");
    pos.SelectedItem = _settings.QuickAccessPosition;
    pos.SelectionChanged += (_, _) => { if (pos.SelectedItem is string s) _settings.QuickAccessPosition = s; };
    posRow.Children.Add(pos);
    p.Children.Add(posRow);
    return p;
  }

  private StackPanel BuildHistory()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("History & storage"));
    p.Children.Add(NumberField("Max history items", () => _settings.MaxHistoryItems, v => _settings.MaxHistoryItems = v));
    p.Children.Add(NumberField("Max pinned items", () => _settings.MaxPinnedItems, v => _settings.MaxPinnedItems = v));
    p.Children.Add(NumberField("Auto-delete after days (0=never)", () => _settings.AutoDeleteAfterDays, v => _settings.AutoDeleteAfterDays = v));
    p.Children.Add(Check("Store images on disk", () => _settings.StoreImagesOnDisk, v => _settings.StoreImagesOnDisk = v));
    p.Children.Add(Check("Compress image storage", () => _settings.CompressImageStorage, v => _settings.CompressImageStorage = v));
    p.Children.Add(TextField("Custom database path (empty=default)", () => _settings.HistoryDatabasePath, v => _settings.HistoryDatabasePath = v));
    return p;
  }

  private StackPanel BuildImageLibrary()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Image library"));
    p.Children.Add(Note("Manage auto-save, sync folder, and how images appear in the library panel."));

    p.Children.Add(SubHeading("Sync folder"));
    p.Children.Add(Note(
        "Links the library to one folder: existing images there show up, and new clipboard images can be saved into it. "
        + "Different from auto-save, which only copies new captures and does not import files already on disk."));
    p.Children.Add(Check("Enable sync folder", () => _settings.ImageLibrarySyncFolderEnabled,
        v => _settings.ImageLibrarySyncFolderEnabled = v));
    p.Children.Add(FolderPathRow("Sync folder path", () => _settings.ImageLibrarySyncFolderPath,
        v => _settings.ImageLibrarySyncFolderPath = v, "Choose sync folder"));
    p.Children.Add(Check("Show images in subfolders", () => _settings.ImageLibrarySyncScanSubfolders,
        v => _settings.ImageLibrarySyncScanSubfolders = v));
    p.Children.Add(Check("Save new clipboard images to sync folder", () => _settings.ImageLibrarySyncSaveNewCaptures,
        v => _settings.ImageLibrarySyncSaveNewCaptures = v));
    var openSync = new Button
    {
      Content = "Open sync folder",
      Style = (Style)FindResource("GhostButton"),
      HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
      Margin = new Thickness(0, 4, 0, 8)
    };
    openSync.Click += (_, _) =>
    {
      if (string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath))
        AppDialog.Warning("Choose a sync folder path first.", "Sync folder", Window.GetWindow(this));
      else
        ImageLibraryService.OpenFolder(_settings.ImageLibrarySyncFolderPath);
    };
    p.Children.Add(openSync);

    p.Children.Add(SubHeading("Auto-save"));
    p.Children.Add(Note("Optionally copy each new clipboard image to a separate folder (does not import existing files)."));
    p.Children.Add(Check("Enable auto-save", () => _settings.ImageLibraryAutoSaveEnabled,
        v => _settings.ImageLibraryAutoSaveEnabled = v));
    p.Children.Add(FolderPathRow("Auto-save folder", () => _settings.ImageLibraryAutoSaveFolder,
        v => _settings.ImageLibraryAutoSaveFolder = v, "Choose auto-save folder", () =>
            string.IsNullOrWhiteSpace(_settings.ImageLibraryAutoSaveFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CopyPaste Pro")
                : _settings.ImageLibraryAutoSaveFolder));
    p.Children.Add(Check("Open folder after saving", () => _settings.ImageLibraryOpenFolderAfterSave,
        v => _settings.ImageLibraryOpenFolderAfterSave = v));

    p.Children.Add(SubHeading("Files & naming"));
    p.Children.Add(ComboField("Save format", ["png", "jpg"], () => _settings.ImageLibrarySaveFormat,
        v => _settings.ImageLibrarySaveFormat = v));
    p.Children.Add(ComboField("Duplicate files", ["rename", "skip", "overwrite"],
        () => _settings.ImageLibraryDuplicateHandling, v => _settings.ImageLibraryDuplicateHandling = v));
    p.Children.Add(Check("Organize auto-save by date subfolders", () => _settings.ImageLibraryOrganizeByDate,
        v => _settings.ImageLibraryOrganizeByDate = v));
    p.Children.Add(TextField("File name pattern", () => _settings.ImageLibraryFileNamePattern,
        v => _settings.ImageLibraryFileNamePattern = v));

    p.Children.Add(SubHeading("Library display"));
    p.Children.Add(NumberField("Thumbnail size (px)", () => _settings.ImageLibraryThumbnailSize,
        v => _settings.ImageLibraryThumbnailSize = Math.Clamp(v, 80, 220)));
    p.Children.Add(Check("Show dimensions on thumbnails", () => _settings.ImageLibraryShowDimensionsOnThumb,
        v => _settings.ImageLibraryShowDimensionsOnThumb = v));
    p.Children.Add(Check("Focus preview mode (narrow list + large preview)",
        () => _settings.ImageLibraryFocusPreview, v => _settings.ImageLibraryFocusPreview = v));
    return p;
  }

  private UIElement FolderPathRow(string label, Func<string> get, Action<string> set, string browseTitle,
      Func<string>? defaultPath = null)
  {
    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
    sp.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("TextMutedBrush"), FontSize = 11 });
    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
    var tb = new TextBox { Text = get(), MinWidth = 280, Margin = new Thickness(0, 0, 8, 0) };
    tb.LostFocus += (_, _) => set(tb.Text.Trim());
    var browse = new Button { Content = "Browse…", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 6, 12, 6) };
    browse.Click += (_, _) =>
    {
      using var dlg = new Forms.FolderBrowserDialog
      {
        Description = browseTitle,
        SelectedPath = string.IsNullOrWhiteSpace(get())
            ? (defaultPath?.Invoke() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
            : get(),
        ShowNewFolderButton = true
      };
      if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
      tb.Text = dlg.SelectedPath;
      set(dlg.SelectedPath);
    };
    row.Children.Add(tb);
    row.Children.Add(browse);
    sp.Children.Add(row);
    return sp;
  }

  private static UIElement ComboField(string label, string[] options, Func<string> get, Action<string> set)
  {
    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
    sp.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("TextMutedBrush"), FontSize = 11 });
    var combo = new ComboBox { Width = 200, Margin = new Thickness(0, 4, 0, 0) };
    foreach (var o in options) combo.Items.Add(o);
    var current = get();
    combo.SelectedItem = options.FirstOrDefault(o => o.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    combo.SelectionChanged += (_, _) =>
    {
      if (combo.SelectedItem is string s) set(s);
    };
    sp.Children.Add(combo);
    return sp;
  }

  private StackPanel BuildNotifications()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Notifications"));
    p.Children.Add(Check("Sound on capture", () => _settings.PlaySoundOnCapture, v => _settings.PlaySoundOnCapture = v));
    p.Children.Add(Check("Balloon tip on capture", () => _settings.BalloonOnCapture, v => _settings.BalloonOnCapture = v));
    p.Children.Add(Check("Balloon tip on paste", () => _settings.BalloonOnPaste, v => _settings.BalloonOnPaste = v));
    p.Children.Add(Check("Flash tray icon on capture", () => _settings.FlashTrayOnCapture, v => _settings.FlashTrayOnCapture = v));
    return p;
  }

  private StackPanel BuildAutomation()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Automation & rules"));
    p.Children.Add(Check("Enable rules engine", () => _settings.RulesEnabled, v => _settings.RulesEnabled = v));
    p.Children.Add(Check("Auto-delete sensitive patterns", () => _settings.AutoDeleteSensitivePatterns, v => _settings.AutoDeleteSensitivePatterns = v));
    p.Children.Add(TextField("Sensitive keywords (comma-separated)", () => _settings.SensitivePatterns, v => _settings.SensitivePatterns = v));
    p.Children.Add(Check("Auto-clear history on Windows logout", () => _settings.AutoClearOnLogout, v => _settings.AutoClearOnLogout = v));
    p.Children.Add(SubHeading("Rules"));
    p.Children.Add(Note("When a rule matches on capture, its action runs automatically."));
    var rulesList = new ListBox { Height = 120, Margin = new Thickness(0, 8, 0, 8), DisplayMemberPath = "Name" };
    void RefreshRules() { rulesList.ItemsSource = null; rulesList.ItemsSource = _settings.Rules.ToList(); }
    RefreshRules();
    p.Children.Add(rulesList);
    var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
    var addBtn = new Button { Content = "Add rule", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
    addBtn.Click += (_, _) =>
    {
      _settings.Rules.Add(new ClipboardRule { Name = $"Rule {_settings.Rules.Count + 1}" });
      RefreshRules();
    };
    var delBtn = new Button { Content = "Remove selected", Style = (Style)FindResource("GhostButton") };
    delBtn.Click += (_, _) =>
    {
      if (rulesList.SelectedItem is ClipboardRule r) _settings.Rules.Remove(r);
      RefreshRules();
    };
    btnRow.Children.Add(addBtn);
    btnRow.Children.Add(delBtn);
    p.Children.Add(btnRow);
    if (_settings.Rules.Count == 0)
    {
      _settings.Rules.Add(new ClipboardRule
      {
        Name = "Ignore tiny clips",
        ConditionType = RuleConditionType.SmallerThanBytes,
        ConditionValue = "3",
        Action = RuleAction.Ignore
      });
    }
    return p;
  }

  private StackPanel BuildPrivacy()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Privacy & security"));
    p.Children.Add(Note("All data stays on this PC. No cloud sync. Configure what gets captured, stored, shown, and deleted."));

    p.Children.Add(SubHeading("Block sensitive content (never save)"));
    p.Children.Add(Check("Credit card numbers", () => _settings.BlockCreditCards, v => _settings.BlockCreditCards = v));
    p.Children.Add(Check("Social Security numbers (US)", () => _settings.BlockSocialSecurityNumbers, v => _settings.BlockSocialSecurityNumbers = v));
    p.Children.Add(Check("IBAN / bank account numbers", () => _settings.BlockIbanAndBankNumbers, v => _settings.BlockIbanAndBankNumbers = v));
    p.Children.Add(Check("Email addresses", () => _settings.BlockEmailAddresses, v => _settings.BlockEmailAddresses = v));
    p.Children.Add(Check("Phone numbers", () => _settings.BlockPhoneNumbers, v => _settings.BlockPhoneNumbers = v));
    p.Children.Add(Check("API keys & secrets", () => _settings.BlockApiKeysAndTokens, v => _settings.BlockApiKeysAndTokens = v));
    p.Children.Add(Check("JWT tokens", () => _settings.BlockJwtTokens, v => _settings.BlockJwtTokens = v));
    p.Children.Add(Check("Private keys (PEM)", () => _settings.BlockPrivateKeys, v => _settings.BlockPrivateKeys = v));
    p.Children.Add(Check("Password-like text (password=…)", () => _settings.BlockPasswordLikeContent, v => _settings.BlockPasswordLikeContent = v));
    p.Children.Add(Check("Crypto wallet addresses", () => _settings.BlockCryptoWalletAddresses, v => _settings.BlockCryptoWalletAddresses = v));
    p.Children.Add(Check("Sensitive keywords", () => _settings.BlockSensitiveKeywords, v => _settings.BlockSensitiveKeywords = v));
    p.Children.Add(TextField("Keywords (comma-separated)", () => _settings.SensitivePatterns, v => _settings.SensitivePatterns = v));
    p.Children.Add(TextField("Blocked URL domains (comma-separated)", () => string.Join(", ", _settings.BlockedDomains),
        v => _settings.BlockedDomains = SplitList(v)));
    p.Children.Add(TextField("Custom block regex", () => _settings.CustomBlockedRegex, v => _settings.CustomBlockedRegex = v));

    p.Children.Add(SubHeading("Private / Incognito browsing"));
    p.Children.Add(Note(
        "Location: Settings → Privacy → Private / Incognito browsing (below).\n"
        + "Detected from the active window title (InPrivate, Incognito, Private Browsing, and similar). "
        + "When restriction is on, choose which types to save; blocked types can show a one-time allow prompt."));
    p.Children.Add(Check("Restrict copies from private/incognito browser windows",
        () => _settings.IncognitoRestrictEnabled, v => _settings.IncognitoRestrictEnabled = v));
    p.Children.Add(Check("Save text from private windows (includes HTML & rich text)",
        () => _settings.IncognitoSaveText, v => _settings.IncognitoSaveText = v));
    p.Children.Add(Check("Save images from private windows (not the same as “files”)",
        () => _settings.IncognitoSaveImages, v => _settings.IncognitoSaveImages = v));
    p.Children.Add(Check("Save files from private windows",
        () => _settings.IncognitoSaveFiles, v => _settings.IncognitoSaveFiles = v));
    p.Children.Add(Check("Ask before saving when a type is blocked (Allow only this time)",
        () => _settings.IncognitoPromptWhenBlocked, v => _settings.IncognitoPromptWhenBlocked = v));

    p.Children.Add(SubHeading("Apps & browsers"));
    p.Children.Add(Check("Never capture from password managers", () => _settings.NeverCaptureFromPasswordManagers, v => _settings.NeverCaptureFromPasswordManagers = v));
    p.Children.Add(TextField("Password manager processes", () => string.Join(", ", _settings.PasswordManagerProcesses),
        v => _settings.PasswordManagerProcesses = SplitList(v)));
    p.Children.Add(Check("Never capture from banking apps", () => _settings.NeverCaptureFromBankingApps, v => _settings.NeverCaptureFromBankingApps = v));
    p.Children.Add(TextField("Banking processes (comma-separated)", () => string.Join(", ", _settings.BankingProcesses),
        v => _settings.BankingProcesses = SplitList(v)));
    p.Children.Add(TextField("Excluded applications (comma-separated)", () => string.Join(", ", _settings.ExcludedApplications),
        v => _settings.ExcludedApplications = SplitList(v)));
    p.Children.Add(Check("Allowlist mode — only capture from listed apps", () => _settings.AllowlistModeOnlyCaptureFromListed, v => _settings.AllowlistModeOnlyCaptureFromListed = v));
    p.Children.Add(TextField("Trusted processes only (comma-separated)", () => string.Join(", ", _settings.TrustedProcessesOnly),
        v => _settings.TrustedProcessesOnly = SplitList(v)));

    p.Children.Add(SubHeading("Storage"));
    p.Children.Add(Check("Never store file paths (metadata only)", () => _settings.NeverStoreFilePaths, v => _settings.NeverStoreFilePaths = v));
    p.Children.Add(Check("Store preview only (drop full text)", () => _settings.StoreOnlyPreviewNotFullText, v => _settings.StoreOnlyPreviewNotFullText = v));
    p.Children.Add(Check("Redact sensitive text in database", () => _settings.RedactSensitiveInStorage, v => _settings.RedactSensitiveInStorage = v));
    p.Children.Add(Check("Hash sensitive previews", () => _settings.HashSensitivePreviews, v => _settings.HashSensitivePreviews = v));
    p.Children.Add(Check("Never store images", () => _settings.NeverStoreImages, v => _settings.NeverStoreImages = v));
    p.Children.Add(Check("Strip HTML tags when storing", () => _settings.StripHtmlWhenStoring, v => _settings.StripHtmlWhenStoring = v));
    p.Children.Add(NumberField("Auto-delete sensitive items after (minutes, 0=off)", () => _settings.AutoDeleteSensitiveAfterMinutes, v => _settings.AutoDeleteSensitiveAfterMinutes = v));

    p.Children.Add(SubHeading("Display"));
    p.Children.Add(Check("Mask sensitive items in lists", () => _settings.MaskSensitiveInUi, v => _settings.MaskSensitiveInUi = v));
    p.Children.Add(Check("Hide preview for sensitive items", () => _settings.DisablePreviewForSensitive, v => _settings.DisablePreviewForSensitive = v));
    p.Children.Add(Check("Hide source app name in UI", () => _settings.HideSourceAppInUi, v => _settings.HideSourceAppInUi = v));

    p.Children.Add(SubHeading("Session & shutdown"));
    p.Children.Add(Check("Pause capture when PC is locked", () => _settings.PauseCaptureOnLock, v => _settings.PauseCaptureOnLock = v));
    p.Children.Add(Check("Clear unpinned history when locked", () => _settings.ClearUnpinnedOnLock, v => _settings.ClearUnpinnedOnLock = v));
    p.Children.Add(Check("Clear all history when locked", () => _settings.ClearHistoryOnLock, v => _settings.ClearHistoryOnLock = v));
    p.Children.Add(Check("Clear system clipboard when locked", () => _settings.ClearClipboardOnLock, v => _settings.ClearClipboardOnLock = v));
    p.Children.Add(Check("Clear history on app exit", () => _settings.ClearHistoryOnExit, v => _settings.ClearHistoryOnExit = v));
    p.Children.Add(Check("Clear system clipboard on exit", () => _settings.ClearClipboardOnExit, v => _settings.ClearClipboardOnExit = v));
    p.Children.Add(Check("Auto-clear on Windows sign-out", () => _settings.AutoClearOnLogout, v => _settings.AutoClearOnLogout = v));

    p.Children.Add(SubHeading("Encryption (Windows DPAPI — this user account)"));
    p.Children.Add(Check("Encrypt text in database", () => _settings.EncryptDatabase, v => _settings.EncryptDatabase = v));
    p.Children.Add(Check("Encrypt image payload files", () => _settings.EncryptPayloadFiles, v => _settings.EncryptPayloadFiles = v));
    p.Children.Add(Note("Encryption protects files on disk from other users. It is not a cloud backup password."));

    p.Children.Add(SubHeading("Secure deletion"));
    p.Children.Add(NumberField("Overwrite passes when deleting files (0=off)", () => _settings.SecureDeletePasses, v => _settings.SecureDeletePasses = v));
    p.Children.Add(Check("Secure-delete image payload files", () => _settings.SecureDeletePayloadFiles, v => _settings.SecureDeletePayloadFiles = v));

    p.Children.Add(SubHeading("Privacy lock & panic"));
    p.Children.Add(TextField("PIN to open manager (empty=off)", () => _settings.PrivacyLockPin, v => _settings.PrivacyLockPin = v));
    p.Children.Add(Check("Require PIN to open manager", () => _settings.RequirePinToOpenManager, v => _settings.RequirePinToOpenManager = v));
    p.Children.Add(Check("Enable panic hotkey", () => _settings.PanicHotkeyEnabled, v => _settings.PanicHotkeyEnabled = v));
    p.Children.Add(TextField("Panic modifiers", () => _settings.PanicHotkeyModifiers, v => _settings.PanicHotkeyModifiers = v));
    p.Children.Add(TextField("Panic key", () => _settings.PanicHotkeyKey, v => _settings.PanicHotkeyKey = v));
    p.Children.Add(Check("Panic clears history", () => _settings.PanicClearsHistory, v => _settings.PanicClearsHistory = v));
    p.Children.Add(Check("Panic clears system clipboard", () => _settings.PanicClearsClipboard, v => _settings.PanicClearsClipboard = v));

    p.Children.Add(SubHeading("Actions"));
    var purge = new Button { Content = "Delete all sensitive items now", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 8, 0, 4) };
    purge.Click += (_, _) =>
    {
      var n = _privacy.PurgeSensitive(_repository);
      AppDialog.Info($"Removed {n} sensitive item(s).", "Privacy", Window.GetWindow(this));
    };
    p.Children.Add(purge);
    var wipe = new Button { Content = "Panic wipe — history + clipboard", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 4, 0, 8) };
    wipe.Click += (_, _) =>
        _quickActions?.PanicWipe(Window.GetWindow(this), respectSettings: false);
    p.Children.Add(wipe);
    return p;
  }

  private StackPanel BuildAdvanced()
  {
    var p = new StackPanel();
    p.Children.Add(Heading("Advanced"));
    p.Children.Add(Check("Debug logging", () => _settings.DebugLogging, v => _settings.DebugLogging = v));
    p.Children.Add(Check("Export history on crash", () => _settings.ExportHistoryOnCrash, v => _settings.ExportHistoryOnCrash = v));
    p.Children.Add(NumberField("Database vacuum every N days", () => _settings.DatabaseVacuumEveryDays, v => _settings.DatabaseVacuumEveryDays = v));
    p.Children.Add(NumberField("Clipboard poll fallback (ms, 0=off)", () => _settings.ClipboardPollFallbackMs, v => _settings.ClipboardPollFallbackMs = v));
    return p;
  }

  private static System.Windows.Media.Brush ThemeBrush(string key) =>
      (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource(key);

  private static TextBlock Heading(string t) => new()
  {
    Text = t, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12),
    Foreground = ThemeBrush("TextPrimaryBrush")
  };

  private static TextBlock SubHeading(string t) => new()
  {
    Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6),
    Foreground = ThemeBrush("TextPrimaryBrush")
  };

  private static TextBlock Note(string t) => new()
  {
    Text = t, Foreground = ThemeBrush("TextMutedBrush"), TextWrapping = TextWrapping.Wrap,
    Margin = new Thickness(0, 0, 0, 8), FontSize = 11
  };

  private static UIElement Check(string label, Func<bool> get, Action<bool> set)
  {
    var cb = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 4, 0, 4) };
    cb.Checked += (_, _) => set(true);
    cb.Unchecked += (_, _) => set(false);
    return cb;
  }

  private static UIElement TextField(string label, Func<string> get, Action<string> set)
  {
    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
    sp.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("TextMutedBrush"), FontSize = 11 });
    var tb = new TextBox { Text = get(), Margin = new Thickness(0, 4, 0, 0) };
    tb.LostFocus += (_, _) => set(tb.Text);
    sp.Children.Add(tb);
    return sp;
  }

  private static UIElement NumberField(string label, Func<int> get, Action<int> set)
  {
    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
    sp.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("TextMutedBrush"), FontSize = 11 });
    var tb = new TextBox { Text = get().ToString(), Margin = new Thickness(0, 4, 0, 0) };
    tb.LostFocus += (_, _) => { if (int.TryParse(tb.Text, out var n)) set(n); };
    sp.Children.Add(tb);
    return sp;
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    var owner = Window.GetWindow(this);
    if (_settings.ImageLibrarySyncFolderEnabled && string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath))
    {
      AppDialog.Warning("Sync folder is enabled but no folder path is set. Choose a folder or disable sync.", "Image library", owner);
      return;
    }

    _settings.Save();
    ThemeManager.Apply(_settings);
    _sensitiveRetention?.ResetTimer();
    if (_settings.RecategorizeOnSaveSettings)
      _repository.RecategorizeAll(_classifier.Classify);
    SettingsSaved?.Invoke(this, EventArgs.Empty);
    HotkeysChanged?.Invoke(this, EventArgs.Empty);
    AppDialog.Info("Settings saved.", "CopyPaste Pro", owner);
  }

  private void ResetSection_Click(object sender, RoutedEventArgs e)
  {
    var cat = _currentCategory;
    // Reset only matching properties via reflection is heavy; rebuild panel from defaults for category
    AppDialog.Info($"To reset '{cat}' defaults, use Save after editing or delete settings.json.", "CopyPaste Pro",
        Window.GetWindow(this));
  }
}
