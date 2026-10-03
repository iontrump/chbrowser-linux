using System;
using System.Collections.Generic;
using System.IO;
using ChBrowser.Models;
using ChBrowser.Services.Api;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Ng;
using ChBrowser.Services.Render;
using ChBrowser.Services.Shortcuts;
using ChBrowser.Services.Storage;
using Xunit;

namespace ChBrowser.Core.Tests;

public class CoreServicesTests
{
    [Fact]
    public void EmbeddedAssets_CanReadEmbeddedHtmlAndCss()
    {
        var html = EmbeddedAssets.Read("thread.html");
        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);

        var css = EmbeddedAssets.ReadCss("thread.css");
        Assert.False(string.IsNullOrWhiteSpace(css));
    }

    [Fact]
    public void BoardListHtmlBuilder_CanBuildShell()
    {
        var shell = BoardListHtmlBuilder.BuildShell();
        Assert.False(string.IsNullOrWhiteSpace(shell));
        Assert.Contains("board-list", shell, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortcutRegistry_ContainsDefaultActions()
    {
        var actions = ShortcutRegistry.Actions;
        Assert.NotEmpty(actions);
        Assert.Contains(actions, a => a.Id == "main.exit");
        Assert.Contains(actions, a => a.Id == "favorites.patrol");
    }

    [Fact]
    public void NgService_ExtractWatchoi_ExtractsPatternCorrectly()
    {
        var watchoi = NgService.ExtractWatchoi("名無しさん (ｽｯｯ資 Sd9f-abcd [123.456.78.90])");
        Assert.Equal("Sd9f-abcd", watchoi);

        var none = NgService.ExtractWatchoi("名無しさん");
        Assert.Equal("", none);
    }

    [Fact]
    public void NgService_ComputesHiddenPostsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var storage = new NgStorage(paths);
            var service = new NgService(storage);

            service.Save(new NgRuleSet
            {
                Version = 1,
                Rules = new List<NgRule>
                {
                    new NgRule
                    {
                        Target = "word",
                        MatchKind = "literal",
                        Pattern = "NGワード",
                        Enabled = true
                    }
                }
            });

            var posts = new List<Post>
            {
                new Post(1, "名無しさん", "", "2026/04/25 12:00:00", "id1", "こんにちは、通常の投稿です。", null),
                new Post(2, "名無しさん", "", "2026/04/25 12:01:00", "id2", "ここにはNGワードが含まれています。", null)
            };

            var hidden = service.ComputeHidden(posts, "example.com", "testboard");

            Assert.DoesNotContain(1L, hidden);
            Assert.Contains(2L, hidden);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EmbeddedAssets_ThreadHtml_ContainsNoReferrerMeta()
    {
        var html = EmbeddedAssets.Read("thread.html");
        Assert.Contains("<meta name=\"referrer\" content=\"no-referrer\">", html);
    }

    [Fact]
    public void UrlExpander_IsAsyncExpandable_IdentifiesSnsUrls()
    {
        Assert.True(ChBrowser.Services.Image.UrlExpander.IsAsyncExpandable("https://x.com/user/status/123456789"));
        Assert.True(ChBrowser.Services.Image.UrlExpander.IsAsyncExpandable("https://twitter.com/user/status/123456789"));
        Assert.True(ChBrowser.Services.Image.UrlExpander.IsAsyncExpandable("https://www.pixiv.net/artworks/123456"));
        Assert.False(ChBrowser.Services.Image.UrlExpander.IsAsyncExpandable("https://example.com/image.jpg"));
        Assert.False(ChBrowser.Services.Image.UrlExpander.IsAsyncExpandable("https://i.imgur.com/abc1234.png"));
    }

    [Fact]
    public void ImageMetaService_CanInstantiateAndDispose()
    {
        using var service = new ChBrowser.Services.Image.ImageMetaService();
        Assert.NotNull(service);
    }

    [Fact]
    public void AppConfig_ProviderVisibility_DefaultIsTrue()
    {
        var config = new AppConfig();
        Assert.True(config.ShowEddi);
        Assert.True(config.ShowFiveCh);
        Assert.True(config.ShowMachi);
        Assert.True(config.ShowOpen2ch);

        Assert.True(config.IsProviderVisible("eddi"));
        Assert.True(config.IsProviderVisible("5ch"));
        Assert.True(config.IsProviderVisible("machi"));
        Assert.True(config.IsProviderVisible("open2ch"));
        Assert.False(config.IsProviderVisible("shitaraba"));
        Assert.False(config.IsProviderVisible("shita"));
    }

    [Fact]
    public void AppConfig_ProviderVisibility_CanToggleAndFilter()
    {
        var config = new AppConfig
        {
            ShowEddi = true,
            ShowFiveCh = false,
            ShowMachi = true,
            ShowOpen2ch = false
        };

        Assert.True(config.IsProviderVisible("eddi"));
        Assert.False(config.IsProviderVisible("5ch"));
        Assert.True(config.IsProviderVisible("machi"));
        Assert.False(config.IsProviderVisible("open2ch"));
        Assert.False(config.IsProviderVisible("shitaraba"));
        Assert.False(config.IsProviderVisible("shita"));

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(restored);
        Assert.True(restored.ShowEddi);
        Assert.False(restored.ShowFiveCh);
        Assert.True(restored.ShowMachi);
        Assert.False(restored.ShowOpen2ch);
    }

    [Fact]
    public void AppConfig_FiveChBbsmenuUrl_DefaultAndSerialization()
    {
        var config = new AppConfig();
        Assert.Equal("https://menu.5ch.io/bbsmenu.html", config.FiveChBbsmenuUrl);

        var custom = config with { FiveChBbsmenuUrl = "https://custom.5ch.io/bbsmenu.json" };
        var json = System.Text.Json.JsonSerializer.Serialize(custom);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal("https://custom.5ch.io/bbsmenu.json", restored.FiveChBbsmenuUrl);
    }

    [Fact]
    public void FiveChProvider_BoardListUrl_RespectsCustomUrl()
    {
        var provider = new ChBrowser.Services.Bbs.FiveChProvider();
        try
        {
            ChBrowser.Services.Bbs.FiveChProvider.CustomBoardListUrl = null;
            Assert.Equal("https://menu.5ch.io/bbsmenu.html", provider.BoardListUrl);
            Assert.Equal("html", provider.BoardListCacheExtension);

            ChBrowser.Services.Bbs.FiveChProvider.CustomBoardListUrl = "https://menu.5ch.io/bbsmenu.json";
            Assert.Equal("https://menu.5ch.io/bbsmenu.json", provider.BoardListUrl);
            Assert.Equal("json", provider.BoardListCacheExtension);
        }
        finally
        {
            ChBrowser.Services.Bbs.FiveChProvider.CustomBoardListUrl = null;
        }
    }

    [Fact]
    public void FiveChProvider_ParseBoardList_ParsesHtml()
    {
        var provider = new ChBrowser.Services.Bbs.FiveChProvider();
        var html = @"
<HTML><HEAD>
<META http-equiv=""Content-Type"" content=""text/html; charset=Shift_JIS"">
<TITLE>BBS MENU</TITLE></HEAD><BODY>
<form method=""GET"" action=""//find.5ch.io/search"" accept-charset=""UTF-8""><input type=""text"" name=""q""></form>
<B>ニュース</B><BR>
<A HREF=""https://asahi.5ch.net/newsplus/"">ニュース速報+</A><BR>
<A HREF='https://hayabusa9.5ch.io/mnewsplus/'>芸スポ速報+</A><BR>
<B>雑談</B><BR>
<A HREF=https://mi.5ch.net/news4vip/>ニュー速VIP</A><BR>
</BODY></HTML>";

        var bytes = System.Text.Encoding.GetEncoding(932).GetBytes(html);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Equal(2, categories.Count);

        Assert.Equal("ニュース", categories[0].CategoryName);
        Assert.Equal(2, categories[0].Boards.Count);
        Assert.Equal("newsplus", categories[0].Boards[0].DirectoryName);
        Assert.Equal("ニュース速報+", categories[0].Boards[0].BoardName);
        Assert.Equal("mnewsplus", categories[0].Boards[1].DirectoryName);
        Assert.Equal("芸スポ速報+", categories[0].Boards[1].BoardName);

        Assert.Equal("雑談", categories[1].CategoryName);
        Assert.Single(categories[1].Boards);
        Assert.Equal("news4vip", categories[1].Boards[0].DirectoryName);
    }

    [Fact]
    public void FiveChProvider_ParseBoardList_ParsesUtf8Html()
    {
        var provider = new ChBrowser.Services.Bbs.FiveChProvider();
        var html = @"
<HTML><HEAD>
<META charset=""utf-8"">
<TITLE>BBS MENU UTF-8</TITLE></HEAD><BODY>
<B>案内</B><BR>
<A HREF=""https://agree.5ch.net/guide/"">総合案内</A><BR>
</BODY></HTML>";

        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Single(categories);
        Assert.Equal("案内", categories[0].CategoryName);
        Assert.Single(categories[0].Boards);
        Assert.Equal("guide", categories[0].Boards[0].DirectoryName);
        Assert.Equal("総合案内", categories[0].Boards[0].BoardName);
    }

    [Fact]
    public void FiveChProvider_ParseBoardList_ParsesJson()
    {
        var provider = new ChBrowser.Services.Bbs.FiveChProvider();
        var json = @"
{
  ""menu_list"": [
    {
      ""category_name"": ""案内"",
      ""category_number"": ""1"",
      ""category_content"": [
        {
          ""board_name"": ""2ch総合案内"",
          ""category_name"": ""案内"",
          ""category_order"": ""1"",
          ""directory_name"": ""guide"",
          ""url"": ""https://agree.5ch.net/guide/""
        }
      ]
    }
  ]
}";

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Single(categories);
        Assert.Equal("案内", categories[0].CategoryName);
        Assert.Single(categories[0].Boards);
        Assert.Equal("guide", categories[0].Boards[0].DirectoryName);
        Assert.Equal("2ch総合案内", categories[0].Boards[0].BoardName);
    }

    [Fact]
    public void AppConfig_MachiBbsmenuUrl_DefaultAndSerialization()
    {
        var config = new AppConfig();
        Assert.Equal("https://www.machi.to/", config.MachiBbsmenuUrl);

        var custom = config with { MachiBbsmenuUrl = "https://machi.to/bbsmenu.html" };
        var json = System.Text.Json.JsonSerializer.Serialize(custom);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal("https://machi.to/bbsmenu.html", restored.MachiBbsmenuUrl);
    }

    [Fact]
    public void MachiProvider_BoardListUrl_RespectsCustomUrl()
    {
        var provider = new ChBrowser.Services.Bbs.MachiProvider();
        try
        {
            ChBrowser.Services.Bbs.MachiProvider.CustomBoardListUrl = null;
            Assert.Equal("https://www.machi.to/", provider.BoardListUrl);

            ChBrowser.Services.Bbs.MachiProvider.CustomBoardListUrl = "https://machi.to/bbsmenu.html";
            Assert.Equal("https://machi.to/bbsmenu.html", provider.BoardListUrl);
        }
        finally
        {
            ChBrowser.Services.Bbs.MachiProvider.CustomBoardListUrl = null;
        }
    }

    [Fact]
    public void MachiProvider_ParseBoardList_ParsesTopPageHtml()
    {
        var provider = new ChBrowser.Services.Bbs.MachiProvider();
        var html = @"
<!DOCTYPE html>
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=cp932"">
<title>まちBBS</title>
</head>
<body>
<a href=""/"">トップ</a>
<a href=""/tokyo/"">東京</a>
<a href=""/kanto/"">関東</a>
<a href=""/bbs/read.cgi/tokyo/12345/"">スレリンク</a>
<a href=""https://external.example.com/"">外部リンク</a>
</body>
</html>";

        var bytes = System.Text.Encoding.GetEncoding(932).GetBytes(html);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Single(categories);
        Assert.Equal("まちBBS", categories[0].CategoryName);
        Assert.Equal(2, categories[0].Boards.Count);

        Assert.Equal("tokyo", categories[0].Boards[0].DirectoryName);
        Assert.Equal("東京", categories[0].Boards[0].BoardName);
        Assert.Equal("https://machi.to/tokyo/", categories[0].Boards[0].Url);

        Assert.Equal("kanto", categories[0].Boards[1].DirectoryName);
        Assert.Equal("関東", categories[0].Boards[1].BoardName);
        Assert.Equal("https://machi.to/kanto/", categories[0].Boards[1].Url);
    }

    [Fact]
    public void MachiProvider_ParseBoardList_ParsesBbsmenuHtml()
    {
        var provider = new ChBrowser.Services.Bbs.MachiProvider();
        var html = @"
<HTML><HEAD>
<META http-equiv=""Content-Type"" content=""text/html; charset=Shift_JIS"">
<TITLE>まちBBSメニュー</TITLE></HEAD><BODY>
<B>地域</B><BR>
<A HREF=""https://machi.to/tokyo/"">東京まちBBS</A><BR>
<A HREF=""https://machi.to/osaka/"">大阪まちBBS</A><BR>
</BODY></HTML>";

        var bytes = System.Text.Encoding.GetEncoding(932).GetBytes(html);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Single(categories);
        Assert.Equal("地域", categories[0].CategoryName);
        Assert.Equal(2, categories[0].Boards.Count);
        Assert.Equal("tokyo", categories[0].Boards[0].DirectoryName);
        Assert.Equal("東京まちBBS", categories[0].Boards[0].BoardName);
        Assert.Equal("osaka", categories[0].Boards[1].DirectoryName);
        Assert.Equal("大阪まちBBS", categories[0].Boards[1].BoardName);
    }

    [Fact]
    public void AppConfig_Open2chBbsmenuUrl_DefaultAndSerialization()
    {
        var config = new AppConfig();
        Assert.Equal("https://menu.open2ch.net/bbsmenu.html", config.Open2chBbsmenuUrl);

        var custom = config with { Open2chBbsmenuUrl = "https://custom.open2ch.net/bbsmenu.html" };
        var json = System.Text.Json.JsonSerializer.Serialize(custom);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal("https://custom.open2ch.net/bbsmenu.html", restored.Open2chBbsmenuUrl);
    }

    [Fact]
    public void Open2chProvider_BoardListUrl_RespectsCustomUrl()
    {
        var provider = new ChBrowser.Services.Bbs.Open2chProvider();
        try
        {
            ChBrowser.Services.Bbs.Open2chProvider.CustomBoardListUrl = null;
            Assert.Equal("https://menu.open2ch.net/bbsmenu.html", provider.BoardListUrl);

            ChBrowser.Services.Bbs.Open2chProvider.CustomBoardListUrl = "https://custom.open2ch.net/bbsmenu.html";
            Assert.Equal("https://custom.open2ch.net/bbsmenu.html", provider.BoardListUrl);
        }
        finally
        {
            ChBrowser.Services.Bbs.Open2chProvider.CustomBoardListUrl = null;
        }
    }

    [Fact]
    public void Open2chProvider_TryParseUrl_ParsesBoardAndThread()
    {
        var provider = new ChBrowser.Services.Bbs.Open2chProvider();

        // 板 URL
        var boardTarget = provider.TryParseUrl(new Uri("https://uni.open2ch.net/newsplus/"), "uni.open2ch.net");
        Assert.NotNull(boardTarget);
        Assert.Equal(ChBrowser.Services.Url.AddressBarTargetKind.Board, boardTarget.Kind);
        Assert.Equal("uni.open2ch.net", boardTarget.Host);
        Assert.Equal("newsplus", boardTarget.Directory);

        // スレッド URL
        var threadTarget = provider.TryParseUrl(new Uri("https://uni.open2ch.net/test/read.cgi/newsplus/1700000000/50"), "uni.open2ch.net");
        Assert.NotNull(threadTarget);
        Assert.Equal(ChBrowser.Services.Url.AddressBarTargetKind.Thread, threadTarget.Kind);
        Assert.Equal("newsplus", threadTarget.Directory);
        Assert.Equal("1700000000", threadTarget.ThreadKey);
        Assert.Equal(50, threadTarget.PostNumber);
    }

    [Fact]
    public void Open2chProvider_ParseBoardList_ParsesHtml()
    {
        var provider = new ChBrowser.Services.Bbs.Open2chProvider();
        var html = @"
<HTML><HEAD>
<META http-equiv=""Content-Type"" content=""text/html; charset=Shift_JIS"">
<TITLE>BBS MENU for open2ch</TITLE></HEAD><BODY>
<B>ニュース</B><BR>
<A HREF=http://uni.open2ch.net/newsplus/>ニュース速報+</A><BR>
<A HREF=http://awabi.open2ch.net/mnewsplus/>芸スポ速報+</A><BR>
<B>雑談</B><BR>
<A HREF=http://ikura.open2ch.net/news4vip/>ニュー速VIP</A><BR>
</BODY></HTML>";

        var bytes = System.Text.Encoding.GetEncoding(932).GetBytes(html);
        var categories = provider.ParseBoardList(bytes);

        Assert.NotNull(categories);
        Assert.Equal(2, categories.Count);

        Assert.Equal("ニュース", categories[0].CategoryName);
        Assert.Equal(2, categories[0].Boards.Count);
        Assert.Equal("newsplus", categories[0].Boards[0].DirectoryName);
        Assert.Equal("ニュース速報+", categories[0].Boards[0].BoardName);
        Assert.Equal("https://uni.open2ch.net/newsplus/", categories[0].Boards[0].Url);

        Assert.Equal("mnewsplus", categories[0].Boards[1].DirectoryName);
        Assert.Equal("芸スポ速報+", categories[0].Boards[1].BoardName);
        Assert.Equal("https://awabi.open2ch.net/mnewsplus/", categories[0].Boards[1].Url);

        Assert.Equal("雑談", categories[1].CategoryName);
        Assert.Single(categories[1].Boards);
        Assert.Equal("news4vip", categories[1].Boards[0].DirectoryName);
        Assert.Equal("ニュー速VIP", categories[1].Boards[0].BoardName);
        Assert.Equal("https://ikura.open2ch.net/news4vip/", categories[1].Boards[0].Url);
    }

    [Fact]
    public void Open2chProvider_BuildPostSubmission_ConstructsCorrectSubmission()
    {
        var provider = new ChBrowser.Services.Bbs.Open2chProvider();
        Assert.True(provider.PostForm.PersistsCookies);
        Assert.False(provider.PostForm.UsesDonguriAuth);
        Assert.Null(provider.BoardInfoUrl(new Board("newsplus", "ニュース速報+", "https://uni.open2ch.net/newsplus/", "ニュース", 1)));

        // 返信
        var board = new Board("newsplus", "ニュース速報+", "https://uni.open2ch.net/newsplus/", "ニュース", 1);
        var replyReq = new PostRequest(board, "1700000000", null, "名無し", "sage", "テスト返信");
        var replySub = provider.BuildPostSubmission(replyReq);

        Assert.Equal("https://uni.open2ch.net/test/bbs.cgi", replySub.Endpoint.ToString());
        Assert.Equal("https://uni.open2ch.net/test/read.cgi/newsplus/1700000000/", replySub.Referer);
        Assert.Equal("Shift_JIS", replySub.CharsetName);
        Assert.Equal(7, replySub.Fields.Count);
        Assert.Equal("FROM", replySub.Fields[0].Key);
        Assert.Equal("名無し", replySub.Fields[0].Value);
        Assert.Equal("mail", replySub.Fields[1].Key);
        Assert.Equal("sage", replySub.Fields[1].Value);
        Assert.Equal("MESSAGE", replySub.Fields[2].Key);
        Assert.Equal("テスト返信", replySub.Fields[2].Value);
        Assert.Equal("bbs", replySub.Fields[3].Key);
        Assert.Equal("newsplus", replySub.Fields[3].Value);
        Assert.Equal("time", replySub.Fields[4].Key);
        Assert.Equal("key", replySub.Fields[5].Key);
        Assert.Equal("1700000000", replySub.Fields[5].Value);
        Assert.Equal("submit", replySub.Fields[6].Key);
        Assert.Equal("書き込む", replySub.Fields[6].Value);

        // 新規スレッド
        var threadReq = new PostRequest(board, null, "テストスレ題名", "スレ主", "", "1行目本文");
        var threadSub = provider.BuildPostSubmission(threadReq);
        Assert.Equal("https://uni.open2ch.net/newsplus/", threadSub.Referer);
        Assert.Equal("FROM", threadSub.Fields[0].Key);
        Assert.Equal("スレ主", threadSub.Fields[0].Value);
        Assert.Equal("subject", threadSub.Fields[5].Key);
        Assert.Equal("テストスレ題名", threadSub.Fields[5].Value);
        Assert.Equal("submit", threadSub.Fields[6].Key);
        Assert.Equal("新規スレッド作成", threadSub.Fields[6].Value);
    }

    [Fact]
    public void Open2chProvider_ClassifyPostResponse_ClassifiesOutcomesCorrectly()
    {
        var provider = new ChBrowser.Services.Bbs.Open2chProvider();
        var endpoint = new Uri("https://uni.open2ch.net/test/bbs.cgi");

        // 成功文言
        var successHtml = "<html><body><b>書きこみました。</b></body></html>";
        var resSuccess = provider.ClassifyPostResponse(successHtml, PostResponseContext.None(endpoint));
        Assert.Equal(PostOutcome.Success, resSuccess.Outcome);

        // リダイレクト (302)
        var redirectCtx = new PostResponseContext(302, true, new Uri("https://uni.open2ch.net/test/read.cgi/newsplus/1700000000/"), endpoint);
        var resRedirect = provider.ClassifyPostResponse("<html>Redirecting...</html>", redirectCtx);
        Assert.Equal(PostOutcome.Success, resRedirect.Outcome);

        // クッキー確認画面 (bbs.cgi への form あり)
        var confirmHtml = @"
<html><body>
<h2>書き込み＆クッキー確認</h2>
<form method=POST action=""../test/bbs.cgi"">
<input type=hidden name=bbs value=""newsplus"">
<input type=hidden name=key value=""1700000000"">
<input type=hidden name=time value=""1700000001"">
<input type=hidden name=mona value=""abcdef"">
<input type=submit value=""上記を承諾して書き込む"">
</form></body></html>";
        var resConfirm = provider.ClassifyPostResponse(confirmHtml, PostResponseContext.None(endpoint));
        Assert.Equal(PostOutcome.NeedsConfirm, resConfirm.Outcome);

        // アク禁画面
        var banHtml = @"
<html><head><title>アク禁されますた(´・ω・｀)</title></head>
<body><h2>アク禁されますた(´・ω・｀)</h2>どうやらアクセス禁止になってしまったみたいです。。。！！</body></html>";
        var resBan = provider.ClassifyPostResponse(banHtml, PostResponseContext.None(endpoint));
        Assert.Equal(PostOutcome.BlockedByRule, resBan.Outcome);

        // 一般規制画面
        var errorHtml = "<html><head><title>ERROR</title></head><body>ERROR: 規制されています。書き込めません。</body></html>";
        var resError = provider.ClassifyPostResponse(errorHtml, PostResponseContext.None(endpoint));
        Assert.Equal(PostOutcome.BlockedByRule, resError.Outcome);
    }

    [Fact]
    public void AppConfig_GetEffectiveBoardOrder_ReturnsDefaultOrderWhenUnset()
    {
        var config = new AppConfig();
        var order = config.GetEffectiveBoardOrder();
        Assert.Equal(new[] { "eddi", "5ch", "machi", "open2ch" }, order);
    }

    [Fact]
    public void AppConfig_GetEffectiveBoardOrder_RespectsCustomOrder()
    {
        var config = new AppConfig
        {
            BoardOrder = new[] { "5ch", "open2ch", "eddi", "machi" }
        };
        var order = config.GetEffectiveBoardOrder();
        Assert.Equal(new[] { "5ch", "open2ch", "eddi", "machi" }, order);
    }

    [Fact]
    public void AppConfig_GetEffectiveBoardOrder_HandlesPartialAndDuplicatesAndUnknown()
    {
        var config = new AppConfig
        {
            BoardOrder = new[] { "machi", "unknown", "machi", "5ch" }
        };
        var order = config.GetEffectiveBoardOrder();
        Assert.Equal(new[] { "machi", "5ch", "eddi", "open2ch" }, order);
    }

    [Fact]
    public void AppConfig_WithProviderVisibility_UpdatesVisibilityCorrectly()
    {
        var config = new AppConfig();
        Assert.True(config.ShowFiveCh);
        Assert.True(config.ShowEddi);

        var updated = config.WithProviderVisibility("5ch", false);
        Assert.False(updated.ShowFiveCh);
        Assert.True(updated.ShowEddi);
        Assert.True(updated.ShowMachi);
        Assert.True(updated.ShowOpen2ch);

        var reenabled = updated.WithProviderVisibility("5ch", true);
        Assert.True(reenabled.ShowFiveCh);
    }

    [Fact]
    public void ConfigStorage_PersistsBoardOrderCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var storage = new ConfigStorage(paths);

            var initial = new AppConfig
            {
                BoardOrder = new[] { "open2ch", "machi", "5ch", "eddi" },
                ShowMachi = false
            };
            storage.Save(initial);

            var loaded = storage.Load();
            Assert.Equal(new[] { "open2ch", "machi", "5ch", "eddi" }, loaded.GetEffectiveBoardOrder());
            Assert.False(loaded.ShowMachi);
            Assert.True(loaded.ShowOpen2ch);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BoardListHtmlBuilder_BuildTree_PreservesProviderOrder()
    {
        var providers = new List<BoardListProviderNode>
        {
            new("open2ch", "おーぷん２ちゃんねる", IsExpanded: true),
            new("5ch", "5ちゃんねる", IsExpanded: false),
            new("eddi", "エッヂ", IsExpanded: true)
        };
        var categories = new List<IBoardCategoryTreeItem>
        {
            new BoardCategory("エッヂ", 0, Array.Empty<Board>(), ProviderId: "eddi"),
            new BoardCategory("ニュース", 0, Array.Empty<Board>(), ProviderId: "5ch"),
            new BoardCategory("雑談", 0, Array.Empty<Board>(), ProviderId: "open2ch")
        };

        var tree = BoardListHtmlBuilder.BuildTree(providers, categories);
        Assert.Equal(3, tree.Count);
        Assert.Equal("open2ch", tree[0].Id);
        Assert.Equal("5ch", tree[1].Id);
        Assert.Equal("eddi", tree[2].Id);
    }

    [Fact]
    public void FavoritesStorage_Load_ReturnsDefaultTopButtonsWhenFileMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-fav-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var storage = new FavoritesStorage(paths);

            var loaded = storage.Load();
            Assert.NotNull(loaded);
            Assert.Single(loaded.Root);
            Assert.IsType<FavoriteFolder>(loaded.Root[0]);
            Assert.Equal(FavoriteDefaults.TopButtonsFolderName, ((FavoriteFolder)loaded.Root[0]).Name);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FavoritesStorage_SaveAndLoad_PersistsBoardsAndFolders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-fav-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var storage = new FavoritesStorage(paths);

            var board1 = new FavoriteBoard
            {
                Id = Guid.NewGuid(),
                Host = "liveedge.ninja",
                DirectoryName = "liveedge",
                BoardName = "エッヂ",
                AddedAt = DateTimeOffset.UtcNow
            };
            var board2 = new FavoriteBoard
            {
                Id = Guid.NewGuid(),
                Host = "ikura.open2ch.net",
                DirectoryName = "qa",
                BoardName = "質問",
                AddedAt = DateTimeOffset.UtcNow
            };
            var folder = new FavoriteFolder
            {
                Id = Guid.NewGuid(),
                Name = "よく見る板",
                Children = new FavoriteEntry[] { board1, board2 }
            };

            var data = new FavoritesData
            {
                Root = new FavoriteEntry[] { folder }
            };

            storage.Save(data);

            var loaded = storage.Load();
            Assert.NotNull(loaded);
            Assert.Single(loaded.Root);
            var loadedFolder = Assert.IsType<FavoriteFolder>(loaded.Root[0]);
            Assert.Equal("よく見る板", loadedFolder.Name);
            Assert.Equal(2, loadedFolder.Children.Count);

            var b1 = Assert.IsType<FavoriteBoard>(loadedFolder.Children[0]);
            Assert.Equal("liveedge.ninja", b1.Host);
            Assert.Equal("liveedge", b1.DirectoryName);
            Assert.Equal("エッヂ", b1.BoardName);

            var b2 = Assert.IsType<FavoriteBoard>(loadedFolder.Children[1]);
            Assert.Equal("ikura.open2ch.net", b2.Host);
            Assert.Equal("qa", b2.DirectoryName);
            Assert.Equal("質問", b2.BoardName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FavoritesManager_CreateInitialFavorites_CreatesFoldersByBbsName()
    {
        var initial = FavoritesManager.CreateInitialFavorites();
        Assert.NotNull(initial);
        Assert.Equal(2, initial.Root.Count);

        var eddiFolder = Assert.IsType<FavoriteFolder>(initial.Root[0]);
        Assert.Equal("エッヂ", eddiFolder.Name);
        Assert.Single(eddiFolder.Children);
        var eddiBoard = Assert.IsType<FavoriteBoard>(eddiFolder.Children[0]);
        Assert.Equal("liveedge", eddiBoard.DirectoryName);

        var fiveChFolder = Assert.IsType<FavoriteFolder>(initial.Root[1]);
        Assert.Equal("5ちゃんねる", fiveChFolder.Name);
        Assert.Single(fiveChFolder.Children);
        var fiveChBoard = Assert.IsType<FavoriteBoard>(fiveChFolder.Children[0]);
        Assert.Equal("newsplus", fiveChBoard.DirectoryName);
    }

    [Fact]
    public void FavoritesManager_MigrateLegacyFavorites_MigratesYokuMiruItaToBbsFolders()
    {
        var board1 = new FavoriteBoard
        {
            Id = Guid.NewGuid(),
            Host = EddiProvider.CanonicalHost,
            DirectoryName = "liveedge",
            BoardName = "エッヂ"
        };
        var board2 = new FavoriteBoard
        {
            Id = Guid.NewGuid(),
            Host = "asahi.5ch.net",
            DirectoryName = "newsplus",
            BoardName = "ニュース速報+"
        };
        var board3 = new FavoriteBoard
        {
            Id = Guid.NewGuid(),
            Host = "ikura.open2ch.net",
            DirectoryName = "qa",
            BoardName = "質問"
        };
        var legacyFolder = new FavoriteFolder
        {
            Id = Guid.NewGuid(),
            Name = "よく見る板",
            Children = new FavoriteEntry[] { board1, board2, board3 }
        };

        var initial = new FavoritesData
        {
            Root = new FavoriteEntry[] { legacyFolder }
        };

        var migratedSuccess = FavoritesManager.MigrateLegacyFavorites(initial, out var migrated);
        Assert.True(migratedSuccess);
        Assert.DoesNotContain(migrated.Root, e => e is FavoriteFolder f && f.Name == "よく見る板");

        var eddiFolder = Assert.IsType<FavoriteFolder>(migrated.Root.FirstOrDefault(e => (e as FavoriteFolder)?.Name == "エッヂ"));
        Assert.Single(eddiFolder.Children);

        var fiveFolder = Assert.IsType<FavoriteFolder>(migrated.Root.FirstOrDefault(e => (e as FavoriteFolder)?.Name == "5ちゃんねる"));
        Assert.Single(fiveFolder.Children);

        var openFolder = Assert.IsType<FavoriteFolder>(migrated.Root.FirstOrDefault(e => (e as FavoriteFolder)?.Name == "おーぷん２ちゃんねる"));
        Assert.Single(openFolder.Children);
    }

    [Fact]
    public void FavoritesManager_AddBoard_CreatesFolderWithBbsNameAndAddsBoard()
    {
        var data = new FavoritesData
        {
            Root = Array.Empty<FavoriteEntry>()
        };

        // 1. おーぷん２ちゃんねるの板を追加
        var added1 = FavoritesManager.AddBoard(data, "ikura.open2ch.net", "qa", "質問", out data);
        Assert.True(added1);
        Assert.Single(data.Root);
        var openFolder = Assert.IsType<FavoriteFolder>(data.Root[0]);
        Assert.Equal("おーぷん２ちゃんねる", openFolder.Name);
        Assert.Single(openFolder.Children);

        // 2. おーぷん２ちゃんねるの別の板を追加 (既存フォルダに追加されること)
        var added2 = FavoritesManager.AddBoard(data, "hayabusa.open2ch.net", "livejupiter", "なんJ", out data);
        Assert.True(added2);
        Assert.Single(data.Root);
        openFolder = Assert.IsType<FavoriteFolder>(data.Root[0]);
        Assert.Equal(2, openFolder.Children.Count);

        // 3. まちBBSの板を追加 (新フォルダ「まちBBS」が作成されること)
        var added3 = FavoritesManager.AddBoard(data, "kanto.machi.to", "kana", "神奈川", out data);
        Assert.True(added3);
        Assert.Equal(2, data.Root.Count);
        var machiFolder = Assert.IsType<FavoriteFolder>(data.Root[1]);
        Assert.Equal("まちBBS", machiFolder.Name);
        Assert.Single(machiFolder.Children);

        // 4. 重複追加は拒絶されること
        var duplicate = FavoritesManager.AddBoard(data, "ikura.open2ch.net", "qa", "質問", out _);
        Assert.False(duplicate);
    }

    [Fact]
    public void FavoritesManager_CollectFavoriteThreadKeys_CollectsKeysRecursively()
    {
        var thread1 = new FavoriteThread
        {
            Id = Guid.NewGuid(),
            Host = "bbs.eddibb.cc",
            DirectoryName = "liveedge",
            ThreadKey = "1111",
            Title = "スレ1"
        };
        var thread2 = new FavoriteThread
        {
            Id = Guid.NewGuid(),
            Host = "ikura.open2ch.net",
            DirectoryName = "qa",
            ThreadKey = "2222",
            Title = "スレ2"
        };
        var folder = new FavoriteFolder
        {
            Id = Guid.NewGuid(),
            Name = "お気に入りフォルダ",
            Children = new FavoriteEntry[] { thread1, thread2 }
        };

        var keys = FavoritesManager.CollectFavoriteThreadKeys(new[] { folder });
        Assert.Equal(2, keys.Count);
        Assert.Contains(("bbs.eddibb.cc", "liveedge", "1111"), keys);
        Assert.Contains(("ikura.open2ch.net", "qa", "2222"), keys);
    }

    [Fact]
    public void AllLogsService_BuildAllLogsItems_CollectsDatFilesAndHandlesExcludeFavorites()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-alllogs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var client = new MonazillaClient();
            var subjectClient = new SubjectTxtClient(client, paths);
            var settingClient = new SettingTxtClient(client, paths);
            var threadIndex = new ThreadIndexService(paths);
            var favStorage = new FavoritesStorage(paths);

            // テスト用のログファイル作成
            var sjis = System.Text.Encoding.GetEncoding(932);
            var eddiDir = Path.Combine(tempDir, "eddibb.cc", "liveedge");
            Directory.CreateDirectory(eddiDir);
            var eddiDat = Path.Combine(eddiDir, "1001.dat");
            File.WriteAllText(eddiDat, "名前<>mail<>2026/04/25 12:00:00<>本文<>エッヂスレタイトル\n", sjis);

            var openDir = Path.Combine(tempDir, "open2ch.net", "qa");
            Directory.CreateDirectory(openDir);
            var openDat = Path.Combine(openDir, "2002.dat");
            File.WriteAllText(openDat, "名前<>mail<>2026/04/25 12:00:00<>本文<>質問スレタイトル\n", sjis);

            // お気に入りデータ作成 (open2ch のスレをお気に入りに登録)
            var favThread = new FavoriteThread
            {
                Id = Guid.NewGuid(),
                Host = "open2ch.net",
                DirectoryName = "qa",
                ThreadKey = "2002",
                Title = "質問スレタイトル"
            };
            favStorage.Save(new FavoritesData
            {
                Root = new FavoriteEntry[] { favThread }
            });

            var service = new AllLogsService(paths, subjectClient, settingClient, threadIndex, favStorage);

            // 1. 全ログ (excludeFavorites: false)
            var allItems = service.BuildAllLogsItems(excludeFavorites: false);
            Assert.Equal(2, allItems.Count);
            Assert.Contains(allItems, i => i.Info.Key == "1001" && i.Info.Title == "エッヂスレタイトル" && !i.IsFavorited);
            Assert.Contains(allItems, i => i.Info.Key == "2002" && i.Info.Title == "質問スレタイトル" && i.IsFavorited);

            // 2. お気に入り以外の全ログ (excludeFavorites: true)
            var nonFavItems = service.BuildAllLogsItems(excludeFavorites: true);
            Assert.Single(nonFavItems);
            Assert.Equal("1001", nonFavItems[0].Info.Key);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AllLogsService_BuildUnlistedBoardsItems_DiscoversUnlistedBoards()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-unlisted-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var client = new MonazillaClient();
            var subjectClient = new SubjectTxtClient(client, paths);
            var settingClient = new SettingTxtClient(client, paths);
            var threadIndex = new ThreadIndexService(paths);
            var favStorage = new FavoritesStorage(paths);

            // 板一覧外の板 (5ch.io) のログデータ作成
            var unlistedBoardDir = Path.Combine(tempDir, "5ch.io", "secretboard");
            Directory.CreateDirectory(unlistedBoardDir);
            File.WriteAllText(Path.Combine(unlistedBoardDir, "9999.dat"), "名前<>mail<>2026/04/25 12:00:00<>本文<>未掲載スレ<>\n");

            var service = new AllLogsService(paths, subjectClient, settingClient, threadIndex, favStorage);
            var unlisted = service.BuildUnlistedBoardsItems(resolveListed: (r, d) => null);

            Assert.Single(unlisted);
            Assert.Equal(ThreadListItemKind.Board, unlisted[0].Kind);
            Assert.Equal("secretboard", unlisted[0].DirectoryName);
            Assert.Equal(1, unlisted[0].Info.PostCount); // 1 dat file
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ThreadHtmlExporter_GetSafeFileName_SanitizesCorrectly()
    {
        var safe = ThreadHtmlExporter.GetSafeFileName("【Linux/GTK4】テストスレッド: *?<>|");
        Assert.DoesNotContain("/", safe);
        Assert.DoesNotContain("\\", safe);
        Assert.DoesNotContain(":", safe);
        Assert.DoesNotContain("*", safe);
        Assert.DoesNotContain("?", safe);
        Assert.DoesNotContain("<", safe);
        Assert.DoesNotContain(">", safe);
        Assert.DoesNotContain("|", safe);

        var emptySafe = ThreadHtmlExporter.GetSafeFileName("   ");
        Assert.Equal("thread", emptySafe);
    }

    [Fact]
    public void ThreadHtmlExporter_FormatPostBody_FormatsUrlsAnchorsAndQuotes()
    {
        var rawBody = ">>1 乙です\n> 引用文です\n詳細は https://example.com/test を参照\n<script>alert(1)</script>";
        var formatted = ThreadHtmlExporter.FormatPostBody(rawBody);

        // 改行
        Assert.Contains("<br>", formatted);
        // アンカー
        Assert.Contains("<a href=\"#p1\" class=\"post-anchor\">&gt;&gt;1</a>", formatted);
        // 引用
        Assert.Contains("<span class=\"quote-line\">&gt; 引用文です</span>", formatted);
        // URL
        Assert.Contains("<a href=\"https://example.com/test\" target=\"_blank\"", formatted);
        // XSS エスケープ
        Assert.DoesNotContain("<script>", formatted);
        Assert.Contains("&lt;script&gt;", formatted);
    }

    [Fact]
    public void ThreadHtmlExporter_Export_GeneratesCompleteStandaloneHtml()
    {
        var posts = new[]
        {
            new Post(
                Number: 1,
                Name: "名無しさん 🐧",
                Mail: "sage",
                DateText: "2026/10/03(土) 12:00:00.00",
                Id: "test001",
                Body: "スレッド本文テストです。\n>>2 よろしく",
                ThreadTitle: "エクスポートテストスレッド"
            ),
            new Post(
                Number: 2,
                Name: "名無しさん",
                Mail: "",
                DateText: "2026/10/03(土) 12:01:00.00",
                Id: "test002",
                Body: ">>1\nレステスト",
                ThreadTitle: null
            )
        };

        var html = ThreadHtmlExporter.Export(
            title: "エクスポートテストスレッド",
            boardName: "エッヂ",
            threadUrl: "https://bbs.eddibb.cc/liveedge/1234567890",
            posts: posts
        );

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>エクスポートテストスレッド</title>", html);
        Assert.Contains("エッヂ", html);
        Assert.Contains("レス数: 2", html);
        Assert.Contains("https://bbs.eddibb.cc/liveedge/1234567890", html);
        Assert.Contains("<article class=\"post\" id=\"p1\">", html);
        Assert.Contains("<article class=\"post\" id=\"p2\">", html);
        Assert.Contains("名無しさん 🐧", html);
        Assert.Contains("[sage]", html);
        Assert.Contains("ID:test001", html);
        Assert.Contains("<style>", html);
        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.Contains("ChBrowser Linux", html);
    }

    [Fact]
    public void FavoritesManager_AddThread_AddsThreadToCategoryFolder()
    {
        var initial = FavoritesManager.CreateInitialFavorites();
        var ok = FavoritesManager.AddThread(initial, "bbs.eddibb.cc", "liveedge", "1234567890", "テストスレ", out var updated);
        Assert.True(ok);

        var hasThread = FavoritesManager.ContainsThread(updated.Root, "bbs.eddibb.cc", "liveedge", "1234567890");
        Assert.True(hasThread);

        // 重複追加不可
        var duplicate = FavoritesManager.AddThread(updated, "bbs.eddibb.cc", "liveedge", "1234567890", "テストスレ", out _);
        Assert.False(duplicate);
    }
}

