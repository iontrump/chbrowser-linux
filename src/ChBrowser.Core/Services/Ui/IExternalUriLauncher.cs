using System;
using System.Threading.Tasks;

namespace ChBrowser.Services.Ui;

/// <summary>
/// 外部 URI / ファイル起動の抽象。
/// Windows の Process.Start(UseShellExecute=true)、Linux の xdg-open / XDG Desktop Portal OpenURI を抽象化する。
/// </summary>
public interface IExternalUriLauncher
{
    Task<bool> LaunchUriAsync(Uri uri);
    Task<bool> OpenDirectoryAsync(string directoryPath);
}
