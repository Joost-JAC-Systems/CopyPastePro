using System.IO;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

/// <summary>
/// Two-way folder sync: show existing images from a folder and save new clipboard images into it.</summary>
public sealed class SyncedFolderService
{
  private static readonly string[] ImageExtensions =
      [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic"];

  private readonly AppSettings _settings;
  private readonly ImageLibraryService _library;
  private readonly ClipboardHistoryRepository _repository;

  public SyncedFolderService(AppSettings settings, ImageLibraryService library, ClipboardHistoryRepository repository)
  {
    _settings = settings;
    _library = library;
    _repository = repository;
  }

  public bool IsEnabled =>
      _settings.ImageLibrarySyncFolderEnabled
      && !string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath);

  public string SyncFolderPath => _settings.ImageLibrarySyncFolderPath.Trim();

  public void TrySyncSave(ClipboardEntry entry)
  {
    if (!IsEnabled || !_settings.ImageLibrarySyncSaveNewCaptures || entry.ContentType != ClipboardContentType.Image)
      return;
    if (string.IsNullOrEmpty(entry.PayloadPath))
      return;

    try
    {
      var path = BuildSyncFilePath(entry);
      if (path == null)
        return;
      if (_library.SaveEntryToFile(entry, path))
      {
        _repository.SetSyncPath(entry.Id, path);
        entry.SyncPath = path;
      }
    }
    catch
    {
      // Best effort
    }
  }

  public IReadOnlyList<SyncedFolderFile> ScanFolderImages()
  {
    if (!IsEnabled)
      return Array.Empty<SyncedFolderFile>();

    var root = SyncFolderPath;
    if (!Directory.Exists(root))
      return Array.Empty<SyncedFolderFile>();

    var list = new List<SyncedFolderFile>();
    try
    {
      var search = _settings.ImageLibrarySyncScanSubfolders
          ? SearchOption.AllDirectories
          : SearchOption.TopDirectoryOnly;
      foreach (var path in Directory.EnumerateFiles(root, "*.*", search))
      {
        if (!IsImageFile(path))
          continue;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
          continue;
        list.Add(new SyncedFolderFile(Path.GetFullPath(path), info.Length, info.LastWriteTimeUtc));
      }
    }
    catch
    {
      return Array.Empty<SyncedFolderFile>();
    }

    return list;
  }

  public HashSet<string> CollectLinkedPaths(IEnumerable<ClipboardEntry> dbImages)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var e in dbImages)
    {
      if (!string.IsNullOrEmpty(e.SyncPath) && File.Exists(e.SyncPath))
        set.Add(Path.GetFullPath(e.SyncPath));
      if (!string.IsNullOrEmpty(e.ExportPath) && File.Exists(e.ExportPath))
        set.Add(Path.GetFullPath(e.ExportPath));
      if (!string.IsNullOrEmpty(e.PayloadPath) && File.Exists(e.PayloadPath))
        set.Add(Path.GetFullPath(e.PayloadPath));
    }

    return set;
  }

  public string? BuildSyncFilePath(ClipboardEntry entry)
  {
    if (!IsEnabled)
      return null;

    var root = SyncFolderPath;
    Directory.CreateDirectory(root);
    var ext = Path.GetExtension(entry.PayloadPath ?? "") is { Length: > 0 } e
        ? e
        : ".png";
    var name = SanitizeFileName($"clipboard_{entry.CapturedAt:yyyy-MM-dd_HH-mm-ss}{ext}");
    var path = Path.Combine(root, name);
    return ResolveDuplicatePath(path);
  }

  private string? ResolveDuplicatePath(string path)
  {
    if (!File.Exists(path))
      return path;
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

  private static string SanitizeFileName(string name)
  {
    foreach (var c in Path.GetInvalidFileNameChars())
      name = name.Replace(c, '-');
    return name;
  }

  private static bool IsImageFile(string path)
  {
    var ext = Path.GetExtension(path);
    return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
  }

  public sealed record SyncedFolderFile(string FullPath, long SizeBytes, DateTime ModifiedUtc);
}
