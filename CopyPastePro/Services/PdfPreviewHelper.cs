using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CopyPastePro.Services;

public static class PdfPreviewHelper
{
  public static async Task<BitmapImage?> RenderFirstPageAsync(string filePath, int maxWidth = 900)
  {
    if (!File.Exists(filePath)) return null;
    try
    {
      var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
      var doc = await PdfDocument.LoadFromFileAsync(file);
      if (doc.PageCount == 0) return null;

      using var page = doc.GetPage(0);
      var scale = Math.Min(2.0, maxWidth / page.Size.Width);
      using var stream = new InMemoryRandomAccessStream();
      var opts = new PdfPageRenderOptions { DestinationWidth = (uint)(page.Size.Width * scale) };
      await page.RenderToStreamAsync(stream, opts);
      stream.Seek(0);

      var img = new BitmapImage();
      img.BeginInit();
      img.CacheOption = BitmapCacheOption.OnLoad;
      img.StreamSource = stream.AsStreamForRead();
      img.EndInit();
      img.Freeze();
      return img;
    }
    catch
    {
      return null;
    }
  }
}
