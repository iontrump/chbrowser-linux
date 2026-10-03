using System;
using System.Collections.Generic;
using System.Linq;
using Adw;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Storage;
using Gtk;

namespace ChBrowser.GtkApp;

public static class PreferencesDialog
{
    public static PreferencesWindow Create(
        DataPaths paths,
        ConfigStorage configStorage,
        Action? onConfigChanged = null,
        ChBrowser.Services.Api.BbsmenuClient? bbsmenuClient = null,
        Action? onCacheDirChanged = null,
        Action? onDataDirChanged = null)
    {
        var window = PreferencesWindow.New();
        window.Title = "設定 - ChBrowser";
        window.SetDefaultSize(600, 520);

        var config = configStorage.Load();

        // ページ 1: 一般設定
        var generalPage = PreferencesPage.New();
        generalPage.SetTitle("一般");
        generalPage.SetIconName("preferences-system-symbolic");

        var storageGroup = PreferencesGroup.New();
        storageGroup.SetTitle("ストレージとパス");
        storageGroup.SetDescription("ポータブルモードおよび各種データの保存先設定です。");

        var dataDirRow = ActionRow.New();
        dataDirRow.SetTitle("データルートディレクトリ");
        var effectiveDataDir = !string.IsNullOrWhiteSpace(config.CustomDataRootDir)
            ? config.CustomDataRootDir
            : paths.Root;
        dataDirRow.SetSubtitle(effectiveDataDir);

        var chooseDataBtn = Button.NewWithLabel("フォルダ選択...");
        chooseDataBtn.AddCssClass("flat");
        chooseDataBtn.SetValign(Align.Center);
        chooseDataBtn.OnClicked += async (s, e) =>
        {
            try
            {
                var dialog = Gtk.FileDialog.New();
                dialog.SetTitle("データルートディレクトリの保存先を選択");
                var folder = await dialog.SelectFolderAsync(window);
                var path = folder?.GetPath();
                if (!string.IsNullOrEmpty(path))
                {
                    var currentCfg = configStorage.Load();
                    currentCfg = currentCfg with { CustomDataRootDir = path };
                    configStorage.Save(currentCfg);
                    paths.CustomDataRootDir = path;
                    dataDirRow.SetSubtitle(path);
                    onDataDirChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreferencesDialog] Data folder selection error: {ex.Message}");
            }
        };
        dataDirRow.AddSuffix(chooseDataBtn);

        var resetDataBtn = Button.NewWithLabel("既定に戻す");
        resetDataBtn.AddCssClass("flat");
        resetDataBtn.SetValign(Align.Center);
        resetDataBtn.OnClicked += (s, e) =>
        {
            var currentCfg = configStorage.Load();
            currentCfg = currentCfg with { CustomDataRootDir = "" };
            configStorage.Save(currentCfg);
            paths.CustomDataRootDir = null;
            dataDirRow.SetSubtitle(paths.Root);
            onDataDirChanged?.Invoke();
        };
        dataDirRow.AddSuffix(resetDataBtn);

        storageGroup.Add(dataDirRow);

        var cacheDirRow = ActionRow.New();
        cacheDirRow.SetTitle("画像/動画キャッシュ");
        var effectiveCacheDir = !string.IsNullOrWhiteSpace(config.CustomMediaCacheDir)
            ? config.CustomMediaCacheDir
            : paths.MediaCacheBaseDir;
        cacheDirRow.SetSubtitle(effectiveCacheDir);

        var chooseBtn = Button.NewWithLabel("フォルダ選択...");
        chooseBtn.AddCssClass("flat");
        chooseBtn.SetValign(Align.Center);
        chooseBtn.OnClicked += async (s, e) =>
        {
            try
            {
                var dialog = Gtk.FileDialog.New();
                dialog.SetTitle("画像/動画キャッシュの保存先ディレクトリを選択");
                var folder = await dialog.SelectFolderAsync(window);
                var path = folder?.GetPath();
                if (!string.IsNullOrEmpty(path))
                {
                    var currentCfg = configStorage.Load();
                    currentCfg = currentCfg with { CustomMediaCacheDir = path };
                    configStorage.Save(currentCfg);
                    paths.CustomMediaCacheDir = path;
                    cacheDirRow.SetSubtitle(path);
                    onCacheDirChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreferencesDialog] Folder selection error: {ex.Message}");
            }
        };
        cacheDirRow.AddSuffix(chooseBtn);

        var resetBtn = Button.NewWithLabel("既定に戻す");
        resetBtn.AddCssClass("flat");
        resetBtn.SetValign(Align.Center);
        resetBtn.OnClicked += (s, e) =>
        {
            var currentCfg = configStorage.Load();
            currentCfg = currentCfg with { CustomMediaCacheDir = "" };
            configStorage.Save(currentCfg);
            paths.CustomMediaCacheDir = null;
            cacheDirRow.SetSubtitle(paths.MediaCacheBaseDir);
            onCacheDirChanged?.Invoke();
        };
        cacheDirRow.AddSuffix(resetBtn);

        storageGroup.Add(cacheDirRow);

        generalPage.Add(storageGroup);

        // ページ 2: 掲示板・ネットワーク設定
        var bbsPage = PreferencesPage.New();
        bbsPage.SetTitle("掲示板");
        bbsPage.SetIconName("network-workgroup-symbolic");

        // 掲示板の表示・順序設定
        var visibilityGroup = PreferencesGroup.New();
        visibilityGroup.SetTitle("掲示板の表示と順序");
        visibilityGroup.SetDescription("板一覧ツリーに表示する掲示板と表示順序（▲/▼ボタンで入替）を設定します。");
        bbsPage.Add(visibilityGroup);

        var currentOrder = new List<string>(config.GetEffectiveBoardOrder());

        var boardMetas = new Dictionary<string, (string Title, string Subtitle)>
        {
            ["eddi"] = ("エッヂ", "エッヂ掲示板 (eddi)"),
            ["5ch"] = ("5ちゃんねる", "5ちゃんねる / BBSPINK (5ch)"),
            ["machi"] = ("まちBBS", "地域コミュニティ掲示板 (machi)"),
            ["open2ch"] = ("おーぷん２ちゃんねる", "おーぷん２ちゃんねる (open2ch)"),
        };

        var eddiGroup = PreferencesGroup.New();
        eddiGroup.SetTitle("エッヂ接続・認証 (eddi)");
        eddiGroup.SetDescription("エッヂ掲示板の接続先および認証トークン設定です。");

        var eddiUrlRow = EntryRow.New();
        eddiUrlRow.SetTitle("ベース URL");
        var initialUrl = !string.IsNullOrWhiteSpace(config.EddiBaseUrl)
            ? config.EddiBaseUrl
            : (Environment.GetEnvironmentVariable("CHBROWSER_EDDI_BASE_URL") ?? "https://bbs.eddibb.cc/");
        eddiUrlRow.SetText(initialUrl);
        eddiUrlRow.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "text")
            {
                var val = eddiUrlRow.GetText()?.Trim() ?? "";
                var currentCfg = configStorage.Load();
                currentCfg = currentCfg with { EddiBaseUrl = val };
                configStorage.Save(currentCfg);
                EddiProvider.CustomBaseUrl = string.IsNullOrWhiteSpace(val) ? null : val;
            }
        };
        eddiGroup.Add(eddiUrlRow);

        var eddiTokenRow = EntryRow.New();
        eddiTokenRow.SetTitle("認証トークン (任意)");
        var currentToken = config.PostAuthTokens != null && config.PostAuthTokens.TryGetValue("eddi", out var t) ? t : "";
        eddiTokenRow.SetText(currentToken);
        eddiTokenRow.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "text")
            {
                var val = eddiTokenRow.GetText()?.Trim() ?? "";
                var currentCfg = configStorage.Load();
                var tokens = currentCfg.PostAuthTokens != null
                    ? new System.Collections.Generic.Dictionary<string, string>(currentCfg.PostAuthTokens)
                    : new System.Collections.Generic.Dictionary<string, string>();
                tokens["eddi"] = val;

                // メール欄もトークンに合わせて更新
                var mails = currentCfg.PostMails != null
                    ? new System.Collections.Generic.Dictionary<string, string>(currentCfg.PostMails)
                    : new System.Collections.Generic.Dictionary<string, string>();
                mails["eddi"] = string.IsNullOrEmpty(val) ? "" : $"#{val}";

                currentCfg = currentCfg with { PostAuthTokens = tokens, PostMails = mails };
                configStorage.Save(currentCfg);
            }
        };
        eddiGroup.Add(eddiTokenRow);

        // 5ちゃんねる設定 (5ch)
        var fiveChGroup = PreferencesGroup.New();
        fiveChGroup.SetTitle("5ちゃんねる (5ch)");
        fiveChGroup.SetDescription("5ちゃんねるの接続先および板一覧の取得設定です。");

        var fiveChUrlRow = EntryRow.New();
        fiveChUrlRow.SetTitle("板一覧取得 URL");
        var initial5chUrl = !string.IsNullOrWhiteSpace(config.FiveChBbsmenuUrl)
            ? config.FiveChBbsmenuUrl
            : "https://menu.5ch.io/bbsmenu.html";
        fiveChUrlRow.SetText(initial5chUrl);
        fiveChUrlRow.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "text")
            {
                var val = fiveChUrlRow.GetText()?.Trim() ?? "";
                var currentCfg = configStorage.Load();
                currentCfg = currentCfg with { FiveChBbsmenuUrl = val };
                configStorage.Save(currentCfg);
                FiveChProvider.CustomBoardListUrl = string.IsNullOrWhiteSpace(val) ? null : val;
            }
        };
        fiveChGroup.Add(fiveChUrlRow);

        var updateRow = ActionRow.New();
        updateRow.SetTitle("板一覧の更新");
        updateRow.SetSubtitle("指定した URL から最新の板一覧を取得して保存します");

        var updateBtn = Button.NewWithLabel("板一覧を更新");
        updateBtn.AddCssClass("suggested-action");
        updateBtn.SetValign(Align.Center);
        updateBtn.OnClicked += async (s, e) =>
        {
            if (bbsmenuClient == null) return;
            updateBtn.SetSensitive(false);
            updateBtn.SetLabel("取得中...");
            try
            {
                var categories = await bbsmenuClient.FetchAndSaveAsync(BbsRegistry.FiveCh);
                var totalBoards = 0;
                foreach (var cat in categories) totalBoards += cat.Boards.Count;
                updateBtn.SetLabel($"更新完了 ({totalBoards}板)");
                onConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreferencesDialog] 5ch board list update error: {ex.Message}");
                updateBtn.SetLabel("取得失敗");
            }
            finally
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2500);
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        updateBtn.SetLabel("板一覧を更新");
                        updateBtn.SetSensitive(true);
                        return false;
                    });
                });
            }
        };
        updateRow.AddSuffix(updateBtn);
        fiveChGroup.Add(updateRow);

        // まちBBS設定 (machi)
        var machiGroup = PreferencesGroup.New();
        machiGroup.SetTitle("まちBBS (machi)");
        machiGroup.SetDescription("まちBBSの接続先および板一覧の取得設定です。");

        var machiUrlRow = EntryRow.New();
        machiUrlRow.SetTitle("板一覧取得 URL");
        var initialMachiUrl = !string.IsNullOrWhiteSpace(config.MachiBbsmenuUrl)
            ? config.MachiBbsmenuUrl
            : "https://www.machi.to/";
        machiUrlRow.SetText(initialMachiUrl);
        machiUrlRow.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "text")
            {
                var val = machiUrlRow.GetText()?.Trim() ?? "";
                var currentCfg = configStorage.Load();
                currentCfg = currentCfg with { MachiBbsmenuUrl = val };
                configStorage.Save(currentCfg);
                MachiProvider.CustomBoardListUrl = string.IsNullOrWhiteSpace(val) ? null : val;
            }
        };
        machiGroup.Add(machiUrlRow);

        var machiUpdateRow = ActionRow.New();
        machiUpdateRow.SetTitle("板一覧の更新");
        machiUpdateRow.SetSubtitle("指定した URL から最新の板一覧を取得して保存します");

        var machiUpdateBtn = Button.NewWithLabel("板一覧を更新");
        machiUpdateBtn.AddCssClass("suggested-action");
        machiUpdateBtn.SetValign(Align.Center);
        machiUpdateBtn.OnClicked += async (s, e) =>
        {
            var machiProvider = BbsRegistry.Machi;
            if (bbsmenuClient == null || machiProvider == null) return;
            machiUpdateBtn.SetSensitive(false);
            machiUpdateBtn.SetLabel("取得中...");
            try
            {
                var categories = await bbsmenuClient.FetchAndSaveAsync(machiProvider);
                var totalBoards = 0;
                foreach (var cat in categories) totalBoards += cat.Boards.Count;
                machiUpdateBtn.SetLabel($"更新完了 ({totalBoards}板)");
                onConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreferencesDialog] Machi board list update error: {ex.Message}");
                machiUpdateBtn.SetLabel("取得失敗");
            }
            finally
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2500);
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        machiUpdateBtn.SetLabel("板一覧を更新");
                        machiUpdateBtn.SetSensitive(true);
                        return false;
                    });
                });
            }
        };
        machiUpdateRow.AddSuffix(machiUpdateBtn);
        machiGroup.Add(machiUpdateRow);

        // おーぷん２ちゃんねる設定 (open2ch)
        var open2chGroup = PreferencesGroup.New();
        open2chGroup.SetTitle("おーぷん２ちゃんねる (open2ch)");
        open2chGroup.SetDescription("おーぷん２ちゃんねるの接続先および板一覧の取得設定です。");

        var open2chUrlRow = EntryRow.New();
        open2chUrlRow.SetTitle("板一覧取得 URL");
        var initialOpen2chUrl = !string.IsNullOrWhiteSpace(config.Open2chBbsmenuUrl)
            ? config.Open2chBbsmenuUrl
            : "https://menu.open2ch.net/bbsmenu.html";
        open2chUrlRow.SetText(initialOpen2chUrl);
        open2chUrlRow.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "text")
            {
                var val = open2chUrlRow.GetText()?.Trim() ?? "";
                var currentCfg = configStorage.Load();
                currentCfg = currentCfg with { Open2chBbsmenuUrl = val };
                configStorage.Save(currentCfg);
                Open2chProvider.CustomBoardListUrl = string.IsNullOrWhiteSpace(val) ? null : val;
            }
        };
        open2chGroup.Add(open2chUrlRow);

        var open2chUpdateRow = ActionRow.New();
        open2chUpdateRow.SetTitle("板一覧の更新");
        open2chUpdateRow.SetSubtitle("指定した URL から最新の板一覧を取得して保存します");

        var open2chUpdateBtn = Button.NewWithLabel("板一覧を更新");
        open2chUpdateBtn.AddCssClass("suggested-action");
        open2chUpdateBtn.SetValign(Align.Center);
        open2chUpdateBtn.OnClicked += async (s, e) =>
        {
            var open2chProvider = BbsRegistry.Open2ch;
            if (bbsmenuClient == null || open2chProvider == null) return;
            open2chUpdateBtn.SetSensitive(false);
            open2chUpdateBtn.SetLabel("取得中...");
            try
            {
                var categories = await bbsmenuClient.FetchAndSaveAsync(open2chProvider);
                var totalBoards = 0;
                foreach (var cat in categories) totalBoards += cat.Boards.Count;
                open2chUpdateBtn.SetLabel($"更新完了 ({totalBoards}板)");
                onConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreferencesDialog] Open2ch board list update error: {ex.Message}");
                open2chUpdateBtn.SetLabel("取得失敗");
            }
            finally
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2500);
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        open2chUpdateBtn.SetLabel("板一覧を更新");
                        open2chUpdateBtn.SetSensitive(true);
                        return false;
                    });
                });
            }
        };
        open2chUpdateRow.AddSuffix(open2chUpdateBtn);
        open2chGroup.Add(open2chUpdateRow);

        var securityGroup = PreferencesGroup.New();
        securityGroup.SetTitle("書き込みセキュリティ");
        securityGroup.SetDescription("外部掲示板サーバへの書き込み制限設定です。");

        var safePostRow = ActionRow.New();
        safePostRow.SetTitle("安全投稿モード");
        safePostRow.SetSubtitle("外部実サーバへの書き込みを遮断し、ローカル開発環境のみ許可します");
        var safePostSwitch = Switch.New();
        safePostSwitch.SetActive(config.SafePostMode);
        safePostSwitch.SetValign(Align.Center);
        safePostSwitch.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "active")
            {
                var currentCfg = configStorage.Load();
                currentCfg = currentCfg with { SafePostMode = safePostSwitch.GetActive() };
                configStorage.Save(currentCfg);
            }
        };
        safePostRow.AddSuffix(safePostSwitch);
        securityGroup.Add(safePostRow);

        var detailGroups = new Dictionary<string, PreferencesGroup>
        {
            ["eddi"] = eddiGroup,
            ["5ch"] = fiveChGroup,
            ["machi"] = machiGroup,
            ["open2ch"] = open2chGroup,
        };

        var addedDetailGroups = new List<PreferencesGroup>();
        void RefreshDetailGroups()
        {
            foreach (var g in addedDetailGroups)
                bbsPage.Remove(g);
            addedDetailGroups.Clear();

            foreach (var id in currentOrder)
            {
                if (detailGroups.TryGetValue(id, out var g))
                {
                    bbsPage.Add(g);
                    addedDetailGroups.Add(g);
                }
            }
            bbsPage.Add(securityGroup);
            addedDetailGroups.Add(securityGroup);
        }

        var renderedRows = new List<ActionRow>();
        void RebuildVisibilityRows()
        {
            foreach (var row in renderedRows)
            {
                visibilityGroup.Remove(row);
            }
            renderedRows.Clear();

            for (int i = 0; i < currentOrder.Count; i++)
            {
                var index = i;
                var providerId = currentOrder[i];
                if (!boardMetas.TryGetValue(providerId, out var meta)) continue;

                var row = ActionRow.New();
                row.SetTitle(meta.Title);
                row.SetSubtitle(meta.Subtitle);

                var btnBox = Box.New(Orientation.Horizontal, 4);
                btnBox.SetValign(Align.Center);

                var upBtn = Button.NewFromIconName("go-up-symbolic");
                upBtn.AddCssClass("flat");
                upBtn.SetTooltipText("上へ移動");
                upBtn.SetSensitive(index > 0);
                upBtn.OnClicked += (s, e) =>
                {
                    if (index > 0)
                    {
                        var target = index - 1;
                        var item = currentOrder[index];
                        currentOrder.RemoveAt(index);
                        currentOrder.Insert(target, item);

                        var currentCfg = configStorage.Load();
                        currentCfg = currentCfg with { BoardOrder = currentOrder.ToArray() };
                        configStorage.Save(currentCfg);
                        onConfigChanged?.Invoke();

                        RebuildVisibilityRows();
                        RefreshDetailGroups();
                    }
                };
                btnBox.Append(upBtn);

                var downBtn = Button.NewFromIconName("go-down-symbolic");
                downBtn.AddCssClass("flat");
                downBtn.SetTooltipText("下へ移動");
                downBtn.SetSensitive(index < currentOrder.Count - 1);
                downBtn.OnClicked += (s, e) =>
                {
                    if (index < currentOrder.Count - 1)
                    {
                        var target = index + 1;
                        var item = currentOrder[index];
                        currentOrder.RemoveAt(index);
                        currentOrder.Insert(target, item);

                        var currentCfg = configStorage.Load();
                        currentCfg = currentCfg with { BoardOrder = currentOrder.ToArray() };
                        configStorage.Save(currentCfg);
                        onConfigChanged?.Invoke();

                        RebuildVisibilityRows();
                        RefreshDetailGroups();
                    }
                };
                btnBox.Append(downBtn);

                row.AddSuffix(btnBox);

                var visSwitch = Switch.New();
                visSwitch.SetValign(Align.Center);
                visSwitch.SetTooltipText("板一覧での表示 / 非表示");
                var activeCfg = configStorage.Load();
                visSwitch.SetActive(activeCfg.IsProviderVisible(providerId));
                visSwitch.OnNotify += (s, e) =>
                {
                    if (e.Pspec.GetName() == "active")
                    {
                        var currentCfg = configStorage.Load();
                        currentCfg = currentCfg.WithProviderVisibility(providerId, visSwitch.GetActive());
                        configStorage.Save(currentCfg);
                        onConfigChanged?.Invoke();
                    }
                };
                row.AddSuffix(visSwitch);

                visibilityGroup.Add(row);
                renderedRows.Add(row);
            }
        }

        RebuildVisibilityRows();
        RefreshDetailGroups();

        // ページ 3: 外観とフォント
        var appearancePage = PreferencesPage.New();
        appearancePage.SetTitle("外観・フォント");
        appearancePage.SetIconName("preferences-desktop-font-symbolic");

        var fontGroup = PreferencesGroup.New();
        fontGroup.SetTitle("フォント設定");
        var fontRow = ActionRow.New();
        fontRow.SetTitle("テキストフォント");
        fontRow.SetSubtitle("Noto Sans CJK JP / IPA モナー");
        fontGroup.Add(fontRow);

        var emojiRow = ActionRow.New();
        emojiRow.SetTitle("カラー絵文字");
        emojiRow.SetSubtitle("Noto Color Emoji (COLRv1)");
        fontGroup.Add(emojiRow);

        appearancePage.Add(fontGroup);

        window.Add(generalPage);
        window.Add(bbsPage);
        window.Add(appearancePage);

        return window;
    }
}
