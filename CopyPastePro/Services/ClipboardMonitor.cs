using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CopyPastePro.Services;

public sealed class ClipboardMonitor : IDisposable
{
  private const int WmClipboardupdate = 0x031D;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool AddClipboardFormatListener(IntPtr hwnd);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

  private HwndSource? _source;
  private IntPtr _handle;
  private volatile bool _paused;

  public event EventHandler? ClipboardChanged;

  public void SetPaused(bool paused) => _paused = paused;

  public void Start(Window window)
  {
    var helper = new WindowInteropHelper(window);
    _handle = helper.EnsureHandle();
    _source = HwndSource.FromHwnd(_handle);
    _source?.AddHook(WndProc);
    AddClipboardFormatListener(_handle);
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == WmClipboardupdate && !_paused)
    {
      ClipboardChanged?.Invoke(this, EventArgs.Empty);
      handled = false;
    }
    return IntPtr.Zero;
  }

  public void Dispose()
  {
    if (_handle != IntPtr.Zero)
    {
      RemoveClipboardFormatListener(_handle);
    }
    _source?.RemoveHook(WndProc);
  }
}
