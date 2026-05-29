using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CopyPastePro.Models;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using TextDataFormat = System.Windows.TextDataFormat;

namespace CopyPastePro.Services;

public sealed class ClipboardPasteService
{
  private readonly AppSettings? _settings;
  private readonly ClipboardHistoryRepository? _repository;

  public ClipboardPasteService(AppSettings? settings = null, ClipboardHistoryRepository? repository = null)
  {
    _settings = settings;
    _repository = repository;
  }

  public void SetClipboard(ClipboardEntry entry) => _ = TrySetClipboard(entry);

  public bool TrySetClipboard(ClipboardEntry entry)
  {
    var data = new DataObject();

    switch (entry.ContentType)
    {
      case ClipboardContentType.Text:
      case ClipboardContentType.Html:
      case ClipboardContentType.Rtf:
      case ClipboardContentType.Other:
        var text = _settings?.EncryptDatabase == true
            ? DataProtectionHelper.UnprotectString(entry.TextContent) ?? entry.TextContent
            : entry.TextContent;
        if (!string.IsNullOrEmpty(text))
        {
          data.SetText(text, TextDataFormat.UnicodeText);
          if (entry.ContentType == ClipboardContentType.Html)
            data.SetData(DataFormats.Html, text);
          if (entry.ContentType == ClipboardContentType.Rtf)
            data.SetData(DataFormats.Rtf, text);
        }
        break;

      case ClipboardContentType.Image:
        if (entry.PayloadPath != null && File.Exists(entry.PayloadPath))
        {
          var encrypted = _settings?.EncryptPayloadFiles == true;
          byte[]? bytes = _repository?.ReadPayload(entry.PayloadPath, encrypted)
              ?? File.ReadAllBytes(entry.PayloadPath);
          if (encrypted && _repository == null)
            bytes = DataProtectionHelper.UnprotectBytes(bytes);
          using var ms = new MemoryStream(bytes);
          var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
          data.SetImage(decoder.Frames[0]);
        }
        break;

      case ClipboardContentType.Files:
        var paths = entry.FilePaths.Where(File.Exists).ToArray();
        if (paths.Length > 0)
        {
          data.SetData(DataFormats.FileDrop, paths);
        }
        else if (!string.IsNullOrEmpty(entry.FilePathsJson))
        {
          try
          {
            var stored = JsonSerializer.Deserialize<string[]>(entry.FilePathsJson) ?? Array.Empty<string>();
            var valid = stored.Where(File.Exists).ToArray();
            if (valid.Length > 0) data.SetData(DataFormats.FileDrop, valid);
          }
          catch { }
        }
        break;
    }

    if (data.GetFormats().Length == 0)
      return false;

    return SystemClipboardHelper.TrySetDataObject(data);
  }

  public static void SendPasteKeys()
  {
    // Ctrl+V via SendInput would need P/Invoke; user can paste after clipboard is set
    System.Windows.Forms.SendKeys.SendWait("^v");
  }
}
