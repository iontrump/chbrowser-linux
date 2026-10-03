using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Api;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Donguri;
using ChBrowser.Services.Storage;
using Xunit;

namespace ChBrowser.Core.Tests;

public class PostClientTests
{
    [Fact]
    public void IsLocalHost_IdentifiesLocalAndExternalHostsCorrectly()
    {
        Assert.True(PostClient.IsLocalHost("127.0.0.1"));
        Assert.True(PostClient.IsLocalHost("localhost"));
        Assert.True(PostClient.IsLocalHost("::1"));

        Assert.False(PostClient.IsLocalHost("bbs.eddibb.cc"));
        Assert.False(PostClient.IsLocalHost("5ch.net"));
        Assert.False(PostClient.IsLocalHost("example.com"));
        Assert.False(PostClient.IsLocalHost(""));
    }

    [Fact]
    public void SafePostMode_DefaultIsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-safemode-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var http = new MonazillaClient();
            var jar = new CookieJar(paths.DonguriCookiesPath);
            var donguri = new DonguriService(jar, paths);
            var client = new PostClient(http, donguri);

            Assert.False(client.SafePostMode);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostAsync_WhenSafeModeEnabled_BlocksExternalHosts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-post-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var http = new MonazillaClient();
            var jar = new CookieJar(paths.DonguriCookiesPath);
            var donguri = new DonguriService(jar, paths);
            var client = new PostClient(http, donguri)
            {
                SafePostMode = true // 外部書き込み禁止
            };

            // 外部本番ホスト (5ch) の掲示板
            var externalBoard = new Board("newsplus", "ニュース速報+", "https://asahi.5ch.io/newsplus/", "ニュース", 1);
            var req = new PostRequest(externalBoard, "1234567890", null, "名無し", "sage", "テスト投稿");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync(req));
            Assert.Contains("[安全ガード]", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task SubmitDummyAuthAsync_SendsDummyAuthAndPersistsCookie()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-auth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            HttpRequestMessage? capturedRequest = null;

            var mockHandler = new MockHttpMessageHandler(req =>
            {
                capturedRequest = req;
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = req
                };
                resp.Headers.TryAddWithoutValidation("Set-Cookie", "edge-token=test_dummy_token_12345; Path=/; HttpOnly");
                return resp;
            });

            var http = new MonazillaClient(mockHandler);
            var jar = new CookieJar(paths.DonguriCookiesPath);
            var donguri = new DonguriService(jar, paths);
            var cookieJars = new ProviderCookieJars(paths);
            var client = new PostClient(http, donguri, cookieJars: cookieJars)
            {
                SafePostMode = true
            };

            var success = await client.SubmitDummyAuthAsync("ABC123", "http://127.0.0.1:8787/auth-code");
            Assert.True(success);
            Assert.NotNull(capturedRequest);
            Assert.Equal("http://127.0.0.1:8787/auth-code", capturedRequest.RequestUri?.ToString());

            // CookieJar に edge-token が保存されたか確認
            var provider = BbsRegistry.ResolveOrDefault("bbs.eddibb.cc");
            var providerJar = cookieJars.Get(provider);
            var cookie = providerJar.Find("127.0.0.1", "edge-token");
            Assert.NotNull(cookie);
            Assert.Equal("test_dummy_token_12345", cookie);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostAsync_Open2ch_ExecutesTwoStagePostWithCookiesAndSucceeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser-open2ch-post-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var paths = new DataPaths(tempDir);
            var stage = 0;
            HttpRequestMessage? req1 = null;
            HttpRequestMessage? req2 = null;

            var mockHandler = new MockHttpMessageHandler(req =>
            {
                stage++;
                if (stage == 1)
                {
                    req1 = req;
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
                    resp.Headers.TryAddWithoutValidation("Set-Cookie", "PREN=cookie_val_12345; Path=/; Domain=open2ch.net");
                    resp.Content = new ByteArrayContent(System.Text.Encoding.GetEncoding(932).GetBytes(@"
<html><body>
<h2>クッキー確認</h2>
<form method=POST action=../test/bbs.cgi>
<input type=hidden name=bbs value=""newsplus"">
<input type=hidden name=key value=""1700000000"">
<input type=hidden name=time value=""1700000001"">
<input type=hidden name=mona value=""open2ch_token_xyz"">
<input type=submit value=""上記全てを承諾して書き込む"">
</form></body></html>"));
                    return resp;
                }
                else
                {
                    req2 = req;
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
                    resp.Content = new ByteArrayContent(System.Text.Encoding.GetEncoding(932).GetBytes(@"
<html><body><b>書きこみました。</b></body></html>"));
                    return resp;
                }
            });

            var http = new MonazillaClient(mockHandler);
            var jar = new CookieJar(paths.DonguriCookiesPath);
            var donguri = new DonguriService(jar, paths);
            var cookieJars = new ProviderCookieJars(paths);
            var client = new PostClient(http, donguri, cookieJars: cookieJars)
            {
                SafePostMode = false // モックテストなので外部ホスト検証をバイパス
            };

            var board = new Board("newsplus", "ニュース速報+", "https://uni.open2ch.net/newsplus/", "ニュース", 1);
            var postReq = new PostRequest(board, "1700000000", null, "オープン名無し", "sage", "テスト投稿本文");

            var result = await client.PostAsync(postReq);

            Assert.Equal(PostOutcome.Success, result.Outcome);
            Assert.Equal(2, stage);

            // STAGE 1 の検証
            Assert.NotNull(req1);
            Assert.Equal("https://uni.open2ch.net/test/bbs.cgi", req1.RequestUri?.ToString());
            Assert.Equal("https://uni.open2ch.net/test/read.cgi/newsplus/1700000000/", req1.Headers.Referrer?.ToString());

            // STAGE 2 の検証 (Cookie が送られていること)
            Assert.NotNull(req2);
            var cookieHeader = req2.Headers.GetValues("Cookie");
            Assert.Contains(cookieHeader, c => c.Contains("PREN=cookie_val_12345"));

            // 永続化された CookieJar の検証
            var provider = BbsRegistry.ResolveOrDefault("uni.open2ch.net");
            var providerJar = cookieJars.Get(provider);
            var savedCookie = providerJar.Find("uni.open2ch.net", "PREN");
            Assert.NotNull(savedCookie);
            Assert.Equal("cookie_val_12345", savedCookie);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
