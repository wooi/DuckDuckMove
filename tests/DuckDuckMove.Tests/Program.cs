using DuckDuckMove.Core;

int passed = 0;
void Test(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected failure"); }
AvatarSnapshot Original() => new(true, new() { ["Image32"] = new("before32.png"), ["Image192"] = new("%TEST%/before192.png", 2) });
(FakeBackend backend, FakeStore store, AvatarEngine engine) Setup()
{
    var backend = new FakeBackend { State = Original() }; var store = new FakeStore(); return (backend, store, new(backend, store));
}
Test("Apply changes only existing image sizes", () => { var (b, s, e) = Setup(); e.Apply("new.gif"); Check(b.State.Values.Count == 2 && b.State.Values.Values.All(v => v.Path == "new.gif") && s.HasOriginal && s.Pending == null); });
Test("Original backup survives multiple GIFs and default reset", () => { var (b, s, e) = Setup(); e.Apply("one.gif"); e.Apply("two.gif"); e.ResetDefault(size => $"default-{size}.png"); e.RestoreOriginal(); Check(b.State.Values["Image32"].Path == "before32.png" && b.State.Values["Image192"].Kind == 2 && s.SaveCount == 1); });
Test("Missing key is initialized and restored to absence", () => { var (b, s, e) = Setup(); b.State = new(false, []); e.Apply("new.gif"); Check(b.State.Values.Count == AvatarEngine.Sizes.Length); e.RestoreOriginal(); Check(!b.State.KeyExisted && b.State.Values.Count == 0); });
Test("Default reset can be the first operation", () => { var (b, s, e) = Setup(); e.ResetDefault(size => $"user-{size}.png"); Check(b.State.Values["Image32"].Path == "user-32.png"); e.RestoreOriginal(); Check(b.State.Values["Image32"].Path == "before32.png"); });
Test("Missing default image fails before any mutation", () => { var (b, s, e) = Setup(); Throws(() => e.ResetDefault(_ => throw new FileNotFoundException())); Check(b.Writes == 0 && !s.HasOriginal); });
Test("Partial registry failure rolls back", () => { var (b, s, e) = Setup(); b.FailWrites.Add(1); Throws(() => e.Apply("bad.gif")); Check(b.State.Values["Image32"].Path == "before32.png" && s.Pending == null && b.Writes == 2); });
Test("Rollback failure preserves durable recovery journal", () => { var (b, s, e) = Setup(); b.FailWrites.UnionWith([1, 2]); Throws(() => e.Apply("bad.gif")); Check(s.Pending != null); b.FailWrites.Clear(); e.RecoverPending(); Check(b.State.Values["Image32"].Path == "before32.png" && s.Pending == null); });
Test("Readback mismatch triggers rollback", () => { var (b, s, e) = Setup(); b.IgnoreWrite = 1; Throws(() => e.Apply("ignored.gif")); Check(b.Writes == 2 && s.Pending == null); });
Test("Backup failure prevents registry write", () => { var (b, s, e) = Setup(); s.FailBackup = true; Throws(() => e.Apply("x.gif")); Check(b.Writes == 0); });
Test("Journal failure prevents registry write", () => { var (b, s, e) = Setup(); s.FailJournal = true; Throws(() => e.Apply("x.gif")); Check(b.Writes == 0); });
Test("Unfinished operation recovered before next change", () => { var (b, s, e) = Setup(); s.Pending = Original(); b.State = new(true, new() { ["Image32"] = new("interrupted.gif") }); e.Apply("next.gif"); Check(s.Original!.Values.Count == 2 && b.Writes == 2); });
Test("Restore without backup does not write", () => { var (b, _, e) = Setup(); Throws(e.RestoreOriginal); Check(b.Writes == 0); });
Test("Registry value names restricted", () => { Check(AvatarEngine.IsPictureName("Image192") && !AvatarEngine.IsPictureName("Image") && !AvatarEngine.IsPictureName("Image192\\Other") && !AvatarEngine.IsPictureName("UserName") && !AvatarEngine.IsPictureName("Image１２")); });
byte[] Gif(int count)
{
    var bytes = new List<byte>("GIF89a"u8.ToArray()); bytes.AddRange([1, 0, 1, 0, 128, 0, 0, 0, 0, 0, 255, 255, 255]);
    for (int i = 0; i < count; i++) bytes.AddRange([0x21, 0xf9, 4, 0, 10, 0, 0, 0, 0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0]);
    bytes.Add(0x3b); return bytes.ToArray();
}
Test("Animated GIF metadata accepted", () => { var info = GifInfo.Inspect(Gif(2)); Check(info.Frames == 2 && info.Width == 1); });
Test("Static GIF rejected", () => Throws(() => GifInfo.Inspect(Gif(1))));
Test("Truncated GIF rejected", () => Throws(() => GifInfo.Inspect(Gif(2)[..^2])));
Test("Disguised non-GIF rejected", () => Throws(() => GifInfo.Inspect("not a gif with a gif filename"u8.ToArray())));
Test("Too many frames rejected", () => Throws(() => GifInfo.Inspect(Gif(501))));
Test("Out-of-bounds frame rejected", () => { var bytes = Gif(2); bytes[32] = 2; Throws(() => GifInfo.Inspect(bytes)); });
Test("Oversized dimensions rejected", () => { var bytes = Gif(2); bytes[7] = 20; Throws(() => GifInfo.Inspect(bytes)); });
Test("Byte limit rejected", () => Throws(() => GifInfo.Inspect(new byte[GifInfo.MaxBytes + 1])));
Console.WriteLine($"{passed} tests passed.");

sealed class FakeBackend : IAvatarBackend
{
    public AvatarSnapshot State = new(false, []);
    public int Writes, IgnoreWrite;
    public HashSet<int> FailWrites = [];
    public AvatarSnapshot Capture() => new(State.KeyExisted, new(State.Values));
    public void Write(AvatarSnapshot snapshot)
    {
        Writes++;
        if (FailWrites.Contains(Writes)) { State = new(true, new() { ["Image32"] = new("partial.gif") }); throw new IOException("injected partial failure"); }
        if (Writes != IgnoreWrite) State = new(snapshot.KeyExisted, new(snapshot.Values));
    }
}
sealed class FakeStore : IBackupStore
{
    public AvatarSnapshot? Original, Pending;
    public int SaveCount;
    public bool FailBackup, FailJournal;
    public bool HasOriginal => Original != null;
    public void SaveOriginal(AvatarSnapshot snapshot) { if (FailBackup) throw new IOException(); Original = snapshot; SaveCount++; }
    public AvatarSnapshot LoadOriginal() => Original!;
    public AvatarSnapshot? ReadPending() => Pending;
    public void SavePending(AvatarSnapshot snapshot) { if (FailJournal) throw new IOException(); Pending = snapshot; }
    public void ClearPending() => Pending = null;
}
