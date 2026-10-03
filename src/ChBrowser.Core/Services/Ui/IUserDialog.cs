using System.Threading.Tasks;

namespace ChBrowser.Services.Ui;

/// <summary>
/// ユーザー向けダイアログ表示の抽象。
/// WPF の MessageBox、GTK4/libadwaita の Adw.AlertDialog を抽象化する。
/// </summary>
public interface IUserDialog
{
    Task ShowInfoAsync(string title, string message);
    Task ShowWarningAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");
}
