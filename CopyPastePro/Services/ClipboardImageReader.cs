using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.IDataObject;

namespace CopyPastePro.Services;

/// <summary>Reads bitmap data from the clipboard using every common browser format (Firefox, Chrome, Edge).</summary>
public static class ClipboardImageReader
{
  private static readonly string[] PngFormatNames =
  [
    "PNG", "image/png", "ImagePNG", "png", "PNG\r\n"
  ];

  private static readonly string[] JpegFormatNames =
  [
    "JFIF", "image/jpeg", "image/jpg", "JPEG"
  ];

  private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"
  };

  public sealed record CapturedImage(byte[] PngBytes, int Width, int Height);

  public static CapturedImage? TryRead(int maxAttempts = 3)
  {
    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
      var data = Clipboard.GetDataObject();
      if (data == null)
        return null;

      var captured = TryReadFromDataObject(data);
      if (captured != null)
        return captured;

      if (attempt < maxAttempts - 1)
        Thread.Sleep(40);
    }

    return null;
  }

  public static CapturedImage? TryReadFromDataObject(WpfDataObject data)
  {
    foreach (var format in PngFormatNames)
    {
      if (TryReadRawBytes(data, format, out var png) && TryDecodeAsPng(png, out var fromPng))
        return fromPng;
    }

    foreach (var format in JpegFormatNames)
    {
      if (TryReadRawBytes(data, format, out var jpeg) && TryDecodeAsPng(jpeg, out var fromJpeg))
        return fromJpeg;
    }

    if (TryReadBitmapSource(data, out var bitmap) && TryEncodePng(bitmap, out var pngBytes))
      return new CapturedImage(pngBytes, bitmap.PixelWidth, bitmap.PixelHeight);

    if (TryReadSingleImageFile(data, out var fileBytes, out var fw, out var fh))
      return new CapturedImage(fileBytes, fw, fh);

    return null;
  }

  private static bool TryReadRawBytes(WpfDataObject data, string format, out byte[] bytes)
  {
    bytes = Array.Empty<byte>();
    try
    {
      if (!data.GetDataPresent(format))
        return false;

      var obj = data.GetData(format);
      switch (obj)
      {
        case byte[] raw when raw.Length > 0:
          bytes = raw;
          return true;
        case MemoryStream ms:
          bytes = ms.ToArray();
          return bytes.Length > 0;
        case Stream stream:
          using (var copy = new MemoryStream())
          {
            stream.CopyTo(copy);
            bytes = copy.ToArray();
          }
          return bytes.Length > 0;
      }
    }
    catch
    {
      // Format present but not readable in this STA context
    }

    return false;
  }

  private static bool TryReadBitmapSource(WpfDataObject data, out BitmapSource bitmap)
  {
    bitmap = null!;
    try
    {
      BitmapSource? source = null;

      if (data.GetDataPresent(DataFormats.Bitmap, true))
      {
        if (data.GetData(DataFormats.Bitmap, true) is BitmapSource bs)
          source = bs;
      }

      source ??= Clipboard.GetImage();

      if (source == null || !IsValidDimensions(source.PixelWidth, source.PixelHeight))
        return false;

      source = EnsureReadableFormat(source);
      if (!IsLikelyBlank(source))
      {
        bitmap = source;
        return true;
      }
    }
    catch
    {
      // Fall through
    }

    return false;
  }

  private static BitmapSource EnsureReadableFormat(BitmapSource source)
  {
    if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
      return source;

    var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
    converted.Freeze();
    return converted;
  }

  /// <summary>Detects all-transparent or nearly empty bitmaps (common WPF quirk with some DIBs).</summary>
  private static bool IsLikelyBlank(BitmapSource source)
  {
    try
    {
      var stride = (source.PixelWidth * 32 + 7) / 8;
      var buffer = new byte[stride * source.PixelHeight];
      source.CopyPixels(buffer, stride, 0);

      var step = Math.Max(4, buffer.Length / 4000);
      var nonTransparent = 0;
      for (var i = 3; i < buffer.Length; i += step)
      {
        if (buffer[i] > 8)
          nonTransparent++;
      }

      return nonTransparent < 2;
    }
    catch
    {
      return false;
    }
  }

  private static bool TryReadSingleImageFile(WpfDataObject data, out byte[] pngBytes, out int width, out int height)
  {
    pngBytes = Array.Empty<byte>();
    width = height = 0;

    if (!data.GetDataPresent(DataFormats.FileDrop))
      return false;

    if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
      return false;

    var path = files[0];
    if (!File.Exists(path) || !IsImageFile(path))
      return false;

    try
    {
      var info = new FileInfo(path);
      if (info.Length < 32)
        return false;

      var raw = File.ReadAllBytes(path);
      if (!TryDecodeAsPng(raw, out var captured))
        return false;

      pngBytes = captured.PngBytes;
      width = captured.Width;
      height = captured.Height;
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool TryDecodeAsPng(byte[] raw, out CapturedImage captured)
  {
    captured = null!;
    if (raw.Length < 24)
      return false;

    try
    {
      using var ms = new MemoryStream(raw);
      var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
      if (decoder.Frames.Count == 0)
        return false;

      var frame = decoder.Frames[0];
      if (!IsValidDimensions(frame.PixelWidth, frame.PixelHeight))
        return false;

      if (TryEncodePng(frame, out var pngBytes))
      {
        captured = new CapturedImage(pngBytes, frame.PixelWidth, frame.PixelHeight);
        return true;
      }
    }
    catch
    {
      // Invalid image bytes
    }

    return false;
  }

  private static bool TryEncodePng(BitmapSource source, out byte[] pngBytes)
  {
    pngBytes = Array.Empty<byte>();
    try
    {
      var encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(source));
      using var ms = new MemoryStream();
      encoder.Save(ms);
      pngBytes = ms.ToArray();
      return pngBytes.Length >= 32;
    }
    catch
    {
      return false;
    }
  }

  private static bool IsValidDimensions(int width, int height) => width > 0 && height > 0;

  private static bool IsImageFile(string path)
  {
    var ext = Path.GetExtension(path);
    return !string.IsNullOrEmpty(ext) && ImageFileExtensions.Contains(ext);
  }

  public static CapturedImage? TryReadImageFile(string path)
  {
    if (!File.Exists(path) || !IsImageFile(path))
      return null;
    try
    {
      var raw = File.ReadAllBytes(path);
      return TryDecodeAsPng(raw, out var captured) ? captured : null;
    }
    catch
    {
      return null;
    }
  }

  public static bool LooksLikeImageClipboard(WpfDataObject data)
  {
    if (data.GetDataPresent(DataFormats.Bitmap, true))
      return true;

    foreach (var format in PngFormatNames)
    {
      if (data.GetDataPresent(format))
        return true;
    }

    foreach (var format in JpegFormatNames)
    {
      if (data.GetDataPresent(format))
        return true;
    }

    if (data.GetDataPresent(DataFormats.FileDrop)
        && data.GetData(DataFormats.FileDrop) is string[] files
        && files.Length == 1
        && IsImageFile(files[0]))
      return true;

    return false;
  }
}
