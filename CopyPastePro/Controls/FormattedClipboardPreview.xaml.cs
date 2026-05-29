using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CopyPastePro.Models;
using CopyPastePro.Services;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPoint = System.Windows.Point;

namespace CopyPastePro.Controls;

public partial class FormattedClipboardPreview : UserControl
{
  private readonly DispatcherTimer _overlayHideTimer;
  private DateTime _lastMouseMove = DateTime.UtcNow;
  private bool _htmlVisualMode;
  private string _htmlSource = "";
  private string _commandText = "";
  private ShellKind _commandShell;
  private AppSettings? _settings;
  private Window? _owner;

  public FormattedClipboardPreview()
  {
    InitializeComponent();
    MouseMove += OnMouseMove;
    MouseEnter += OnMouseMove;
    _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    _overlayHideTimer.Tick += (_, _) => TickOverlay();
  }

  public void Clear()
  {
    HideAll();
    PlainText.Text = "";
    OverlayButtons.Children.Clear();
    OverlayBar.Opacity = 0;
  }

  public async void ShowEntry(ClipboardEntry entry, string displayText, AppSettings settings, bool formattingActive, Window? owner)
  {
    _settings = settings;
    _owner = owner;
    Clear();

    if (!formattingActive || !settings.FormattingEnabled)
    {
      ShowPlain(displayText, settings);
      return;
    }

    var text = Truncate(entry.TextContent ?? displayText, settings.FormattingMaxPreviewChars);
    var kind = PreviewFormatDetector.Detect(entry, settings);

    switch (kind)
    {
      case PreviewFormatKind.Command:
        _commandText = text;
        _commandShell = ShellCommandRunner.DetectShell(text, entry.FileExtension ?? "");
        ShowCode(text, settings);
        if (settings.ShowRunCommandButton && ShellCommandRunner.CanRunOnThisMachine(_commandShell))
          AddOverlayButton("▶ Run", RunCommand, accent: true);
        break;
      case PreviewFormatKind.Code:
        ShowCode(text, settings);
        break;
      case PreviewFormatKind.Markdown:
        ShowHtml(MarkdownPreviewBuilder.ToHtml(text), settings);
        break;
      case PreviewFormatKind.Html:
        _htmlSource = text;
        _htmlVisualMode = false;
        ShowPlain(HtmlUtility.StripTagsForDisplay(text), settings);
        AddOverlayButton("Visual", () => ToggleHtml(true, settings), accent: true);
        AddOverlayButton("Code", () => ToggleHtml(false, settings));
        break;
      case PreviewFormatKind.RtfDocument:
        ShowRtf(text);
        break;
      case PreviewFormatKind.ExcelGrid:
        ShowExcelGrid(text);
        break;
      case PreviewFormatKind.Pdf:
        await ShowPdfAsync(entry);
        break;
      case PreviewFormatKind.Json:
      case PreviewFormatKind.Xml:
      case PreviewFormatKind.Yaml:
      case PreviewFormatKind.Csv:
      case PreviewFormatKind.Sql:
      case PreviewFormatKind.Ini:
      case PreviewFormatKind.Log:
        ShowCode(text, settings);
        break;
      default:
        ShowPlain(displayText, settings);
        break;
    }
  }

  private void ShowPlain(string text, AppSettings settings)
  {
    HideAll();
    PlainText.FontFamily = new WpfFontFamily(settings.FormattingSansFont);
    PlainText.FontSize = settings.FormattingFontSize;
    PlainText.Text = text;
    PlainText.Visibility = Visibility.Visible;
  }

  private void ShowCode(string code, AppSettings settings)
  {
    HideAll();
    CodeViewer.Visibility = Visibility.Visible;
    CodeSyntaxHighlighter.ApplyToRichTextBox(CodeViewer, code, settings.FormattingMonoFont, settings.FormattingFontSize);
  }

  private void ShowRtf(string rtf)
  {
    HideAll();
    RtfViewer.Visibility = Visibility.Visible;
    try
    {
      var doc = new FlowDocument();
      RtfViewer.Document = doc;
      var range = new TextRange(doc.ContentStart, doc.ContentEnd);
      using var ms = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
      range.Load(ms, System.Windows.DataFormats.Rtf);
    }
    catch
    {
      ShowPlain(rtf, _settings!);
    }
  }

  private void ShowHtml(string html, AppSettings settings)
  {
    HideAll();
    HtmlBrowser.Visibility = Visibility.Visible;
    HtmlBrowser.NavigateToString(html);
  }

  private void ToggleHtml(bool visual, AppSettings settings)
  {
    _htmlVisualMode = visual;
    if (visual)
    {
      HideAll();
      HtmlBrowser.Visibility = Visibility.Visible;
      try { HtmlBrowser.NavigateToString(_htmlSource); }
      catch { ShowPlain(_htmlSource, settings); }
    }
    else
    {
      ShowPlain(HtmlUtility.StripTagsForDisplay(_htmlSource), settings);
    }
    FlashOverlay();
  }

  private void ShowExcelGrid(string text)
  {
    HideAll();
    var rows = ParseGrid(text);
    if (rows.Count == 0)
    {
      ShowPlain(text, _settings!);
      return;
    }
    ExcelGrid.ItemsSource = rows;
    ExcelGrid.Visibility = Visibility.Visible;
  }

  private async Task ShowPdfAsync(ClipboardEntry entry)
  {
    var path = entry.FilePaths?.FirstOrDefault(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
    if (path == null || !File.Exists(path))
    {
      ShowPlain("PDF preview requires a copied PDF file path.", _settings!);
      return;
    }
    HideAll();
    var bmp = await PdfPreviewHelper.RenderFirstPageAsync(path);
    if (bmp == null)
    {
      ShowPlain($"Could not render PDF:\n{path}", _settings!);
      return;
    }
    PdfImage.Source = bmp;
    PdfImage.Visibility = Visibility.Visible;
  }

  private void RunCommand()
  {
    if (_settings == null) return;
    ShellCommandRunner.Run(_commandText, _commandShell, _owner, _settings.ConfirmBeforeRunCommand);
  }

  private void AddOverlayButton(string label, Action click, bool accent = false)
  {
    var btn = new Button
    {
      Content = label,
      Margin = new Thickness(4, 2, 4, 2),
      Padding = new Thickness(12, 6, 12, 6),
      Cursor = WpfCursors.Hand,
      Foreground = WpfBrushes.White,
      Background = accent
          ? new LinearGradientBrush(
              WpfColor.FromRgb(0x6A, 0x5A, 0xFF),
              WpfColor.FromRgb(0x3A, 0x9A, 0xFF),
              new WpfPoint(0, 0), new WpfPoint(1, 1))
          : new SolidColorBrush(WpfColor.FromArgb(0x88, 0x40, 0x40, 0x50)),
      BorderThickness = new Thickness(0)
    };
    btn.Click += (_, _) => click();
    OverlayButtons.Children.Add(btn);
    OverlayBar.Opacity = 1;
  }

  private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
  {
    _lastMouseMove = DateTime.UtcNow;
    if (OverlayButtons.Children.Count > 0 && OverlayBar.Opacity < 0.5)
      FlashOverlay();
  }

  private void FlashOverlay()
  {
    if (OverlayButtons.Children.Count == 0) return;
    OverlayBar.BeginAnimation(UIElement.OpacityProperty, null);
    OverlayBar.Opacity = 1;
    _overlayHideTimer.Start();
  }

  private void TickOverlay()
  {
    if (OverlayButtons.Children.Count == 0) { _overlayHideTimer.Stop(); return; }
    if ((DateTime.UtcNow - _lastMouseMove).TotalMilliseconds < 2500) return;

    var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
    {
      EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };
    OverlayBar.BeginAnimation(UIElement.OpacityProperty, anim);
    _overlayHideTimer.Stop();
  }

  private void HideAll()
  {
    PlainText.Visibility = Visibility.Collapsed;
    CodeViewer.Visibility = Visibility.Collapsed;
    RtfViewer.Visibility = Visibility.Collapsed;
    HtmlBrowser.Visibility = Visibility.Collapsed;
    ExcelGrid.Visibility = Visibility.Collapsed;
    PdfImage.Visibility = Visibility.Collapsed;
    OverlayButtons.Children.Clear();
  }

  private static string Truncate(string s, int max) =>
      s.Length <= max ? s : s[..max] + "\n… (truncated)";

  private static List<Dictionary<string, string>> ParseGrid(string text)
  {
    var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(200).ToList();
    if (lines.Count == 0) return [];
    var delim = lines[0].Contains('\t') ? '\t' : ',';
    var headers = lines[0].Split(delim).Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Col{i + 1}" : h.Trim()).ToArray();
    var rows = new List<Dictionary<string, string>>();
    foreach (var line in lines.Skip(1))
    {
      var cells = line.Split(delim);
      var row = new Dictionary<string, string>();
      for (var i = 0; i < headers.Length; i++)
        row[headers[i]] = i < cells.Length ? cells[i].Trim() : "";
      rows.Add(row);
    }
    return rows;
  }
}

internal static class HtmlUtility
{
  public static string StripTagsForDisplay(string html) =>
      System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
          .Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
}
