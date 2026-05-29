using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CopyPastePro.Services;

public static class ImageExportService
{
  public static readonly string[] SupportedFormats = ["png", "jpg", "jpeg", "bmp", "gif", "tiff", "tif"];

  public static string FormatFilter =>
      "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp|GIF (*.gif)|*.gif|TIFF (*.tiff)|*.tiff";

  public static bool TryExport(byte[] sourceBytes, string destinationPath, string? formatHint = null)
  {
    if (sourceBytes == null || sourceBytes.Length == 0) return false;
    try
    {
      using var ms = new MemoryStream(sourceBytes);
      var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
      var frame = decoder.Frames[0];
      var ext = Path.GetExtension(destinationPath);
      if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(formatHint))
        destinationPath += NormalizeExtension(formatHint);

      var encoder = CreateEncoder(Path.GetExtension(destinationPath));
      encoder.Frames.Add(BitmapFrame.Create(frame));
      using var outMs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
      encoder.Save(outMs);
      return true;
    }
    catch
    {
      return false;
    }
  }

  public static bool PromptAndExport(Window? owner, byte[] sourceBytes, string defaultBaseName = "clipboard_image")
  {
    var dlg = new SaveFileDialog
    {
      Title = "Export image as",
      Filter = FormatFilter,
      FileName = defaultBaseName + ".png",
      AddExtension = true
    };
    if (dlg.ShowDialog(owner) != true) return false;
    return TryExport(sourceBytes, dlg.FileName);
  }

  public static BitmapEncoder CreateEncoder(string extension)
  {
    var ext = extension.Trim().ToLowerInvariant();
    return ext switch
    {
      ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
      ".bmp" => new BmpBitmapEncoder(),
      ".gif" => new GifBitmapEncoder(),
      ".tif" or ".tiff" => new TiffBitmapEncoder(),
      _ => new PngBitmapEncoder()
    };
  }

  public static string NormalizeExtension(string format) =>
      format.Trim().TrimStart('.').ToLowerInvariant() switch
      {
        "jpg" or "jpeg" => ".jpg",
        "bmp" => ".bmp",
        "gif" => ".gif",
        "tif" or "tiff" => ".tiff",
        _ => ".png"
      };
}
