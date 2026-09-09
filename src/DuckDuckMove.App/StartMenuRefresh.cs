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
                ? "已刷新开始菜单。"
                : "下次打开开始菜单时查看头像。";
        }
        catch (Exception ex)
        {
            // A refresh failure must never be reported as an avatar write failure.
            return "开始菜单刷新未完成（" + ex.Message + "）。头像已保存，可注销后查看。";
        }
    }
}
