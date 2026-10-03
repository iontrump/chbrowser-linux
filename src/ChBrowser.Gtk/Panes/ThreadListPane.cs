using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Render;
using Gtk;
using WebKit;

namespace ChBrowser.GtkApp.Panes;

public sealed class ThreadListPane
{
    public Box Container { get; }
    private readonly WebView _webView;
    private readonly WebKitBridge _bridge;
    private readonly LocalAssetServer _assetServer;
    private IReadOnlyList<ThreadListItem> _currentItems = Array.Empty<ThreadListItem>();

    public event Action<string, string, string, string>? ThreadSelected; // (host, dir, key, title)
    public event Action<string, string, string>? BoardSelected; // (host, dir, name)
    public event Action<string, string, string, string>? ExportHtmlRequested; // (host, dir, key, title)
    public event Action<string, string, string, string>? AddToFavoritesRequested; // (host, dir, key, title)
    public event Action<string>? StatusUpdated;

    private readonly Popover _rowContextMenu;
    private string _contextKind = "thread";
    private string _contextHost = "";
    private string _contextDir = "";
    private string _contextKey = "";
    private string _contextTitle = "";

    public ThreadListPane(LocalAssetServer assetServer)
    {
        _assetServer = assetServer;
        Container = Box.New(Orientation.Vertical, 0);

        _webView = WebView.New();
        _webView.SetVexpand(true);
        _webView.SetHexpand(true);

        _bridge = new WebKitBridge(_webView);
        _bridge.MessageReceived += OnMessageReceived;

        Container.Append(_webView);

        _rowContextMenu = Popover.New();
        _rowContextMenu.SetParent(_webView);
        _rowContextMenu.SetHasArrow(false);
        _rowContextMenu.SetAutohide(true);

        var menuBox = Box.New(Orientation.Vertical, 2);
        menuBox.SetMarginTop(4);
        menuBox.SetMarginBottom(4);
        menuBox.SetMarginStart(4);
        menuBox.SetMarginEnd(4);

        var exportHtmlBtn = Button.NewWithLabel("📄 HTMLでエクスポート");
        exportHtmlBtn.AddCssClass("flat");
        exportHtmlBtn.SetHalign(Align.Fill);
        exportHtmlBtn.OnClicked += (s, e) =>
        {
            _rowContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir) && !string.IsNullOrEmpty(_contextKey))
            {
                ExportHtmlRequested?.Invoke(_contextHost, _contextDir, _contextKey, _contextTitle);
            }
        };
        menuBox.Append(exportHtmlBtn);

        var openBtn = Button.NewWithLabel("スレッドを開く");
        openBtn.AddCssClass("flat");
        openBtn.SetHalign(Align.Fill);
        openBtn.OnClicked += (s, e) =>
        {
            _rowContextMenu.Popdown();
            if (_contextKind == "board")
            {
                BoardSelected?.Invoke(_contextHost, _contextDir, _contextTitle);
            }
            else if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir) && !string.IsNullOrEmpty(_contextKey))
            {
                ThreadSelected?.Invoke(_contextHost, _contextDir, _contextKey, _contextTitle);
            }
        };
        menuBox.Append(openBtn);

        var addFavBtn = Button.NewWithLabel("★ お気に入りに追加");
        addFavBtn.AddCssClass("flat");
        addFavBtn.SetHalign(Align.Fill);
        addFavBtn.OnClicked += (s, e) =>
        {
            _rowContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir))
            {
                AddToFavoritesRequested?.Invoke(_contextHost, _contextDir, _contextKey, _contextTitle);
            }
        };
        menuBox.Append(addFavBtn);

        var copyUrlBtn = Button.NewWithLabel("URL をコピー");
        copyUrlBtn.AddCssClass("flat");
        copyUrlBtn.SetHalign(Align.Fill);
        copyUrlBtn.OnClicked += (s, e) =>
        {
            _rowContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir))
            {
                var provider = ChBrowser.Services.Bbs.BbsRegistry.ResolveOrDefault(_contextHost);
                var url = _contextKind == "board" || string.IsNullOrEmpty(_contextKey)
                    ? provider.BoardUrl(_contextHost, _contextDir)
                    : provider.ThreadUrl(_contextHost, _contextDir, _contextKey);
                _webView.GetClipboard().SetText(url);
                StatusUpdated?.Invoke($"URL をコピーしました: {url}");
            }
        };
        menuBox.Append(copyUrlBtn);

        _rowContextMenu.SetChild(menuBox);

        var shell = ThreadListHtmlBuilder.BuildShell();
        _webView.LoadHtml(WebKitBridge.InjectShim(shell), _assetServer.BaseUrl);
    }

    private async void OnMessageReceived(string type, JsonElement root)
    {
        if (type == "ready")
        {
            if (_currentItems.Count > 0)
            {
                await PushItemsAsync(_currentItems);
            }
        }
        else if (type == "openThread")
        {
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var key = root.TryGetProperty("key", out var k) ? k.GetString() : "";
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : "";
            ThreadSelected?.Invoke(host ?? "", dir ?? "", key ?? "", title ?? "");
        }
        else if (type == "openBoard")
        {
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            BoardSelected?.Invoke(host ?? "", dir ?? "", name ?? "");
        }
        else if (type == "threadListRowMenu")
        {
            var kind = root.TryGetProperty("kind", out var kp) ? kp.GetString() : "thread";
            var host = root.TryGetProperty("host", out var hp) ? hp.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var dp) ? dp.GetString() : "";
            var key = root.TryGetProperty("key", out var keyp) ? keyp.GetString() : "";
            var title = root.TryGetProperty("title", out var tp) ? tp.GetString() : "";
            var x = root.TryGetProperty("x", out var xp) ? xp.GetInt32() : 0;
            var y = root.TryGetProperty("y", out var yp) ? yp.GetInt32() : 0;

            if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(dir))
            {
                _contextKind = kind ?? "thread";
                _contextHost = host;
                _contextDir = dir;
                _contextKey = key ?? "";
                _contextTitle = title ?? "";

                var rect = new Gdk.Rectangle();
                rect.X = x;
                rect.Y = y;
                rect.Width = 1;
                rect.Height = 1;
                _rowContextMenu.SetPointingTo(rect);
                _rowContextMenu.Popup();
            }
        }
    }

    public async Task SetThreadsAsync(IReadOnlyList<ThreadInfo> threads, string boardName, string host, string dir)
    {
        var items = threads.Select(t => new ThreadListItem(
            Info: t,
            Host: host,
            DirectoryName: dir,
            BoardName: boardName,
            State: LogMarkState.None,
            IsFavorited: false,
            Kind: ThreadListItemKind.Thread
        )).ToList();

        _currentItems = items;
        await PushItemsAsync(items);
    }

    public async Task SetRawItemsAsync(IReadOnlyList<ThreadListItem> items)
    {
        _currentItems = items;
        await PushItemsAsync(items);
    }

    private async Task PushItemsAsync(IReadOnlyList<ThreadListItem> items)
    {
        try
        {
            var rows = ThreadListHtmlBuilder.BuildRows(items, DateTimeOffset.Now);
            await _bridge.PostMessageAsync(new
            {
                type = "setItems",
                rows = rows
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThreadListPane] PushItems error: {ex.Message}");
        }
    }
}
