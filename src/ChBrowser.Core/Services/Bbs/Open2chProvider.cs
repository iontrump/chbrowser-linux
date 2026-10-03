using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ChBrowser.Models;
using ChBrowser.Services.Url;

namespace ChBrowser.Services.Bbs;

/// <summary>おーぷん２ちゃんねる (open2ch.net) の提供者。
/// 2ch / 5ch 互換の Monazilla 形式 (subject.txt, dat, bbs.cgi, bbsmenu.html)。
/// エンコーディングは Shift_JIS (CP932)。
/// </summary>
public sealed class Open2chProvider : IBbsProvider
{
    static Open2chProvider()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public const string CanonicalHost = "open2ch.net";

    private static readonly Regex BoardPathRegex = new(
        @"^/(?<dir>[A-Za-z0-9_]+)/?$",
        RegexOptions.Compiled);

    private static readonly Regex ThreadPathRegex = new(
        @"^/test/read\.cgi/(?<dir>[A-Za-z0-9_]+)/(?<key>[0-9]+)(?:/(?<post>[0-9]+))?.*$",
        RegexOptions.Compiled);

    public string Id          => "open2ch";
    public string DisplayName => "おーぷん２ちゃんねる";

    public BbsCapabilities Capabilities =>
        BbsCapabilities.BoardList | BbsCapabilities.ThreadList | BbsCapabilities.ThreadFetch |
        BbsCapabilities.DeltaByRange | BbsCapabilities.Posting | BbsCapabilities.ThreadCreation;

    public Encoding TextEncoding => Encoding.GetEncoding(932);

    public IReadOnlyList<string> StorageRoots { get; } = new[] { "open2ch.net" };

    public string DefaultHost => CanonicalHost;

    public int PostNumberDigits => 4;

    public IReadOnlyList<string> HostSuffixes { get; } = new[] { "open2ch.net" };

    public string ThreadLinkJsPattern
        => @"^https?:\/\/(?<host>(?:[A-Za-z0-9-]+\.)*open2ch\.net)\/test\/read\.cgi\/(?<dir>[A-Za-z0-9_]+)\/(?<key>\d+)(?:\/(?<post>[^/?#]*))?";

    public IReadOnlyList<AnchorRule> DefaultAnchorRules { get; } = new[]
    {
        new AnchorRule(">>N", @">>\s*(?<spec>\d+(?:\s*-\s*\d+)?(?:\s*[,，、]\s*\d+(?:\s*-\s*\d+)?)*)"),
        new AnchorRule("＞＞N (全角)", @"＞＞\s*(?<spec>\d+(?:\s*-\s*\d+)?(?:\s*[,，、]\s*\d+(?:\s*-\s*\d+)?)*)", Enabled: false),
    };

    public bool OwnsHost(string host)
        => string.Equals(host, CanonicalHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + CanonicalHost, StringComparison.OrdinalIgnoreCase);

    public string NormalizeHost(string host) => host;

    public AddressBarTarget? TryParseUrl(Uri uri, string host)
    {
        var path = uri.AbsolutePath;

        var threadMatch = ThreadPathRegex.Match(path);
        if (threadMatch.Success)
        {
            var postGroup = threadMatch.Groups["post"];
            var postNo    = postGroup.Success && long.TryParse(postGroup.Value, out var n) ? n : 0;
            return new AddressBarTarget(
                AddressBarTargetKind.Thread,
                host,
                threadMatch.Groups["dir"].Value,
                threadMatch.Groups["key"].Value,
                postNo);
        }

        var boardMatch = BoardPathRegex.Match(path);
        if (boardMatch.Success)
        {
            var dir = boardMatch.Groups["dir"].Value;
            if (string.Equals(dir, "test", StringComparison.OrdinalIgnoreCase)) return null;
            return new AddressBarTarget(
                AddressBarTargetKind.Board,
                host,
                dir,
                "");
        }

        return null;
    }

    public string BoardUrl(string host, string directoryName)
        => $"https://{host}/{directoryName}/";

    public string ThreadUrl(string host, string directoryName, string threadKey, long postNumber = 0)
        => postNumber > 0
            ? $"https://{host}/test/read.cgi/{directoryName}/{threadKey}/{postNumber}"
            : $"https://{host}/test/read.cgi/{directoryName}/{threadKey}/";

    public string ThreadListUrl(Board board)                => board.Url.TrimEnd('/') + "/subject.txt";
    public string ThreadFetchUrl(Board board, string key)   => $"{board.Url.TrimEnd('/')}/dat/{key}.dat";
    public string ThreadPageUrl(Board board, string key)    => ThreadUrl(board.Host, board.DirectoryName, key);
    /// <summary>open2ch には SETTING.TXT が無く、アクセスするとアク禁応答が返るため null。</summary>
    public string? BoardInfoUrl(Board board)                => null;

    public string? PostEndpointUrl(Board board)
        => new Uri(new Uri(board.Url), "/test/bbs.cgi").ToString();

    /// <summary>おーぷん２ちゃんねる板一覧取得 URL のカスタム設定。</summary>
    public static string? CustomBoardListUrl { get; set; }

    public string? BoardListUrl => CustomBoardListUrl ?? "https://menu.open2ch.net/bbsmenu.html";

    public string BoardListCacheExtension => "html";

    public IReadOnlyList<BoardCategory> ParseBoardList(byte[] bytes)
        => Api.BbsmenuClient.ParseBbsmenuHtml(this, bytes);

    public bool UsesNativeDat => true;

    /// <summary>open2ch の差分は HTTP Range (バイト) で、番号以降取得は無い。</summary>
    public string ThreadFetchRangeUrl(Board board, string threadKey, long fromNumber)
        => throw new NotSupportedException("open2ch は番号以降の差分取得 (DeltaByNumber) を持たない。Range で差分を取る。");

    public IReadOnlyList<ThreadInfo> ParseThreadList(byte[] bytes)
        => FiveChProvider.ParseSubjectTxt(bytes, TextEncoding);

    /// <summary>生 dat は行番号 = レス番号なので 1 行単独では番号が決まらない。<see cref="Api.DatParser.ParseLine"/> を使う。</summary>
    public Post? ParseThreadLine(string line) => null;

    /// <summary>名前 / メール / スレ立てあり。どんぐりは使わず、open2ch サーバが発行する Cookie を
    /// 提供者ごとの Cookie 保管に保持・送信する (<see cref="PostFormSpec.PersistsCookies"/>)。</summary>
    public PostFormSpec PostForm { get; } = new(
        SupportsName: true,
        SupportsMail: true,
        SupportsNewThread: true,
        UsesDonguriAuth: false,
        PersistsCookies: true);

    /// <summary>bbs.cgi へのフォーム: <c>FROM, mail, MESSAGE, bbs, time, [key], [subject], submit</c> (5ch 伝統順)。
    /// Shift_JIS。Referer は板 URL (末尾 '/')。</summary>
    public PostSubmission BuildPostSubmission(PostRequest req)
    {
        var endpoint = PostEndpointUrl(req.Board)!;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("FROM",    req.Name),
            new("mail",    req.Mail),
            new("MESSAGE", req.Message),
            new("bbs",     req.Board.DirectoryName),
            new("time",    PostResponseHtml.UnixNow()),
        };
        if (req.IsReply)
        {
            fields.Add(new("key", req.ThreadKey!));
            fields.Add(new("submit", "書き込む"));
        }
        else if (req.IsNewThread)
        {
            fields.Add(new("subject", req.Subject!));
            fields.Add(new("submit", "新規スレッド作成"));
        }

        // 2ch / open2ch bbs.cgi の標準仕様:
        // スレ返信 (key あり) はスレッド URL を Referer として要求する
        // スレ立て (subject あり) は板 URL を Referer として要求する
        var referer = req.IsReply
            ? ThreadUrl(req.Board.Host, req.Board.DirectoryName, req.ThreadKey!)
            : (req.Board.Url.EndsWith('/') ? req.Board.Url : req.Board.Url + "/");

        return new PostSubmission(
            new Uri(endpoint),
            fields,
            TextEncoding,
            "Shift_JIS",
            referer);
    }

    /// <summary>open2ch の書き込みレスポンスを判定する。
    /// 1) リダイレクト (302) → 成功
    /// 2) 成功文言 ("書きこみました" 等) → 成功
    /// 3) 2段目 POST を要求するフォーム (&lt;form action="...bbs.cgi..."&gt;) または確認トークン ("クッキー確認" 等) → NeedsConfirm
    /// 4) 規制・アク禁・エラー文言 → BlockedByRule
    /// 5) その他 → UnknownError
    /// </summary>
    public PostResult ClassifyPostResponse(string html, PostResponseContext context)
    {
        var bodyText = PostResponseHtml.ExtractBodyText(html);
        var snippet  = PostResponseHtml.Snippet(bodyText);

        if (context is { Redirected: true })
            return new PostResult(PostOutcome.Success, "", snippet);

        if (PostResponseHtml.Contains(html, PostResponseHtml.SuccessTokens))
            return new PostResult(PostOutcome.Success, "", snippet);

        // 2段目 POST フォーム、または確認トークンを含む場合は NeedsConfirm
        var hasBbsCgiForm = Regex.IsMatch(html,
            @"<form\b[^>]*?action\s*=\s*(?:""[^""]*\.cgi[^""]*""|'[^']*\.cgi[^']*'|[^\s>]*\.cgi[^\s>]*)",
            RegexOptions.IgnoreCase);
        if (hasBbsCgiForm || PostResponseHtml.Contains(html, PostResponseHtml.ConfirmTokens))
            return new PostResult(PostOutcome.NeedsConfirm, "", snippet);

        // 規制・アク禁・エラー判定
        var errorZone = bodyText.Length > 400 ? bodyText[..400] : bodyText;
        if (PostResponseHtml.LooksLikeError(errorZone) || PostResponseHtml.LooksLikeError(html))
            return new PostResult(PostOutcome.BlockedByRule, PostResponseHtml.ShortenForUser(bodyText), snippet);

        return new PostResult(PostOutcome.UnknownError, PostResponseHtml.ShortenForUser(bodyText), snippet);
    }
}
