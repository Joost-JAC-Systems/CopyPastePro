using CopyPastePro.Models;

namespace CopyPastePro.Services;

/// <summary>Post-capture automation: auto-pin, auto-favorite, per-category retention.</summary>
public sealed class SmartOrganizerService
{
  private readonly AppSettings _settings;
  private readonly ClipboardHistoryRepository _repository;

  public SmartOrganizerService(AppSettings settings, ClipboardHistoryRepository repository)
  {
    _settings = settings;
    _repository = repository;
  }

  public void AfterCapture(ClipboardEntry entry)
  {
    if (_settings.AutoPinCategories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
      _repository.SetPinned(entry.Id, true);

    if (_settings.AutoFavoriteCategories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
      _repository.SetFavorite(entry.Id, true);

    if (_settings.AutoPinLargeClips && entry.SizeBytes >= _settings.AutoPinMinBytes)
      _repository.SetPinned(entry.Id, true);

    RunRetention();
  }

  public void RunRetention()
  {
    if (_settings.AutoDeleteAfterDays > 0)
      _repository.DeleteOlderThan(_settings.AutoDeleteAfterDays, pinnedOnly: false);

    foreach (var rule in _settings.CategoryRetentionDays)
    {
      if (rule.Days > 0 && !string.IsNullOrWhiteSpace(rule.Category))
        _repository.DeleteCategoryOlderThan(rule.Category, rule.Days);
    }

    foreach (var limit in _settings.CategoryMaxItems)
    {
      if (limit.Max > 0 && !string.IsNullOrWhiteSpace(limit.Category))
        _repository.TrimCategory(limit.Category, limit.Max);
    }
  }
}

public sealed class CategoryRetentionRule
{
  public string Category { get; set; } = "";
  public int Days { get; set; }
}

public sealed class CategoryItemLimit
{
  public string Category { get; set; } = "";
  public int Max { get; set; }
}
