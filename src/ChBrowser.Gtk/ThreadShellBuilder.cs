using ChBrowser.Services.Fonts;
using ChBrowser.Services.Render;
using ChBrowser.Services.Theme;

namespace ChBrowser.GtkApp;

public static class ThreadShellBuilder
{
    private static string? _shellHtmlCache;
    private static readonly object ShellLock = new();

    public static string BuildShell()
    {
        if (_shellHtmlCache is not null) return _shellHtmlCache;
        lock (ShellLock)
        {
            if (_shellHtmlCache is not null) return _shellHtmlCache;
            var html = EmbeddedAssets.Read("thread.html");
            var css = EmbeddedAssets.ReadCss("thread.css");
            var js = EmbeddedAssets.Read("thread.js");
            var bridge = EmbeddedAssets.Read("shortcut-bridge.js");

            var theme = ThemeService.CurrentInstance?.LoadActiveTheme();
            var postTemplate = theme?.PostHtmlTemplate ?? EmbeddedAssets.Read("post.html");
            var postCss = theme?.PostCss ?? EmbeddedAssets.Read("post.css");

            var emojiCss = EmojiFontService.BuildBodyFontCssOrNull("'Noto Sans CJK JP', 'IPA Pゴシック', 'MS Pゴシック', sans-serif", "monospace") ?? "";

            _shellHtmlCache = html
                .Replace("/*{{CSS}}*/", css + "\n" + postCss + emojiCss)
                .Replace("/*{{SHORTCUT_BRIDGE}}*/", bridge)
                .Replace("/*{{JS}}*/", js)
                .Replace("<!--{{POST_TEMPLATE}}-->", postTemplate);
            return _shellHtmlCache;
        }
    }

    public static void InvalidateCache()
    {
        lock (ShellLock) _shellHtmlCache = null;
    }
}
