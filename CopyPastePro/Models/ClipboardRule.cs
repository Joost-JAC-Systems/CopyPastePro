namespace CopyPastePro.Models;

public enum RuleTrigger { OnCapture, OnPaste }
public enum RuleConditionType
{
  ContainsText,
  StartsWith,
  MatchesRegex,
  ContentTypeIs,
  FromProcess,
  LargerThanBytes,
  SmallerThanBytes
}
public enum RuleAction { Ignore, Pin, DeleteFromHistory, MoveToTop, AddTag }

public sealed class ClipboardRule
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public bool Enabled { get; set; } = true;
  public string Name { get; set; } = "New rule";
  public RuleTrigger Trigger { get; set; } = RuleTrigger.OnCapture;
  public RuleConditionType ConditionType { get; set; } = RuleConditionType.ContainsText;
  public string ConditionValue { get; set; } = string.Empty;
  public RuleAction Action { get; set; } = RuleAction.Ignore;
  public string? Tag { get; set; }
}
