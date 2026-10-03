using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Api;
using ChBrowser.Services.Bbs;

namespace ChBrowser.Services.Storage;

/// <summary>
/// 「全ログ」「お気に入り以外の全ログ」「板一覧以外の取得済み板」の集約・一覧生成サービス。
/// </summary>
public sealed class AllLogsService
{
    static AllLogsService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly DataPaths _paths;
    private readonly SubjectTxtClient _subjectClient;
    private readonly SettingTxtClient _settingClient;
    private readonly ThreadIndexService _threadIndex;
    private readonly FavoritesStorage _favoritesStorage;

    public AllLogsService(
        DataPaths paths,
        SubjectTxtClient subjectClient,
        SettingTxtClient settingClient,
        ThreadIndexService threadIndex,
        FavoritesStorage favoritesStorage)
    {
        _paths = paths;
        _subjectClient = subjectClient;
        _settingClient = settingClient;
        _threadIndex = threadIndex;
        _favoritesStorage = favoritesStorage;
    }

    /// <summary>
    /// ローカルに保存されている全ログ (*.dat) をスキャンしてスレッド一覧を構築する。
    /// </summary>
    /// <param name="excludeFavorites">true の場合、お気に入り登録済みスレッドを除外する。</param>
    /// <param name="resolveListed">板一覧に登録されている板を解決するコールバック（任意）。</param>
    public List<ThreadListItem> BuildAllLogsItems(bool excludeFavorites, Func<string, string, Board?>? resolveListed = null)
    {
        var items = new List<ThreadListItem>();
        var favData = _favoritesStorage.Load();
        var favSet = FavoritesManager.CollectFavoriteThreadKeys(favData.Root);

        foreach (var rootDomain in BbsRegistry.StorageRoots)
        {
            var rootDir = Path.Combine(_paths.Root, rootDomain);
            if (!Directory.Exists(rootDir)) continue;

            var boardDirs = new List<(string DirName, List<string> DatFiles)>();
            foreach (var dirPath in Directory.EnumerateDirectories(rootDir))
            {
                var dirName = Path.GetFileName(dirPath);
                var datFiles = Directory.EnumerateFiles(dirPath, "*.dat").ToList();
                if (datFiles.Count > 0)
                {
                    boardDirs.Add((dirName, datFiles));
                    continue;
                }
                // 2 階層 dir (したらば等)
                foreach (var subPath in Directory.EnumerateDirectories(dirPath))
                {
                    var subDats = Directory.EnumerateFiles(subPath, "*.dat").ToList();
                    if (subDats.Count == 0) continue;
                    boardDirs.Add((dirName + "/" + Path.GetFileName(subPath), subDats));
                }
            }

            foreach (var (dirName, datFiles) in boardDirs)
            {
                var board = resolveListed?.Invoke(rootDomain, dirName)
                            ?? BuildBoardWithFallback(rootDomain, dirName);

                var subjList = LoadSubjectFromDiskSync(board);
                var subjByKey = subjList.ToDictionary(t => t.Key);

                foreach (var datFile in datFiles)
                {
                    var key = Path.GetFileNameWithoutExtension(datFile);
                    if (string.IsNullOrEmpty(key)) continue;

                    ThreadInfo info;
                    LogMarkState state;
                    if (subjByKey.TryGetValue(key, out var subj))
                    {
                        info = subj;
                        state = LogMarkState.Cached;
                    }
                    else
                    {
                        var title = ReadDatTitle(datFile) ?? "(タイトル不明)";
                        var idx = _threadIndex.Load(board.Host, dirName, key);
                        var fetchedCount = idx?.LastFetchedPostCount ?? 0;
                        info = new ThreadInfo(key, title, fetchedCount, 0);
                        state = BbsRegistry.ResolveOrDefault(board.Host).ThreadListIsComplete
                            ? LogMarkState.Dropped : LogMarkState.Cached;
                    }

                    var isFav = favSet.Contains((board.Host, dirName, key));
                    if (excludeFavorites && isFav) continue;

                    items.Add(new ThreadListItem(
                        Info: info,
                        Host: board.Host,
                        DirectoryName: dirName,
                        BoardName: board.BoardName,
                        State: state,
                        IsFavorited: isFav,
                        Kind: ThreadListItemKind.Thread
                    ));
                }
            }
        }

        return items;
    }

    /// <summary>
    /// 板一覧 (bbsmenu) に載っていないローカル取得済み板をスキャンして一覧を構築する。
    /// </summary>
    public List<ThreadListItem> BuildUnlistedBoardsItems(Func<string, string, Board?>? resolveListed = null)
    {
        var scanner = new UnlistedBoardScanner(_paths, _settingClient);
        var found = scanner.Scan(resolveListed ?? ((r, d) => null));
        var items = new List<ThreadListItem>();

        foreach (var ub in found)
        {
            var title = string.IsNullOrWhiteSpace(ub.Board.BoardName) ? ub.Board.DirectoryName : ub.Board.BoardName;
            var boardCol = $"{ub.Provider.DisplayName} {ub.Board.Host}";
            items.Add(new ThreadListItem(
                Info: new ThreadInfo("", title, ub.DatCount, 0),
                Host: ub.Board.Host,
                DirectoryName: ub.Board.DirectoryName,
                BoardName: boardCol,
                State: LogMarkState.None,
                IsFavorited: false,
                Kind: ThreadListItemKind.Board
            ));
        }

        return items;
    }

    private Board BuildBoardWithFallback(string rootDomain, string directoryName)
    {
        var board = UnlistedBoardScanner.BuildFallbackBoard(rootDomain, directoryName, directoryName);
        var path = _paths.SettingTxtPath(board.Host, board.DirectoryName);
        if (File.Exists(path))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var dict = BbsRegistry.ResolveOrDefault(board.Host).ParseBoardInfo(bytes);
                if (dict.TryGetValue("BBS_TITLE", out var title) && !string.IsNullOrWhiteSpace(title))
                {
                    return board with { BoardName = title.Trim() };
                }
            }
            catch { }
        }
        return board;
    }

    private IReadOnlyList<ThreadInfo> LoadSubjectFromDiskSync(Board board)
    {
        try
        {
            return _subjectClient.LoadFromDiskAsync(board).GetAwaiter().GetResult();
        }
        catch
        {
            return Array.Empty<ThreadInfo>();
        }
    }

    public static string? ReadDatTitle(string datPath)
    {
        if (NumberedLogFormat.TryReadTitle(datPath, out var numberedTitle) && !string.IsNullOrWhiteSpace(numberedTitle))
        {
            return numberedTitle;
        }
        try
        {
            var sjis = Encoding.GetEncoding(932);
            using var sr = new StreamReader(datPath, sjis);
            var first = sr.ReadLine();
            if (!string.IsNullOrEmpty(first))
            {
                var fields = first.Split("<>", StringSplitOptions.None);
                if (fields.Length >= 5 && !string.IsNullOrWhiteSpace(fields[4]))
                {
                    return WebUtility.HtmlDecode(fields[4].Trim());
                }
            }
        }
        catch
        {
        }

        try
        {
            using var sr = new StreamReader(datPath, Encoding.UTF8);
            var first = sr.ReadLine();
            if (!string.IsNullOrEmpty(first))
            {
                var fields = first.Split("<>", StringSplitOptions.None);
                if (fields.Length >= 5 && !string.IsNullOrWhiteSpace(fields[4]))
                {
                    return WebUtility.HtmlDecode(fields[4].Trim());
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
