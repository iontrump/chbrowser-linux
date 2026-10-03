using System;
using System.Collections.Generic;
using System.Linq;
using ChBrowser.Models;
using ChBrowser.Services.Bbs;

namespace ChBrowser.Services.Storage;

/// <summary>
/// お気に入りツリーの生成・操作・掲示板別フォルダ管理を提供する。
/// </summary>
public static class FavoritesManager
{
    /// <summary>
    /// 初期お気に入りデータを生成する。
    /// 「よく見る板」ではなく、掲示板名（「エッヂ」「5ちゃんねる」等）のフォルダ直下に板を配置する。
    /// </summary>
    public static FavoritesData CreateInitialFavorites()
    {
        return new FavoritesData
        {
            Root = new FavoriteEntry[]
            {
                new FavoriteFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "エッヂ",
                    Children = new FavoriteEntry[]
                    {
                        new FavoriteBoard
                        {
                            Id = Guid.NewGuid(),
                            Host = EddiProvider.CanonicalHost,
                            DirectoryName = "liveedge",
                            BoardName = "エッヂ",
                            AddedAt = DateTimeOffset.UtcNow
                        }
                    }
                },
                new FavoriteFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "5ちゃんねる",
                    Children = new FavoriteEntry[]
                    {
                        new FavoriteBoard
                        {
                            Id = Guid.NewGuid(),
                            Host = "asahi.5ch.net",
                            DirectoryName = "newsplus",
                            BoardName = "ニュース速報+",
                            AddedAt = DateTimeOffset.UtcNow
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// 旧バージョンで生成された「よく見る板」フォルダが存在する場合、
    /// その中の各板・スレッドを対応する掲示板名のフォルダに移動し、「よく見る板」フォルダを削除する。
    /// </summary>
    public static bool MigrateLegacyFavorites(FavoritesData data, out FavoritesData migrated)
    {
        var rootList = new List<FavoriteEntry>(data.Root);
        var legacyFolder = rootList.OfType<FavoriteFolder>().FirstOrDefault(f => f.Name == "よく見る板");
        if (legacyFolder == null)
        {
            migrated = data;
            return false;
        }

        rootList.Remove(legacyFolder);
        foreach (var child in legacyFolder.Children)
        {
            if (child is FavoriteBoard board)
            {
                var folderName = ResolveFolderName(board.Host);
                AddEntryToFolder(rootList, folderName, board);
            }
            else if (child is FavoriteThread thread)
            {
                var folderName = ResolveFolderName(thread.Host);
                AddEntryToFolder(rootList, folderName, thread);
            }
            else
            {
                rootList.Add(child);
            }
        }

        migrated = data with { Root = rootList };
        return true;
    }

    /// <summary>
    /// 指定された板をお気に入りに追加する。
    /// 板のホストから掲示板名を特定し、その掲示板名のフォルダが存在しなければ作成して中に追加する。
    /// 既に同一板が存在する場合は false を返す。
    /// </summary>
    public static bool AddBoard(FavoritesData data, string host, string directoryName, string boardName, out FavoritesData updated)
    {
        if (string.Equals(host, "liveedge", StringComparison.OrdinalIgnoreCase))
        {
            host = EddiProvider.CanonicalHost;
        }

        var rootList = new List<FavoriteEntry>(data.Root);

        if (ContainsBoard(rootList, host, directoryName))
        {
            updated = data;
            return false;
        }

        var newEntry = new FavoriteBoard
        {
            Id = Guid.NewGuid(),
            Host = host,
            DirectoryName = directoryName,
            BoardName = string.IsNullOrWhiteSpace(boardName) ? directoryName : boardName,
            AddedAt = DateTimeOffset.UtcNow
        };

        var folderName = ResolveFolderName(host);
        AddEntryToFolder(rootList, folderName, newEntry);

        updated = data with { Root = rootList };
        return true;
    }

    /// <summary>
    /// 指定されたスレッドをお気に入りに追加する。
    /// スレッドのホストから掲示板名を特定し、その掲示板名のフォルダが存在しなければ作成して中に追加する。
    /// 既に同一スレッドが存在する場合は false を返す。
    /// </summary>
    public static bool AddThread(FavoritesData data, string host, string directoryName, string threadKey, string threadTitle, out FavoritesData updated)
    {
        if (string.Equals(host, "liveedge", StringComparison.OrdinalIgnoreCase))
        {
            host = EddiProvider.CanonicalHost;
        }

        var rootList = new List<FavoriteEntry>(data.Root);

        if (ContainsThread(rootList, host, directoryName, threadKey))
        {
            updated = data;
            return false;
        }

        var newEntry = new FavoriteThread
        {
            Id = Guid.NewGuid(),
            Host = host,
            DirectoryName = directoryName,
            ThreadKey = threadKey,
            Title = string.IsNullOrWhiteSpace(threadTitle) ? threadKey : threadTitle,
            BoardName = directoryName,
            AddedAt = DateTimeOffset.UtcNow
        };

        var folderName = ResolveFolderName(host);
        AddEntryToFolder(rootList, folderName, newEntry);

        updated = data with { Root = rootList };
        return true;
    }

    /// <summary>
    /// ホスト名に対応する掲示板名（フォルダ名）を取得する。
    /// UI の板一覧表示名（「エッヂ」「5ちゃんねる」「まちBBS」「おーぷん２ちゃんねる」等）と一致させる。
    /// </summary>
    public static string ResolveFolderName(string? host)
    {
        if (string.IsNullOrEmpty(host)) return "その他";
        if (string.Equals(host, "liveedge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, EddiProvider.CanonicalHost, StringComparison.OrdinalIgnoreCase))
        {
            return "エッヂ";
        }
        var provider = BbsRegistry.ResolveOrDefault(host);
        return provider.Id switch
        {
            "5ch" => "5ちゃんねる",
            "eddi" => "エッヂ",
            "machi" => "まちBBS",
            "open2ch" => "おーぷん２ちゃんねる",
            "shitaraba" => "したらば",
            _ => provider.DisplayName
        };
    }

    private static void AddEntryToFolder(List<FavoriteEntry> rootList, string folderName, FavoriteEntry entry)
    {
        var targetFolder = rootList.OfType<FavoriteFolder>().FirstOrDefault(f => f.Name == folderName);
        if (targetFolder != null)
        {
            var folderChildren = new List<FavoriteEntry>(targetFolder.Children) { entry };
            var updatedFolder = targetFolder with { Children = folderChildren };
            var idx = rootList.IndexOf(targetFolder);
            rootList[idx] = updatedFolder;
        }
        else
        {
            var newFolder = new FavoriteFolder
            {
                Id = Guid.NewGuid(),
                Name = folderName,
                Children = new[] { entry }
            };
            rootList.Add(newFolder);
        }
    }

    public static bool ContainsBoard(IEnumerable<FavoriteEntry> entries, string host, string dir)
    {
        foreach (var entry in entries)
        {
            if (entry is FavoriteBoard b &&
                string.Equals(b.Host, host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.DirectoryName, dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (entry is FavoriteFolder f && ContainsBoard(f.Children, host, dir))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ContainsThread(IEnumerable<FavoriteEntry> entries, string host, string dir, string key)
    {
        foreach (var entry in entries)
        {
            if (entry is FavoriteThread t &&
                string.Equals(t.Host, host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.DirectoryName, dir, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.ThreadKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (entry is FavoriteFolder f && ContainsThread(f.Children, host, dir, key))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// お気に入りに登録されているスレッドのキー (Host, DirectoryName, ThreadKey) の一覧を収集する。
    /// </summary>
    public static HashSet<(string Host, string Dir, string Key)> CollectFavoriteThreadKeys(IEnumerable<FavoriteEntry> entries)
    {
        var set = new HashSet<(string, string, string)>();
        CollectRecursive(entries, set);
        return set;

        static void CollectRecursive(IEnumerable<FavoriteEntry> list, HashSet<(string, string, string)> acc)
        {
            foreach (var item in list)
            {
                if (item is FavoriteThread t)
                {
                    acc.Add((t.Host, t.DirectoryName, t.ThreadKey));
                }
                else if (item is FavoriteFolder f)
                {
                    CollectRecursive(f.Children, acc);
                }
            }
        }
    }
}
