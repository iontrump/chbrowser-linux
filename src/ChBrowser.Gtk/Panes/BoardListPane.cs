using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Render;
using ChBrowser.Services.Storage;
using Gtk;
using WebKit;

namespace ChBrowser.GtkApp.Panes;

public sealed class BoardListPane
{
    public Box Container { get; }
    private readonly WebView _webView;
    private readonly WebKitBridge _bridge;
    private readonly LocalAssetServer _assetServer;
    private readonly ConfigStorage? _configStorage;
    private readonly ChBrowser.Services.Api.BbsmenuClient? _bbsmenuClient;

    public event Action<string, string, string>? BoardSelected; // (host, dir, name)
    public event Action<string, string, string>? AddToFavoritesRequested; // (host, dir, name)
    public event Action<string>? StatusUpdated;

    private readonly Popover _boardContextMenu;
    private string _contextHost = "";
    private string _contextDir = "";
    private string _contextName = "";

    public BoardListPane(LocalAssetServer assetServer, ConfigStorage? configStorage = null, ChBrowser.Services.Api.BbsmenuClient? bbsmenuClient = null)
    {
        _assetServer = assetServer;
        _configStorage = configStorage;
        _bbsmenuClient = bbsmenuClient;
        Container = Box.New(Orientation.Vertical, 0);

        _webView = WebView.New();
        _webView.SetVexpand(true);
        _webView.SetHexpand(true);

        _bridge = new WebKitBridge(_webView);
        _bridge.MessageReceived += OnMessageReceived;

        Container.Append(_webView);

        _boardContextMenu = Popover.New();
        _boardContextMenu.SetParent(_webView);
        _boardContextMenu.SetHasArrow(false);
        _boardContextMenu.SetAutohide(true);

        var menuBox = Box.New(Orientation.Vertical, 2);
        menuBox.SetMarginTop(4);
        menuBox.SetMarginBottom(4);
        menuBox.SetMarginStart(4);
        menuBox.SetMarginEnd(4);

        var addFavBtn = Button.NewWithLabel("★ お気に入りに追加");
        addFavBtn.AddCssClass("flat");
        addFavBtn.SetHalign(Align.Fill);
        addFavBtn.OnClicked += (s, e) =>
        {
            _boardContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir))
            {
                AddToFavoritesRequested?.Invoke(_contextHost, _contextDir, _contextName);
            }
        };
        menuBox.Append(addFavBtn);

        var openBoardBtn = Button.NewWithLabel("板を開く");
        openBoardBtn.AddCssClass("flat");
        openBoardBtn.SetHalign(Align.Fill);
        openBoardBtn.OnClicked += (s, e) =>
        {
            _boardContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir))
            {
                BoardSelected?.Invoke(_contextHost, _contextDir, _contextName);
            }
        };
        menuBox.Append(openBoardBtn);

        var copyUrlBtn = Button.NewWithLabel("板の URL をコピー");
        copyUrlBtn.AddCssClass("flat");
        copyUrlBtn.SetHalign(Align.Fill);
        copyUrlBtn.OnClicked += (s, e) =>
        {
            _boardContextMenu.Popdown();
            if (!string.IsNullOrEmpty(_contextHost) && !string.IsNullOrEmpty(_contextDir))
            {
                var provider = ChBrowser.Services.Bbs.BbsRegistry.ResolveOrDefault(_contextHost);
                var url = provider.BoardUrl(_contextHost, _contextDir);
                _webView.GetClipboard().SetText(url);
                StatusUpdated?.Invoke($"板の URL をコピーしました: {url}");
            }
        };
        menuBox.Append(copyUrlBtn);

        _boardContextMenu.SetChild(menuBox);

        var shell = BoardListHtmlBuilder.BuildShell();
        _webView.LoadHtml(WebKitBridge.InjectShim(shell), _assetServer.BaseUrl);
    }

    private async void OnMessageReceived(string type, JsonElement root)
    {
        if (type == "ready")
        {
            await RefreshTreeAsync();
        }
        else if (type == "openBoard")
        {
            var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
            var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            BoardSelected?.Invoke(host ?? "", dir ?? "", name ?? "");
        }
        else if (type == "contextMenu")
        {
            var target = root.TryGetProperty("target", out var tp) ? tp.GetString() : "";
            if (target == "board")
            {
                var host = root.TryGetProperty("host", out var h) ? h.GetString() : "";
                var dir = root.TryGetProperty("directoryName", out var d) ? d.GetString() : "";
                var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
                var x = root.TryGetProperty("x", out var xp) ? xp.GetInt32() : 0;
                var y = root.TryGetProperty("y", out var yp) ? yp.GetInt32() : 0;

                if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(dir))
                {
                    _contextHost = host;
                    _contextDir = dir;
                    _contextName = name ?? "";

                    var rect = new Gdk.Rectangle();
                    rect.X = x;
                    rect.Y = y;
                    rect.Width = 1;
                    rect.Height = 1;
                    _boardContextMenu.SetPointingTo(rect);
                    _boardContextMenu.Popup();
                }
            }
        }
    }

    public async Task RefreshTreeAsync()
    {
        try
        {
            var config = _configStorage?.Load() ?? new AppConfig();
            var order = config.GetEffectiveBoardOrder();

            var providers = new List<BoardListProviderNode>();
            foreach (var id in order)
            {
                if (!config.IsProviderVisible(id)) continue;
                switch (id)
                {
                    case "eddi":
                        providers.Add(new("eddi", "エッヂ", IsExpanded: true, HasBoardList: true));
                        break;
                    case "5ch":
                        providers.Add(new("5ch", "5ちゃんねる", IsExpanded: true, HasBoardList: true));
                        break;
                    case "machi":
                        providers.Add(new("machi", "まちBBS", IsExpanded: false, HasBoardList: true));
                        break;
                    case "open2ch":
                        providers.Add(new("open2ch", "おーぷん２ちゃんねる", IsExpanded: false, HasBoardList: true));
                        break;
                }
            }

            var allCategories = new List<IBoardCategoryTreeItem>();

            // 1. エッヂ
            allCategories.Add(new SimpleCategoryItem
            {
                ProviderId = "eddi",
                CategoryName = "エッヂ",
                IsExpanded = true,
                Boards = new[]
                {
                    new Board("liveedge", "エッヂ", "https://bbs.eddibb.cc/liveedge/", "エッヂ", 0)
                }
            });

            // 2. 5ch
            IReadOnlyList<BoardCategory>? fiveChCats = null;
            if (_bbsmenuClient != null)
            {
                try
                {
                    fiveChCats = await _bbsmenuClient.LoadFromDiskAsync(ChBrowser.Services.Bbs.BbsRegistry.FiveCh);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BoardListPane] 5ch cache load error: {ex.Message}");
                }
            }

            if (fiveChCats != null && fiveChCats.Count > 0)
            {
                allCategories.AddRange(fiveChCats);
            }
            else
            {
                allCategories.Add(new SimpleCategoryItem
                {
                    ProviderId = "5ch",
                    CategoryName = "ニュース",
                    IsExpanded = true,
                    Boards = new[]
                    {
                        new Board("newsplus", "ニュース速報+", "https://asahi.5ch.net/newsplus/", "ニュース", 0),
                        new Board("mnewsplus", "芸スポ速報+", "https://hayabusa9.5ch.net/mnewsplus/", "ニュース", 1)
                    }
                });
                allCategories.Add(new SimpleCategoryItem
                {
                    ProviderId = "5ch",
                    CategoryName = "雑談",
                    IsExpanded = false,
                    Boards = new[]
                    {
                        new Board("news4vip", "ニュー速VIP", "https://mi.5ch.net/news4vip/", "雑談", 0)
                    }
                });
            }

            // 3. まちBBS
            IReadOnlyList<BoardCategory>? machiCats = null;
            if (_bbsmenuClient != null && ChBrowser.Services.Bbs.BbsRegistry.Machi is { } machi)
            {
                try
                {
                    machiCats = await _bbsmenuClient.LoadFromDiskAsync(machi);
                }
                catch { }
            }

            if (machiCats != null && machiCats.Count > 0)
            {
                allCategories.AddRange(machiCats);
            }
            else
            {
                allCategories.Add(new SimpleCategoryItem
                {
                    ProviderId = "machi",
                    CategoryName = "地域",
                    IsExpanded = false,
                    Boards = new[]
                    {
                        new Board("tokyo", "東京", "https://tokyo.machi.to/tokyo/", "地域", 0)
                    }
                });
            }

            // 4. おーぷん２ちゃんねる
            IReadOnlyList<BoardCategory>? open2chCats = null;
            if (_bbsmenuClient != null && ChBrowser.Services.Bbs.BbsRegistry.Open2ch is { } open2ch)
            {
                try
                {
                    open2chCats = await _bbsmenuClient.LoadFromDiskAsync(open2ch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BoardListPane] open2ch cache load error: {ex.Message}");
                }
            }

            if (open2chCats != null && open2chCats.Count > 0)
            {
                allCategories.AddRange(open2chCats);
            }
            else
            {
                allCategories.Add(new SimpleCategoryItem
                {
                    ProviderId = "open2ch",
                    CategoryName = "ニュース",
                    IsExpanded = false,
                    Boards = new[]
                    {
                        new Board("newsplus", "ニュース速報+", "https://uni.open2ch.net/newsplus/", "ニュース", 0),
                        new Board("mnewsplus", "芸スポ速報+", "https://awabi.open2ch.net/mnewsplus/", "ニュース", 1)
                    }
                });
                allCategories.Add(new SimpleCategoryItem
                {
                    ProviderId = "open2ch",
                    CategoryName = "雑談",
                    IsExpanded = false,
                    Boards = new[]
                    {
                        new Board("news4vip", "ニュー速VIP", "https://ikura.open2ch.net/news4vip/", "雑談", 0)
                    }
                });
            }

            var categories = allCategories.Where(c => config.IsProviderVisible(c.ProviderId)).ToList();
            var tree = BoardListHtmlBuilder.BuildTree(providers, categories);
            await _bridge.PostMessageAsync(new
            {
                type = "setBoardTree",
                full = true,
                providers = tree
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BoardListPane] RefreshTree error: {ex.Message}");
        }
    }
}
