using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Render;
using ChBrowser.Services.Storage;
using Gtk;
using WebKit;

namespace ChBrowser.GtkApp.Panes;

public sealed class FavoritesPane
{
    public Box Container { get; }
    private readonly WebView _webView;
    private readonly WebKitBridge _bridge;
    private readonly LocalAssetServer _assetServer;
    private readonly FavoritesStorage? _favoritesStorage;

    public event Action<string, string, string>? BoardSelected; // (host, dir, name)
    public event Action<string, string, string, string>? ThreadSelected; // (host, dir, key, title)
    public event Action? AllLogsRequested;
    public event Action? NonFavLogsRequested;
    public event Action? UnlistedBoardsRequested;

    public FavoritesPane(LocalAssetServer assetServer, FavoritesStorage? favoritesStorage = null)
    {
        _assetServer = assetServer;
        _favoritesStorage = favoritesStorage;
        Container = Box.New(Orientation.Vertical, 0);

        _webView = WebView.New();
        _webView.SetVexpand(true);
        _webView.SetHexpand(true);

        _bridge = new WebKitBridge(_webView);
        _bridge.MessageReceived += OnMessageReceived;

        Container.Append(_webView);

        var shell = FavoritesHtmlBuilder.BuildShell();
        _webView.LoadHtml(WebKitBridge.InjectShim(shell), _assetServer.BaseUrl);
    }

    private readonly Dictionary<string, FavoriteNode> _nodesById = new();

    private async void OnMessageReceived(string type, JsonElement root)
    {
        if (type == "ready")
        {
            await RefreshFavoritesAsync();
        }
        else if (type == "openFavorite")
        {
            var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : "";
            var targetType = root.TryGetProperty("targetType", out var tt) ? tt.GetString() : "";
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            var key = root.TryGetProperty("key", out var k) ? k.GetString() : "";
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : "";

            if (!string.IsNullOrEmpty(id) && _nodesById.TryGetValue(id, out var cached))
            {
                if (string.IsNullOrEmpty(targetType)) targetType = cached.Type;
                if (string.IsNullOrEmpty(host)) host = cached.Host;
                if (string.IsNullOrEmpty(dir)) dir = cached.Dir;
                if (string.IsNullOrEmpty(name)) name = cached.Name;
                if (string.IsNullOrEmpty(key)) key = cached.Key;
            }

            if (host == "liveedge")
            {
                host = EddiProvider.CanonicalHost;
            }

            if (targetType == "board" || (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(dir) && string.IsNullOrEmpty(key)))
            {
                BoardSelected?.Invoke(host ?? "", dir ?? "", name ?? "");
            }
            else if (targetType == "thread" || !string.IsNullOrEmpty(key))
            {
                ThreadSelected?.Invoke(host ?? "", dir ?? "", key ?? "", title ?? "");
            }
        }
        else if (type == "openBoard")
        {
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            if (host == "liveedge") host = EddiProvider.CanonicalHost;
            BoardSelected?.Invoke(host ?? "", dir ?? "", name ?? "");
        }
        else if (type == "openThread")
        {
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var key = root.TryGetProperty("key", out var k) ? k.GetString() : "";
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : "";
            if (host == "liveedge") host = EddiProvider.CanonicalHost;
            ThreadSelected?.Invoke(host ?? "", dir ?? "", key ?? "", title ?? "");
        }
        else if (type == "openAllLogs")
        {
            AllLogsRequested?.Invoke();
        }
        else if (type == "openNonFavLogs")
        {
            NonFavLogsRequested?.Invoke();
        }
        else if (type == "openUnlistedBoards")
        {
            UnlistedBoardsRequested?.Invoke();
        }
    }

    public async Task RefreshFavoritesAsync()
    {
        try
        {
            FavoritesData data;
            if (_favoritesStorage != null)
            {
                data = _favoritesStorage.Load();
                // 初回または空の場合、初期セット（掲示板名フォルダ）を生成して保存
                if (data.Root.Count == 0 || (data.Root.Count == 1 && data.Root[0] is FavoriteFolder ff && ff.Name == FavoriteDefaults.TopButtonsFolderName && ff.Children.Count == 0))
                {
                    data = FavoritesManager.CreateInitialFavorites();
                    _favoritesStorage.Save(data);
                }
                else if (FavoritesManager.MigrateLegacyFavorites(data, out var migrated))
                {
                    data = migrated;
                    _favoritesStorage.Save(data);
                }
            }
            else
            {
                data = FavoritesManager.CreateInitialFavorites();
            }

            var tree = BuildTree(data.Root);

            _nodesById.Clear();
            RegisterNodes(tree);

            await _bridge.PostMessageAsync(new
            {
                type = "setFavorites",
                items = tree
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FavoritesPane] RefreshFavorites error: {ex.Message}");
        }
    }

    private static List<FavoriteNode> BuildTree(IReadOnlyList<FavoriteEntry> entries)
    {
        var list = new List<FavoriteNode>();
        foreach (var entry in entries)
        {
            var node = ConvertToNode(entry);
            if (node != null) list.Add(node);
        }
        return list;
    }

    private static FavoriteNode? ConvertToNode(FavoriteEntry entry) => entry switch
    {
        FavoriteFolder f => new FavoriteNode(
            "folder",
            f.Id.ToString(),
            f.Name,
            Expanded: true,
            Children: BuildTree(f.Children)
        ),
        FavoriteBoard b => new FavoriteNode(
            "board",
            b.Id.ToString(),
            b.BoardName,
            Host: b.Host,
            Dir: b.DirectoryName
        ),
        FavoriteThread t => new FavoriteNode(
            "thread",
            t.Id.ToString(),
            t.Title,
            Host: t.Host,
            Dir: t.DirectoryName,
            Key: t.ThreadKey,
            Board: t.BoardName
        ),
        _ => null
    };

    public bool AddBoardToFavorites(string host, string directoryName, string boardName)
    {
        if (_favoritesStorage == null) return false;
        try
        {
            var data = _favoritesStorage.Load();
            if (FavoritesManager.MigrateLegacyFavorites(data, out var migrated))
            {
                data = migrated;
            }

            if (FavoritesManager.AddBoard(data, host, directoryName, boardName, out var updated))
            {
                _favoritesStorage.Save(updated);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FavoritesPane] AddBoardToFavorites error: {ex.Message}");
            return false;
        }
    }

    public bool AddThreadToFavorites(string host, string directoryName, string threadKey, string threadTitle)
    {
        if (_favoritesStorage == null) return false;
        try
        {
            var data = _favoritesStorage.Load();
            if (FavoritesManager.MigrateLegacyFavorites(data, out var migrated))
            {
                data = migrated;
            }

            if (FavoritesManager.AddThread(data, host, directoryName, threadKey, threadTitle, out var updated))
            {
                _favoritesStorage.Save(updated);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FavoritesPane] AddThreadToFavorites error: {ex.Message}");
            return false;
        }
    }

    private void RegisterNodes(IEnumerable<FavoriteNode> nodes)
    {
        foreach (var node in nodes)
        {
            _nodesById[node.Id] = node;
            if (node.Children != null)
            {
                RegisterNodes(node.Children);
            }
        }
    }
}
