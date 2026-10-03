using System.Threading;
using System.Threading.Tasks;

namespace ChBrowser.Services.Browser;

/// <summary>掲示板のページの中で JS を実行する口。掲示板のページが自分で送る要求
/// (ふたばの そうだね 等) を、同じページ・同じ Cookie から送るのに使う。</summary>
public interface IBrowserPageRunner
{
    /// <summary><paramref name="pageUrl"/> を開いて (既に開いていればそのまま)、<paramref name="script"/> を実行し、その戻り値 (文字列) を返す。
    /// スクリプトは同期的に文字列を返す式にすること (<c>ExecuteScriptAsync</c> は Promise を待てない)。</summary>
    Task<string> RunAsync(string pageUrl, string script, CancellationToken ct);
}
