using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CopyPastePro.Services;

namespace CopyPastePro.Controls;

public partial class ImageFullscreenWindow : Window
{
  private static ImageFullscreenWindow? _open;
  private ImageSource? _source;

  private ImageFullscreenWindow()
  {
    InitializeComponent();
  }

  public static void CloseIfOpen()
  {
    _open?.Close();
    _open = null;
  }

  public static void Show(ImageSource? source, Window? owner)
  {
    if (source == null)
      return;

    CloseIfOpen();
    var window = new ImageFullscreenWindow();
    if (owner != null && owner.IsLoaded)
      window.Owner = owner;
    window.ShowImage(source);
    window.Show();
    _open = window;
  }

  private void ShowImage(ImageSource source)
  {
    _source = source;
    PositionToWorkArea();
    Viewer.SetSource(source);
  }

  private void PositionToWorkArea()
  {
    var area = SystemParameters.WorkArea;
    Left = area.Left;
    Top = area.Top;
    Width = area.Width;
    Height = area.Height;
  }

  private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      Close();
      e.Handled = true;
      return;
    }

    if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
    {
      CopyImageToClipboard();
      e.Handled = true;
      return;
    }

    if (Viewer.TryHandleZoomShortcut(e))
      return;
  }

  private void CopyImageToClipboard()
  {
    if (_source is not BitmapSource bmp)
      return;
    try
    {
      System.Windows.Clipboard.SetImage(bmp);
    }
    catch
    {
      // Clipboard busy — ignore
    }
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();

  private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    ContextMenu = ClipboardContextMenuBuilder.Create(menu =>
        ClipboardContextMenuBuilder.AddFullscreenImageMenu(menu,
            CopyImageToClipboard,
            () => Close(),
            _source is BitmapSource));
  }

  protected override void OnClosed(EventArgs e)
  {
    if (ReferenceEquals(_open, this))
      _open = null;
    base.OnClosed(e);
  }
}
