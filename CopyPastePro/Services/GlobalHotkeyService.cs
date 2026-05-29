using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CopyPastePro.Services;

public sealed class GlobalHotkeyService : IDisposable
{
  private const int WmHotkey = 0x0312;

  [DllImport("user32.dll")]
  private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

  [DllImport("user32.dll")]
  private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

  private HwndSource? _source;
  private IntPtr _handle;
  private readonly Dictionary<int, Action> _handlers = new();

  public event EventHandler<int>? HotkeyPressed;

  public bool Register(Window window, int hotkeyId, uint modifiers, uint virtualKey, Action? handler = null)
  {
    var helper = new WindowInteropHelper(window);
    _handle = helper.EnsureHandle();
    _source ??= HwndSource.FromHwnd(_handle);
    _source?.AddHook(WndProc);
    if (handler != null) _handlers[hotkeyId] = handler;
    return RegisterHotKey(_handle, hotkeyId, modifiers, virtualKey);
  }

  public void Unregister(int hotkeyId)
  {
    if (_handle != IntPtr.Zero) UnregisterHotKey(_handle, hotkeyId);
    _handlers.Remove(hotkeyId);
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == WmHotkey)
    {
      var id = wParam.ToInt32();
      HotkeyPressed?.Invoke(this, id);
      if (_handlers.TryGetValue(id, out var action))
      {
        action();
        handled = true;
      }
    }
    return IntPtr.Zero;
  }

  public void Dispose()
  {
    foreach (var id in _handlers.Keys.ToList())
      Unregister(id);
    _source?.RemoveHook(WndProc);
  }

  public static uint ParseModifiers(string csv)
  {
    uint mod = 0;
    foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      mod |= (uint)(part.ToLowerInvariant() switch
      {
        "alt" => 0x0001,
        "control" or "ctrl" => 0x0002,
        "shift" => 0x0004,
        "win" or "windows" => 0x0008,
        _ => 0
      });
    }
    return mod;
  }

  public static uint ParseKey(string key)
  {
    if (!Enum.TryParse<Key>(key, ignoreCase: true, out var wpfKey))
      throw new ArgumentException($"Unknown hotkey key: '{key}'.");
    return (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
  }
}

public static class HotkeyIds
{
  public const int QuickAccess = 9001;
  public const int MainWindow = 9002;
  public const int PastePlain = 9003;
  public const int PanicPrivacy = 9004;
  public const int Screenshot = 9005;
}
