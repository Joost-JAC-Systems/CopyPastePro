using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace CopyPastePro.Services;

public static class CodeSyntaxHighlighter
{
  private static readonly (SolidColorBrush Brush, Regex Pattern)[] Rules =
  [
    (ColorBrush("#6a9955"), new Regex(@"//.*$|#.*$", RegexOptions.Multiline)),
    (ColorBrush("#ce9178"), new Regex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Multiline)),
    (ColorBrush("#569cd6"), new Regex(@"\b(class|interface|enum|struct|namespace|using|import|from|def|async|await|return|if|else|for|while|switch|case|break|continue|public|private|protected|static|void|var|let|const|function|new|true|false|null|undefined|try|catch|finally|throw|package|extends|implements)\b")),
    (ColorBrush("#4ec9b0"), new Regex(@"\b\d+\.?\d*\b")),
    (ColorBrush("#dcdcaa"), new Regex(@"\b[A-Z][a-zA-Z0-9_]*\b"))
  ];

  public static void ApplyToRichTextBox(RichTextBox box, string code, string fontFamily, double fontSize)
  {
    box.Document = new FlowDocument { PagePadding = new Thickness(8), Background = ColorBrush("#121218") };
    var para = new Paragraph { Margin = new Thickness(0), FontFamily = new System.Windows.Media.FontFamily(fontFamily), FontSize = fontSize };
    var plain = ColorBrush("#d4d4d4");
    var spans = new List<(int Start, int Length, SolidColorBrush Color)>();

    foreach (var (brush, rx) in Rules)
    {
      foreach (Match m in rx.Matches(code))
        spans.Add((m.Index, m.Length, brush));
    }

    if (spans.Count == 0)
    {
      para.Inlines.Add(new Run(code) { Foreground = plain });
      box.Document.Blocks.Add(para);
      return;
    }

    spans = spans.OrderBy(s => s.Start).ToList();
    var pos = 0;
    foreach (var span in spans)
    {
      if (span.Start < pos) continue;
      if (span.Start > pos)
        para.Inlines.Add(new Run(code[pos..span.Start]) { Foreground = plain });
      para.Inlines.Add(new Run(code.Substring(span.Start, span.Length)) { Foreground = span.Color });
      pos = span.Start + span.Length;
    }
    if (pos < code.Length)
      para.Inlines.Add(new Run(code[pos..]) { Foreground = plain });
    box.Document.Blocks.Add(para);
  }

  private static SolidColorBrush ColorBrush(string hex) =>
      new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
}
