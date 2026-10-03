using System.Threading.Tasks;

namespace ChBrowser.Services.Ui;

/// <summary>
/// システムクリップボード操作の抽象。
/// WPF の Clipboard、GTK4 の Gdk.Clipboard (Portal 連携) を抽象化する。
/// </summary>
public interface IClipboard
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}
