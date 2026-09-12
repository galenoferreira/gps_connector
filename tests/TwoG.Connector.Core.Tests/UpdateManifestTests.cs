using System.Text;

namespace TwoG.Connector.Core.Tests;

public class UpdateManifestTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static bool Parse(string json, out UpdateManifest? manifest, out string? error) =>
        UpdateManifest.TryParse(Encoding.UTF8.GetBytes(json), out manifest, out error);

    [Fact]
    public void ParsesTheCiFormat()
    {
        // $$$$: o JSON termina em "}}}", e o conteúdo precisa de menos chaves seguidas que o delimitador.
        var ok = Parse($$$$"""{"version":"1.5.0","files":{"2G-Connector.exe":{"sha256":"{{{{Hash}}}}","size":75517030}}}""",
            out var m, out var error);

        Assert.True(ok, error);
        Assert.Equal("1.5.0", m!.Version.ToString());
        Assert.Equal(new UpdateFile(Hash, 75517030), m.Files["2G-Connector.exe"]);
    }

    [Fact]
    public void FileLookupIgnoresCase_AndHashIsNormalizedToLowercase()
    {
        Parse($$$$"""{"version":"1.5.0","files":{"2G-Connector.exe":{"sha256":"{{{{Hash.ToUpperInvariant()}}}}","size":1}}}""",
            out var m, out _);

        Assert.Equal(Hash, m!.Files["2g-connector.EXE"].Sha256);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("""{"files":{}}""")]
    [InlineData("""{"version":"1.5","files":{}}""")]
    [InlineData("""{"version":"1.5.0"}""")]
    [InlineData("""{"version":"1.5.0","files":{}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"abc","size":1}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","size":0}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","size":"1"}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":"x"}}""")]
    public void RejectsMalformedManifests_WithoutThrowing(string json)
    {
        Assert.False(Parse(json, out var m, out var error));
        Assert.Null(m);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SerializeRoundTrips()
    {
        var files = new Dictionary<string, UpdateFile>
        {
            ["2G-Connector.exe"] = new(Hash, 10),
            ["2G-Connector-Setup.exe"] = new(Hash, 20),
        };

        var bytes = UpdateManifest.Serialize(AppVersion.Parse("1.5.0-rc.1"), files);

        Assert.True(UpdateManifest.TryParse(bytes, out var m, out var error), error);
        Assert.Equal("1.5.0-rc.1", m!.Version.ToString());
        Assert.Equal(20, m.Files["2G-Connector-Setup.exe"].Size);
    }

    [Fact]
    public void FromFileHashesAndMeasures()
    {
        using var tmp = new TestTempDir();
        var path = tmp.Sub("a.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

        var file = UpdateFile.FromFile(path);

        // SHA-256("abc"), vetor de teste do FIPS 180-2.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", file.Sha256);
        Assert.Equal(3, file.Size);
    }
}
