using System;
using System.Text;
using ChBrowser.Models;
using ChBrowser.Services.Bbs;
using Xunit;

namespace ChBrowser.Core.Tests;

public class EddiProviderTests
{
    public EddiProviderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void ParseBoardList_ParsesValidJsonCorrectly()
    {
        var provider = new EddiProvider();
        var json = """
        [
            {"board_key":"liveedge","name":"エッヂ","default_name":"エッヂの名無し"},
            {"board_key":"experiment","name":"試験板","default_name":"ポッドの名無し"}
        ]
        """;
        var bytes = Encoding.UTF8.GetBytes(json);

        var categories = provider.ParseBoardList(bytes);

        Assert.Single(categories);
        var cat = categories[0];
        Assert.Equal("エッヂ掲示板", cat.CategoryName);
        Assert.Equal(2, cat.Boards.Count);

        Assert.Equal("liveedge", cat.Boards[0].DirectoryName);
        Assert.Equal("エッヂ", cat.Boards[0].BoardName);

        Assert.Equal("experiment", cat.Boards[1].DirectoryName);
        Assert.Equal("試験板", cat.Boards[1].BoardName);
    }

    [Fact]
    public void Endpoints_ReflectEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL", "http://127.0.0.1:8787");

            var provider = new EddiProvider();
            var board = new Board("liveedge", "エッヂ", "http://127.0.0.1:8787/liveedge/", "エッヂ掲示板", 1);

            Assert.Equal("http://127.0.0.1:8787/api/boards", provider.BoardListUrl);
            Assert.Equal("http://127.0.0.1:8787/liveedge/subject.txt", provider.ThreadListUrl(board));
            Assert.Equal("http://127.0.0.1:8787/liveedge/dat/1234567890.dat", provider.ThreadFetchUrl(board, "1234567890"));
            Assert.Equal("http://127.0.0.1:8787/liveedge/SETTING.TXT", provider.BoardInfoUrl(board));
            Assert.Equal("http://127.0.0.1:8787/test/bbs.cgi", provider.PostEndpointUrl(board));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL", original);
        }
    }

    [Fact]
    public void Endpoints_ReflectCustomBaseUrl_AndTakesPrecedenceOverEnvironmentVariable()
    {
        var originalEnv = Environment.GetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL");
        var originalCustom = EddiProvider.CustomBaseUrl;
        try
        {
            Environment.SetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL", "http://127.0.0.1:8787");
            EddiProvider.CustomBaseUrl = "https://custom-eddi.example.com:9000/subpath";

            var provider = new EddiProvider();
            var board = new Board("liveedge", "エッヂ", "https://custom-eddi.example.com:9000/liveedge/", "エッヂ掲示板", 1);

            Assert.Equal("https://custom-eddi.example.com:9000/api/boards", provider.BoardListUrl);
            Assert.Equal("https://custom-eddi.example.com:9000/liveedge/subject.txt", provider.ThreadListUrl(board));
            Assert.Equal("https://custom-eddi.example.com:9000/liveedge/dat/1234567890.dat", provider.ThreadFetchUrl(board, "1234567890"));
            Assert.Equal("https://custom-eddi.example.com:9000/liveedge/SETTING.TXT", provider.BoardInfoUrl(board));
            Assert.Equal("https://custom-eddi.example.com:9000/test/bbs.cgi", provider.PostEndpointUrl(board));
            Assert.True(provider.OwnsHost("custom-eddi.example.com:9000"));
            Assert.True(provider.OwnsHost("custom-eddi.example.com"));
            Assert.True(provider.OwnsHost("bbs.eddibb.cc"));

            // リセットすると環境変数へフォールバック
            EddiProvider.CustomBaseUrl = "";
            Assert.Equal("http://127.0.0.1:8787/api/boards", provider.BoardListUrl);

            // 環境変数も消すと既定値へフォールバック
            Environment.SetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL", null);
            Assert.Equal("https://bbs.eddibb.cc/api/boards", provider.BoardListUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL", originalEnv);
            EddiProvider.CustomBaseUrl = originalCustom;
        }
    }
}
