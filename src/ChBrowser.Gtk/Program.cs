using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Adw;
using ChBrowser.Services.Logging;
using ChBrowser.Services.Storage;
using WebKit;

namespace ChBrowser.GtkApp;

public static class Program
{
    private static DataPaths? _paths;
    private static LocalAssetServer? _assetServer;
    private static GtkUiDispatcher? _dispatcher;
    private static Application? _app;
    private static MainWindow? _mainWindow;
    private static bool _isTestMode;
    private static int _exitCode = 0;

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "setenv")]
    private static extern int NativeSetEnv(string name, string value, int overwrite);

    /// <summary>
    /// NVIDIA プロプライエタリドライバ環境では WebKitGTK の DMA-BUF レンダラが GBM バッファを確保できず
    /// (Mesa が NVIDIA の render ノードに kms_swrast でダムバッファを作ろうとして EACCES)、WebView が何も描画されない。
    /// その環境に限り DMA-BUF レンダラを無効化する。ユーザーが明示的に指定している場合はそれを優先する。
    /// WebKitWebProcess は GLib (ネイティブ) から起動されるため、.NET の Environment.SetEnvironmentVariable
    /// (= マネージド側の環境変数だけを書き換える) ではなく libc の setenv で設定する。
    /// </summary>
    private static void ConfigureWebKitRenderer()
    {
        const string name = "WEBKIT_DISABLE_DMABUF_RENDERER";
        if (Environment.GetEnvironmentVariable(name) is not null) return;
        if (!File.Exists("/proc/driver/nvidia/version")) return;
        NativeSetEnv(name, "1", 0);
        Environment.SetEnvironmentVariable(name, "1");
        Console.WriteLine("[ChBrowser.Gtk] NVIDIA ドライバを検出したため WebKit の DMA-BUF レンダラを無効化しました");
    }

    public static int Main(string[] args)
    {
        // Shift_JIS (CP932) 等のレガシー日本語エンコーディングのサポートを登録
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Linux デスクトップ環境 (fcitx5 / ibus) における GTK4 日本語入力対応
        // GTK4 では fcitx5 のモジュール名は "fcitx5" であるため、旧来の "fcitx" が指定されている場合は自動補正する
        // GLib/GTK4 のネイティブ側 (getenv) に反映させるため NativeSetEnv も併せて呼ぶ
        var gtkIm = Environment.GetEnvironmentVariable("GTK_IM_MODULE");
        if (gtkIm == "fcitx")
        {
            NativeSetEnv("GTK_IM_MODULE", "fcitx5", 1);
            Environment.SetEnvironmentVariable("GTK_IM_MODULE", "fcitx5");
        }
        else if (string.IsNullOrEmpty(gtkIm))
        {
            var xmod = Environment.GetEnvironmentVariable("XMODIFIERS");
            if (xmod != null && xmod.Contains("@im=fcitx"))
            {
                NativeSetEnv("GTK_IM_MODULE", "fcitx5", 1);
                Environment.SetEnvironmentVariable("GTK_IM_MODULE", "fcitx5");
            }
        }

        ConfigureWebKitRenderer();

        WebKit.Module.Initialize();
        Adw.Module.Initialize();
        _isTestMode = args.Contains("--test") || Environment.GetEnvironmentVariable("CHBROWSER_GTK_TEST") == "1";

        _paths = new DataPaths();
        _assetServer = new LocalAssetServer(_paths);
        _assetServer.Start();

        var app = Application.New("org.chbrowser.chbrowser.gtk", Gio.ApplicationFlags.FlagsNone);
        _app = app;
        app.OnActivate += (sender, e) => OnAppActivate((Application)sender);

        var code = app.RunWithSynchronizationContext(args);
        _assetServer.Dispose();
        return _isTestMode ? _exitCode : code;
    }

    private static void OnAppActivate(Application app)
    {
        _dispatcher = new GtkUiDispatcher();
        LogService.DispatcherInvoker = action => _dispatcher.Post(action);

        // アイコンテーマの検索パス追加
        try
        {
            var display = Gdk.Display.GetDefault();
            if (display != null)
            {
                var iconTheme = Gtk.IconTheme.GetForDisplay(display);
                var exeDir = AppContext.BaseDirectory;
                var localIconDir = Path.Combine(exeDir, "data", "icons");
                var repoIconDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "data", "icons"));
                if (Directory.Exists(localIconDir)) iconTheme.AddSearchPath(localIconDir);
                if (Directory.Exists(repoIconDir)) iconTheme.AddSearchPath(repoIconDir);

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var userIconDir = Path.Combine(home, ".local", "share", "icons");
                if (Directory.Exists(userIconDir)) iconTheme.AddSearchPath(userIconDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] Icon theme path setup warning: {ex.Message}");
        }

        SetupAppActions(app);

        try
        {
            var sessionDir = Path.Combine(_paths!.CacheDir, "webkit");
            Directory.CreateDirectory(sessionDir);
            var session = NetworkSession.GetDefault();
            session.GetCookieManager().SetPersistentStorage(
                Path.Combine(sessionDir, "cookies.txt"),
                CookiePersistentStorage.Text
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] Cookie storage config warning: {ex.Message}");
        }

        var window = new MainWindow(app, _paths!, _assetServer!, _dispatcher);
        _mainWindow = window;
        window.Present();

        if (_isTestMode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await window.RunAutomatedVerificationAsync();
                    _exitCode = 0;
                    await Task.Delay(500);
                    _dispatcher.Post(() => _app?.Quit());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Test Failure] {ex}");
                    _exitCode = 1;
                    _dispatcher.Post(() => _app?.Quit());
                }
            });
        }
    }

    private static void SetupAppActions(Application app)
    {
        // 1. app.quit
        var quitAction = Gio.SimpleAction.New("quit", null);
        quitAction.OnActivate += (s, e) => app.Quit();
        app.AddAction(quitAction);
        app.SetAccelsForAction("app.quit", new[] { "<Control>q" });

        // 2. app.preferences
        var prefAction = Gio.SimpleAction.New("preferences", null);
        prefAction.OnActivate += (s, e) => _mainWindow?.OpenSettings();
        app.AddAction(prefAction);
        app.SetAccelsForAction("app.preferences", new[] { "<Control>comma" });

        // 3. app.about
        var aboutAction = Gio.SimpleAction.New("about", null);
        aboutAction.OnActivate += (s, e) => _mainWindow?.ShowAboutDialog();
        app.AddAction(aboutAction);
        app.SetAccelsForAction("app.about", new[] { "F1" });

        // Window actions accels
        app.SetAccelsForAction("win.refresh", new[] { "<Control>r", "F5" });
        app.SetAccelsForAction("win.new-thread", new[] { "<Control>n" });
        app.SetAccelsForAction("win.post", new[] { "<Control>Return" });
        app.SetAccelsForAction("win.close-tab", new[] { "<Control>w" });
    }
}
