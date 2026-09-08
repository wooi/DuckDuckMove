using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using DuckDuckMove.Core;
using Microsoft.Win32;

namespace DuckDuckMove.App;

internal static class Storage
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DuckDuckMove");
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string At(params string[] components)
    {
        var path = Root;
        foreach (var component in components)
        {
            if (component.Length == 0 || component is "." or ".." || component.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new IOException("无效的存储路径。");
            path = Path.Combine(path, component);
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("存储目录不能包含符号链接或重解析点。");
        }
        return path;
    }
    internal static void EnsureRoot()
    {
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var info = new DirectoryInfo(Root);
        if (info.Exists)
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("程序数据目录不可为链接。");
            var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (!admin.Equals(owner) && !system.Equals(owner)) throw new IOException("程序数据目录的所有者不可信，请检查 " + Root);
        }
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(admin);
        foreach (var sid in new[] { admin, system }) acl.AddAccessRule(new(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        // Suppress implicit WRITE_DAC for creator-owned children after the admin token is filtered by UAC.
        acl.AddAccessRule(new(new SecurityIdentifier("S-1-3-4"), FileSystemRights.ReadPermissions,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (info.Exists) info.SetAccessControl(acl); else info.Create(acl);
    }
    internal static void CopyNew(string source, string destination)
    {
        // Create a new file with the protected parent's ACL. CopyFile can copy source security metadata.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output); output.Flush(true);
    }
    internal static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json);
            stream.Flush(true);
        }
        File.Move(tmp, path, true);
    }
    internal static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new IOException("备份文件为空。");
    internal static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}

internal sealed class WindowsAvatar(string sid) : IAvatarBackend
{
    private readonly string subkey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + new SecurityIdentifier(sid).Value;
    public AvatarSnapshot Capture()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(subkey);
        var values = new Dictionary<string, PictureValue>();
        if (key != null) foreach (var name in key.GetValueNames().Where(AvatarEngine.IsPictureName))
        {
            if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path)
                throw new IOException("头像注册表值类型异常：" + name);
            values.Add(name, new(path, (int)key.GetValueKind(name)));
        }
        return new(key != null, values);
    }
    public void Write(AvatarSnapshot state)
    {
        if (state.Values.Any(p => !AvatarEngine.IsPictureName(p.Key) || p.Value.Kind is not (1 or 2)))
            throw new IOException("拒绝写入非头像配置。");
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        // Open existing key first: restoring an absent key must not create one.
        using (var key = hive.OpenSubKey(subkey, true) ?? (state.KeyExisted || state.Values.Count != 0 ? hive.CreateSubKey(subkey) : null))
        {
            if (key == null) return;
            foreach (var name in key.GetValueNames().Where(AvatarEngine.IsPictureName))
                if (!state.Values.ContainsKey(name)) key.DeleteValue(name, false);
            foreach (var (name, value) in state.Values) key.SetValue(name, value.Path, (RegistryValueKind)value.Kind);
            key.Flush();
        }
        if (!state.KeyExisted)
        {
            using var check = hive.OpenSubKey(subkey);
            bool empty = check is { ValueCount: 0, SubKeyCount: 0 };
            check?.Dispose();
            if (empty) hive.DeleteSubKey(subkey, false);
        }
    }
    internal static bool ForcedDefault()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer");
        return key?.GetValue("UseDefaultTile") is int value && value != 0;
    }
    internal static string DefaultPath(int size)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures");
        foreach (var name in new[] { $"user-{size}.png", "user.png", "user-192.png", "user.bmp" })
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("没有找到 Windows 默认头像文件。");
    }
    internal static string ExpandForUser(string sid, string path)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var profileKey = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
        if (profileKey?.GetValue("ProfileImagePath") is string profile)
        {
            profile = Environment.ExpandEnvironmentVariables(profile);
            foreach (var (name, value) in new[] { ("USERPROFILE", profile), ("LOCALAPPDATA", Path.Combine(profile, "AppData", "Local")), ("APPDATA", Path.Combine(profile, "AppData", "Roaming")) })
                path = path.Replace("%" + name + "%", value, StringComparison.OrdinalIgnoreCase);
        }
        return Environment.ExpandEnvironmentVariables(path);
    }
}

internal sealed record SavedFile(string BackupPath, string Sha256);
internal sealed record BackupDocument(AvatarSnapshot Snapshot, Dictionary<string, SavedFile> Files);
internal sealed class BackupStore : IBackupStore
{
    private readonly string directory;
    private readonly string accountSid;
    private string Original => Path.Combine(directory, "original.json");
    private string Pending => Path.Combine(directory, "pending.json");
    public BackupStore(string sid)
    {
        accountSid = sid;
        directory = Storage.At("Accounts", new SecurityIdentifier(sid).Value);
        Directory.CreateDirectory(directory);
    }
    public bool HasOriginal => File.Exists(Original);
    public void SaveOriginal(AvatarSnapshot snapshot)
    {
        if (HasOriginal) return;
        var files = new Dictionary<string, SavedFile>();
        foreach (var value in snapshot.Values.Values)
        {
            var source = WindowsAvatar.ExpandForUser(accountSid, value.Path);
            if (files.ContainsKey(value.Path) || !File.Exists(source)) continue;
            if (new FileInfo(source).Length > GifInfo.MaxBytes) throw new IOException("原头像文件过大，无法完成备份。");
            var destination = Path.Combine(directory, "original-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            Storage.CopyNew(source, destination);
            files.Add(value.Path, new(destination, Storage.Hash(destination)));
        }
        Storage.Save(Original, new BackupDocument(snapshot, files));
    }
    public AvatarSnapshot LoadOriginal()
    {
        var document = Storage.Read<BackupDocument>(Original);
        var values = new Dictionary<string, PictureValue>(document.Snapshot.Values);
        foreach (var (name, value) in values.ToArray())
        {
            if (!document.Files.TryGetValue(value.Path, out var file)) continue;
            if (!File.Exists(file.BackupPath) || Storage.Hash(file.BackupPath) != file.Sha256) throw new IOException("原头像备份文件损坏。");
            var originalPath = WindowsAvatar.ExpandForUser(accountSid, value.Path);
            // Keep exact original registry data when intact; fall back to the durable copy otherwise.
            if (!File.Exists(originalPath) || Storage.Hash(originalPath) != file.Sha256)
                values[name] = new(file.BackupPath);
        }
        return new(document.Snapshot.KeyExisted, values);
    }
    public AvatarSnapshot? ReadPending() => File.Exists(Pending) ? Storage.Read<AvatarSnapshot>(Pending) : null;
    public void SavePending(AvatarSnapshot snapshot) => Storage.Save(Pending, snapshot);
    public void ClearPending() => File.Delete(Pending);
}
