using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CopyPastePro.Services;

/// <summary>Triggers Windows screenshot / snipping UI.</summary>
public static class ScreenshotInput
{
  private const int InputKeyboard = 1;
  private const uint KeyeventfKeyup = 0x0002;
  private const ushort VkSnapshot = 0x2C;
  private const ushort VkLWin = 0x5B;
  private const ushort VkLShift = 0xA0;
  private const ushort VkS = 0x53;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  public static extern uint GetClipboardSequenceNumber();

  [DllImport("user32.dll")]
  private static extern IntPtr GetDesktopWindow();

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  public static void Trigger(ScreenshotMode mode)
  {
    NudgeFocusToDesktop();
    Thread.Sleep(50);

    switch (mode)
    {
      case ScreenshotMode.FullScreen:
        SendPrintScreen();
        break;
      default:
        if (!TryLaunchSnipOverlay())
          SendWinShiftS();
        break;
    }
  }

  /// <summary>Windows 10/11 snipping overlay (same as Win+Shift+S).</summary>
  public static bool TryLaunchSnipOverlay()
  {
    try
    {
      _ = Process.Start(new ProcessStartInfo("ms-screenclip:")
      {
        UseShellExecute = true
      });
      return true;
    }
    catch
    {
      // Fall through to other launchers
    }

    try
    {
      _ = Process.Start(new ProcessStartInfo("explorer.exe", "ms-screenclip:")
      {
        UseShellExecute = true
      });
      return true;
    }
    catch
    {
      // Fall through
    }

    var snipTool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SnippingTool.exe");
    if (!File.Exists(snipTool))
      return false;

    try
    {
      _ = Process.Start(new ProcessStartInfo(snipTool, "/clip") { UseShellExecute = true });
      return true;
    }
    catch
    {
      return false;
    }
  }

  public static void SendWinShiftS()
  {
    PressKey(VkLWin);
    Thread.Sleep(40);
    PressKey(VkLShift);
    Thread.Sleep(40);
    PressKey(VkS);
    Thread.Sleep(60);
    ReleaseKey(VkS);
    Thread.Sleep(20);
    ReleaseKey(VkLShift);
    Thread.Sleep(20);
    ReleaseKey(VkLWin);
  }

  public static void SendPrintScreen()
  {
    PressKey(VkSnapshot);
    Thread.Sleep(60);
    ReleaseKey(VkSnapshot);
  }

  private static void NudgeFocusToDesktop()
  {
    try { SetForegroundWindow(GetDesktopWindow()); }
    catch { /* best effort */ }
  }

  private static void PressKey(ushort virtualKey) => SendKeyboard(virtualKey, keyUp: false);

  private static void ReleaseKey(ushort virtualKey) => SendKeyboard(virtualKey, keyUp: true);

  private static void SendKeyboard(ushort virtualKey, bool keyUp)
  {
    var inputs = new[]
    {
      new INPUT
      {
        type = InputKeyboard,
        U = new InputUnion
        {
          ki = new KEYBDINPUT
          {
            wVk = virtualKey,
            dwFlags = keyUp ? KeyeventfKeyup : 0u
          }
        }
      }
    };
    _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct INPUT
  {
    public uint type;
    public InputUnion U;
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct InputUnion
  {
    [FieldOffset(0)] public KEYBDINPUT ki;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct KEYBDINPUT
  {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }
}

public enum ScreenshotMode
{
  Snip,
  FullScreen
}
