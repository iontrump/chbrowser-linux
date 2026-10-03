using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Image;
using ChBrowser.Services.Media;
using ChBrowser.Services.Storage;
using Gtk;
using WebKit;

namespace ChBrowser.GtkApp.Panes;

public sealed class ThreadDisplayPane
{
    public Box Container { get; }
    private readonly Notebook _notebook;
    private readonly LocalAssetServer _assetServer;
    private readonly DataPaths _paths;
    private readonly ImageMetaService? _imageMetaService;
    private readonly UrlExpander? _urlExpander;
    private readonly ImageCacheService? _imageCache;
    private readonly MediaAcquisitionTracker? _tracker;
    private readonly VideoDownloadManager? _videoDownloadManager;
    private readonly Dictionary<string, (int PageIndex, ThreadTab Tab)> _tabs = new();

    public event Action<string, string, string, string>? PostRequested;
    public event Action<string>? OpenUrlRequested;
    public event Action<string>? OpenViewerRequested;
    public event Action<string, string, string, string, IReadOnlyList<Post>?>? ExportHtmlRequested; // (host, dir, key, title, posts)

    public ThreadDisplayPane(
        LocalAssetServer assetServer,
        DataPaths paths,
        ImageMetaService? imageMetaService = null,
        UrlExpander? urlExpander = null,
        ImageCacheService? imageCache = null,
        MediaAcquisitionTracker? tracker = null,
        VideoDownloadManager? videoDownloadManager = null)
    {
        _assetServer = assetServer;
        _paths = paths;
        _imageMetaService = imageMetaService;
        _urlExpander = urlExpander;
        _imageCache = imageCache;
        _tracker = tracker;
        _videoDownloadManager = videoDownloadManager;

        Container = Box.New(Orientation.Vertical, 0);

        _notebook = Notebook.New();
        _notebook.SetScrollable(true);
        _notebook.SetVexpand(true);
        _notebook.SetHexpand(true);

        Container.Append(_notebook);
    }

    public IReadOnlyList<Post>? GetCurrentTabPosts(string host, string dir, string key)
    {
        var tabKey = $"{host}/{dir}/{key}";
        if (_tabs.TryGetValue(tabKey, out var tab))
        {
            return tab.Tab.LoadedPosts;
        }
        return null;
    }

    public async Task OpenThreadAsync(string host, string dir, string key, string title, IReadOnlyList<Post>? initialPosts = null)
    {
        var tabKey = $"{host}/{dir}/{key}";
        if (_tabs.TryGetValue(tabKey, out var existing))
        {
            _notebook.SetCurrentPage(existing.PageIndex);
            if (initialPosts is { Count: > 0 })
            {
                await existing.Tab.AppendPostsAsync(initialPosts);
            }
            return;
        }

        var tab = new ThreadTab(_assetServer, _paths, host, dir, key, title, initialPosts, _imageMetaService, _urlExpander, _imageCache, _tracker, _videoDownloadManager);
        tab.PostRequested += (h, d, k, t) => PostRequested?.Invoke(h, d, k, t);
        tab.OpenUrlRequested += url => OpenUrlRequested?.Invoke(url);
        tab.OpenViewerRequested += url => OpenViewerRequested?.Invoke(url);
        tab.ExportHtmlRequested += (h, d, k, t, p) => ExportHtmlRequested?.Invoke(h, d, k, t, p);

        // タブヘッダー
        var headerBox = Box.New(Orientation.Horizontal, 6);
        var label = Label.New(title.Length > 20 ? title[..17] + "..." : title);
        label.SetTooltipText(title);
        headerBox.Append(label);

        var closeBtn = Button.NewFromIconName("window-close-symbolic");
        closeBtn.AddCssClass("flat");
        closeBtn.AddCssClass("circular");
        closeBtn.OnClicked += (s, e) => CloseTab(tabKey);
        headerBox.Append(closeBtn);

        var pageIndex = _notebook.AppendPage(tab.Container, headerBox);
        _tabs[tabKey] = (pageIndex, tab);
        _notebook.SetCurrentPage(pageIndex);
    }

    public void CloseTab(string tabKey)
    {
        if (_tabs.TryGetValue(tabKey, out var existing))
        {
            var pageNum = _notebook.PageNum(existing.Tab.Container);
            if (pageNum >= 0)
            {
                _notebook.RemovePage(pageNum);
            }
            existing.Tab.Dispose();
            _tabs.Remove(tabKey);
        }
    }

    public void CloseCurrentTab()
    {
        var currentPage = _notebook.GetCurrentPage();
        if (currentPage < 0) return;

        foreach (var (key, (_, tab)) in _tabs)
        {
            if (_notebook.PageNum(tab.Container) == currentPage)
            {
                CloseTab(key);
                break;
            }
        }
    }

    public sealed class ThreadTab : IDisposable
    {
        public Box Container { get; }
        public WebView WebView { get; }
        public WebKitBridge Bridge { get; }
        public string Host { get; }
        public string Dir { get; }
        public string Key { get; }
        public string Title { get; }
        private readonly List<Post> _loadedPosts = new();
        private bool _isReady = false;

        private readonly LocalAssetServer _assetServer;
        private readonly DataPaths _paths;
        private readonly ImageMetaService? _imageMetaService;
        private readonly UrlExpander? _urlExpander;
        private readonly ImageCacheService? _imageCache;
        private readonly MediaAcquisitionTracker? _tracker;
        private readonly VideoDownloadManager? _videoDownloadManager;
        private static readonly HttpClient _httpClient = new();

        public event Action<string, string, string, string>? PostRequested;
        public event Action<string>? OpenUrlRequested;
        public event Action<string>? OpenViewerRequested;
        public event Action<string, string, string, string, IReadOnlyList<Post>?>? ExportHtmlRequested;

        public IReadOnlyList<Post> LoadedPosts => _loadedPosts;

        public ThreadTab(
            LocalAssetServer assetServer,
            DataPaths paths,
            string host,
            string dir,
            string key,
            string title,
            IReadOnlyList<Post>? initialPosts,
            ImageMetaService? imageMetaService = null,
            UrlExpander? urlExpander = null,
            ImageCacheService? imageCache = null,
            MediaAcquisitionTracker? tracker = null,
            VideoDownloadManager? videoDownloadManager = null)
        {
            _assetServer = assetServer;
            _paths = paths;
            Host = host;
            Dir = dir;
            Key = key;
            Title = title;
            _imageMetaService = imageMetaService;
            _urlExpander = urlExpander;
            _imageCache = imageCache;
            _tracker = tracker;
            _videoDownloadManager = videoDownloadManager;

            if (_videoDownloadManager != null)
            {
                _videoDownloadManager.DownloadCompleted += OnVideoDownloadCompleted;
                _videoDownloadManager.DownloadFailed += OnVideoDownloadFailed;
            }

            if (initialPosts != null)
            {
                _loadedPosts.AddRange(initialPosts);
            }

            Container = Box.New(Orientation.Vertical, 0);
            Container.SetVexpand(true);
            Container.SetHexpand(true);

            WebView = WebView.New();
            WebView.SetVexpand(true);
            WebView.SetHexpand(true);

            var settings = WebView.GetSettings();
            if (settings != null)
            {
                settings.AutoLoadImages = true;
                settings.AllowUniversalAccessFromFileUrls = true;
                settings.AllowFileAccessFromFileUrls = true;
                settings.MediaPlaybackAllowsInline = true;
                settings.MediaPlaybackRequiresUserGesture = false;
                settings.UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15 ChBrowser/0.1";
            }

            Container.Append(WebView);

            // 下部ツールバー (書き込みボタン、HTMLエクスポートボタン等)
            var bottomBar = Box.New(Orientation.Horizontal, 8);
            bottomBar.SetMarginStart(12);
            bottomBar.SetMarginEnd(12);
            bottomBar.SetMarginTop(6);
            bottomBar.SetMarginBottom(6);

            var postBtn = Button.NewWithLabel("書き込む");
            postBtn.AddCssClass("suggested-action");
            postBtn.OnClicked += (s, e) => PostRequested?.Invoke(Host, Dir, Key, Title);
            bottomBar.Append(postBtn);

            var exportBtn = Button.NewWithLabel("HTMLエクスポート");
            exportBtn.AddCssClass("flat");
            exportBtn.SetTooltipText("このスレッドをオフライン閲覧用HTMLとして保存");
            exportBtn.OnClicked += (s, e) => ExportHtmlRequested?.Invoke(Host, Dir, Key, Title, _loadedPosts);
            bottomBar.Append(exportBtn);

            var infoLabel = Label.New($"スレッド: {Key} ({Dir})");
            infoLabel.AddCssClass("dim-label");
            bottomBar.Append(infoLabel);

            Container.Append(bottomBar);

            Bridge = new WebKitBridge(WebView);
            Bridge.MessageReceived += OnMessage;

            var shell = ThreadShellBuilder.BuildShell();
            WebView.LoadHtml(WebKitBridge.InjectShim(shell), assetServer.BaseUrl);
        }

        public void Dispose()
        {
            if (_videoDownloadManager != null)
            {
                _videoDownloadManager.DownloadCompleted -= OnVideoDownloadCompleted;
                _videoDownloadManager.DownloadFailed -= OnVideoDownloadFailed;
            }
        }

        private void OnVideoDownloadCompleted(object? sender, VideoDownloadEventArgs e)
        {
            _ = PushVideoCacheStateAsync(e.Url);
        }

        private void OnVideoDownloadFailed(object? sender, VideoDownloadEventArgs e)
        {
            _ = PushVideoCacheStateAsync(e.Url);
        }

        private async void OnMessage(string type, JsonElement root)
        {
            switch (type)
            {
                case "ready":
                    _isReady = true;
                    if (_loadedPosts.Count > 0)
                    {
                        await SendPostsAsync(_loadedPosts);
                    }
                    break;

                case "imageMetaRequest":
                    await HandleImageMetaRequestAsync(root);
                    break;

                case "imageLoadFailed":
                    HandleImageLoadFailed(root);
                    break;

                case "mediaSlotRetry":
                    HandleMediaSlotRetry(root);
                    break;

                case "videoThumbnailCache":
                    HandleVideoThumbnailCache(root);
                    break;

                case "videoThumbnailCacheFailed":
                    HandleVideoThumbnailCacheFailed(root);
                    break;

                case "videoCacheQuery":
                    HandleVideoCacheQuery(root);
                    break;

                case "videoDownloadStart":
                    HandleVideoDownloadStart(root);
                    break;

                case "openUrl":
                    HandleOpenUrl(root);
                    break;

                case "openInViewer":
                    HandleOpenInViewer(root);
                    break;
            }
        }

        private async Task HandleImageMetaRequestAsync(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

            try
            {
                string? resolvedUrl = null;
                var isAsync = UrlExpander.IsAsyncExpandable(url);

                if (isAsync && _urlExpander is not null)
                {
                    var isFailed = _tracker?.IsFailed(url, MediaAcquisitionKind.SnsExpand) == true;
                    if (!isFailed)
                    {
                        var expand = await _urlExpander.ExpandAsync(url).ConfigureAwait(false);
                        if (expand.IsNoMedia)
                        {
                            await Bridge.PostMessageAsync(new
                            {
                                type = "imageMeta",
                                url,
                                noMedia = true,
                            });
                            return;
                        }
                        if (expand.IsResolved)
                        {
                            resolvedUrl = expand.Url;
                        }
                        else if (expand.IsUnavailable)
                        {
                            _tracker?.MarkFailed(url, MediaAcquisitionKind.SnsExpand);
                        }
                    }
                }

                var actualUrl = resolvedUrl ?? url;
                ImageCacheHit hit = default;
                bool cached = _imageCache != null && _imageCache.TryGet(actualUrl, out hit, CacheKind.Image);
                long? size = null;
                bool ok;

                if (isAsync && resolvedUrl is null)
                {
                    ok = false;
                }
                else if (cached)
                {
                    ok = true;
                    size = hit.Size;
                    var rel = Path.GetRelativePath(_imageCache!.CacheRootDir, hit.FilePath).Replace('\\', '/');
                    resolvedUrl = _assetServer.GetCacheUrl(rel);
                }
                else if (_imageMetaService is not null)
                {
                    var meta = await _imageMetaService.GetAsync(actualUrl).ConfigureAwait(false);
                    ok = meta.Ok;
                    size = meta.Size;
                    if (ok && _imageCache is not null)
                    {
                        _ = DownloadAndCacheImageAsync(actualUrl);
                    }
                }
                else
                {
                    ok = true;
                    if (_imageCache is not null)
                    {
                        _ = DownloadAndCacheImageAsync(actualUrl);
                    }
                }

                var imageLoadFailed = !cached && _tracker is not null
                    && _tracker.IsFailed(actualUrl, MediaAcquisitionKind.Image);

                await Bridge.PostMessageAsync(new
                {
                    type = "imageMeta",
                    url,
                    resolvedUrl,
                    ok,
                    size,
                    cached,
                    imageLoadFailed,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ThreadTab] HandleImageMetaRequest error: {ex.Message}");
            }
        }

        private async Task DownloadAndCacheImageAsync(string actualUrl)
        {
            if (_imageCache == null || _imageCache.Contains(actualUrl, CacheKind.Image)) return;
            if (_tracker?.IsFailed(actualUrl, MediaAcquisitionKind.Image) == true) return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, actualUrl);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15 ChBrowser/0.1");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _tracker?.MarkFailed(actualUrl, MediaAcquisitionKind.Image);
                    return;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await _imageCache.SaveAsync(actualUrl, stream, contentType, CacheKind.Image).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ThreadTab] DownloadAndCacheImageAsync failed for {actualUrl}: {ex.Message}");
                _tracker?.MarkFailed(actualUrl, MediaAcquisitionKind.Image);
            }
        }

        private void HandleImageLoadFailed(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            _tracker?.MarkFailed(url, MediaAcquisitionKind.Image);
        }

        private void HandleMediaSlotRetry(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            _tracker?.ResetAll(url);
        }

        private void HandleVideoThumbnailCache(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            if (!payload.TryGetProperty("dataUri", out var dataUriProp)) return;
            var url = urlProp.GetString();
            var dataUri = dataUriProp.GetString();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(dataUri)) return;

            if (_imageCache == null) return;

            var commaIdx = dataUri.IndexOf(',');
            if (commaIdx < 0) return;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(dataUri[(commaIdx + 1)..]); }
            catch { return; }

            var ms = new MemoryStream(bytes);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _imageCache.SaveAsync(url, ms, "image/jpeg", CacheKind.VideoThumb).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VideoThumbCache] SaveAsync failed: {ex.Message}");
                }
                await PushVideoCacheStateAsync(url);
            });
        }

        private void HandleVideoThumbnailCacheFailed(JsonElement payload)
        {
            var failUrl = payload.TryGetProperty("url", out var fup) ? fup.GetString() : "";
            if (!string.IsNullOrEmpty(failUrl))
            {
                _videoDownloadManager?.MarkThumbFailed(failUrl);
            }
        }

        private void HandleVideoCacheQuery(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            _ = PushVideoCacheStateAsync(url);
        }

        private void HandleVideoDownloadStart(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;

            _videoDownloadManager?.ResetThumbFailedState(url);
            _videoDownloadManager?.Request(url);
            _ = PushVideoCacheStateAsync(url);
        }

        private async Task PushVideoCacheStateAsync(string url)
        {
            if (_imageCache == null) return;

            var hasThumb = _imageCache.TryGet(url, out var thumbHit, CacheKind.VideoThumb);
            var hasVideo = _imageCache.TryGet(url, out var videoHit, CacheKind.Video);
            string? thumbUrl = null;
            if (hasThumb)
            {
                var rel = Path.GetRelativePath(_imageCache.CacheRootDir, thumbHit.FilePath).Replace('\\', '/');
                thumbUrl = _assetServer.GetCacheUrl(rel);
            }
            string? videoUrl = null;
            if (hasVideo)
            {
                var rel = Path.GetRelativePath(_imageCache.CacheRootDir, videoHit.FilePath).Replace('\\', '/');
                videoUrl = _assetServer.GetCacheUrl(rel);
            }
            long videoSize = hasVideo ? videoHit.Size : 0L;

            var downloading = _videoDownloadManager?.IsDownloading(url) ?? false;
            var downloadFailed = _videoDownloadManager?.IsFailed(url) ?? false;
            var thumbExtractFailed = _videoDownloadManager?.IsThumbFailed(url) ?? false;

            await Bridge.PostMessageAsync(new
            {
                type = "videoCacheState",
                url,
                hasThumb,
                hasVideo,
                thumbUrl,
                videoUrl,
                videoSize,
                downloading,
                downloadFailed,
                thumbExtractFailed,
            });
        }

        private void HandleOpenUrl(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            OpenUrlRequested?.Invoke(url);
        }

        private void HandleOpenInViewer(JsonElement payload)
        {
            if (!payload.TryGetProperty("url", out var urlProp)) return;
            var url = urlProp.GetString();
            if (string.IsNullOrEmpty(url)) return;
            OpenViewerRequested?.Invoke(url);
        }

        public async Task AppendPostsAsync(IReadOnlyList<Post> posts)
        {
            _loadedPosts.AddRange(posts);
            if (_isReady)
            {
                await SendPostsAsync(posts);
            }
        }

        private async Task SendPostsAsync(IReadOnlyList<Post> posts)
        {
            try
            {
                var payload = new List<object>(posts.Count);
                foreach (var p in posts)
                {
                    payload.Add(new
                    {
                        number = p.Number,
                        name = p.Name,
                        date = p.DateText + (string.IsNullOrEmpty(p.Id) ? "" : " ID:" + p.Id),
                        body = p.Body,
                        id = p.Id
                    });
                }
                var json = JsonSerializer.Serialize(payload);
                var js = $"window.appendPosts && window.appendPosts({json});";
                await Bridge.EvaluateAsync(js);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ThreadTab] SendPosts error: {ex.Message}");
            }
        }
    }
}
