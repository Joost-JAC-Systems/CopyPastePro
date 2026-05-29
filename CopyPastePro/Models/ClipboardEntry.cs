namespace CopyPastePro.Models;

public sealed class ClipboardEntry
{
  public long Id { get; set; }
  public ClipboardContentType ContentType { get; set; }
  public string Preview { get; set; } = string.Empty;
  public string? TextContent { get; set; }
  public string? PayloadPath { get; set; }
  public string? FilePathsJson { get; set; }
  public string ContentHash { get; set; } = string.Empty;
  public DateTime CapturedAt { get; set; }
  public bool IsPinned { get; set; }
  public bool IsFavorite { get; set; }
  public long SizeBytes { get; set; }
  public string Category { get; set; } = "General";
  public string Tags { get; set; } = "";
  public string SourceApp { get; set; } = "";
  public string FileExtension { get; set; } = "";
  public bool IsSensitive { get; set; }
  public string? ExportPath { get; set; }
  /// <summary>Path in the user-chosen sync folder (separate from auto-save export_path).</summary>
  public string? SyncPath { get; set; }

  public string TypeLabel => ContentType switch
  {
    ClipboardContentType.Text => "Text",
    ClipboardContentType.Image => "Image",
    ClipboardContentType.Files => "Files",
    ClipboardContentType.Html => "HTML",
    ClipboardContentType.Rtf => "Rich Text",
    _ => "Other"
  };

  public string CategoryIcon => Category switch
  {
    "Image" => "🖼",
    "Code" => "💻",
    "Document" => "📄",
    "Spreadsheet" => "📊",
    "Presentation" => "📽",
    "Archive" => "📦",
    "Audio" => "🎵",
    "Video" => "🎬",
    "Executable" => "⚙",
    "Design" => "🎨",
    "Link" => "🔗",
    "Web" => "🌐",
    "Files" => "📁",
    "LongText" => "📝",
    _ => "📋"
  };

  public string SourceDisplay => string.IsNullOrEmpty(SourceApp) ? "" : SourceApp;

  public string TimeAgo
  {
    get
    {
      var span = DateTime.Now - CapturedAt;
      if (span.TotalSeconds < 60) return "Just now";
      if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
      if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
      if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
      return CapturedAt.ToString("MMM d, yyyy");
    }
  }

  public IReadOnlyList<string> FilePaths
  {
    get
    {
      if (string.IsNullOrWhiteSpace(FilePathsJson)) return Array.Empty<string>();
      try
      {
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(FilePathsJson) ?? new List<string>();
      }
      catch
      {
        return Array.Empty<string>();
      }
    }
  }
}
