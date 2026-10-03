using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Adw;
using ChBrowser.Services.Logging;
using Gdk;
using Gtk;

namespace ChBrowser.GtkApp;

public sealed class ImageViewerWindow
{
    private readonly Adw.Window _window;
    private readonly Picture _picture;
    private readonly Gtk.Spinner _spinner;
    private readonly Label _statusLabel;
    private readonly string _url;
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
        }
    };

    public ImageViewerWindow(Gtk.Window? parent, string url)
    {
        _url = url;
        _window = Adw.Window.New();
        _window.SetTitle("画像プレビュー - ChBrowser");
        _window.SetDefaultSize(800, 600);
        if (parent != null)
        {
            _window.SetTransientFor(parent);
            _window.SetModal(false);
        }

        var mainBox = Box.New(Orientation.Vertical, 0);

        // ヘッダーバー
        var headerBar = Adw.HeaderBar.New();

        var openBrowserBtn = Button.NewWithLabel("ブラウザで開く");
        openBrowserBtn.AddCssClass("flat");
        openBrowserBtn.OnClicked += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _url, UseShellExecute = true });
            }
            catch
            {
                try { Process.Start("xdg-open", _url); } catch { }
            }
        };
        headerBar.PackEnd(openBrowserBtn);

        var copyUrlBtn = Button.NewWithLabel("URLコピー");
        copyUrlBtn.AddCssClass("flat");
        copyUrlBtn.OnClicked += (s, e) =>
        {
            var clipboard = _window.GetClipboard();
            clipboard?.SetText(_url);
        };
        headerBar.PackEnd(copyUrlBtn);

        mainBox.Append(headerBar);

        // 画像表示領域 (Overlay)
        var overlay = Overlay.New();
        overlay.SetVexpand(true);
        overlay.SetHexpand(true);

        _picture = Picture.New();
        _picture.SetCanShrink(true);
        _picture.SetKeepAspectRatio(true);
        _picture.SetVexpand(true);
        _picture.SetHexpand(true);
        overlay.SetChild(_picture);

        var statusBox = Box.New(Orientation.Vertical, 8);
        statusBox.SetHalign(Align.Center);
        statusBox.SetValign(Align.Center);

        _spinner = Gtk.Spinner.New();
        _spinner.SetSizeRequest(32, 32);
        _spinner.Start();
        statusBox.Append(_spinner);

        _statusLabel = Label.New("画像を読み込み中...");
        _statusLabel.AddCssClass("dim-label");
        statusBox.Append(_statusLabel);

        overlay.AddOverlay(statusBox);

        mainBox.Append(overlay);
        _window.SetContent(mainBox);

        _ = LoadImageAsync(statusBox);
    }

    public void Present()
    {
        _window.Present();
    }

    private async Task LoadImageAsync(Box statusBox)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url);
            using var res = await _httpClient.SendAsync(req);
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync();

            using var glibBytes = GLib.Bytes.New(bytes);
            var texture = Texture.NewFromBytes(glibBytes);
            _picture.SetPaintable(texture);

            _spinner.Stop();
            statusBox.SetVisible(false);
        }
        catch (Exception ex)
        {
            LogService.Instance.Write($"[ImageViewer] Load failed for {_url}: {ex.Message}");
            _spinner.Stop();
            _spinner.SetVisible(false);
            _statusLabel.SetText($"画像の読み込みに失敗しました\n{ex.Message}");
        }
    }
}
