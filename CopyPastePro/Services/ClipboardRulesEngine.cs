using System.Diagnostics;
using System.Text.RegularExpressions;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

public sealed class ClipboardRulesEngine
{
  private readonly AppSettings _settings;
  private readonly PrivacyService _privacy;

  public ClipboardRulesEngine(AppSettings settings, PrivacyService privacy)
  {
    _settings = settings;
    _privacy = privacy;
  }

  public bool ShouldCaptureType(ClipboardContentType type) => ShouldCaptureTypeInternal(type);

  public bool ShouldCaptureEntry(ClipboardEntry entry)
  {
    if (!_settings.MonitoringEnabled || _privacy.IsMonitoringPaused) return false;
    if (_settings.IgnoreBlankClips && string.IsNullOrWhiteSpace(entry.Preview)) return false;
    if (_settings.MaxSingleItemBytes > 0 && entry.SizeBytes > _settings.MaxSingleItemBytes) return false;
    if (!_privacy.ShouldCaptureStoredEntry(entry)) return false;

    if (_settings.RulesEnabled)
    {
      var proc = PrivacyService.GetForegroundContext().ProcessName;
      foreach (var rule in _settings.Rules.Where(r => r.Enabled && r.Trigger == RuleTrigger.OnCapture))
      {
        if (Matches(rule, entry, proc) && rule.Action == RuleAction.Ignore) return false;
      }
    }

    if (_settings.AutoDeleteSensitivePatterns && _privacy.Analyze(entry).IsSensitive) return false;

    return true;
  }

  public bool ShouldCapture(ClipboardEntry entry) => ShouldCaptureEntry(entry);

  public void ApplyPostCapture(ClipboardEntry entry, ClipboardHistoryRepository repo)
  {
    if (!_settings.RulesEnabled) return;
    var proc = PrivacyService.GetForegroundContext().ProcessName;
    foreach (var rule in _settings.Rules.Where(r => r.Enabled && r.Trigger == RuleTrigger.OnCapture))
    {
      if (!Matches(rule, entry, proc)) continue;
      switch (rule.Action)
      {
        case RuleAction.Pin:
          repo.SetPinned(entry.Id, true);
          break;
        case RuleAction.DeleteFromHistory:
          repo.Delete(entry.Id);
          break;
      }
    }
  }

  private bool ShouldCaptureTypeInternal(ClipboardContentType type) => type switch
  {
    ClipboardContentType.Text => _settings.CaptureText,
    ClipboardContentType.Image => _settings.CaptureImages,
    ClipboardContentType.Files => _settings.CaptureFiles,
    ClipboardContentType.Html => _settings.CaptureHtml,
    ClipboardContentType.Rtf => _settings.CaptureRtf,
    _ => _settings.CaptureOtherFormats
  };

  private static bool Matches(ClipboardRule rule, ClipboardEntry entry, string process)
  {
    var val = rule.ConditionValue ?? "";
    return rule.ConditionType switch
    {
      RuleConditionType.ContainsText => (entry.TextContent ?? entry.Preview).Contains(val, StringComparison.OrdinalIgnoreCase),
      RuleConditionType.StartsWith => (entry.TextContent ?? entry.Preview).StartsWith(val, StringComparison.OrdinalIgnoreCase),
      RuleConditionType.MatchesRegex => Regex.IsMatch(entry.TextContent ?? entry.Preview, val, RegexOptions.IgnoreCase),
      RuleConditionType.ContentTypeIs => entry.ContentType.ToString().Equals(val, StringComparison.OrdinalIgnoreCase),
      RuleConditionType.FromProcess => process.Contains(val, StringComparison.OrdinalIgnoreCase),
      RuleConditionType.LargerThanBytes => long.TryParse(val, out var min) && entry.SizeBytes > min,
      RuleConditionType.SmallerThanBytes => long.TryParse(val, out var max) && entry.SizeBytes < max,
      _ => false
    };
  }
}
