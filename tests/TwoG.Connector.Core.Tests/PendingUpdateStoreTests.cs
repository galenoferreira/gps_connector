using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core.Tests;

public class PendingUpdateStoreTests
{
    private static PendingUpdate WriteDownload(TestTempDir tmp, string content = "binário novo")
    {
        var dir = tmp.Sub("1.5.0");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "2G-Connector.exe");
        File.WriteAllText(path, content, Encoding.UTF8);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        return new PendingUpdate("1.5.0", "2G-Connector.exe", path, hash, Attempts: 0);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);

        PendingUpdateStore.Save(tmp.Path, pending);

        Assert.Equal(pending, PendingUpdateStore.Load(tmp.Path));
    }

    [Fact]
    public void LoadWithoutStateIsNull()
    {
        using var tmp = new TestTempDir();
        Assert.Null(PendingUpdateStore.Load(tmp.Path));
        Assert.Null(PendingUpdateStore.Load(tmp.Sub("não-existe")));
    }

    [Fact]
    public void LoadWithCorruptStateIsNull_NotAnException()
    {
        using var tmp = new TestTempDir();
        File.WriteAllText(Path.Combine(tmp.Path, PendingUpdateStore.StateFileName), "{ quebrado");
        Assert.Null(PendingUpdateStore.Load(tmp.Path));
    }

    [Fact]
    public void RecordAttemptPersistsTheCount()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);
        PendingUpdateStore.Save(tmp.Path, pending);

        PendingUpdateStore.RecordAttempt(tmp.Path, pending);

        Assert.Equal(1, PendingUpdateStore.Load(tmp.Path)!.Attempts);
    }

    [Fact]
    public void RecordAttemptThatCannotBeSavedThrows_SoTheCallerDoesNotInstall()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);
        // Um arquivo no lugar da pasta: a gravação falha em qualquer SO.
        var updates = tmp.Sub("updates");
        File.WriteAllText(updates, "");

        Assert.ThrowsAny<IOException>(() => PendingUpdateStore.RecordAttempt(updates, pending));
    }

    [Fact]
    public void FileIsIntactDetectsLocalChanges()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);
        Assert.True(PendingUpdateStore.FileIsIntact(pending));

        File.AppendAllText(pending.FilePath, "!");
        Assert.False(PendingUpdateStore.FileIsIntact(pending));

        File.Delete(pending.FilePath);
        Assert.False(PendingUpdateStore.FileIsIntact(pending));
    }

    [Fact]
    public void ClearRemovesStateAndDownloads()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        Directory.CreateDirectory(updates);
        PendingUpdateStore.Save(updates, new PendingUpdate("1.5.0", "a", "b", "c", 0));

        PendingUpdateStore.Clear(updates);

        Assert.Null(PendingUpdateStore.Load(updates));
        Assert.False(Directory.Exists(updates));
    }
}
