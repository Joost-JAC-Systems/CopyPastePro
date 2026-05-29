using System.Text.RegularExpressions;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

public static partial class ImageDisplayHelper
{
  public static string FormatTitle(ClipboardEntry entry, int? width = null, int? height = null)
  {
    var (pw, ph) = TryParseDimensions(entry.Preview);
    // Prefer dimensions recorded at capture — never thumbnail decode sizes.
    var w = pw > 0 ? pw : (width ?? 0);
    var h = ph > 0 ? ph : (height ?? 0);
    var title = StripDimensionSuffix(entry.Preview);
    if (string.IsNullOrWhiteSpace(title))
      title = "Image";
    if (w > 0 && h > 0)
      return $"{title} ({w}×{h})";
    return title;
  }

  public static (int Width, int Height) TryParseDimensions(string preview)
  {
    var m = DimensionRegex().Match(preview);
    if (!m.Success)
      return (0, 0);
    return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
  }

  public static string StripDimensionSuffix(string preview) =>
      DimensionSuffixRegex().Replace(preview, "").Trim();

  [GeneratedRegex(@"(\d+)\s*[x×]\s*(\d+)", RegexOptions.IgnoreCase)]
  private static partial Regex DimensionRegex();

  [GeneratedRegex(@"\s*\d+\s*[x×]\s*\d+\s*$", RegexOptions.IgnoreCase)]
  private static partial Regex DimensionSuffixRegex();
}
