namespace CopyPastePro.Models;

/// <summary>Custom mapping: extensions (without dot) → category name.</summary>
public sealed class ExtensionCategoryRule
{
  public string Category { get; set; } = "Custom";
  public string Extensions { get; set; } = ""; // e.g. ".ps1,.psm1,.psd1"
}
