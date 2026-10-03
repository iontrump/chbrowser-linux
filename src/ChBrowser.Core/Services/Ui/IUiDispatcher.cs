using System;
using System.Threading.Tasks;

namespace ChBrowser.Services.Ui;

/// <summary>
/// UI スレッドへの処理マーシャリングを行うディスパッチャ抽象。
/// WPF の Dispatcher、GTK4 の GLib MainContext (GLib.Idle.Add 等) を抽象化する。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>現在実行中のスレッドが UI スレッドであるかを判定する。</summary>
    bool CheckAccess();

    /// <summary>UI スレッドへ非同期でアクションを投げる。</summary>
    void Post(Action action);

    /// <summary>UI スレッドでアクションを実行し、完了を待機する。</summary>
    Task InvokeAsync(Action action);

    /// <summary>UI スレッドでファンクションを実行し、戻り値を受け取る。</summary>
    Task<T> InvokeAsync<T>(Func<T> function);
}
