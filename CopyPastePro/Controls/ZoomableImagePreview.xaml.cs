using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CopyPastePro.Controls;

public partial class ZoomableImagePreview : System.Windows.Controls.UserControl
{
  private const double ZoomStep = 1.15;
  private const double MinZoom = 0.2;
  private const double MaxZoom = 16.0;

  private double _zoomFactor = 1.0;
  private double _fitScale = 1.0;
  private bool _layoutDeferred;
  private bool _isPanning;
  private System.Windows.Point _panStart;
  private double _panStartH;
  private double _panStartV;
  private bool _chromeVisible = true;

  public static readonly DependencyProperty SourceProperty =
      DependencyProperty.Register(
          nameof(Source),
          typeof(ImageSource),
          typeof(ZoomableImagePreview),
          new PropertyMetadata(null, (d, e) =>
          {
            if (d is not ZoomableImagePreview control) return;
            control._zoomFactor = 1.0;
            control.ResetScroll();
            control.PreviewImage.Source = e.NewValue as ImageSource;
            control.ApplyLayout();
            control.UpdateFullscreenButton();
          }));

  public static readonly DependencyProperty ShowFullscreenButtonProperty =
      DependencyProperty.Register(
          nameof(ShowFullscreenButton),
          typeof(bool),
          typeof(ZoomableImagePreview),
          new PropertyMetadata(true, (d, _) =>
          {
            if (d is ZoomableImagePreview control)
              control.UpdateFullscreenButton();
          }));

  public ImageSource? Source
  {
    get => (ImageSource?)GetValue(SourceProperty);
    set => SetValue(SourceProperty, value);
  }

  public bool ShowFullscreenButton
  {
    get => (bool)GetValue(ShowFullscreenButtonProperty);
    set => SetValue(ShowFullscreenButtonProperty, value);
  }

  /// <summary>Raised before fullscreen opens; set <see cref="FullscreenRequestEventArgs.Source"/> for a higher-resolution image.</summary>
  public event EventHandler<FullscreenRequestEventArgs>? PreparingFullscreen;

  public ZoomableImagePreview()
  {
    InitializeComponent();
    Loaded += (_, _) => ApplyLayout();
    SizeChanged += (_, _) => ApplyLayout();
    IsVisibleChanged += (_, _) =>
    {
      if (IsVisible)
        ApplyLayout();
    };
  }

  public void SetSource(ImageSource? source)
  {
    _zoomFactor = 1.0;
    ResetScroll();
    Source = source;
    UpdateFullscreenButton();
  }

  public void RefreshLayout() => ApplyLayout();

  public void ResetZoom()
  {
    _zoomFactor = 1.0;
    ResetScroll();
    ApplyLayout();
  }

  /// <summary>Handles Ctrl+0 / Ctrl+NumPad0 to reset zoom to 100% (fit scale).</summary>
  public bool TryHandleZoomShortcut(WpfKeyEventArgs e)
  {
    if (Source == null || Visibility != Visibility.Visible)
      return false;
    if (Keyboard.Modifiers != ModifierKeys.Control)
      return false;
    if (e.Key is not Key.D0 and not Key.NumPad0)
      return false;

    ResetZoom();
    e.Handled = true;
    return true;
  }

  private void UserControl_PreviewKeyDown(object sender, WpfKeyEventArgs e) => TryHandleZoomShortcut(e);

  private void Scroller_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
  {
    if (Source != null)
      Focus();
  }

  public void SetChromeVisible(bool visible)
  {
    _chromeVisible = visible;
    FullscreenBtn.BeginAnimation(UIElement.OpacityProperty, null);
    FullscreenBtn.Opacity = visible ? 1 : 0;
    FullscreenBtn.IsHitTestVisible = visible;
  }

  public void AnimateChromeHide(TimeSpan duration)
  {
    _chromeVisible = false;
    FullscreenBtn.BeginAnimation(UIElement.OpacityProperty, null);
    if (FullscreenBtn.Visibility != Visibility.Visible)
      return;

    FullscreenBtn.IsHitTestVisible = true;
    var anim = new DoubleAnimation(FullscreenBtn.Opacity, 0, new Duration(duration))
    {
      EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
    };
    anim.Completed += (_, _) => FullscreenBtn.IsHitTestVisible = false;
    FullscreenBtn.BeginAnimation(UIElement.OpacityProperty, anim);
  }

  public void OpenFullscreen()
  {
    if (Source == null)
      return;

    var args = new FullscreenRequestEventArgs { Source = Source };
    PreparingFullscreen?.Invoke(this, args);
    ImageFullscreenWindow.Show(args.Source, Window.GetWindow(this));
  }

  private void FullscreenBtn_Click(object sender, RoutedEventArgs e) => OpenFullscreen();

  private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
      return;

    if (Source is not BitmapSource)
      return;

    e.Handled = true;
    var factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
    ZoomAtPoint(e.GetPosition(Scroller), factor);
  }

  private void ZoomAtPoint(System.Windows.Point viewportPoint, double factor)
  {
    if (Source is not BitmapSource bmp)
      return;

    var oldZoom = _zoomFactor;
    var newZoom = Math.Clamp(_zoomFactor * factor, MinZoom, MaxZoom);
    if (Math.Abs(newZoom - oldZoom) < 0.0001)
      return;

    var oldW = bmp.PixelWidth * _fitScale * oldZoom;
    var oldH = bmp.PixelHeight * _fitScale * oldZoom;
    var relX = oldW > 0 ? (Scroller.HorizontalOffset + viewportPoint.X) / oldW : 0.5;
    var relY = oldH > 0 ? (Scroller.VerticalOffset + viewportPoint.Y) / oldH : 0.5;

    _zoomFactor = newZoom;
    ApplyLayout();

    var newW = bmp.PixelWidth * _fitScale * _zoomFactor;
    var newH = bmp.PixelHeight * _fitScale * _zoomFactor;
    Scroller.ScrollToHorizontalOffset(Math.Max(0, relX * newW - viewportPoint.X));
    Scroller.ScrollToVerticalOffset(Math.Max(0, relY * newH - viewportPoint.Y));
    UpdatePanCursor();
  }

  private void Scroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (Source == null || !CanPan())
      return;

    _isPanning = true;
    _panStart = e.GetPosition(Scroller);
    _panStartH = Scroller.HorizontalOffset;
    _panStartV = Scroller.VerticalOffset;
    Scroller.CaptureMouse();
    Scroller.Cursor = System.Windows.Input.Cursors.SizeAll;
    e.Handled = true;
  }

  private void Scroller_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
  {
    if (!_isPanning)
      return;

    var pos = e.GetPosition(Scroller);
    Scroller.ScrollToHorizontalOffset(_panStartH + (_panStart.X - pos.X));
    Scroller.ScrollToVerticalOffset(_panStartV + (_panStart.Y - pos.Y));
    e.Handled = true;
  }

  private void Scroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan();

  private void Scroller_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => EndPan();

  private void EndPan()
  {
    if (!_isPanning)
      return;
    _isPanning = false;
    Scroller.ReleaseMouseCapture();
    UpdatePanCursor();
  }

  private bool CanPan() =>
      Scroller.ExtentWidth > Scroller.ViewportWidth + 1
      || Scroller.ExtentHeight > Scroller.ViewportHeight + 1;

  private void UpdatePanCursor()
  {
    if (_isPanning)
      return;
    Scroller.Cursor = Source != null && CanPan() ? System.Windows.Input.Cursors.Hand : null;
  }

  private void UpdateFullscreenButton()
  {
    var show = ShowFullscreenButton && Source != null;
    FullscreenBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    if (show)
      SetChromeVisible(_chromeVisible);
  }

  private void ResetScroll()
  {
    if (!Scroller.IsLoaded)
      return;
    Scroller.ScrollToHorizontalOffset(0);
    Scroller.ScrollToVerticalOffset(0);
  }

  private void ApplyLayout()
  {
    PreviewImage.Source = Source;

    if (!IsLoaded || Visibility != Visibility.Visible || !IsVisible)
      return;

    if (Source is not BitmapSource bmp || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
    {
      ClearImageHost();
      Scroller.Cursor = null;
      return;
    }

    var vw = Scroller.ActualWidth;
    var vh = Scroller.ActualHeight;
    if (vw < 1 || vh < 1)
    {
      DeferLayoutOnce();
      return;
    }

    _fitScale = Math.Min(vw / bmp.PixelWidth, vh / bmp.PixelHeight);
    var totalScale = _fitScale * _zoomFactor;

    PreviewImage.Width = bmp.PixelWidth;
    PreviewImage.Height = bmp.PixelHeight;
    ZoomTransform.ScaleX = totalScale;
    ZoomTransform.ScaleY = totalScale;
    UpdatePanCursor();
  }

  private void DeferLayoutOnce()
  {
    if (_layoutDeferred)
      return;
    _layoutDeferred = true;
    Dispatcher.BeginInvoke(() =>
    {
      _layoutDeferred = false;
      ApplyLayout();
    }, DispatcherPriority.Loaded);
  }

  private void ClearImageHost()
  {
    PreviewImage.Source = null;
    PreviewImage.Width = double.NaN;
    PreviewImage.Height = double.NaN;
    ZoomTransform.ScaleX = 1;
    ZoomTransform.ScaleY = 1;
    _fitScale = 1;
  }
}

public sealed class FullscreenRequestEventArgs : EventArgs
{
  public ImageSource? Source { get; set; }
}
