using System.Drawing;
using System.Windows;
using CopyPastePro.Models;
using CopyPastePro.Services;
using Application = System.Windows.Application;
namespace CopyPastePro;

public partial class App : Application
{
  private ClipboardHistoryRepository? _repository;
  private ClipboardCaptureService? _capture;
  private ClipboardPasteService? _paste;
  private ClipboardRulesEngine? _rules;
  private CategoryClassifier? _classifier;
  private SmartOrganizerService? _organizer;
  private AutoBackupService? _backup;
  private ClipboardMonitor? _monitor;
  private GlobalHotkeyService? _hotkey;
  private MainWindow? _mainWindow;
  private QuickAccessWindow? _quickAccess;
  private HotkeyHostWindow? _hotkeyHost;
  private System.Windows.Forms.NotifyIcon? _tray;
  private AppSettings? _settings;
  private PrivacyService? _privacy;
  private SessionPrivacyMonitor? _sessionPrivacy;
  private SensitiveRetentionService? _sensitiveRetention;
  private ImageLibraryService? _imageLibrary;
  private ScreenshotCaptureService? _screenshot;
  private ClipboardQuickActionsService? _quickActions;
  private WindowsClipboardOverrideService? _windowsClipboardOverride;
  private ResourceThrottleService? _resourceThrottle;
  private System.Windows.Forms.ToolStripMenuItem? _trayToggleCaptureItem;

  private void Application_Startup(object sender, StartupEventArgs e)
  {
    DispatcherUnhandledException += (_, args) =>
    {
      AppDialog.Error(args.Exception.Message, "CopyPaste Pro — Error", _mainWindow);
      args.Handled = true;
    };

    _settings = AppSettings.Load();
    ThemeManager.Apply(_settings);
    AppIconSource.RegisterApplicationResources();

    _resourceThrottle = new ResourceThrottleService(_settings);
    _windowsClipboardOverride = new WindowsClipboardOverrideService(_settings);
    _windowsClipboardOverride.BindThrottle(_resourceThrottle);
    _windowsClipboardOverride.ApplyAtStartup();
    _windowsClipboardOverride.StartEnforcement(Dispatcher, ShowWindowsClipboardHistoryBlockedAlert);

    _privacy = new PrivacyService(_settings);
    _repository = new ClipboardHistoryRepository();
    _rules = new ClipboardRulesEngine(_settings, _privacy);
    _classifier = new CategoryClassifier(_settings);
    _organizer = new SmartOrganizerService(_settings, _repository);
    _imageLibrary = new ImageLibraryService(_settings, _repository);
    _paste = new ClipboardPasteService(_settings, _repository);
    _capture = new ClipboardCaptureService(_repository, _settings, _rules, _classifier, _organizer, _privacy, _imageLibrary);
    _capture.EntryCaptured += OnEntryCaptured;
    _quickActions = new ClipboardQuickActionsService(_settings, _repository, _capture, _paste, _privacy, RefreshAllUi);
    _sessionPrivacy = new SessionPrivacyMonitor(_privacy, _repository, _settings);
    _sensitiveRetention = new SensitiveRetentionService(_settings, _repository, _privacy);

    _backup = new AutoBackupService(_settings);
    _backup.BindThrottle(_resourceThrottle);
    _backup.Start();

    _mainWindow = new MainWindow(_repository, _capture, _paste, _settings, _classifier, _backup, _privacy, _sensitiveRetention, _imageLibrary, OpenQuickAccess, _quickActions, _resourceThrottle);
    _mainWindow.Closing += MainWindow_Closing;
    _mainWindow.HotkeysChanged += (_, _) => RegisterHotkeys();

    _quickAccess = new QuickAccessWindow(_repository, _capture, _paste, _settings, ShowMain, _quickActions);

    _screenshot = new ScreenshotCaptureService(_settings, _capture, _mainWindow, _quickAccess);
    _mainWindow.SetScreenshotCapture(() => _screenshot.StartCapture());

    _hotkeyHost = new HotkeyHostWindow();
    // Create HWND for RegisterHotKey without Show() — avoids stealing focus from quick access.
    _ = new System.Windows.Interop.WindowInteropHelper(_hotkeyHost).EnsureHandle();

    _monitor = new ClipboardMonitor();
    _monitor.ClipboardChanged += (_, _) => Dispatcher.BeginInvoke(() => _capture?.ProcessClipboard());
    _monitor.Start(_mainWindow);
    _quickActions?.ConfigureWipeDependencies(_monitor, _backup!, _windowsClipboardOverride);

    RegisterHotkeys();
    SetupTray();

    try { _capture.ProcessClipboard(); } catch { }
    _mainWindow.RefreshList();

    if (!_settings.StartMinimized)
      _mainWindow.Show();
    else
      _mainWindow.Hide();
  }

  private void RegisterHotkeys()
  {
    if (_hotkey == null)
    {
      _hotkey = new GlobalHotkeyService();
      _hotkey.HotkeyPressed += (_, id) => Dispatcher.BeginInvoke(() => OnHotkey(id));
    }

    _hotkey.Unregister(HotkeyIds.QuickAccess);
    _hotkey.Unregister(HotkeyIds.MainWindow);
    _hotkey.Unregister(HotkeyIds.PanicPrivacy);
    _hotkey.Unregister(HotkeyIds.Screenshot);

    if (_settings!.QuickAccessHotkeyEnabled)
    {
      try
      {
        var mod = GlobalHotkeyService.ParseModifiers(_settings.QuickAccessModifiers);
        var key = GlobalHotkeyService.ParseKey(_settings.QuickAccessKey);
        _hotkey.Register(_hotkeyHost!, HotkeyIds.QuickAccess, mod, key);
      }
      catch (Exception ex)
      {
        AppDialog.Warning($"Quick access hotkey invalid: {ex.Message}", "CopyPaste Pro", _mainWindow);
      }
    }

    if (_settings.MainWindowHotkeyEnabled)
    {
      try
      {
        var mod = GlobalHotkeyService.ParseModifiers(_settings.MainWindowModifiers);
        var key = GlobalHotkeyService.ParseKey(_settings.MainWindowKey);
        _hotkey.Register(_hotkeyHost!, HotkeyIds.MainWindow, mod, key);
      }
      catch { }
    }

    if (_settings.PanicHotkeyEnabled)
    {
      try
      {
        var mod = GlobalHotkeyService.ParseModifiers(_settings.PanicHotkeyModifiers);
        var key = GlobalHotkeyService.ParseKey(_settings.PanicHotkeyKey);
        _hotkey.Register(_hotkeyHost!, HotkeyIds.PanicPrivacy, mod, key);
      }
      catch { }
    }

    if (_settings.ScreenshotEnabled && _settings.ScreenshotHotkeyEnabled)
    {
      try
      {
        var mod = GlobalHotkeyService.ParseModifiers(_settings.ScreenshotModifiers);
        var key = GlobalHotkeyService.ParseKey(_settings.ScreenshotKey);
        _hotkey.Register(_hotkeyHost!, HotkeyIds.Screenshot, mod, key);
      }
      catch (Exception ex)
      {
        AppDialog.Warning($"Screenshot hotkey invalid: {ex.Message}", "CopyPaste Pro", _mainWindow);
      }
    }
  }

  private void OnHotkey(int id)
  {
    switch (id)
    {
      case HotkeyIds.QuickAccess:
        _quickAccess?.ApplySizeFromSettings();
        _quickAccess?.ShowNearCursor();
        break;
      case HotkeyIds.MainWindow:
        ShowMain();
        break;
      case HotkeyIds.PanicPrivacy:
        _quickActions?.PanicWipe(_mainWindow, respectSettings: true);
        break;
      case HotkeyIds.Screenshot:
        _screenshot?.StartCapture();
        break;
    }
  }

  private void SetupTray()
  {
    _tray = new System.Windows.Forms.NotifyIcon
    {
      Icon = AppIconSource.Tray,
      Text = "CopyPaste Pro",
      Visible = _settings!.ShowTrayIcon
    };
    var menu = new System.Windows.Forms.ContextMenuStrip();
    menu.Items.Add("Quick access", null, (_, _) => Dispatcher.BeginInvoke(OpenQuickAccess));
    menu.Items.Add("Open manager", null, (_, _) => Dispatcher.BeginInvoke(ShowMain));
    menu.Items.Add("Image library", null, (_, _) => Dispatcher.BeginInvoke(() => { ShowMain(); _mainWindow?.OpenImageLibraryPanel(); }));
    menu.Items.Add("Screenshot (hide app → snip)", null, (_, _) => Dispatcher.BeginInvoke(() => _screenshot?.StartCapture()));
    menu.Items.Add("Paste latest", null, (_, _) => Dispatcher.BeginInvoke(() => _quickActions?.PasteLatestToApp(_mainWindow)));
    menu.Items.Add("Settings", null, (_, _) => Dispatcher.BeginInvoke(() => { ShowMain(); _mainWindow?.OpenSettingsPanel(); }));
    menu.Items.Add("-");
    var clipboardMenu = new System.Windows.Forms.ToolStripMenuItem("Clipboard actions");
    clipboardMenu.DropDownItems.Add("Clear clipboard", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.ClearSystemClipboard(_mainWindow)));
    clipboardMenu.DropDownItems.Add("Copy latest to clipboard", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.CopyLatestToClipboard(_mainWindow)));
    clipboardMenu.DropDownItems.Add("Clear unpinned (app only)", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.ClearUnpinnedHistory(_mainWindow)));
    clipboardMenu.DropDownItems.Add("Clear app history", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.ClearAppHistory(_mainWindow)));
    clipboardMenu.DropDownItems.Add("Clear app + Windows clipboard", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.ClearAllHistory(_mainWindow)));
    menu.Items.Add(clipboardMenu);
    menu.Items.Add("-");
    _trayToggleCaptureItem = new System.Windows.Forms.ToolStripMenuItem("Pause capture", null, (_, _) =>
        Dispatcher.BeginInvoke(() => _quickActions?.ToggleCapturePaused(_mainWindow)));
    menu.Items.Add(_trayToggleCaptureItem);
    menu.Opening += (_, _) =>
    {
      if (_trayToggleCaptureItem != null && _quickActions != null)
        _trayToggleCaptureItem.Text = _quickActions.IsCapturePaused ? "Resume capture" : "Pause capture";
    };
    menu.Items.Add("Panic — wipe history", null, (_, _) => Dispatcher.BeginInvoke(RunPanicWipe));
    menu.Items.Add("-");
    menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(ShutdownApp));
    menu.Items.Add("Terminate", null, (_, _) => Dispatcher.BeginInvoke(TerminateApp));
    _tray.ContextMenuStrip = menu;
    _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenQuickAccess);
  }

  private void OnEntryCaptured(object? sender, Models.ClipboardEntry entry)
  {
    _mainWindow?.RefreshList();
    _quickAccess?.Refresh();
    if (entry.ContentType == ClipboardContentType.Image && _settings?.ImageLibraryOpenFolderAfterSave == true
        && !string.IsNullOrEmpty(entry.ExportPath))
      ImageLibraryService.OpenInExplorer(entry.ExportPath);
    if (_settings?.PlaySoundOnCapture == true)
    {
      try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }
  }

  private void OpenQuickAccess() => _quickAccess?.ShowNearCursor();

  private void ShowMain()
  {
    if (_mainWindow == null) return;
    if (_settings?.RequirePinToOpenManager == true && !string.IsNullOrEmpty(_settings.PrivacyLockPin))
    {
      if (!Views.PrivacyPinDialog.TryUnlock(_privacy!, _settings)) return;
    }
    _mainWindow.Show();
    _mainWindow.WindowState = WindowState.Normal;
    _mainWindow.Activate();
    _mainWindow.Topmost = true;
    _mainWindow.Topmost = false;
    _mainWindow.Focus();
  }

  private void RefreshAllUi()
  {
    _mainWindow?.RefreshList();
    _quickAccess?.Refresh();
  }

  private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
  {
    e.Cancel = true;
    _mainWindow?.Hide();
  }

  private void RunPanicWipe() => _quickActions?.PanicWipe(_mainWindow, respectSettings: false);

  private void ShutdownApp()
  {
    if (_settings?.ConfirmOnExit == true &&
        !AppDialog.Confirm("Exit CopyPaste Pro?", "Confirm", _mainWindow))
      return;

    PerformShutdown();
  }

  /// <summary>Immediately stop capture, release resources, and exit (no confirmation).</summary>
  private void TerminateApp() => PerformShutdown();

  private void ShowWindowsClipboardHistoryBlockedAlert() =>
      AppDialog.Warning(
          "Windows clipboard history (Win+V) was turned on.\n\n"
          + "CopyPaste Pro requires this setting to stay off and has disabled it again. "
          + "Use CopyPaste Pro for your clipboard history while the app is running.",
          "Clipboard history blocked",
          _mainWindow);

  private void PerformShutdown()
  {
    _windowsClipboardOverride?.RestoreOnExit();
    _privacy?.OnExit(_repository!);
    _sessionPrivacy?.Dispose();
    _sensitiveRetention?.Dispose();
    _backup?.Dispose();
    _resourceThrottle?.Dispose();
    _tray!.Visible = false;
    _tray.Dispose();
    _monitor?.Dispose();
    _hotkey?.Dispose();
    _repository?.Dispose();
    Shutdown();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _windowsClipboardOverride?.RestoreOnExit();
    if (_privacy != null && _repository != null) _privacy.OnExit(_repository);
    _sessionPrivacy?.Dispose();
    _sensitiveRetention?.Dispose();
    _backup?.Dispose();
    _resourceThrottle?.Dispose();
    _tray?.Dispose();
    _monitor?.Dispose();
    _hotkey?.Dispose();
    _repository?.Dispose();
    base.OnExit(e);
  }
}
