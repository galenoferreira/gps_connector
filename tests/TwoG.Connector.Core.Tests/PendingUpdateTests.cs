namespace TwoG.Connector.Core.Tests;

/// <summary>
/// A regra de "instalável por esta cópia" é uma só: o TryApply instala e a faixa da
/// interface oferece exatamente o mesmo conjunto de pendências.
/// </summary>
public class PendingUpdateTests
{
    private static readonly AppVersion Installed = AppVersion.Parse("1.4.0");

    private static PendingUpdate Pending(string version = "1.5.0", string asset = "2G-Connector.exe", int attempts = 0) =>
        new(version, asset, "arquivo", "hash", attempts);

    [Theory]
    [InlineData("2G-Connector.exe", InstallKind.Portable, 0)]
    [InlineData("2G-Connector.exe", InstallKind.Portable, 1)]
    [InlineData("2G-Connector-Setup.exe", InstallKind.Installer, 0)]
    [InlineData("2G-Connector-Setup.exe", InstallKind.Installer, 1)]
    public void NewerVersionOfTheRightAssetWithAttemptsLeftIsInstallable(string asset, InstallKind kind, int attempts)
    {
        Assert.True(Pending(asset: asset, attempts: attempts).CanBeInstalledBy(kind, Installed));
    }

    [Theory]
    [InlineData("2G-Connector-Setup.exe", InstallKind.Portable)]    // trocaria o exe avulso pelo setup
    [InlineData("2G-Connector.exe", InstallKind.Installer)]         // rodaria o exe como se fosse o setup
    public void AssetOfTheOtherKindIsNotInstallable(string asset, InstallKind kind)
    {
        Assert.False(Pending(asset: asset).CanBeInstalledBy(kind, Installed));
    }

    [Theory]
    [InlineData("2G-Connector.exe")]
    [InlineData("2G-Connector-Setup.exe")]
    public void ReadOnlyFolderInstallsNothing(string asset)
    {
        Assert.False(Pending(asset: asset).CanBeInstalledBy(InstallKind.ReadOnly, Installed));
    }

    [Fact]
    public void ExhaustedAttemptsAreNotInstallable()
    {
        // O TryApply desiste da versão aqui; a faixa não pode oferecer o botão.
        Assert.False(Pending(attempts: UpdatePolicy.MaxAttempts).CanBeInstalledBy(InstallKind.Portable, Installed));
    }

    [Theory]
    [InlineData("1.4.0")]      // igual à instalada: a atualização já deu certo
    [InlineData("1.3.0")]      // anterior à instalada
    [InlineData("lixo")]       // ilegível
    public void StaleOrUnreadableVersionIsNotInstallable(string version)
    {
        Assert.False(Pending(version: version).CanBeInstalledBy(InstallKind.Portable, Installed));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void AttemptsRunOutAtMaxAttempts(int attempts, bool expected)
    {
        Assert.Equal(expected, Pending(attempts: attempts).AttemptsExhausted);
    }

    [Theory]
    [InlineData("1.4.0", true)]
    [InlineData("1.3.0", true)]
    [InlineData("lixo", true)]
    [InlineData("1.5.0", false)]
    public void StaleMeansNotNewerThanTheInstalledVersion(string version, bool expected)
    {
        Assert.Equal(expected, Pending(version: version).IsStaleFor(Installed));
    }

    [Fact]
    public void PreReleaseOfTheInstalledVersionIsStale()
    {
        // SemVer: 1.5.0-rc.1 < 1.5.0. Quem já está na final não "volta" para o rc.
        Assert.True(Pending(version: "1.5.0-rc.1").IsStaleFor(AppVersion.Parse("1.5.0")));
    }
}
