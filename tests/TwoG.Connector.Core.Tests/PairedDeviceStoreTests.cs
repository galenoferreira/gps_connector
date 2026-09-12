namespace TwoG.Connector.Core.Tests;

public class PairedDeviceStoreTests
{
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private PairedDeviceStore NewStore(TestTempDir tmp) => new(tmp.Sub("paired-devices.json"), () => _now);

    [Fact]
    public void TokenIs32BytesInBase64Url()
    {
        using var tmp = new TestTempDir();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", NewStore(tmp).Pair("ipad-1", "iPad"));
    }

    [Fact]
    public void FileKeepsOnlyTheHash()
    {
        using var tmp = new TestTempDir();
        var token = NewStore(tmp).Pair("ipad-1", "iPad do Galeno");
        var file = File.ReadAllText(tmp.Sub("paired-devices.json"));

        Assert.DoesNotContain(token, file);
        Assert.Contains("tokenSha256", file);
        Assert.Contains("iPad do Galeno", file);
    }

    [Fact]
    public void KnownTokenAuthenticatesAndUpdatesLastSeen()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var token = store.Pair("ipad-1", "iPad");
        _now = _now.AddHours(1);

        var device = store.Authenticate(token);

        Assert.Equal("ipad-1", device!.Id);
        Assert.Equal(_now, device.LastSeenUtc);
    }

    [Fact]
    public void UnknownTokenDoesNotAuthenticate()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        store.Pair("ipad-1", "iPad");

        Assert.Null(store.Authenticate("não-é-um-token"));
    }

    [Fact]
    public void PairingTheSameDeviceAgainRevokesTheOldToken()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var old = store.Pair("ipad-1", "iPad");
        var fresh = store.Pair("ipad-1", "iPad renomeado");

        Assert.Null(store.Authenticate(old));
        Assert.Equal("iPad renomeado", store.Authenticate(fresh)!.Name);
        Assert.Single(store.Devices);
    }

    [Fact]
    public void RemoveRevokesAndRaisesRemoved()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var token = store.Pair("ipad-1", "iPad");
        string? removed = null;
        store.Removed += id => removed = id;

        Assert.True(store.Remove("ipad-1"));

        Assert.Equal("ipad-1", removed);
        Assert.Null(store.Authenticate(token));
        Assert.False(store.Remove("ipad-1"));
    }

    [Fact]
    public void ChangedFiresOnPairAndRemove()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var changes = 0;
        store.Changed += () => changes++;

        store.Pair("ipad-1", "iPad");
        store.Remove("ipad-1");

        Assert.Equal(2, changes);
    }

    [Fact]
    public void PairingsSurviveARestart()
    {
        using var tmp = new TestTempDir();
        var token = NewStore(tmp).Pair("ipad-1", "iPad");

        Assert.Equal("ipad-1", NewStore(tmp).Authenticate(token)!.Id);
    }

    [Fact]
    public void CorruptFileMeansNoPairings_NotACrash()
    {
        using var tmp = new TestTempDir();
        File.WriteAllText(tmp.Sub("paired-devices.json"), "{ corrompido");

        Assert.Empty(NewStore(tmp).Devices);
    }
}
