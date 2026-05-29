using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using CopyPastePro.Models;
using Clipboard = System.Windows.Clipboard;

namespace CopyPastePro.Services;

public sealed class ImageLibraryService
{
  private readonly AppSettings _settings;
  private readonly ClipboardHistoryRepository _repository;
  private readonly SyncedFolderService _syncedFolder;

  public ImageLibraryService(AppSettings settings, ClipboardHistoryRepository repository)
  {
    _settings = settings;
    _repository = repository;
    _syncedFolder = new SyncedFolderService(settings, this, repository);
  }

  public SyncedFolderService SyncedFolder => _syncedFolder;

  public void TrySyncSave(ClipboardEntry entry) => _syncedFolder.TrySyncSave(entry);

  public string DefaultAutoSaveFolder
  {
    get
    {
      if (!string.IsNullOrWhiteSpace(_settings.ImageLibraryAutoSaveFolder))
        return _settings.ImageLibraryAutoSaveFolder;
      return Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
          "CopyPaste Pro");
    }
  }

  public List<ClipboardEntry> GetImages(HistorySortMode sort = HistorySortMode.NewestFirst) =>
      _repository.Query(new HistoryQuery { ContentType = ClipboardContentType.Image, Sort = sort });

  public void TryAutoSave(ClipboardEntry entry)
  {
    if (!_settings.ImageLibraryAutoSaveEnabled) return;
    if (entry.ContentType != ClipboardContentType.Image) return;
    if (string.IsNullOrEmpty(entry.PayloadPath)) return;

    try
    {
      var path = BuildAutoSavePath(entry);
      if (path == null) return;
      SaveEntryToFile(entry, path);
      _repository.SetExportPath(entry.Id, path);
      entry.ExportPath = path;
    }
    catch { }
  }

  public string? SaveAs(ClipboardEntry entry, string destinationPath) =>
      SaveEntryToFile(entry, destinationPath) ? destinationPath : null;

  public string? BuildAutoSavePath(ClipboardEntry entry)
  {
    var root = DefaultAutoSaveFolder;
    Directory.CreateDirectory(root);
    if (_settings.ImageLibraryOrganizeByDate)
    {
      root = Path.Combine(root, entry.CapturedAt.ToString("yyyy-MM-dd"));
      Directory.CreateDirectory(root);
    }

    var ext = NormalizeExtension(_settings.ImageLibrarySaveFormat);
    var baseName = FormatFileName(entry);
    var path = Path.Combine(root, baseName + ext);
    return ResolveDuplicatePath(path);
  }

  public bool SaveEntryToFile(ClipboardEntry entry, string destinationPath)
  {
    var bytes = ReadImageBytes(entry);
    if (bytes == null || bytes.Length == 0) return false;

    var dir = Path.GetDirectoryName(destinationPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    return ImageExportService.TryExport(bytes, destinationPath);
  }

  public byte[]? ReadImageBytes(ClipboardEntry entry)
  {
    if (string.IsNullOrEmpty(entry.PayloadPath) || !File.Exists(entry.PayloadPath))
      return null;
    return _repository.ReadPayload(entry.PayloadPath, _settings.EncryptPayloadFiles);
  }

  /// <summary>Loads a downscaled bitmap for grid/list thumbnails.</summary>
  public BitmapImage? LoadThumbnail(ClipboardEntry entry, int decodeWidth = 200) =>
      LoadImage(entry, decodeWidth);

  /// <summary>Loads the stored image at full resolution (or capped by <paramref name="maxDecodeWidth"/>).</summary>
  public BitmapImage? LoadImage(ClipboardEntry entry, int? maxDecodeWidth = null)
  {
    var bytes = ReadImageBytes(entry);
    if (bytes == null) return null;
    try
    {
      using var ms = new MemoryStream(bytes);
      var img = new BitmapImage();
      img.BeginInit();
      img.CacheOption = BitmapCacheOption.OnLoad;
      if (maxDecodeWidth is > 0)
        img.DecodePixelWidth = maxDecodeWidth.Value;
      img.StreamSource = ms;
      img.EndInit();
      img.Freeze();
      return img;
    }
    catch
    {
      return null;
    }
  }

  public (int Width, int Height) GetImageDimensions(ClipboardEntry entry)
  {
    var fromPreview = ImageDisplayHelper.TryParseDimensions(entry.Preview);
    if (fromPreview.Width > 0)
      return fromPreview;

    var bytes = ReadImageBytes(entry);
    if (bytes == null) return (0, 0);
    try
    {
      using var ms = new MemoryStream(bytes);
      var frame = BitmapFrame.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
      return (frame.PixelWidth, frame.PixelHeight);
    }
    catch
    {
      return (0, 0);
    }
  }

  public void CopyToClipboard(ClipboardEntry entry, ClipboardPasteService paste, ClipboardCaptureService capture)
  {
    using (capture.SuppressCapture()) paste.SetClipboard(entry);
  }

  public void CopyFileToClipboard(string filePath, ClipboardCaptureService capture)
  {
    var img = LoadImageFromPath(filePath);
    if (img == null)
      return;
    using (capture.SuppressCapture())
      Clipboard.SetImage(img);
  }

  public BitmapImage? LoadImageFromPath(string filePath)
  {
    if (!File.Exists(filePath))
      return null;
    try
    {
      var img = new BitmapImage();
      img.BeginInit();
      img.CacheOption = BitmapCacheOption.OnLoad;
      img.UriSource = new Uri(Path.GetFullPath(filePath), UriKind.Absolute);
      img.EndInit();
      img.Freeze();
      return img;
    }
    catch
    {
      return null;
    }
  }

  public BitmapImage? LoadThumbnailFromPath(string filePath, int decodeWidth)
  {
    if (!File.Exists(filePath))
      return null;
    try
    {
      var img = new BitmapImage();
      img.BeginInit();
      img.CacheOption = BitmapCacheOption.OnLoad;
      img.DecodePixelWidth = decodeWidth;
      img.UriSource = new Uri(Path.GetFullPath(filePath), UriKind.Absolute);
      img.EndInit();
      img.Freeze();
      return img;
    }
    catch
    {
      return null;
    }
  }

  public BitmapImage? LoadImageForEntry(ClipboardEntry? entry, string? filePath)
  {
    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
      return LoadImageFromPath(filePath);
    if (entry == null)
      return null;
    if (!string.IsNullOrEmpty(entry.SyncPath) && File.Exists(entry.SyncPath))
      return LoadImageFromPath(entry.SyncPath);
    return LoadImage(entry);
  }

  public static void OpenInExplorer(string path)
  {
    if (File.Exists(path))
      System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    else if (Directory.Exists(path))
      System.Diagnostics.Process.Start("explorer.exe", path);
  }

  public static void OpenFolder(string folder)
  {
    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
    System.Diagnostics.Process.Start("explorer.exe", folder);
  }

  private string? ResolveDuplicatePath(string path)
  {
    if (!File.Exists(path)) return path;
    return _settings.ImageLibraryDuplicateHandling.ToLowerInvariant() switch
    {
      "skip" => null,
      "overwrite" => path,
      _ => GetUniquePath(path)
    };
  }

  private static string GetUniquePath(string path)
  {
    var dir = Path.GetDirectoryName(path)!;
    var name = Path.GetFileNameWithoutExtension(path);
    var ext = Path.GetExtension(path);
    var i = 1;
    string candidate;
    do
    {
      candidate = Path.Combine(dir, $"{name}_{i}{ext}");
      i++;
    } while (File.Exists(candidate));
    return candidate;
  }

  private string FormatFileName(ClipboardEntry entry)
  {
    var pattern = string.IsNullOrWhiteSpace(_settings.ImageLibraryFileNamePattern)
        ? "clipboard_{yyyy-MM-dd}_{HH-mm-ss}"
        : _settings.ImageLibraryFileNamePattern;
    var t = entry.CapturedAt;
    var result = pattern
        .Replace("{yyyy-MM-dd}", t.ToString("yyyy-MM-dd"))
        .Replace("{HH-mm-ss}", t.ToString("HH-mm-ss"))
        .Replace("{date}", t.ToString("yyyy-MM-dd"))
        .Replace("{time}", t.ToString("HHmmss"))
        .Replace("{id}", entry.Id.ToString());
    return Regex.Replace(result, @"[<>:""/\\|?*]", "-");
  }

  private static string NormalizeExtension(string format) =>
      ImageExportService.NormalizeExtension(format);
}
