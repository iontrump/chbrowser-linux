using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WebKit;

namespace ChBrowser.GtkApp;

public sealed class WebKitBridge
{
    private readonly WebView _webView;
    public event Action<string, JsonElement>? MessageReceived;

    /// <summary>JS 側 (board-list.js / favorites.js / thread-list.js) は camelCase のプロパティ名を読むため、
    /// WPF 版 WebView2Helper.PostJsonOptions と同じく camelCase で送る。</summary>
    private static readonly JsonSerializerOptions PostJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public const string ShimScript = @"
<script>
(function() {
    if (!window.chrome) window.chrome = {};
    if (!window.chrome.webview) {
        var listeners = [];
        window.chrome.webview = {
            postMessage: function(msg) {
                if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.chbrowserBridge) {
                    window.webkit.messageHandlers.chbrowserBridge.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
                }
            },
            addEventListener: function(type, cb) {
                if (type === 'message') listeners.push(cb);
            },
            removeEventListener: function(type, cb) {
                if (type === 'message') {
                    var idx = listeners.indexOf(cb);
                    if (idx >= 0) listeners.splice(idx, 1);
                }
            },
            _dispatchMessage: function(data) {
                var evt = { data: data };
                listeners.slice().forEach(function(cb) {
                    try { cb(evt); } catch (e) { console.error(e); }
                });
            }
        };
    }
})();
</script>
";

    public WebKitBridge(WebView webView)
    {
        _webView = webView;
        var ucm = _webView.GetUserContentManager();
        ucm.RegisterScriptMessageHandler("chbrowserBridge", null);
        ucm.OnScriptMessageReceived += OnScriptMessage;

        var rawJs = @"
            (function() {
                if (!window.chrome) window.chrome = {};
                if (!window.chrome.webview) {
                    var listeners = [];
                    window.chrome.webview = {
                        postMessage: function(msg) {
                            if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.chbrowserBridge) {
                                window.webkit.messageHandlers.chbrowserBridge.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
                            }
                        },
                        addEventListener: function(type, cb) {
                            if (type === 'message') listeners.push(cb);
                        },
                        removeEventListener: function(type, cb) {
                            if (type === 'message') {
                                var idx = listeners.indexOf(cb);
                                if (idx >= 0) listeners.splice(idx, 1);
                            }
                        },
                        _dispatchMessage: function(data) {
                            var evt = { data: data };
                            listeners.slice().forEach(function(cb) {
                                try { cb(evt); } catch (e) { console.error(e); }
                            });
                        }
                    };
                }
            })();
        ";
        var script = UserScript.New(
            source: rawJs,
            injectedFrames: UserContentInjectedFrames.AllFrames,
            injectionTime: UserScriptInjectionTime.Start,
            allowList: null,
            blockList: null
        );
        ucm.AddScript(script);
    }

    public static string InjectShim(string html)
    {
        if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("<head>", "<head>" + ShimScript, StringComparison.OrdinalIgnoreCase);
        }
        return ShimScript + html;
    }

    private void OnScriptMessage(UserContentManager sender, UserContentManager.ScriptMessageReceivedSignalArgs args)
    {
        try
        {
            var jsonStr = args.Value.ToJson(0) ?? "";
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var innerStr = root.GetString() ?? "";
                if (innerStr.StartsWith("{"))
                {
                    using var innerDoc = JsonDocument.Parse(innerStr);
                    var type = innerDoc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "";
                    MessageReceived?.Invoke(type ?? "", innerDoc.RootElement.Clone());
                    return;
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";
                MessageReceived?.Invoke(type ?? "", root.Clone());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebKitBridge] Parse error: {ex.Message}");
        }
    }

    public async Task PostMessageAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, PostJsonOptions);
        var script = $@"
            (function() {{
                var msg = {json};
                if (window.chrome && window.chrome.webview && window.chrome.webview._dispatchMessage) {{
                    window.chrome.webview._dispatchMessage(msg);
                }}
                window.dispatchEvent(new MessageEvent('message', {{ data: msg }}));
            }})();
        ";
        await EvaluateAsync(script);
    }

    public async Task EvaluateAsync(string js)
    {
        await _webView.EvaluateJavascriptAsync(js);
    }
}
