using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CopyPastePro.Models;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;

namespace CopyPastePro.Services;

public sealed class ClipboardCaptureService
{
  private readonly ClipboardHistoryRepository _repository;
  private readonly AppSettings _settings;
  private readonly ClipboardRulesEngine _rules;
  private readonly CategoryClassifier _classifier;
  private readonly SmartOrganizerService _organizer;
  private readonly PrivacyService _privacy;
  private readonly ImageLibraryService? _imageLibrary;
  private string? _lastHash;
  private bool _suppressCapture;
  private int _wipeModeDepth;
  private int _ignoreClipboardUntilTick;

  public event EventHandler<ClipboardEntry>? EntryCaptured;

  public ClipboardCaptureService(
      ClipboardHistoryRepository repository,
      AppSettings settings,
      ClipboardRulesEngine rules,
      CategoryClassifier classifier,
      SmartOrganizerService organizer,
      PrivacyService privacy,
      ImageLibraryService? imageLibrary = null)
  {
    _repository = repository;
    _settings = settings;
    _rules = rules;
    _classifier = classifier;
    _organizer = organizer;
    _privacy = privacy;
    _imageLibrary = imageLibrary;
  }

  public IDisposable SuppressCapture() => new SuppressScope(this);

  /// <summary>Hard block capture during history/clipboard wipe (monitor may still fire; this ignores it).</summary>
  public IDisposable EnterWipeMode() => new WipeModeScope(this);

  /// <summary>Ignore clipboard change notifications after programmatic clipboard updates.</summary>
  public void IgnoreClipboardChanges(int milliseconds = 800) =>
      _ignoreClipboardUntilTick = Environment.TickCount + Math.Max(milliseconds, 0);

  /// <summary>After history is cleared, remember what is still on the clipboard so it is not re-captured immediately.</summary>
  public void OnHistoryCleared(bool systemClipboardWasCleared)
  {
    IgnoreClipboardChanges(5000);
    if (systemClipboardWasCleared)
    {
      _lastHash = null;
      return;
    }

    try
    {
      var data = Clipboard.GetDataObject();
      if (data == null)
      {
        _lastHash = null;
        return;
      }

      var contentType = PeekPrimaryContentType(data);
      if (contentType == null)
      {
        _lastHash = null;
        return;
      }

      var entry = CaptureFromDataObject(data, contentType.Value);
      _lastHash = entry?.ContentHash;
    }
    catch
    {
      _lastHash = null;
    }
  }

  public void ProcessClipboard()
  {
    if (_wipeModeDepth > 0) return;
    if (_suppressCapture) return;
    if (Environment.TickCount < _ignoreClipboardUntilTick) return;
    if (!_settings.MonitoringEnabled || _privacy.IsMonitoringPaused) return;

    try
    {
      var data = Clipboard.GetDataObject();
      if (data == null) return;

      var (proc, title) = PrivacyService.GetForegroundContext();
      if (PeekPrimaryContentType(data) is not { } contentType)
        return;

      if (!_rules.ShouldCaptureType(contentType))
        return;
      if (!_privacy.ShouldCapture(contentType, proc, title))
        return;

      var entry = CaptureFromDataObject(data, contentType);
      if (entry == null) return;

      if (!_rules.ShouldCaptureEntry(entry)) return;

      if (_settings.IgnoreDuplicates)
      {
        if (entry.ContentHash == _lastHash) return;
        if (_repository.ExistsByHash(entry.ContentHash)) return;
      }

      _classifier.Enrich(entry, proc);
      _privacy.PrepareForStorage(entry);
      _repository.Add(entry);
      _rules.ApplyPostCapture(entry, _repository);
      _organizer.AfterCapture(entry);
      if (entry.ContentType == ClipboardContentType.Image)
      {
        _imageLibrary?.TryAutoSave(entry);
        _imageLibrary?.TrySyncSave(entry);
      }
      _lastHash = entry.ContentHash;
      EntryCaptured?.Invoke(this, entry);

      _repository.TrimToMax(_settings.MaxHistoryItems,
          _repository.GetAll().Where(e => e.IsPinned).Select(e => e.Id));
    }
    catch
    {
      // Clipboard may be locked by another app
    }
  }

  internal static ClipboardContentType? PeekPrimaryContentType(System.Windows.IDataObject data)
  {
    if (ClipboardImageReader.LooksLikeImageClipboard(data))
      return ClipboardContentType.Image;

    if (data.GetDataPresent(DataFormats.FileDrop)
        && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 })
      return ClipboardContentType.Files;

    if (data.GetDataPresent(DataFormats.Html))
      return ClipboardContentType.Html;
    if (data.GetDataPresent(DataFormats.Rtf))
      return ClipboardContentType.Rtf;
    if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
      return ClipboardContentType.Text;

    return ClipboardContentType.Other;
  }

  private ClipboardEntry? CaptureFromDataObject(System.Windows.IDataObject data, ClipboardContentType contentType) =>
      contentType switch
      {
        ClipboardContentType.Image => CaptureImage(),
        ClipboardContentType.Files => data.GetData(DataFormats.FileDrop) is string[] files
            ? CaptureFiles(files)
            : null,
        ClipboardContentType.Html => CaptureTextFromData(data, DataFormats.Html, ClipboardContentType.Html),
        ClipboardContentType.Rtf => CaptureTextFromData(data, DataFormats.Rtf, ClipboardContentType.Rtf),
        ClipboardContentType.Text => CaptureTextFromData(data, DataFormats.UnicodeText, ClipboardContentType.Text)
            ?? CaptureTextFromData(data, DataFormats.Text, ClipboardContentType.Text),
        _ => CaptureOtherFormats(data)
      };

  private ClipboardEntry? CaptureTextFromData(System.Windows.IDataObject data, string format, ClipboardContentType type)
  {
    if (!data.GetDataPresent(format)) return null;
    var text = data.GetData(format) as string;
    return string.IsNullOrWhiteSpace(text) ? null : CaptureText(text, type);
  }

  private ClipboardEntry? CaptureOtherFormats(System.Windows.IDataObject data)
  {
    if (data.GetDataPresent(DataFormats.Html))
    {
      var html = data.GetData(DataFormats.Html) as string;
      if (!string.IsNullOrWhiteSpace(html)) return CaptureText(html, ClipboardContentType.Html);
    }

    if (data.GetDataPresent(DataFormats.Rtf))
    {
      var rtf = data.GetData(DataFormats.Rtf) as string;
      if (!string.IsNullOrWhiteSpace(rtf)) return CaptureText(rtf, ClipboardContentType.Rtf);
    }

    if (data.GetDataPresent(DataFormats.UnicodeText))
    {
      var text = data.GetData(DataFormats.UnicodeText) as string;
      if (!string.IsNullOrWhiteSpace(text)) return CaptureText(text, ClipboardContentType.Text);
    }

    if (data.GetDataPresent(DataFormats.Text))
    {
      var text = data.GetData(DataFormats.Text) as string;
      if (!string.IsNullOrWhiteSpace(text)) return CaptureText(text, ClipboardContentType.Text);
    }

    // Custom / unknown formats
    var formats = data.GetFormats();
    foreach (var format in formats)
    {
      if (format == DataFormats.Bitmap || format == DataFormats.FileDrop || format == DataFormats.Html
          || format == DataFormats.Rtf || format == DataFormats.Text || format == DataFormats.UnicodeText)
        continue;

      try
      {
        var obj = data.GetData(format);
        if (obj is string s && !string.IsNullOrWhiteSpace(s))
        {
          return new ClipboardEntry
          {
            ContentType = ClipboardContentType.Other,
            Preview = Truncate($"[{format}] {s}"),
            TextContent = s,
            ContentHash = ClipboardHistoryRepository.ComputeHash(Encoding.UTF8.GetBytes(format + s)),
            CapturedAt = DateTime.Now,
            SizeBytes = Encoding.UTF8.GetByteCount(s)
          };
        }
      }
      catch { }
    }

    return null;
  }

  private ClipboardEntry CaptureText(string text, ClipboardContentType type)
  {
    var hash = ClipboardHistoryRepository.ComputeHash(Encoding.UTF8.GetBytes(text));
    return new ClipboardEntry
    {
      ContentType = type,
      Preview = Truncate(text.Replace("\r\n", " ").Replace('\n', ' ')),
      TextContent = text,
      ContentHash = hash,
      CapturedAt = DateTime.Now,
      SizeBytes = Encoding.UTF8.GetByteCount(text)
    };
  }

  private ClipboardEntry? CaptureImage()
  {
    if (ClipboardImageReader.TryRead() is not { } captured)
      return null;

    return CreateImageEntry(captured.PngBytes, captured.Width, captured.Height);
  }

  private ClipboardEntry CreateImageEntry(byte[] bytes, int width, int height)
  {
    var path = _repository.StorePayload(bytes, ".png", _settings.EncryptPayloadFiles);
    var hash = ClipboardHistoryRepository.ComputeHash(bytes);
    return new ClipboardEntry
    {
      ContentType = ClipboardContentType.Image,
      Preview = $"Image {width}×{height}",
      PayloadPath = path,
      ContentHash = hash,
      CapturedAt = DateTime.Now,
      SizeBytes = bytes.Length
    };
  }

  private ClipboardEntry? CaptureFiles(string[] files)
  {
    var existing = files.Where(File.Exists).ToArray();

    if (existing.Length == 1 && ClipboardImageReader.TryReadImageFile(existing[0]) is { } fromFile)
      return CreateImageEntry(fromFile.PngBytes, fromFile.Width, fromFile.Height);

    if (existing.Length == 0)
      return null;

    var names = existing.Select(Path.GetFileName).Where(n => n != null).Cast<string>().ToArray();
    var preview = existing.Length == 1
        ? names[0] ?? "File"
        : $"{existing.Length} files: {string.Join(", ", names.Take(3))}{(names.Length > 3 ? "…" : "")}";

    var json = JsonSerializer.Serialize(existing);
    var hash = ClipboardHistoryRepository.ComputeHash(Encoding.UTF8.GetBytes(json));
    var totalSize = existing.Sum(f => new FileInfo(f).Length);

    return new ClipboardEntry
    {
      ContentType = ClipboardContentType.Files,
      Preview = preview,
      FilePathsJson = json,
      ContentHash = hash,
      CapturedAt = DateTime.Now,
      SizeBytes = totalSize
    };
  }

  private string Truncate(string s) =>
      s.Length <= _settings.MaxTextPreviewLength ? s : s[.._settings.MaxTextPreviewLength] + "…";

  private sealed class SuppressScope : IDisposable
  {
    private readonly ClipboardCaptureService _owner;
    public SuppressScope(ClipboardCaptureService owner) { _owner = owner; _owner._suppressCapture = true; }
    public void Dispose() => _owner._suppressCapture = false;
  }

  private sealed class WipeModeScope : IDisposable
  {
    private readonly ClipboardCaptureService _owner;
    public WipeModeScope(ClipboardCaptureService owner)
    {
      _owner = owner;
      _owner._wipeModeDepth++;
    }
    public void Dispose() => _owner._wipeModeDepth--;
  }
}
