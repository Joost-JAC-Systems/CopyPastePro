using Markdig;

namespace CopyPastePro.Services;

/// <summary>Markdown → HTML using Markdig with Obsidian-style extensions (tables, tasks, strikethrough, etc.).</summary>
public static class MarkdownPreviewBuilder
{
  private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .UseEmphasisExtras()
      .UsePipeTables()
      .UseGridTables()
      .UseTaskLists()
      .UseAutoLinks()
      .UseYamlFrontMatter()
      .Build();

  public static string ToHtml(string markdown)
  {
    if (string.IsNullOrEmpty(markdown)) return "<html><body></body></html>";
    var body = Markdown.ToHtml(markdown, Pipeline);
    return WrapHtml(body);
  }

  private static string WrapHtml(string body) =>
      "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
      "<style>body{font-family:'Segoe UI',system-ui,sans-serif;font-size:14px;line-height:1.55;color:#e8e8e8;background:#1a1a1e;margin:12px;}" +
      "h1,h2,h3,h4{color:#f0f0f5;margin-top:1.2em;}a{color:#7eb8ff;}code{background:#2a2a32;padding:2px 5px;border-radius:4px;font-family:Consolas,monospace;}" +
      "pre{background:#121218;padding:12px;border-radius:8px;overflow-x:auto;}pre code{background:transparent;padding:0;}" +
      "blockquote{border-left:3px solid #5a5a70;margin-left:0;padding-left:12px;color:#b0b0c0;}" +
      "table{border-collapse:collapse;width:100%;margin:8px 0;}th,td{border:1px solid #3a3a48;padding:6px 10px;}th{background:#252530;}" +
      "ul.task-list{list-style:none;padding-left:0;}input[type=checkbox]{margin-right:6px;}hr{border:none;border-top:1px solid #3a3a48;}" +
      "</style></head><body>" + body + "</body></html>";
}
