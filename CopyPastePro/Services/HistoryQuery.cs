using CopyPastePro.Models;

namespace CopyPastePro.Services;

public sealed class HistoryQuery
{
  public string? Search { get; set; }
  public ClipboardContentType? ContentType { get; set; }
  public string? Category { get; set; }
  public bool FavoritesOnly { get; set; }
  public bool PinnedOnly { get; set; }
  public HistorySortMode Sort { get; set; } = HistorySortMode.PinnedThenNewest;
  public int? Take { get; set; }
}
