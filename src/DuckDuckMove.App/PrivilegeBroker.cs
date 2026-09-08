using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using DuckDuckMove.Core;
using Microsoft.Win32.SafeHandles;

namespace DuckDuckMove.App;

internal sealed record OperationRequest(string Id, string Sid, string Action, string? ImagePath);
internal sealed record OperationResult(bool Success, string Message);

internal static class PrivilegeBroker
{
    internal static string ResultPath(string id) => Storage.At("Operations", Guid.ParseExact(id, "N").ToString("N") + ".result.json");
    internal static string RequestPath(string id) => Storage.At("Operations", Guid.ParseExact(id, "N").ToString("N") + ".request.json");
    internal static int Run(string[] args)
    {
        if (args.Length is < 5 or > 6) return 2;
        string id;
        try { id = Guid.ParseExact(args[1], "N").ToString("N"); } catch { return 2; }
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        try
        {
            Storage.EnsureRoot();
            Directory.CreateDirectory(Storage.At("Operations"));
            string action = args[2];
            if (action is not ("apply" or "default" or "restore" or "recover")) throw new InvalidOperationException("不支持的操作。");
            using var origin = Process.GetProcessById(int.Parse(args[3]));
            if (origin.StartTime.ToUniversalTime().Ticks != long.Parse(args[4]) || origin.SessionId != Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("原窗口已关闭或会话不匹配。");
            if (!string.Equals(origin.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("操作必须从 DuckDuckMove 窗口发起。");
            if (!OpenProcessToken(origin.Handle, 8, out var token)) throw new Win32Exception();
            string sid;
            using (token) using (var user = new WindowsIdentity(token.DangerousGetHandle()))
                sid = user.User?.Value ?? throw new InvalidOperationException("无法识别当前账户。");
            if (sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20") throw new InvalidOperationException("不能修改系统服务账户的头像。");
            Directory.CreateDirectory(Storage.At("Accounts", sid));
            // A protected, exclusive lock serializes operations for this account across all app instances.
            using var accountLock = new FileStream(Storage.At("Accounts", sid, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string? image = null;
            if (action == "apply")
            {
                if (args.Length != 6) throw new InvalidOperationException("尚未选择 GIF。");
                if (WindowsAvatar.ForcedDefault()) throw new InvalidOperationException("组织策略强制使用默认头像，无法应用动图。");
                var source = Path.GetFullPath(args[5]);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > GifInfo.MaxBytes) throw new InvalidDataException("GIF 超过 20 MB。");
                var bytes = new byte[checked((int)input.Length)];
                input.ReadExactly(bytes);
                GifInfo.Inspect(bytes);
                image = Storage.At("Accounts", sid, "avatar-" + id + ".gif");
                using var output = new FileStream(image, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(bytes); output.Flush(true);
            }
            // Publishing bundles all managed dependencies. Copy the running image into an admin-only directory
            // before SCM executes it as SYSTEM, so a user-writable download folder is never a service path.
            var executable = Environment.ProcessPath ?? throw new IOException("找不到程序文件。");
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "DuckDuckMove.dll")))
                throw new InvalidOperationException("系统操作需使用 dist 中发布的独立 EXE。");
            var binaryDirectory = Storage.At("Worker", Storage.Hash(executable));
            Directory.CreateDirectory(binaryDirectory);
            var worker = Path.Combine(binaryDirectory, "DuckDuckMove.exe");
            if (!File.Exists(worker)) Storage.CopyNew(executable, worker);
            if (Storage.Hash(worker) != Storage.Hash(executable)) throw new IOException("辅助程序完整性检查失败。");
            Storage.Save(RequestPath(id), new OperationRequest(id, sid, action, image));
            ExecuteService(worker, id);
            if (!File.Exists(ResultPath(id))) throw new IOException("辅助程序未返回结果；恢复记录已保留。");
            return Storage.Read<OperationResult>(ResultPath(id)).Success ? 0 : 1;
        }
        catch (Exception error)
        {
            try { Storage.Save(ResultPath(id), new OperationResult(false, Explain(error))); } catch { }
            return 1;
        }
        finally
        {
            try { File.Delete(RequestPath(id)); } catch { }
        }
    }
    internal static string Explain(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current != null; current = current.InnerException)
            if (!messages.Contains(current.Message)) messages.Add(current.Message);
        return string.Join("\n", messages);
    }
    private static void ExecuteService(string executable, string id)
    {
        string name = "DuckDuckMove_" + id;
        var manager = OpenSCManager(null, null, 0x0003);
        if (manager == IntPtr.Zero) throw new Win32Exception();
        IntPtr service = IntPtr.Zero;
        try
        {
            service = CreateService(manager, name, "DuckDuckMove temporary avatar operation", 0xF01FF,
                0x10, 3, 1, $"\"{executable}\" --service {id}", null, IntPtr.Zero, null, null, null);
            if (service == IntPtr.Zero) throw new Win32Exception();
            if (!StartService(service, 0, IntPtr.Zero)) throw new Win32Exception();
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(90))
            {
                if (!QueryServiceStatus(service, out var state)) throw new Win32Exception();
                if (state.CurrentState == 1) return;
                Thread.Sleep(150);
            }
            throw new System.TimeoutException("头像操作耗时过长。请稍后重新打开程序检查恢复状态。");
        }
        finally
        {
            // Mark for deletion even after a timeout; SCM removes the service after its process stops.
            if (service != IntPtr.Zero) { DeleteService(service); CloseServiceHandle(service); }
            CloseServiceHandle(manager);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateService(IntPtr manager, string name, string displayName, uint access, uint type, uint start, uint error, string binary, string? group, IntPtr tag, string? dependencies, string? user, string? password);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool StartService(IntPtr service, int count, IntPtr args);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}

internal sealed class AvatarService(string id) : ServiceBase
{
    protected override void OnStart(string[] args)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (!WindowsIdentity.GetCurrent().IsSystem) throw new InvalidOperationException("辅助服务必须以 SYSTEM 身份执行。");
                var request = Storage.Read<OperationRequest>(PrivilegeBroker.RequestPath(id));
                if (request.Id != id) throw new IOException("操作编号不匹配。");
                var sid = new SecurityIdentifier(request.Sid).Value;
                using var mutationLock = new FileStream(Storage.At("Accounts", sid, "mutation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var engine = new AvatarEngine(new WindowsAvatar(sid), new BackupStore(sid));
                switch (request.Action)
                {
                    case "apply":
                        if (WindowsAvatar.ForcedDefault()) throw new InvalidOperationException("组织策略强制使用默认头像。");
                        if (request.ImagePath != Storage.At("Accounts", sid, "avatar-" + id + ".gif")) throw new IOException("动图路径不合法。");
                        engine.Apply(request.ImagePath); break;
                    case "default": engine.ResetDefault(WindowsAvatar.DefaultPath); break;
                    case "restore": engine.RestoreOriginal(); break;
                    case "recover": engine.RecoverPending(); break;
                    default: throw new InvalidOperationException("不支持的头像操作。");
                }
                Storage.Save(PrivilegeBroker.ResultPath(id), new OperationResult(true, "头像配置已写入并核对。请在 Windows 界面查看实际效果；必要时手动锁屏或重新登录。"));
            }
            catch (Exception error)
            {
                try { Storage.Save(PrivilegeBroker.ResultPath(id), new OperationResult(false, PrivilegeBroker.Explain(error))); } catch { }
            }
            finally { Stop(); }
        });
    }
    internal void Run() { ServiceName = "DuckDuckMove_" + id; AutoLog = false; ServiceBase.Run(this); }
}
