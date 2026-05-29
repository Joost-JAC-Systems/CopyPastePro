using System.Text.RegularExpressions;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

public static class PreviewFormatDetector
{
  public static PreviewFormatKind Detect(ClipboardEntry entry, AppSettings settings)
  {
    if (!settings.FormattingEnabled) return PreviewFormatKind.Plain;

    if (entry.ContentType == ClipboardContentType.Rtf && settings.FormatRtfDocuments)
      return PreviewFormatKind.RtfDocument;

    if (entry.ContentType == ClipboardContentType.Html && settings.FormatHtml)
      return PreviewFormatKind.Html;

    var text = entry.TextContent ?? "";
    if (string.IsNullOrWhiteSpace(text) && entry.ContentType == ClipboardContentType.Files)
    {
      var pdf = entry.FilePaths?.FirstOrDefault(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
      if (pdf != null && settings.FormatPdfPreview) return PreviewFormatKind.Pdf;
    }

    if (string.IsNullOrWhiteSpace(text)) return PreviewFormatKind.Plain;

    var ext = entry.FileExtension?.ToLowerInvariant() ?? "";
    if (settings.FormatPdfPreview && ext == ".pdf") return PreviewFormatKind.Pdf;

    if (settings.FormatExcelGrid && LooksLikeSpreadsheet(text, entry)) return PreviewFormatKind.ExcelGrid;
    if (settings.FormatMarkdown && LooksLikeMarkdown(text, ext)) return PreviewFormatKind.Markdown;
    if (settings.FormatCommands && ShellCommandRunner.LooksLikeCommand(text, ext)) return PreviewFormatKind.Command;
    if (settings.FormatCode && LooksLikeCode(text, ext)) return PreviewFormatKind.Code;
    if (settings.FormatJson && (ext is ".json" or ".jsonc" or ".json5"
        || (ext == "" && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))))
      return PreviewFormatKind.Json;
    if (settings.FormatXml && (ext is ".xml" or ".xsd" or ".xsl"
        || (string.IsNullOrEmpty(ext) && text.TrimStart().StartsWith('<'))))
      return PreviewFormatKind.Xml;
    if (settings.FormatYaml && (ext is ".yaml" or ".yml")) return PreviewFormatKind.Yaml;
    if (settings.FormatCsv && (ext == ".csv" || LooksLikeCsv(text))) return PreviewFormatKind.Csv;
    if (settings.FormatSql && (ext == ".sql" || LooksLikeSql(text))) return PreviewFormatKind.Sql;
    if (settings.FormatIniAndConfig && ext is ".ini" or ".cfg" or ".conf" or ".env") return PreviewFormatKind.Ini;
    if (settings.FormatLogFiles && (ext == ".log" || LooksLikeLog(text))) return PreviewFormatKind.Log;

    return PreviewFormatKind.Plain;
  }

  public static string GetFormatIcon(PreviewFormatKind kind) => kind switch
  {
    PreviewFormatKind.Command => "▶",
    PreviewFormatKind.Code => "⌨",
    PreviewFormatKind.Markdown => "📝",
    PreviewFormatKind.Html => "🌐",
    PreviewFormatKind.RtfDocument => "📄",
    PreviewFormatKind.ExcelGrid => "📊",
    PreviewFormatKind.Pdf => "📕",
    PreviewFormatKind.Json => "{ }",
    PreviewFormatKind.Xml => "⟨/⟩",
    PreviewFormatKind.Csv => "📋",
    PreviewFormatKind.Sql => "🗃",
    PreviewFormatKind.Yaml => "⚙",
    PreviewFormatKind.Ini => "⚙",
    PreviewFormatKind.Log => "📜",
    _ => ""
  };

  private static bool LooksLikeSpreadsheet(string text, ClipboardEntry entry)
  {
    if (entry.Category is "Spreadsheet") return true;
    if (!text.Contains('\t') && !text.Contains(',')) return false;
    var lines = text.Split('\n').Take(20).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    if (lines.Count < 2) return false;
    var cols = lines[0].Split('\t').Length;
    if (cols < 2) cols = lines[0].Split(',').Length;
    return cols >= 2 && lines.Count >= 2;
  }

  private static bool LooksLikeMarkdown(string text, string ext)
  {
    if (ext is ".md" or ".markdown" or ".mdown") return true;
    if (text.Length > 8000) return false;
    var hits = 0;
    if (Regex.IsMatch(text, @"^#{1,6}\s", RegexOptions.Multiline)) hits++;
    if (text.Contains("```")) hits++;
    if (Regex.IsMatch(text, @"\[[^\]]+\]\([^)]+\)")) hits++;
    if (Regex.IsMatch(text, @"^[-*+]\s", RegexOptions.Multiline)) hits++;
    if (text.Contains("|") && Regex.IsMatch(text, @"^\|.+\|", RegexOptions.Multiline)) hits++;
    return hits >= 2;
  }

  private static bool LooksLikeCode(string text, string ext)
  {
    if (FileExtensionCatalog.CategoryExtensions.TryGetValue("Code", out var codeExts)
        && codeExts.Contains(ext.StartsWith('.') ? ext : "." + ext))
      return true;
    if (text.Contains("```")) return true;
    if (text.Length > 50000) return false;
    var codeSignals = new[] { "{", "}", ";", "=>", "function ", "class ", "def ", "public ", "import ", "#include" };
    return codeSignals.Count(text.Contains) >= 2;
  }

  private static bool LooksLikeCsv(string text)
  {
    var lines = text.Split('\n').Take(8).Where(l => l.Length > 0).ToList();
    if (lines.Count < 2) return false;
    var commas = lines.Count(l => l.Count(c => c == ',') >= 2);
    return commas >= lines.Count / 2;
  }

  private static bool LooksLikeSql(string text)
  {
    var t = text.TrimStart();
    return Regex.IsMatch(t, @"^(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|WITH)\b", RegexOptions.IgnoreCase);
  }

  private static bool LooksLikeLog(string text)
  {
    var lines = text.Split('\n').Take(5).ToList();
    if (lines.Count < 2) return false;
    return lines.Count(l => Regex.IsMatch(l, @"\d{4}[-/]\d{2}[-/]\d{2}|\d{2}:\d{2}:\d{2}|ERROR|WARN|INFO|DEBUG")) >= 2;
  }
}
