namespace CopyPastePro.Services;

using System.IO;

public static class SecureDeleteHelper
{
  public static void DeleteFile(string path, int passes)
  {
    if (!File.Exists(path)) return;
    try
    {
      if (passes <= 0)
      {
        File.Delete(path);
        return;
      }

      var len = new FileInfo(path).Length;
      using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
      {
        var buffer = new byte[Math.Min(65536, len)];
        for (var pass = 0; pass < passes; pass++)
        {
          fs.Position = 0;
          var remaining = len;
          var pattern = (byte)(pass % 2 == 0 ? 0xFF : 0x00);
          Array.Fill(buffer, pattern);
          while (remaining > 0)
          {
            var write = (int)Math.Min(buffer.Length, remaining);
            fs.Write(buffer, 0, write);
            remaining -= write;
          }
          fs.Flush(true);
        }
      }
      File.Delete(path);
    }
    catch
    {
      try { File.Delete(path); } catch { }
    }
  }

  public static void WipeDirectoryContents(string dir, int passes)
  {
    if (!Directory.Exists(dir)) return;
    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
    {
      try { DeleteFile(file, passes); } catch { }
    }
  }
}
