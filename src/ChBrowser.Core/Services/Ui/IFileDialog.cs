using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChBrowser.Services.Ui;

/// <summary>ファイルフィルタの定義。</summary>
public sealed record FileDialogFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>
/// ファイルダイアログの抽象。
/// WPF の OpenFileDialog/SaveFileDialog、GTK4 の Gtk.FileDialog / XDG Desktop Portal を抽象化する。
/// </summary>
public interface IFileDialog
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? defaultPath = null);
    Task<string?> SaveFileAsync(string title, string defaultFileName, IReadOnlyList<FileDialogFilter>? filters = null, string? defaultPath = null);
    Task<string?> SelectFolderAsync(string title, string? defaultPath = null);
}
