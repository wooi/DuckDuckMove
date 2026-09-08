namespace DuckDuckMove.Core;

public sealed record PictureValue(string Path, int Kind = 1);
public sealed record AvatarSnapshot(bool KeyExisted, Dictionary<string, PictureValue> Values);

public interface IAvatarBackend
{
    AvatarSnapshot Capture();
    void Write(AvatarSnapshot state);
}

public interface IBackupStore
{
    bool HasOriginal { get; }
    void SaveOriginal(AvatarSnapshot snapshot);
    AvatarSnapshot LoadOriginal();
    AvatarSnapshot? ReadPending();
    void SavePending(AvatarSnapshot snapshot);
    void ClearPending();
}

/// <summary>All mutations have a durable undo journal and a write/read verification.</summary>
public sealed class AvatarEngine(IAvatarBackend backend, IBackupStore store)
{
    public static readonly int[] Sizes = [32, 40, 48, 96, 192, 240, 448, 1080];
    public static bool IsPictureName(string name) => name.StartsWith("Image", StringComparison.Ordinal)
        && name.Length is > 5 and <= 10 && name.AsSpan(5).ToString().All(char.IsAsciiDigit);

    public void RecoverPending()
    {
        if (store.ReadPending() is not { } previous) return;
        backend.Write(previous);
        Verify(previous, backend.Capture());
        store.ClearPending();
    }

    public void Apply(string path) => Change(current => new(true,
        Names(current).ToDictionary(n => n, _ => new PictureValue(path))));

    public void ResetDefault(Func<int, string> defaultPath) => Change(current => new(true,
        Names(current).ToDictionary(n => n, n => new PictureValue(defaultPath(int.Parse(n[5..]))))));

    public void RestoreOriginal()
    {
        if (!store.HasOriginal) throw new InvalidOperationException("还没有原头像备份。");
        Change(_ => store.LoadOriginal());
    }

    private static IEnumerable<string> Names(AvatarSnapshot snapshot) => snapshot.Values.Count > 0
        ? snapshot.Values.Keys : Sizes.Select(size => "Image" + size);

    private void Change(Func<AvatarSnapshot, AvatarSnapshot> create)
    {
        RecoverPending();
        var before = backend.Capture();
        var desired = create(before);
        if (!store.HasOriginal) store.SaveOriginal(before);
        store.SavePending(before);
        try
        {
            backend.Write(desired);
            Verify(desired, backend.Capture());
            store.ClearPending();
        }
        catch (Exception failure)
        {
            try
            {
                backend.Write(before);
                Verify(before, backend.Capture());
                store.ClearPending();
            }
            catch (Exception rollback)
            {
                throw new AggregateException("设置失败，回滚也未完成；恢复记录已保留，请再次运行以恢复。", failure, rollback);
            }
            throw new InvalidOperationException("设置失败，已恢复操作前的头像配置。", failure);
        }
    }

    private static void Verify(AvatarSnapshot expected, AvatarSnapshot actual)
    {
        if (expected.Values.Count != actual.Values.Count || expected.Values.Any(p =>
            !actual.Values.TryGetValue(p.Key, out var value) || value != p.Value))
            throw new IOException("头像配置读回校验失败。");
    }
}
