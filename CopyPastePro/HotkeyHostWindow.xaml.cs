using System.Windows;
using System.Windows.Interop;
using CopyPastePro.Services;

namespace CopyPastePro;

/// <summary>Invisible window used only to receive WM_HOTKEY — must never take foreground.</summary>
public partial class HotkeyHostWindow : Window
{
  public HotkeyHostWindow() => InitializeComponent();

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    WindowFocusHelper.PreventActivation(new WindowInteropHelper(this).Handle);
  }
}
