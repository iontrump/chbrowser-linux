using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChBrowser.Models;
using ChBrowser.Services.Api;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Storage;
using Gtk;

namespace ChBrowser.GtkApp;

public sealed class PostDialog
{
    private readonly Window _window;
    private readonly PostClient _postClient;
    private readonly ConfigStorage _configStorage;
    private readonly Board _board;
    private readonly string? _threadKey;
    private readonly bool _isNewThread;

    private readonly Entry? _titleEntry;
    private readonly Entry _nameEntry;
    private readonly Entry _mailEntry;
    private readonly TextView _bodyView;
    private readonly Button _submitBtn;
    private readonly Button _cancelBtn;
    private readonly Label _statusLabel;

    public event Action<PostRequest>? PostSucceeded;

    public PostDialog(Window parent, PostClient postClient, ConfigStorage configStorage, Board board, string? threadKey = null, string? threadTitle = null)
    {
        _postClient = postClient;
        _configStorage = configStorage;
        _board = board;
        _threadKey = threadKey;
        _isNewThread = string.IsNullOrEmpty(threadKey);

        _window = Window.New();
        _window.SetTitle(_isNewThread ? $"新規スレッド作成 - {board.BoardName}" : $"書き込み - {threadTitle ?? threadKey}");
        _window.SetTransientFor(parent);
        _window.SetModal(true);
        _window.SetDefaultSize(560, 420);

        var mainBox = Box.New(Orientation.Vertical, 12);
        mainBox.SetMarginStart(18);
        mainBox.SetMarginEnd(18);
        mainBox.SetMarginTop(18);
        mainBox.SetMarginBottom(18);

        // ヘッダー情報
        var infoBox = Box.New(Orientation.Horizontal, 8);
        var targetLabel = Label.New(_isNewThread ? $"板: {board.BoardName} ({board.DirectoryName})" : $"スレッド: {threadTitle ?? threadKey}");
        targetLabel.AddCssClass("title-4");
        targetLabel.SetHalign(Align.Start);
        infoBox.Append(targetLabel);
        mainBox.Append(infoBox);

        // スレタイ入力 (新規スレのみ)
        if (_isNewThread)
        {
            var titleBox = Box.New(Orientation.Vertical, 4);
            var titleLabel = Label.New("スレッドタイトル:");
            titleLabel.SetHalign(Align.Start);
            titleBox.Append(titleLabel);

            _titleEntry = Entry.New();
            _titleEntry.SetInputPurpose(InputPurpose.FreeForm);
            _titleEntry.SetPlaceholderText("スレッドのタイトルを入力してください");
            titleBox.Append(_titleEntry);
            mainBox.Append(titleBox);
        }

        // 名前・メール入力 (横並び)
        var metaBox = Box.New(Orientation.Horizontal, 12);

        var provider = BbsRegistry.ResolveOrDefault(board.Host);
        var providerId = provider.Id;
        var config = _configStorage.Load();

        var nameBox = Box.New(Orientation.Vertical, 4);
        nameBox.SetHexpand(true);
        var nameLabel = Label.New("お名前:");
        nameLabel.SetHalign(Align.Start);
        nameBox.Append(nameLabel);
        _nameEntry = Entry.New();
        _nameEntry.SetInputPurpose(InputPurpose.FreeForm);
        _nameEntry.SetPlaceholderText("省略時: 名無し");
        if (!string.IsNullOrEmpty(config.LastPostName))
        {
            _nameEntry.SetText(config.LastPostName);
        }
        nameBox.Append(_nameEntry);
        metaBox.Append(nameBox);

        var mailBox = Box.New(Orientation.Vertical, 4);
        mailBox.SetHexpand(true);
        var mailLabel = Label.New("E-mail:");
        mailLabel.SetHalign(Align.Start);
        mailBox.Append(mailLabel);
        _mailEntry = Entry.New();
        _mailEntry.SetInputPurpose(InputPurpose.FreeForm);

        // E-mail 欄の初期値復元 (エッヂの認証トークン連携を含む)
        string initialMail = "";
        if (config.PostMails != null && config.PostMails.TryGetValue(providerId, out var savedMail) && !string.IsNullOrEmpty(savedMail))
        {
            if (savedMail == "sage")
            {
                initialMail = "";
            }
            else if (savedMail.StartsWith("sage#"))
            {
                initialMail = savedMail["sage".Length..];
            }
            else
            {
                initialMail = savedMail;
            }
        }
        else if (providerId == "eddi" && config.PostAuthTokens != null && config.PostAuthTokens.TryGetValue("eddi", out var eddiToken) && !string.IsNullOrWhiteSpace(eddiToken))
        {
            initialMail = $"#{eddiToken.Trim()}";
        }
        _mailEntry.SetText(initialMail);
        mailBox.Append(_mailEntry);
        metaBox.Append(mailBox);

        mainBox.Append(metaBox);

        // 本文入力
        var bodyBox = Box.New(Orientation.Vertical, 4);
        bodyBox.SetVexpand(true);
        var bodyLabel = Label.New("本文:");
        bodyLabel.SetHalign(Align.Start);
        bodyBox.Append(bodyLabel);

        var scroll = ScrolledWindow.New();
        scroll.SetVexpand(true);
        scroll.SetHexpand(true);
        scroll.SetMinContentHeight(160);

        _bodyView = TextView.New();
        _bodyView.SetInputPurpose(InputPurpose.FreeForm);
        _bodyView.SetWrapMode(WrapMode.WordChar);
        scroll.SetChild(_bodyView);
        bodyBox.Append(scroll);
        mainBox.Append(bodyBox);

        // ステータス表示
        _statusLabel = Label.New("");
        _statusLabel.SetWrap(true);
        _statusLabel.SetHalign(Align.Start);
        mainBox.Append(_statusLabel);

        // ボタンボックス
        var buttonBox = Box.New(Orientation.Horizontal, 12);
        buttonBox.SetHalign(Align.End);

        _cancelBtn = Button.NewWithLabel("キャンセル");
        _cancelBtn.OnClicked += (s, e) => _window.Close();
        buttonBox.Append(_cancelBtn);

        _submitBtn = Button.NewWithLabel(_isNewThread ? "新規スレッド作成" : "書き込む");
        _submitBtn.AddCssClass("suggested-action");
        _submitBtn.OnClicked += async (s, e) => await OnSubmitClickedAsync();
        buttonBox.Append(_submitBtn);

        mainBox.Append(buttonBox);
        _window.SetChild(mainBox);
    }

    public void Present()
    {
        _window.Present();
        if (_isNewThread && _titleEntry != null)
        {
            _window.SetFocus(_titleEntry);
            _titleEntry.GrabFocus();
        }
        else
        {
            _window.SetFocus(_bodyView);
            _bodyView.GrabFocus();
        }
    }

    private async Task OnSubmitClickedAsync()
    {
        var subject = _titleEntry?.GetText()?.Trim();
        if (_isNewThread && string.IsNullOrEmpty(subject))
        {
            _statusLabel.SetText("エラー: スレッドタイトルを入力してください。");
            return;
        }

        var buffer = _bodyView.GetBuffer();
        buffer.GetBounds(out var start, out var end);
        var message = buffer.GetText(start, end, false)?.Trim() ?? "";

        if (string.IsNullOrEmpty(message))
        {
            _statusLabel.SetText("エラー: 本文を入力してください。");
            return;
        }

        var name = _nameEntry.GetText()?.Trim() ?? "";
        var mail = _mailEntry.GetText()?.Trim() ?? "";

        // 設定の永続化保存 (E-mail 欄・エッヂ認証トークン・名前)
        SavePostConfig(name, mail);

        var provider = BbsRegistry.ResolveOrDefault(_board.Host);
        string? authToken = null;
        if (provider.Id == "eddi")
        {
            var hashIdx = mail.IndexOf('#');
            if (hashIdx >= 0)
            {
                authToken = mail.Substring(hashIdx + 1).Trim();
            }
            else
            {
                var cfg = _configStorage.Load();
                if (cfg.PostAuthTokens != null && cfg.PostAuthTokens.TryGetValue("eddi", out var t) && !string.IsNullOrWhiteSpace(t))
                {
                    authToken = t.Trim();
                }
            }
        }

        var req = new PostRequest(_board, _threadKey, subject, name, mail, message, AuthToken: authToken);

        await ExecutePostAsync(req);
    }

    private void SavePostConfig(string name, string mail)
    {
        try
        {
            var providerId = BbsRegistry.ResolveOrDefault(_board.Host).Id;
            var config = _configStorage.Load();

            var postMails = config.PostMails != null
                ? new Dictionary<string, string>(config.PostMails)
                : new Dictionary<string, string>();
            postMails[providerId] = mail;

            var postAuthTokens = config.PostAuthTokens != null
                ? new Dictionary<string, string>(config.PostAuthTokens)
                : new Dictionary<string, string>();

            // エッヂの場合、メール欄に '#' があればその右側を認証トークンとして PostAuthTokens にも保存
            if (providerId == "eddi")
            {
                var hashIdx = mail.IndexOf('#');
                if (hashIdx >= 0)
                {
                    var token = mail.Substring(hashIdx + 1).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        postAuthTokens["eddi"] = token;
                    }
                }
            }

            config = config with
            {
                PostMails = postMails,
                PostAuthTokens = postAuthTokens,
                LastPostName = name
            };
            _configStorage.Save(config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PostDialog] Failed to save post config: {ex.Message}");
        }
    }

    private async Task ExecutePostAsync(PostRequest req)
    {
        SetBusy(true);
        _statusLabel.SetText("書き込み中...");

        try
        {
            var result = await _postClient.PostAsync(req);

            if (result.Outcome == PostOutcome.Success)
            {
                _statusLabel.SetText("書き込みが完了しました！");
                PostSucceeded?.Invoke(req);
                await Task.Delay(1000);
                _window.Close();
                return;
            }

            if (result.Outcome == PostOutcome.AuthRequired)
            {
                _statusLabel.SetText("認証が必要です。認証ダイアログを表示します...");
                SetBusy(false);

                var authDialog = new AuthDialog(_window, _postClient, result.AuthCode ?? "", result.AuthUrl);
                authDialog.Authenticated += async () =>
                {
                    _statusLabel.SetText("認証が完了しました。再投稿中...");
                    await ExecutePostAsync(req);
                };
                authDialog.Present();
                return;
            }

            // エラー表示
            var errorMsg = string.IsNullOrWhiteSpace(result.Message)
                ? (string.IsNullOrWhiteSpace(result.RawHtmlSnippet) ? "書き込みに失敗しました。" : result.RawHtmlSnippet)
                : result.Message;

            _statusLabel.SetText($"書き込み失敗 ({result.Outcome}): {errorMsg}");
            SetBusy(false);
        }
        catch (Exception ex)
        {
            _statusLabel.SetText($"エラー: {ex.Message}");
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _submitBtn.SetSensitive(!isBusy);
        _cancelBtn.SetSensitive(!isBusy);
        _nameEntry.SetSensitive(!isBusy);
        _mailEntry.SetSensitive(!isBusy);
        _bodyView.SetSensitive(!isBusy);
        if (_titleEntry != null) _titleEntry.SetSensitive(!isBusy);
    }
}
