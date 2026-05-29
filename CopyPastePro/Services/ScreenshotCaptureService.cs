using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CopyPastePro.Controls;
using Clipboard = System.Windows.Clipboard;

namespace CopyPastePro.Services;

public sealed class ScreenshotCaptureService
{
  private readonly AppSettings _settings;
  private readonly ClipboardCaptureService _capture;
  private readonly MainWindow _mainWindow;
  private readonly QuickAccessWindow _quickAccess;
  private readonly Dispatcher _dispatcher;
  private bool _busy;

  public ScreenshotCaptureService(
      AppSettings settings,
      ClipboardCaptureService capture,
      MainWindow mainWindow,
      QuickAccessWindow quickAccess)
  {
    _settings = settings;
    _capture = capture;
    _mainWindow = mainWindow;
    _quickAccess = quickAccess;
    _dispatcher = mainWindow.Dispatcher;
  }

  public bool IsBusy => _busy;

  public void StartCapture()
  {
    if (_busy || !_settings.ScreenshotEnabled)
      return;
    _ = RunCaptureAsync();
  }

  private async Task RunCaptureAsync()
  {
    _busy = true;
    HiddenWindowState? hidden = null;
    var captured = false;

    try
    {
      hidden = await _dispatcher.InvokeAsync(HideAppWindows);
      // Let windows finish hiding before starting the OS capture UI.
      await Task.Delay(Math.Max(_settings.ScreenshotHideDelayMs, 350));

      var seqBefore = ScreenshotInput.GetClipboardSequenceNumber();
      var mode = ParseMode(_settings.ScreenshotMode);

      // Run on a dedicated STA thread — SendInput and shell launch are more reliable than thread-pool.
      await Task.Run(() =>
      {
        var thread = new Thread(() => ScreenshotInput.Trigger(mode))
        {
          IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));
      });

      captured = await WaitForClipboardImageAsync(seqBefore);
      if (captured)
      {
        await Task.Delay(120);
        await _dispatcher.InvokeAsync(() => _capture.ProcessClipboard());
      }
    }
    catch
    {
      // Best-effort — restore UI even if capture fails
    }
    finally
    {
      if (hidden != null)
        await _dispatcher.InvokeAsync(() => RestoreAppWindows(hidden, captured));
      _busy = false;
    }
  }

  private static ScreenshotMode ParseMode(string? value) =>
      value?.Equals("FullScreen", StringComparison.OrdinalIgnoreCase) == true
          ? ScreenshotMode.FullScreen
          : ScreenshotMode.Snip;

  private async Task<bool> WaitForClipboardImageAsync(uint sequenceBefore)
  {
    var timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.ScreenshotWaitTimeoutSeconds, 5, 120));
    var deadline = DateTime.UtcNow + timeout;

    while (DateTime.UtcNow < deadline)
    {
      await Task.Delay(200);
      var seq = ScreenshotInput.GetClipboardSequenceNumber();
      if (seq == sequenceBefore)
        continue;

      var hasImage = await _dispatcher.InvokeAsync(() =>
      {
        try { return Clipboard.ContainsImage(); }
        catch { return false; }
      });
      if (hasImage)
        return true;
    }

    return false;
  }

  private HiddenWindowState HideAppWindows()
  {
    ImageFullscreenWindow.CloseIfOpen();

    var state = new HiddenWindowState(
        _mainWindow.IsVisible,
        _quickAccess.IsVisible,
        _mainWindow.WindowState);

    _quickAccess.Hide();
    _mainWindow.Hide();
    return state;
  }

  private void RestoreAppWindows(HiddenWindowState state, bool captured)
  {
    if (!_settings.ScreenshotRestoreWindows)
      return;

    if (state.MainWasVisible)
    {
      _mainWindow.Show();
      _mainWindow.WindowState = state.MainWindowState;
      if (captured && _settings.ScreenshotOpenImageLibraryAfterCapture)
        _mainWindow.OpenImageLibraryPanel();
      else
        _mainWindow.Activate();
    }

    if (state.QuickWasVisible)
      _quickAccess.ShowNearCursor();
  }

  private sealed record HiddenWindowState(
      bool MainWasVisible,
      bool QuickWasVisible,
      WindowState MainWindowState);
}
