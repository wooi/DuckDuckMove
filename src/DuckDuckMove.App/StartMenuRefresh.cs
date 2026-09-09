using System.Diagnostics;
using System.IO;

namespace DuckDuckMove.App;

internal static class StartMenuRefresh
{
    // Run only in the interactive UI process, never in the SYSTEM broker/service.
    internal static async Task<string> RunAsync()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SystemApps", "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy", "StartMenuExperienceHost.exe");
            int stopped = 0;
            foreach (var process in Process.GetProcessesByName("StartMenuExperienceHost"))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != current.SessionId) continue;
                        if (!string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) continue;
                        process.Kill(entireProcessTree: false);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await process.WaitForExitAsync(timeout.Token);
                        stopped++;
                    }
                    catch (InvalidOperationException) { /* Already exited. */ }
                }
            }
            return stopped > 0
                ? "已请求重新加载开始菜单，请按 Win 键查看底部头像。Microsoft 账户卡片不在支持范围内。"
                : "未找到可刷新的开始菜单进程，请打开开始菜单后重试。";
        }
        catch (Exception ex)
        {
            // A refresh failure must never be reported as an avatar write failure.
            return "开始菜单刷新未完成（" + ex.Message + "）。头像配置未撤销，可稍后手动重试。";
        }
    }
}
