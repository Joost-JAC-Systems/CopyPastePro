using System.IO;
using System.Text.RegularExpressions;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

public sealed class CategoryClassifier
{
  private readonly AppSettings _settings;

  public CategoryClassifier(AppSettings settings) => _settings = settings;

  public void Enrich(ClipboardEntry entry, string? sourceApp = null)
  {
    entry.SourceApp = sourceApp ?? entry.SourceApp ?? "";
    entry.FileExtension = DetectExtension(entry);
    entry.Category = Classify(entry);
    if (_settings.AutoTagFromCategory && string.IsNullOrEmpty(entry.Tags))
      entry.Tags = entry.Category;
  }

  public string Classify(ClipboardEntry entry)
  {
    if (!_settings.AutoCategorizeEnabled) return entry.Category ?? "General";

    if (entry.ContentType == ClipboardContentType.Image) return "Image";
    if (entry.ContentType == ClipboardContentType.Html) return "Web";
    if (entry.ContentType == ClipboardContentType.Rtf) return "Document";

    var ext = entry.FileExtension ?? DetectExtension(entry);
    if (!string.IsNullOrEmpty(ext))
    {
      var fromCustom = ClassifyByCustomRules(ext);
      if (fromCustom != null) return fromCustom;

      if (FileExtensionCatalog.IsImageExtension(ext))
        return "Image";

      var builtIn = FileExtensionCatalog.GetBuiltInCategory(ext);
      if (builtIn != null) return builtIn;

      return "Files";
    }

    if (entry.ContentType == ClipboardContentType.Files)
      return entry.FilePaths.Count > 1 ? "Files" : "Files";

    var text = entry.TextContent ?? entry.Preview;
    if (IsUrl(text)) return "Link";
    if (LooksLikeCode(text)) return "Code";
    if (entry.ContentType == ClipboardContentType.Text && text.Length > 2000) return "LongText";
    return entry.ContentType switch
    {
      ClipboardContentType.Text => "Text",
      _ => "General"
    };
  }

  private string? ClassifyByCustomRules(string ext)
  {
    foreach (var rule in _settings.CustomExtensionCategories)
    {
      foreach (var part in rule.Extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        var e = part.StartsWith('.') ? part : "." + part;
        if (e.Equals(ext, StringComparison.OrdinalIgnoreCase)) return rule.Category;
      }
    }
    return null;
  }

  private static string DetectExtension(ClipboardEntry entry)
  {
    if (!string.IsNullOrEmpty(entry.FileExtension)) return entry.FileExtension;
    var path = entry.FilePaths.FirstOrDefault() ?? entry.PayloadPath;
    if (!string.IsNullOrEmpty(path))
    {
      var ext = Path.GetExtension(path);
      if (!string.IsNullOrEmpty(ext)) return ext.ToLowerInvariant();
    }
    return "";
  }

  private static bool IsUrl(string s) =>
      Regex.IsMatch(s.Trim(), @"^https?://", RegexOptions.IgnoreCase);

  private static bool LooksLikeCode(string s)
  {
    if (s.Length < 20) return false;
    var markers = new[]
    {
      "{", "}", ";", "=>", "function ", "class ", "def ", "import ", "public ", "#include", "<?", "</",
      "namespace ", "using ", "const ", "let ", "var ", "fn ", "func ", "package ", "interface "
    };
    return markers.Count(m => s.Contains(m, StringComparison.Ordinal)) >= 2;
  }

  public IReadOnlyList<string> AllCategories()
  {
    var set = new HashSet<string>(FileExtensionCatalog.AllBuiltInCategories(), StringComparer.OrdinalIgnoreCase);
    foreach (var r in _settings.CustomExtensionCategories)
      if (!string.IsNullOrWhiteSpace(r.Category)) set.Add(r.Category);
    return set.OrderBy(c => c == "All" ? "" : c).ToList();
  }
}
