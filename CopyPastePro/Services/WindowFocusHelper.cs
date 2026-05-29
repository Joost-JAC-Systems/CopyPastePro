using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CopyPastePro.Services;

internal static class WindowFocusHelper
{
  private const int GwlExstyle = -20;
  private const int WsExNoActivate = 0x08000000;
  private const int WsExToolWindow = 0x00000080;
  private static readonly IntPtr HwndTopmost = new(-1);

  [StructLayout(LayoutKind.Sequential)]
  private struct Rect { public int Left, Top, Right, Bottom; }

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

  [DllImport("kernel32.dll")]
  private static extern uint GetCurrentThreadId();

  [DllImport("user32.dll")]
  private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

  [DllImport("user32.dll")]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool BringWindowToTop(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll")]
  private static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

  [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
  private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
  private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
  private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
  private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  private const uint SwpNomove = 0x0002;
  private const uint SwpNosize = 0x0001;
  private const uint SwpShowWindow = 0x0040;

  public static IntPtr GetForegroundHwnd() => GetForegroundWindow();

  public static void PreventActivation(IntPtr hwnd)
  {
    if (hwnd == IntPtr.Zero) return;
    var style = IntPtr.Size == 8
        ? (int)GetWindowLongPtr64(hwnd, GwlExstyle)
        : GetWindowLong32(hwnd, GwlExstyle);
    var updated = style | WsExNoActivate | WsExToolWindow;
    if (IntPtr.Size == 8)
      SetWindowLongPtr64(hwnd, GwlExstyle, new IntPtr(updated));
    else
      SetWindowLong32(hwnd, GwlExstyle, updated);
  }

  public static void ForceAboveAll(Window window)
  {
    var hwnd = new WindowInteropHelper(window).EnsureHandle();
    SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowWindow);
    BringToForeground(window);
  }

  public static void BringToForeground(Window window)
  {
    var hwnd = new WindowInteropHelper(window).EnsureHandle();
    var fg = GetForegroundWindow();
    if (hwnd == fg) return;

    var fgThread = GetWindowThreadProcessId(fg, out _);
    var thisThread = GetCurrentThreadId();
    var targetThread = GetWindowThreadProcessId(hwnd, out _);

    var attachedFg = false;
    var attachedTarget = false;
    try
    {
      if (fgThread != 0 && fgThread != thisThread)
      {
        AttachThreadInput(fgThread, thisThread, true);
        attachedFg = true;
      }
      if (targetThread != 0 && targetThread != thisThread)
      {
        AttachThreadInput(targetThread, thisThread, true);
        attachedTarget = true;
      }

      SetForegroundWindow(hwnd);
      BringWindowToTop(hwnd);
    }
    finally
    {
      if (attachedTarget)
        AttachThreadInput(targetThread, thisThread, false);
      if (attachedFg)
        AttachThreadInput(fgThread, thisThread, false);
    }
  }

  /// <summary>Hidden main window or 1×1 hotkey host stole focus — not a real user target.</summary>
  public static bool IsIncidentalOwnWindow(IntPtr hwnd, IntPtr quickAccessHwnd)
  {
    if (hwnd == IntPtr.Zero || hwnd == quickAccessHwnd) return false;
    _ = GetWindowThreadProcessId(hwnd, out var pid);
    if (pid != Process.GetCurrentProcess().Id) return false;

    if (!IsWindowVisible(hwnd)) return true;

    if (!GetWindowRect(hwnd, out var r)) return false;
    var w = r.Right - r.Left;
    var h = r.Bottom - r.Top;
    return w <= 2 && h <= 2;
  }
}
