using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core.Tests;

public class UpdateCheckerTests
{
    private const string Asset = "2G-Connector.exe";
    private static readonly Uri ManifestUrl = new("https://feed.test/update.json");
    private static readonly AppVersion Installed = AppVersion.Parse("1.4.0");

    /// <summary>Feed HTTP em memória: responde só o que foi cadastrado e registra cada pedido.</summary>
    private sealed class FakeFeed : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = new();
        public List<string> Requested { get; } = [];

        public void Put(string url, byte[] body) => _files[url] = body;
        public void Put(string url, string body) => Put(url, Encoding.UTF8.GetBytes(body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            return Task.FromResult(_files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>Um release publicado: chave própria, manifesto assinado e o binário.</summary>
    private sealed class Release : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string PublicPem => _key.ExportSubjectPublicKeyInfoPem();
        public string PrivatePem => _key.ExportECPrivateKeyPem();
        public void Dispose() => _key.Dispose();

        public byte[] Manifest(string version, byte[] content, string asset = Asset) =>
            UpdateManifest.Serialize(AppVersion.Parse(version),
                new Dictionary<string, UpdateFile>
                {
                    [asset] = new(Convert.ToHexStringLower(SHA256.HashData(content)), content.Length),
                });

        /// <summary>Publica manifesto, assinatura e binário no feed.</summary>
        public void PublishTo(FakeFeed feed, string version, byte[] content)
        {
            var manifest = Manifest(version, content);
            feed.Put(ManifestUrl.AbsoluteUri, manifest);
            feed.Put(ManifestUrl.AbsoluteUri + ".sig", UpdateSignature.Sign(manifest, PrivatePem));
            feed.Put(AssetUrl(version), content);
        }
    }

    // Cada teste usa uma subpasta "updates": o checker apaga a pasta inteira ao
    // descartar um download, e ela não pode ser a própria pasta temporária.
    private static string AssetUrl(string version, string asset = Asset) => $"https://feed.test/v{version}/{asset}";

    private static UpdateChecker Checker(FakeFeed feed, string updatesDir, string publicPem) =>
        new(new HttpClient(feed),
            new UpdateFeed(ManifestUrl, (v, a) => new Uri(AssetUrl(v.ToString(), a))),
            updatesDir,
            [publicPem]);

    private static readonly byte[] NewBinary = Encoding.UTF8.GetBytes("MZ... binário da 1.5.0");

    [Fact]
    public async Task NewerSignedVersion_IsDownloaded_AndBecomesPending()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Downloaded, result.Status);
        Assert.Equal("1.5.0", result.Latest!.ToString());
        var pending = PendingUpdateStore.Load(updates)!;
        Assert.Equal("1.5.0", pending.Version);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal(NewBinary, File.ReadAllBytes(pending.FilePath));
    }

    [Fact]
    public async Task DownloadsFromTheVersionedUrl_NotFromLatest()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Contains(AssetUrl("1.5.0"), feed.Requested);
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("1.3.0")]
    [InlineData("1.4.0-rc.9")]
    public async Task SameOrOlderVersion_IsUpToDate_AndDownloadsNothing(string published)
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, published, NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.DoesNotContain(AssetUrl(published), feed.Requested);
        Assert.Null(PendingUpdateStore.Load(updates));
    }

    [Fact]
    public async Task TamperedManifest_IsRejected_BeforeAnyDownload()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var tampered = Encoding.UTF8.GetString(release.Manifest("1.5.0", NewBinary)).Replace("1.5.0", "1.5.1");
        feed.Put(ManifestUrl.AbsoluteUri, tampered);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("assinatura", result.Error);
        Assert.DoesNotContain(feed.Requested, u => u.Contains("/v1.5"));
    }

    [Fact]
    public async Task SignatureFromAnUnknownKey_IsRejected()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var attacker = new Release();
        using var ours = new Release();
        var feed = new FakeFeed();
        attacker.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, ours.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(PendingUpdateStore.Load(updates));
    }

    [Fact]
    public async Task BinaryWithWrongHash_IsDiscarded()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var swapped = (byte[])NewBinary.Clone();
        swapped[0] ^= 0xFF;                        // mesmo tamanho, conteúdo diferente
        feed.Put(AssetUrl("1.5.0"), swapped);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("SHA256", result.Error);
        Assert.Null(PendingUpdateStore.Load(updates));
        Assert.False(Directory.Exists(updates));   // Clear apagou o download e a pasta
    }

    [Fact]
    public async Task OversizedBinary_IsCutOff_AndDiscarded()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        feed.Put(AssetUrl("1.5.0"), [.. NewBinary, .. new byte[1024 * 1024]]);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("tamanho", result.Error);
        Assert.False(Directory.Exists(updates));   // Clear apagou o download e a pasta
    }

    [Fact]
    public async Task SecondCheck_WithIntactPending_DoesNotDownloadAgain()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var checker = Checker(feed, updates, release.PublicPem);

        await checker.CheckAsync(Installed, Asset);
        var second = await checker.CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.AlreadyPending, second.Status);
        Assert.Single(feed.Requested, u => u == AssetUrl("1.5.0"));
    }

    [Fact]
    public async Task NewerReleaseReplacesAnOlderPending()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        var checker = Checker(feed, updates, release.PublicPem);
        release.PublishTo(feed, "1.5.0", NewBinary);
        await checker.CheckAsync(Installed, Asset);

        release.PublishTo(feed, "1.5.1", Encoding.UTF8.GetBytes("MZ... 1.5.1"));
        var result = await checker.CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Downloaded, result.Status);
        Assert.Equal("1.5.1", PendingUpdateStore.Load(updates)!.Version);
        Assert.False(Directory.Exists(Path.Combine(updates, "1.5.0")));
    }

    [Fact]
    public async Task CheckOnly_ReportsAvailable_WithoutDownloading()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, assetName: null);

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.DoesNotContain(AssetUrl("1.5.0"), feed.Requested);
    }

    [Fact]
    public async Task ReleaseWithoutOurAsset_Fails()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, "2G-Connector-Setup.exe");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("2G-Connector-Setup.exe", result.Error);
    }

    [Fact]
    public async Task UnreachableFeed_FailsWithoutThrowing()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();

        var result = await Checker(new FakeFeed(), updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void GitHubFeedUsesPermanentLinkForManifest_AndVersionedUrlForBinaries()
    {
        var feed = UpdateFeed.GitHubLatest("galenoferreira/gps_connector");

        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json",
            feed.ManifestUrl.AbsoluteUri);
        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json.sig",
            feed.SignatureUrl.AbsoluteUri);
        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/download/v1.5.0/2G-Connector.exe",
            feed.AssetUrl(AppVersion.Parse("1.5.0"), "2G-Connector.exe").AbsoluteUri);
    }

    [Fact]
    public void TestFeedKeepsBinariesBesideTheManifest()
    {
        var feed = UpdateFeed.Beside(new Uri("https://github.com/o/r/releases/download/v1.4.0-rc.2/update.json"));

        Assert.Equal("https://github.com/o/r/releases/download/v1.4.0-rc.2/2G-Connector.exe",
            feed.AssetUrl(AppVersion.Parse("1.4.0-rc.2"), "2G-Connector.exe").AbsoluteUri);
    }
}
