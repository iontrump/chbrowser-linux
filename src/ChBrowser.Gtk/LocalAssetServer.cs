using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChBrowser.Services.Logging;
using ChBrowser.Services.Render;
using ChBrowser.Services.Storage;

namespace ChBrowser.GtkApp;

public sealed class LocalAssetServer : IDisposable
{
    private readonly DataPaths _paths;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    public string BaseUrl { get; }

    public string GetCacheUrl(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        return $"{BaseUrl}cache/{rel}";
    }

    public LocalAssetServer(DataPaths paths)
    {
        _paths = paths;
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    public void Start()
    {
        _listener.Start();
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Instance.Write($"[LocalAssetServer] error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;
            var path = req.Url?.AbsolutePath.TrimStart('/') ?? "";

            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = (int)HttpStatusCode.NoContent;
                resp.Close();
                return;
            }

            if (path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
            {
                var name = path["resources/".Length..];
                try
                {
                    var content = EmbeddedAssets.Read(name);
                    var bytes = Encoding.UTF8.GetBytes(content);
                    resp.ContentType = GetContentType(name);
                    resp.ContentLength64 = bytes.Length;
                    resp.OutputStream.Write(bytes, 0, bytes.Length);
                    resp.StatusCode = (int)HttpStatusCode.OK;
                }
                catch (FileNotFoundException)
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            else if (path.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase))
            {
                var fontFile = Path.Combine(_paths.FontsDir, path["fonts/".Length..]);
                if (File.Exists(fontFile))
                {
                    using var fs = File.OpenRead(fontFile);
                    resp.ContentType = "font/ttf";
                    resp.ContentLength64 = fs.Length;
                    fs.CopyTo(resp.OutputStream);
                    resp.StatusCode = (int)HttpStatusCode.OK;
                }
                else
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            else if (path.StartsWith("cache/", StringComparison.OrdinalIgnoreCase))
            {
                var relPath = path["cache/".Length..];
                var localFile = Path.Combine(_paths.MediaCacheBaseDir, relPath);
                if (File.Exists(localFile))
                {
                    using var fs = File.OpenRead(localFile);
                    resp.ContentType = GetContentType(localFile);
                    resp.ContentLength64 = fs.Length;
                    fs.CopyTo(resp.OutputStream);
                    resp.StatusCode = (int)HttpStatusCode.OK;
                }
                else
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            else
            {
                resp.StatusCode = (int)HttpStatusCode.NotFound;
            }

            resp.Close();
        }
        catch (Exception ex)
        {
            LogService.Instance.Write($"[LocalAssetServer] request error: {ex.Message}");
            try { context.Response.Abort(); } catch { }
        }
    }

    private static string GetContentType(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ttf" => "font/ttf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _cts.Dispose();
    }
}
