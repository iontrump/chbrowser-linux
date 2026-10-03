using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Storage;

namespace ChBrowser.Services.Api;

/// <summary>
/// 板一覧の取得・保存・読み込み。形式は提供者ごとに違うのでパースは <see cref="ChBrowser.Services.Bbs.IBbsProvider.ParseBoardList"/> に任せる:
/// 5ch は <c>bbsmenu.json</c> (<see cref="ParseBbsmenuJson"/>)、まちBBS は運営提供の <c>bbsmenu.html</c> (5ch 旧 bbsmenu と同じ
/// <c>&lt;B&gt;地区&lt;/B&gt;</c> + <c>&lt;A HREF=...&gt;板名&lt;/A&gt;</c> 形式、Shift_JIS、<see cref="ParseBbsmenuHtml"/>)、
/// エッヂは <c>/api/boards</c> (JSON)。したらばは板一覧を持たない (<see cref="ChBrowser.Services.Bbs.BbsCapabilities.BoardList"/> 無し)。
/// </summary>
public sealed class BbsmenuClient
{
    private readonly MonazillaClient _client;
    private readonly DataPaths       _paths;

    public BbsmenuClient(MonazillaClient client, DataPaths paths)
    {
        _client = client;
        _paths  = paths;
    }

    /// <summary>5ch の板一覧を取得 (互換 API)。</summary>
    public Task<IReadOnlyList<BoardCategory>> FetchAndSaveAsync(CancellationToken ct = default)
        => FetchAndSaveAsync(ChBrowser.Services.Bbs.BbsRegistry.FiveCh, ct);

    /// <summary>5ch の板一覧をディスクから読む (互換 API)。</summary>
    public Task<IReadOnlyList<BoardCategory>> LoadFromDiskAsync(CancellationToken ct = default)
        => LoadFromDiskAsync(ChBrowser.Services.Bbs.BbsRegistry.FiveCh, ct);

    /// <summary>提供者の板一覧をサーバから取得し、生バイト列をディスクに保存した上でパース結果を返す。
    /// 板一覧を持たない提供者は空配列。</summary>
    public async Task<IReadOnlyList<BoardCategory>> FetchAndSaveAsync(ChBrowser.Services.Bbs.IBbsProvider provider, CancellationToken ct = default)
    {
        var url = provider.BoardListUrl;
        if (string.IsNullOrEmpty(url)) return Array.Empty<BoardCategory>();

        using var resp = await _client.Http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        // 取得そのままバイト列で保存 (形式変換しない)
        await File.WriteAllBytesAsync(CachePath(provider), bytes, ct).ConfigureAwait(false);
        return Parse(provider, bytes);
    }

    /// <summary>提供者の板一覧をローカル保存分からパースする。未取得なら空配列。</summary>
    public async Task<IReadOnlyList<BoardCategory>> LoadFromDiskAsync(ChBrowser.Services.Bbs.IBbsProvider provider, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(provider.BoardListUrl)) return Array.Empty<BoardCategory>();
        var path = CachePath(provider);
        if (!File.Exists(path))
        {
            if (provider.Id == "5ch")
            {
                var alt = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? _paths.BbsmenuJsonPath : _paths.BbsmenuHtmlPath;
                if (File.Exists(alt)) path = alt;
                else return Array.Empty<BoardCategory>();
            }
            else
            {
                return Array.Empty<BoardCategory>();
            }
        }
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return Parse(provider, bytes);
    }

    private string CachePath(ChBrowser.Services.Bbs.IBbsProvider provider)
        => _paths.BoardListCachePath(provider, provider.BoardListCacheExtension);

    private static IReadOnlyList<BoardCategory> Parse(ChBrowser.Services.Bbs.IBbsProvider provider, byte[] bytes)
        => provider.ParseBoardList(bytes);

    /// <summary>旧 bbsmenu.html 形式 (まちBBS、5ch bbsmenu.html): <c>&lt;B&gt;カテゴリ&lt;/B&gt;</c> の後に
    /// <c>&lt;A HREF=URL&gt;板名&lt;/A&gt;</c> が並ぶ。タグの大文字小文字・href の引用符の有無は問わない。
    /// URL が提供者のホストでない行 (外部リンク) は捨てる。</summary>
    internal static IReadOnlyList<BoardCategory> ParseBbsmenuHtml(ChBrowser.Services.Bbs.IBbsProvider provider, byte[] bytes)
    {
        var encoding = provider.TextEncoding;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = System.Text.Encoding.UTF8;
        }
        else
        {
            var headerLength = Math.Min(bytes.Length, 4096);
            var asciiHeader = System.Text.Encoding.ASCII.GetString(bytes, 0, headerLength);
            var match = MetaCharsetRe.Match(asciiHeader);
            if (match.Success)
            {
                var encName = match.Groups["enc"].Value.Trim();
                try
                {
                    if (encName.StartsWith("cp", StringComparison.OrdinalIgnoreCase) && int.TryParse(encName[2..], out var cp))
                    {
                        encoding = System.Text.Encoding.GetEncoding(cp);
                    }
                    else
                    {
                        encoding = System.Text.Encoding.GetEncoding(encName);
                    }
                }
                catch
                {
                    // 未知のエンコーディング名は provider.TextEncoding にフォールバック
                }
            }
        }

        var html = encoding.GetString(bytes);
        var categories = new List<BoardCategory>();
        var current    = new List<Board>();
        var currentName = provider.DisplayName;
        var catNo = 0;
        var order = 0;

        void Flush()
        {
            if (current.Count == 0) return;
            categories.Add(new BoardCategory(currentName.Length > 0 ? currentName : "(無名)", ++catNo, current.ToArray(), provider.Id));
            current = new List<Board>();
        }

        Uri? baseUri = null;
        if (!string.IsNullOrEmpty(provider.BoardListUrl) &&
            Uri.TryCreate(provider.BoardListUrl, UriKind.Absolute, out var bUri))
        {
            baseUri = bUri;
        }
        else
        {
            baseUri = new Uri($"https://{provider.DefaultHost}/");
        }

        foreach (Match m in BbsmenuHtmlTokenRe.Matches(html))
        {
            if (m.Groups["cat"].Success)
            {
                Flush();
                currentName = System.Net.WebUtility.HtmlDecode(m.Groups["cat"].Value).Trim();
                continue;
            }
            var href = m.Groups["href"].Value.Trim().Trim('"', '\'');
            var name = System.Net.WebUtility.HtmlDecode(m.Groups["name"].Value).Trim();

            Uri? uri = null;
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(href, UriKind.Absolute, out uri);
            }
            else if (baseUri != null)
            {
                Uri.TryCreate(baseUri, href, out uri);
            }

            if (uri == null) continue;
            if (!provider.OwnsHost(uri.Host)) continue;
            var host = provider.NormalizeHost(uri.Host);
            var dir  = uri.AbsolutePath.Trim('/');
            if (dir.Length == 0 || name.Length == 0) continue;
            if (dir.Contains('/') || dir.Contains('.') || string.Equals(dir, "bbs", StringComparison.OrdinalIgnoreCase)) continue;
            if (current.Exists(b => string.Equals(b.DirectoryName, dir, StringComparison.OrdinalIgnoreCase))) continue;

            current.Add(new Board(dir, name, provider.BoardUrl(host, dir), currentName, ++order));
        }
        Flush();
        return categories;
    }

    private static readonly Regex MetaCharsetRe = new(
        @"<meta\s+[^>]*?(?:charset\s*=\s*[""']?(?<enc>[a-zA-Z0-9_-]+)|content\s*=\s*[""'][^""']*?charset\s*=\s*(?<enc>[a-zA-Z0-9_-]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BbsmenuHtmlTokenRe = new(
        @"<b>(?<cat>[^<]+)</b>|<a\s+[^>]*?href\s*=\s*(?<href>""[^""]*""|'[^']*'|[^\s>]+)[^>]*>(?<name>[^<]*)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new FlexibleIntConverter() },
    };

    /// <summary>5ch の <c>bbsmenu.json</c>。<see cref="BoardCategory.ProviderId"/> は既定の "5ch"。</summary>
    internal static IReadOnlyList<BoardCategory> ParseBbsmenuJson(byte[] bytes)
    {
        var dto = JsonSerializer.Deserialize<BbsmenuJsonDto>(bytes, JsonOpts)
                  ?? throw new InvalidDataException("bbsmenu.json のパースに失敗しました。");

        var categories = new List<BoardCategory>(dto.MenuList?.Count ?? 0);

        if (dto.MenuList is null) return categories;

        foreach (var menu in dto.MenuList)
        {
            if (menu.CategoryContent is null) continue;

            var boards = new List<Board>(menu.CategoryContent.Count);
            foreach (var entry in menu.CategoryContent)
            {
                if (string.IsNullOrEmpty(entry.Url) ||
                    string.IsNullOrEmpty(entry.BoardName) ||
                    string.IsNullOrEmpty(entry.DirectoryName))
                {
                    continue; // 不完全エントリはスキップ
                }

                boards.Add(new Board(
                    DirectoryName: entry.DirectoryName!,
                    BoardName:     entry.BoardName!,
                    Url:           entry.Url!,
                    CategoryName:  entry.CategoryName ?? menu.CategoryName ?? "",
                    CategoryOrder: entry.CategoryOrder ?? 0));
            }

            categories.Add(new BoardCategory(
                CategoryName:   menu.CategoryName ?? "(無名)",
                CategoryNumber: menu.CategoryNumber ?? 0,
                Boards:         boards));
        }

        return categories;
    }
}
