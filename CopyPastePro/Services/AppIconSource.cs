using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace CopyPastePro.Services;

/// <summary>
/// Application branding assets in <c>Assets/</c>:
/// <list type="bullet">
/// <item><c>AppIcon.ico</c> — window title bar, executable, system tray</item>
/// <item><c>AppIcon.png</c> / <c>AppIcon-256px.png</c> — transparent UI (no outer white)</item>
/// <item><c>AppIcon-bgn.png</c> — full square with white padding (store / light surfaces)</item>
/// </list>
/// </summary>
public static class AppIconSource
{
  public const string IcoPackUri = "pack://application:,,,/Assets/AppIcon.ico";
  public const string TransparentPackUri = "pack://application:,,,/Assets/AppIcon-256px.png";
  public const string TransparentLargePackUri = "pack://application:,,,/Assets/AppIcon.png";
  public const string WithBackgroundPackUri = "pack://application:,,,/Assets/AppIcon-bgn.png";

  private static ImageSource? _windowIcon;
  private static ImageSource? _transparent;
  private static ImageSource? _transparentLarge;
  private static ImageSource? _withBackground;
  private static System.Drawing.Icon? _tray;

  /// <summary>Multi-size ICO for <see cref="Window.Icon"/> and the published .exe.</summary>
  public static ImageSource WindowIcon =>
      _windowIcon ??= LoadBitmap(IcoPackUri);

  /// <summary>Transparent artwork for dark UI (256×256).</summary>
  public static ImageSource Transparent =>
      _transparent ??= LoadBitmap(TransparentPackUri, fallback: TransparentLargePackUri);

  /// <summary>Full-resolution transparent artwork.</summary>
  public static ImageSource TransparentLarge =>
      _transparentLarge ??= LoadBitmap(TransparentLargePackUri, fallback: TransparentPackUri);

  /// <summary>Square image with white outer padding — use on light backgrounds only.</summary>
  public static ImageSource WithBackground =>
      _withBackground ??= LoadBitmap(WithBackgroundPackUri, fallback: TransparentPackUri);

  public static System.Drawing.Icon Tray
  {
    get
    {
      if (_tray != null)
        return _tray;
      _tray = TryLoadTrayFromIco() ?? CreateTrayIconFromPng(TransparentPackUri, 32)
          ?? CreateTrayIconFromPng(TransparentLargePackUri, 32)
          ?? System.Drawing.SystemIcons.Application;
      return _tray;
    }
  }

  public static void RegisterApplicationResources()
  {
    if (Application.Current == null)
      return;
    Application.Current.Resources["AppIconWindow"] = WindowIcon;
    Application.Current.Resources["AppIconTransparent"] = Transparent;
    Application.Current.Resources["AppIconTransparentLarge"] = TransparentLarge;
    Application.Current.Resources["AppIconWithBackground"] = WithBackground;
  }

  private static System.Drawing.Icon? TryLoadTrayFromIco()
  {
    try
    {
      var stream = Application.GetResourceStream(new Uri(IcoPackUri, UriKind.Absolute))?.Stream;
      if (stream == null)
        return null;
      using (stream)
        return new System.Drawing.Icon(stream, 32, 32);
    }
    catch
    {
      return null;
    }
  }

  private static System.Drawing.Icon? CreateTrayIconFromPng(string packUri, int size)
  {
    try
    {
      var stream = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))?.Stream;
      if (stream == null)
        return null;
      using (stream)
      using (var src = System.Drawing.Image.FromStream(stream))
      using (var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
      {
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
          g.Clear(System.Drawing.Color.Transparent);
          g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
          g.DrawImage(src, 0, 0, size, size);
        }
        var hIcon = bmp.GetHicon();
        try
        {
          using var temp = System.Drawing.Icon.FromHandle(hIcon);
          return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
          DestroyIcon(hIcon);
        }
      }
    }
    catch
    {
      return null;
    }
  }

  private static ImageSource LoadBitmap(string primaryUri, string? fallback = null)
  {
    try
    {
      return BitmapFrame.Create(new Uri(primaryUri, UriKind.Absolute));
    }
    catch when (fallback != null)
    {
      return BitmapFrame.Create(new Uri(fallback, UriKind.Absolute));
    }
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool DestroyIcon(IntPtr hIcon);
}
