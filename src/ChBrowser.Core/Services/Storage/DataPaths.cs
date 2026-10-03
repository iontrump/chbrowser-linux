using System;
using System.IO;

namespace ChBrowser.Services.Storage;

/// <summary>
/// アプリのデータルートと、その配下の標準ディレクトリ/ファイルパスを解決する。
/// **ポータブルアプリ**: 既定では exe と同じディレクトリ配下の <c>data/</c> を使う
/// (= exe ディレクトリごと別マシンに持っていけばそのまま動く)。設計書 §4.1 参照。
/// 環境変数 <c>CHBROWSER_DATA_DIR</c> を設定すると差し替え可能(開発時のテスト用)。
///
/// 5ch.io / bbspink.com は内部に多数のサブドメイン (hayabusa9.5ch.io 等) を持ち、
/// 板はそれぞれ異なる host にホストされる。このため板ディレクトリは
/// <c>data/&lt;rootDomain&gt;/&lt;directory_name&gt;/</c> 形式で配置する。
///
/// 単一ファイル exe (= <c>PublishSingleFile=true</c>) との共存:
///   <see cref="AppContext.BaseDirectory"/> は単一ファイル + <c>IncludeAllContentForSelfExtract=true</c>
///   の構成だと一時展開ディレクトリ (= <c>%LOCALAPPDATA%\Temp\.net\...</c>) を指してしまうため、
///   ポータブル運用 (= exe と同じ場所の <c>data/</c>) が壊れる。
///   <see cref="Environment.ProcessPath"/> は実際に起動した exe の絶対パスを返すので、
///   そのディレクトリを使うことでこの問題を回避する。
/// </summary>
public sealed class DataPaths
{
    private readonly string _defaultRoot;

    /// <summary>ユーザー指定のデータルート保存先ディレクトリ。null または空文字の場合は既定値を使用する。</summary>
    public string? CustomDataRootDir { get; set; }

    /// <summary>データルートディレクトリ。CustomDataRootDir が設定されていればそれを優先し、未設定なら既定のディレクトリを返す。</summary>
    public string Root => !string.IsNullOrWhiteSpace(CustomDataRootDir)
        ? EnsureDir(CustomDataRootDir)
        : _defaultRoot;

    /// <summary>既定のデータルートディレクトリ。</summary>
    public string DefaultRoot => _defaultRoot;

    public string ConfigDir { get; }
    public string CacheDir { get; }
    public bool IsXdgMode { get; }

    public DataPaths(string? rootOverride = null, bool forceXdg = false)
    {
        var envDataDir = Environment.GetEnvironmentVariable("CHBROWSER_DATA_DIR");
        if (rootOverride != null)
        {
            _defaultRoot = rootOverride;
            ConfigDir = EnsureDir(Path.Combine(_defaultRoot, "app"));
            CacheDir = EnsureDir(Path.Combine(_defaultRoot, "cache"));
            IsXdgMode = false;
        }
        else if (!string.IsNullOrEmpty(envDataDir))
        {
            _defaultRoot = envDataDir;
            ConfigDir = EnsureDir(Path.Combine(_defaultRoot, "app"));
            CacheDir = EnsureDir(Path.Combine(_defaultRoot, "cache"));
            IsXdgMode = false;
        }
        else if (forceXdg || OperatingSystem.IsLinux())
        {
            // Linux XDG / キャッシュ準拠: デフォルトのデータルートは $HOME/.cache/chbrowser/data
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
                configHome = Path.Combine(home, ".config");

            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheHome))
                cacheHome = Path.Combine(home, ".cache");

            CacheDir = EnsureDir(Path.Combine(cacheHome, "chbrowser"));
            _defaultRoot = EnsureDir(Path.Combine(CacheDir, "data"));
            ConfigDir = EnsureDir(Path.Combine(configHome, "chbrowser"));
            IsXdgMode = true;
        }
        else
        {
            var exeData = Path.Combine(GetExeDirectory(), "data");
            _defaultRoot = exeData;
            ConfigDir = EnsureDir(Path.Combine(_defaultRoot, "app"));
            CacheDir = EnsureDir(Path.Combine(_defaultRoot, "cache"));
            IsXdgMode = false;
        }
    }

    /// <summary>実際に起動した .exe が置かれているディレクトリの絶対パスを返す。
    /// <see cref="Environment.ProcessPath"/> (NET 6+) の dirname を採用、取得不能な
    /// 開発時のレアケースだけ <see cref="AppContext.BaseDirectory"/> へフォールバックする。</summary>
    private static string GetExeDirectory()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        return AppContext.BaseDirectory;
    }

    public string AppDir          => ConfigDir;
    public string NgDir           => EnsureDir(Path.Combine(ConfigDir, "ng"));
    public string NgByBoardDir    => EnsureDir(Path.Combine(NgDir, "by_board"));
    public string DonguriDir      => EnsureDir(Path.Combine(Root, "donguri"));
    /// <summary>ユーザー指定の画像/動画キャッシュ保存先ディレクトリ。null または空文字の場合は既定値 (CacheDir) を使用する。</summary>
    public string? CustomMediaCacheDir { get; set; }

    /// <summary>画像/動画キャッシュの基準ディレクトリ。</summary>
    public string MediaCacheBaseDir => !string.IsNullOrWhiteSpace(CustomMediaCacheDir)
        ? EnsureDir(CustomMediaCacheDir)
        : CacheDir;

    public string CacheImagesDir  => !string.IsNullOrWhiteSpace(CustomMediaCacheDir)
        ? EnsureDir(Path.Combine(CustomMediaCacheDir, "images"))
        : EnsureDir(Path.Combine(CacheDir, "images"));
    /// <summary>動画本体ファイルのキャッシュディレクトリ (Phase 動画キャッシュ拡張)。
    /// 画像と同じ index.json (= CacheImagesDir 配下) で集約管理されるが、ファイル本体は
    /// 巨大になりがちなので物理的にディレクトリを分けて運用上の混乱を避ける。
    /// 動画サムネ (JPEG) は Image 同様 CacheImagesDir に置く。</summary>
    public string CacheVideosDir  => !string.IsNullOrWhiteSpace(CustomMediaCacheDir)
        ? EnsureDir(Path.Combine(CustomMediaCacheDir, "videos"))
        : EnsureDir(Path.Combine(CacheDir, "videos"));
    public string FontsDir        => EnsureDir(Path.Combine(CacheDir, "fonts"));
    public string ThemesDir       => EnsureDir(Path.Combine(ConfigDir, "themes"));

    public string Root5chIo       => EnsureDir(Path.Combine(Root, "5ch.io"));
    public string RootBbspink     => EnsureDir(Path.Combine(Root, "bbspink.com"));

    public string BbsmenuJsonPath   => Path.Combine(Root5chIo, "bbsmenu.json");
    public string BbsmenuHtmlPath   => Path.Combine(Root5chIo, "bbsmenu.html");

    /// <summary>提供者ごとの板一覧キャッシュ。5ch は拡張子に応じて <see cref="BbsmenuJsonPath"/> または <see cref="BbsmenuHtmlPath"/>、
    /// それ以外は <c>data/&lt;最初の保存ルート&gt;/boardlist.&lt;拡張子&gt;</c>。</summary>
    public string BoardListCachePath(ChBrowser.Services.Bbs.IBbsProvider provider, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (provider.Id == "5ch") return ext == "html" ? BbsmenuHtmlPath : BbsmenuJsonPath;
        var root = provider.StorageRoots.Count > 0 ? provider.StorageRoots[0] : provider.Id;
        return Path.Combine(EnsureDir(Path.Combine(Root, root)), "boardlist." + ext);
    }
    public string LayoutJsonPath    => Path.Combine(AppDir, "layout.json");
    /// <summary>板一覧ペインのツリーの開閉状態 (起動時に前回の状態を再現する)。</summary>
    public string BoardTreeJsonPath => Path.Combine(AppDir, "board_tree.json");
    public string FavoritesJsonPath => Path.Combine(AppDir, "favorites.json");

    /// <summary>アプリ全体設定 (Phase 11)。<see cref="ChBrowser.Models.AppConfig"/> の保存先。</summary>
    public string ConfigJsonPath    => Path.Combine(AppDir, "config.json");

    /// <summary>AI 向けの「各板/スレの使い分け説明」をユーザが自由に書くテキストファイル。
    /// AI チャットを開くと読み込まれ、Strategist / Worker の文脈に注入される (= 板選びの精度向上)。</summary>
    public string AiBoardGuidePath  => Path.Combine(AppDir, "ai-board-guide.txt");

    /// <summary>終了時に開いていたタブ一覧 (= スレ一覧タブ + スレタブの両方、それぞれ並び順を維持) の永続化先。
    /// 起動時に <see cref="ChBrowser.Models.AppConfig.RestoreOpenTabsOnStartup"/> が true なら
    /// このファイルを読み、保存されていた順番で各タブを再オープンする。</summary>
    public string OpenTabsJsonPath => Path.Combine(AppDir, "open_tabs.json");

    // NG ルールは Phase 13e で `data/ng/rules.json` の単一ファイルに統合 (= NgStorage 内で組み立て)。
    // 旧 `global.json` + `by_board/*.json` は NgStorage.LoadAndMigrate が自動で吸収 + 削除する。

    /// <summary>どんぐり/MonaTicket Cookie の永続化先 (Netscape 形式)。Phase 8。</summary>
    public string DonguriCookiesPath => Path.Combine(DonguriDir, "cookies.txt");

    /// <summary>どんぐりの推定 Lv・最終取得時刻などのメタを置く JSON。Phase 8。</summary>
    public string DonguriStateJsonPath => Path.Combine(DonguriDir, "state.json");

    /// <summary>提供者ごとの Cookie (エッヂの edge-token 等) の永続化先 (Netscape 形式)。
    /// <c>data/&lt;最初の保存ルート&gt;/cookies.txt</c>。5ch のどんぐりは <see cref="DonguriCookiesPath"/> で別管理。</summary>
    public string ProviderCookiesPath(ChBrowser.Services.Bbs.IBbsProvider provider)
    {
        var root = provider.StorageRoots.Count > 0 ? provider.StorageRoots[0] : provider.Id;
        return Path.Combine(EnsureDir(Path.Combine(Root, root)), "cookies.txt");
    }

    /// <summary>投稿者情報 (アイコン・プロフィール) の保存先 (<c>data/&lt;掲示板のルート&gt;/authors.json</c>)。</summary>
    public string AuthorProfilesPath(ChBrowser.Services.Bbs.IBbsProvider provider)
    {
        var root = provider.StorageRoots.Count > 0 ? provider.StorageRoots[0] : provider.Id;
        return Path.Combine(EnsureDir(Path.Combine(Root, root)), "authors.json");
    }

    /// <summary>reddit のログイン状態の表示用メモ (<c>data/reddit.com/auth.json</c>、ユーザー名と最終ログイン時刻だけ)。
    /// ログインの Cookie 自体は WebView2 の reddit 用プロファイルが持つ。</summary>
    public string RedditAuthPath => Path.Combine(EnsureDir(Path.Combine(Root, "reddit.com")), "auth.json");

    /// <summary>reddit の接続テスト (設定 → 認証) の結果を書き出すファイル。</summary>
    public string RedditProbeReportPath => Path.Combine(EnsureDir(Path.Combine(Root, "reddit.com")), "probe.txt");

    /// <summary>書き込み記録 (kakikomi.txt)。Jane Xeno フォーマット互換、UTF-8 (BOM なし) + CRLF、append-only。
    /// ユーザがメモ帳等で同時編集できるよう、書込時のみ open → 即 close する運用。</summary>
    public string KakikomiTxtPath => Path.Combine(Root, "kakikomi.txt");

    /// <summary>板の保存ディレクトリ。host から root domain を判定。</summary>
    public string BoardDir(string host, string directoryName)
    {
        var domain = ExtractRootDomain(host);
        return EnsureDir(Path.Combine(Root, domain, directoryName));
    }

    public string SubjectTxtPath(string host, string directoryName)
        => Path.Combine(BoardDir(host, directoryName), "_subject.txt");

    /// <summary>板の SETTING.TXT を置く場所。アンダースコア prefix は subject.txt と同方針
    /// (= dat ファイル群と並んでも視認しやすいよう板メタファイルにだけ印を付ける)。</summary>
    public string SettingTxtPath(string host, string directoryName)
        => Path.Combine(BoardDir(host, directoryName), "_SETTING.TXT");

    public string DatPath(string host, string directoryName, string threadKey)
        => Path.Combine(BoardDir(host, directoryName), threadKey + ".dat");

    public string IdxJsonPath(string host, string directoryName, string threadKey)
        => Path.Combine(BoardDir(host, directoryName), threadKey + ".idx.json");

    /// <summary>スナップショット方式 (reddit 等) のスレの補助情報 (外部 ID → アプリ内番号の対応表)。<c>doc/reddit-design.md</c> §3 B3。</summary>
    public string ThreadMetaPath(string host, string directoryName, string threadKey)
        => Path.Combine(BoardDir(host, directoryName), threadKey + ".meta.json");

    /// <summary>AI による NG 判定スコア (レス番号 → 1..5) の per-thread 保存先。</summary>
    public string AiNgScoresPath(string host, string directoryName, string threadKey)
        => Path.Combine(BoardDir(host, directoryName), threadKey + ".aing.json");

    /// <summary>AI 翻訳の結果 (レス番号 → 訳文、翻訳表示中のレス、スレ全体の翻訳の ON/OFF)。</summary>
    public string TranslationPath(string host, string directoryName, string threadKey)
        => Path.Combine(BoardDir(host, directoryName), threadKey + ".tr.json");

    /// <summary>"hayabusa9.5ch.io" → "5ch.io"、"mercury.bbspink.com" → "bbspink.com"。</summary>
    public static string ExtractRootDomain(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : host;
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
