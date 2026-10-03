using System;
using System.Threading.Tasks;
using ChBrowser.Services.Api;
using Gtk;

namespace ChBrowser.GtkApp;

public sealed class AuthDialog
{
    private readonly Window _window;
    private readonly PostClient _postClient;
    private readonly string _authCode;
    private readonly string? _authUrl;
    private readonly Label _statusLabel;
    private readonly Button _dummyAuthBtn;

    public event Action? Authenticated;

    public AuthDialog(Window parent, PostClient postClient, string authCode, string? authUrl)
    {
        _postClient = postClient;
        _authCode = authCode;
        _authUrl = authUrl;

        _window = Window.New();
        _window.SetTitle("書き込み認証 - ChBrowser");
        _window.SetTransientFor(parent);
        _window.SetModal(true);
        _window.SetDefaultSize(480, 280);

        var contentBox = Box.New(Orientation.Vertical, 16);
        contentBox.SetMarginStart(24);
        contentBox.SetMarginEnd(24);
        contentBox.SetMarginTop(24);
        contentBox.SetMarginBottom(24);

        var titleLabel = Label.New("書き込み認証が必要です");
        titleLabel.AddCssClass("title-2");
        contentBox.Append(titleLabel);

        var descLabel = Label.New("掲示板への書き込みには初回認証が必要です。\n以下の認証コードを確認の上、認証を完了してください。");
        descLabel.SetWrap(true);
        contentBox.Append(descLabel);

        // 認証コード表示ボックス
        var codeBox = Box.New(Orientation.Horizontal, 8);
        codeBox.SetHalign(Align.Center);
        var codePrefixLabel = Label.New("認証コード:");
        codePrefixLabel.AddCssClass("title-4");
        var codeValueLabel = Label.New(authCode);
        codeValueLabel.AddCssClass("title-3");
        codeValueLabel.SetSelectable(true);
        codeBox.Append(codePrefixLabel);
        codeBox.Append(codeValueLabel);
        contentBox.Append(codeBox);

        if (!string.IsNullOrEmpty(authUrl))
        {
            var urlLabel = Label.New($"認証URL: {authUrl}");
            urlLabel.SetWrap(true);
            urlLabel.SetSelectable(true);
            contentBox.Append(urlLabel);
        }

        _statusLabel = Label.New("");
        contentBox.Append(_statusLabel);

        // アクションボタンボックス
        var actionBox = Box.New(Orientation.Horizontal, 12);
        actionBox.SetHalign(Align.End);

        _dummyAuthBtn = Button.NewWithLabel("ダミー認証を実行 (ローカルテスト)");
        _dummyAuthBtn.AddCssClass("suggested-action");
        _dummyAuthBtn.OnClicked += async (s, e) => await OnDummyAuthClickedAsync();
        actionBox.Append(_dummyAuthBtn);

        var closeBtn = Button.NewWithLabel("閉じる");
        closeBtn.OnClicked += (s, e) => _window.Close();
        actionBox.Append(closeBtn);

        contentBox.Append(actionBox);
        _window.SetChild(contentBox);
    }

    public void Present() => _window.Present();

    private async Task OnDummyAuthClickedAsync()
    {
        _dummyAuthBtn.SetSensitive(false);
        _statusLabel.SetText("ダミー認証を送信中...");

        try
        {
            var success = await _postClient.SubmitDummyAuthAsync(_authCode, _authUrl);
            if (success)
            {
                _statusLabel.SetText("ダミー認証に成功しました！Cookie を保存しました。");
                Authenticated?.Invoke();
                await Task.Delay(1000);
                _window.Close();
            }
            else
            {
                _statusLabel.SetText("ダミー認証の送信に失敗しました。");
                _dummyAuthBtn.SetSensitive(true);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.SetText($"エラー: {ex.Message}");
            _dummyAuthBtn.SetSensitive(true);
        }
    }
}
