using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ChBrowser.Models;

namespace ChBrowser.Services.Render;

/// <summary>
/// スレッドを単体で閲覧可能な完全な自己完結 HTML ファイルとしてエクスポートするサービス。
/// 外部リソース（CSS/JS）に依存せず、ダークモード対応、レスアンカージャンプ、URLリンクに対応する。
/// </summary>
public static class ThreadHtmlExporter
{
    private static readonly Regex UrlRegex = new(
        @"https?://[a-zA-Z0-9\-_]+(?:\.[a-zA-Z0-9\-_]+)*(?::\d+)?(?:/[^\s<>""]*)?",
        RegexOptions.Compiled);

    private static readonly Regex AnchorRegex = new(
        @"(?:&gt;&gt;|＞＞)(\d+)(?:-(\d+))?",
        RegexOptions.Compiled);

    /// <summary>
    /// ファイルシステムで使用可能な安全なファイル名を取得する。
    /// </summary>
    public static string GetSafeFileName(string? title, string fallback = "thread")
    {
        if (string.IsNullOrWhiteSpace(title)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
        {
            if (invalid.Contains(c) || c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }
        var result = sb.ToString().Trim(' ', '.', '_');
        if (result.Length > 80)
        {
            result = result.Substring(0, 80).TrimEnd(' ', '.', '_');
        }
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    /// <summary>
    /// スレッド情報および投稿リストからスタンドアロン HTML を生成する。
    /// </summary>
    public static string Export(
        string title,
        string boardName,
        string? threadUrl,
        IReadOnlyList<Post> posts,
        DateTimeOffset? exportedAt = null)
    {
        var now = exportedAt ?? DateTimeOffset.Now;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "無題スレッド" : title;
        var postCount = posts.Count;

        var sb = new StringBuilder(postCount * 512 + 2048);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ja\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append("  <title>").Append(HtmlEscape.Text(safeTitle)).AppendLine("</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine(@"
    :root {
      --bg-color: #f7f9fa;
      --card-bg: #ffffff;
      --text-color: #24292f;
      --dim-color: #57606a;
      --border-color: #d0d7de;
      --header-bg: #ffffff;
      --header-border: #e1e4e8;
      --link-color: #0969da;
      --anchor-color: #0550ae;
      --quote-color: #1a7f37;
      --number-color: #0969da;
      --name-color: #116329;
      --mail-color: #6e7781;
      --id-color: #6e7781;
      --nav-bg: rgba(255, 255, 255, 0.9);
      --highlight-bg: #fff8c5;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --bg-color: #0d1117;
        --card-bg: #161b22;
        --text-color: #c9d1d9;
        --dim-color: #8b949e;
        --border-color: #30363d;
        --header-bg: #161b22;
        --header-border: #30363d;
        --link-color: #58a6ff;
        --anchor-color: #79c0ff;
        --quote-color: #7ee787;
        --number-color: #58a6ff;
        --name-color: #56d364;
        --mail-color: #8b949e;
        --id-color: #8b949e;
        --nav-bg: rgba(22, 27, 34, 0.9);
        --highlight-bg: #383214;
      }
    }
    * {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
    }
    html {
      scroll-behavior: smooth;
    }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Noto Sans CJK JP', 'Noto Sans JP', 'Hiragino Sans', 'Meiryo', sans-serif;
      background-color: var(--bg-color);
      color: var(--text-color);
      line-height: 1.6;
      padding-bottom: 40px;
    }
    header.thread-header {
      background-color: var(--header-bg);
      border-bottom: 1px solid var(--header-border);
      padding: 16px 24px;
      position: sticky;
      top: 0;
      z-index: 100;
      backdrop-filter: blur(8px);
    }
    .header-inner {
      max-width: 960px;
      margin: 0 auto;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
    }
    .header-info {
      flex: 1 1 300px;
    }
    h1.thread-title {
      font-size: 1.25rem;
      font-weight: 700;
      margin-bottom: 4px;
      word-break: break-word;
    }
    .meta-bar {
      font-size: 0.825rem;
      color: var(--dim-color);
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
    .meta-bar a {
      color: var(--link-color);
      text-decoration: none;
    }
    .meta-bar a:hover {
      text-decoration: underline;
    }
    .header-nav {
      display: flex;
      gap: 8px;
    }
    .nav-btn {
      font-size: 0.8rem;
      padding: 6px 12px;
      border-radius: 6px;
      background-color: var(--card-bg);
      color: var(--text-color);
      border: 1px solid var(--border-color);
      text-decoration: none;
      cursor: pointer;
      transition: background 0.15s ease;
    }
    .nav-btn:hover {
      background-color: var(--border-color);
    }
    main.posts-container {
      max-width: 960px;
      margin: 20px auto;
      padding: 0 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    article.post {
      background-color: var(--card-bg);
      border: 1px solid var(--border-color);
      border-radius: 8px;
      padding: 12px 16px;
      transition: background-color 0.2s;
    }
    article.post:target {
      background-color: var(--highlight-bg);
      border-color: var(--anchor-color);
    }
    .post-header {
      font-size: 0.875rem;
      margin-bottom: 8px;
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: baseline;
    }
    .post-no {
      font-weight: 700;
      color: var(--number-color);
      text-decoration: none;
    }
    .post-no:hover {
      text-decoration: underline;
    }
    .post-name {
      font-weight: 700;
      color: var(--name-color);
      word-break: break-all;
    }
    .post-mail {
      font-size: 0.8rem;
      color: var(--mail-color);
    }
    .post-date {
      color: var(--dim-color);
      font-size: 0.8rem;
    }
    .post-id {
      color: var(--id-color);
      font-size: 0.8rem;
      font-family: monospace;
    }
    .post-body {
      font-size: 0.95rem;
      word-break: break-word;
      white-space: pre-wrap;
      line-height: 1.65;
    }
    .post-body a {
      color: var(--link-color);
      text-decoration: none;
      word-break: break-all;
    }
    .post-body a:hover {
      text-decoration: underline;
    }
    .post-body a.post-anchor {
      color: var(--anchor-color);
      font-weight: 600;
      background-color: rgba(9, 105, 218, 0.08);
      padding: 1px 4px;
      border-radius: 4px;
    }
    .post-body a.post-anchor:hover {
      background-color: rgba(9, 105, 218, 0.16);
    }
    .quote-line {
      color: var(--quote-color);
    }
    footer.thread-footer {
      max-width: 960px;
      margin: 32px auto 0;
      padding: 0 16px;
      text-align: center;
      font-size: 0.8rem;
      color: var(--dim-color);
    }
");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // ヘッダー部
        sb.AppendLine("  <header class=\"thread-header\">");
        sb.AppendLine("    <div class=\"header-inner\">");
        sb.AppendLine("      <div class=\"header-info\">");
        sb.Append("        <h1 class=\"thread-title\">").Append(HtmlEscape.Text(safeTitle)).AppendLine("</h1>");
        sb.AppendLine("        <div class=\"meta-bar\">");
        if (!string.IsNullOrWhiteSpace(boardName))
        {
            sb.Append("          <span>板: <strong>").Append(HtmlEscape.Text(boardName)).AppendLine("</strong></span>");
        }
        sb.Append("          <span>レス数: ").Append(postCount).AppendLine("</span>");
        if (!string.IsNullOrWhiteSpace(threadUrl))
        {
            sb.Append("          <span><a href=\"").Append(HtmlEscape.Attr(threadUrl)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">元スレッドを開く</a></span>");
        }
        sb.Append("          <span>出力日時: ").Append(now.ToString("yyyy/MM/dd HH:mm:ss")).AppendLine("</span>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <nav class=\"header-nav\">");
        sb.AppendLine("        <a href=\"#p1\" class=\"nav-btn\">1 へ</a>");
        if (postCount > 0)
        {
            sb.Append("        <a href=\"#p").Append(postCount).AppendLine("\" class=\"nav-btn\">最新へ</a>");
        }
        sb.AppendLine("      </nav>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </header>");

        // レス本文コンテナ
        sb.AppendLine("  <main class=\"posts-container\">");

        for (int i = 0; i < posts.Count; i++)
        {
            var p = posts[i];
            var num = p.Number > 0 ? p.Number : (i + 1);
            sb.Append("    <article class=\"post\" id=\"p").Append(num).AppendLine("\">");
            
            // レスヘッダー
            sb.AppendLine("      <div class=\"post-header\">");
            sb.Append("        <a href=\"#p").Append(num).Append("\" class=\"post-no\">").Append(num).AppendLine("</a>");
            
            var name = string.IsNullOrWhiteSpace(p.Name) ? "名無しさん" : p.Name;
            sb.Append("        <span class=\"post-name\">").Append(HtmlEscape.Text(name)).AppendLine("</span>");

            if (!string.IsNullOrWhiteSpace(p.Mail))
            {
                sb.Append("        <span class=\"post-mail\">[").Append(HtmlEscape.Text(p.Mail)).AppendLine("]</span>");
            }

            if (!string.IsNullOrWhiteSpace(p.DateText))
            {
                sb.Append("        <span class=\"post-date\">").Append(HtmlEscape.Text(p.DateText)).AppendLine("</span>");
            }

            if (!string.IsNullOrWhiteSpace(p.Id))
            {
                sb.Append("        <span class=\"post-id\">ID:").Append(HtmlEscape.Text(p.Id)).AppendLine("</span>");
            }
            sb.AppendLine("      </div>");

            // レス本文
            sb.Append("      <div class=\"post-body\">");
            sb.Append(FormatPostBody(p.Body));
            sb.AppendLine("</div>");

            sb.AppendLine("    </article>");
        }

        sb.AppendLine("  </main>");

        // フッター
        sb.AppendLine("  <footer class=\"thread-footer\">");
        sb.AppendLine("    <p>Exported by ChBrowser Linux</p>");
        sb.AppendLine("  </footer>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// レス本文を HTML エスケープし、アンカーと URL をリンク化、引用行をスタイリングする。
    /// </summary>
    public static string FormatPostBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return "";

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(body.Length + 64);

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("<br>\n");

            var line = lines[i];
            var escaped = HtmlEscape.Text(line);

            // URL リンク化
            escaped = UrlRegex.Replace(escaped, match =>
            {
                var url = match.Value;
                return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>";
            });

            // アンカーリンク化 (&gt;&gt;123, &gt;&gt;1-5)
            escaped = AnchorRegex.Replace(escaped, match =>
            {
                var targetNum = match.Groups[1].Value;
                var range = match.Groups[2].Success ? "-" + match.Groups[2].Value : "";
                return $"<a href=\"#p{targetNum}\" class=\"post-anchor\">&gt;&gt;{targetNum}{range}</a>";
            });

            // 引用行 (行頭が &gt; または ＞)
            if (escaped.StartsWith("&gt;") || escaped.StartsWith("＞"))
            {
                sb.Append("<span class=\"quote-line\">").Append(escaped).Append("</span>");
            }
            else
            {
                sb.Append(escaped);
            }
        }

        return sb.ToString();
    }
}
