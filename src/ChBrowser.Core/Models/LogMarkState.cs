namespace ChBrowser.Models;

/// <summary>各スレッドのログマーク状態。</summary>
public enum LogMarkState
{
    /// <summary>ログ無し。マーク非表示。</summary>
    None,
    /// <summary>ログあり、最後の取得時から件数変化なし。青丸。</summary>
    Cached,
    /// <summary>ログあり、subject.txt の方が件数多い (新着あり)。緑丸。</summary>
    Updated,
    /// <summary>お気に入り表示時、subject.txt にもう存在しないスレ (= 落ちた)。茶色丸。
    /// 通常の板表示 (subject.txt 由来の一覧) では出ない状態。</summary>
    Dropped,
    /// <summary>「自分の書き込み」へ誰かが返信したログがある。赤丸。
    /// idx.json の <c>HasReplyToOwn</c> が立っているスレッドに付く。<see cref="Dropped"/> 以外の
    /// すべての状態より優先される (= 新着の有無や読了済みかには依らない)。</summary>
    RepliedToOwn,
}
