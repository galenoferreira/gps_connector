namespace TwoG.Connector.Core.Tests;

public class InstallKindDetectorTests
{
    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("unins001.exe")]
    [InlineData("UNINS000.EXE")]
    public void InnoUninstallerMeansInstaller(string uninstaller)
    {
        Assert.Equal(InstallKind.Installer,
            InstallKindDetector.Detect(["2G-Connector.exe", uninstaller, "unins000.dat"], exeDirWritable: true));
    }

    [Theory]
    [InlineData("uninstall.exe")]
    [InlineData("unins00.exe")]
    [InlineData("uninsABC.exe")]
    public void OtherUninstallersDoNotCount(string file)
    {
        Assert.Equal(InstallKind.Portable, InstallKindDetector.Detect(["2G-Connector.exe", file], true));
    }

    [Fact]
    public void LoneExeInWritableFolderIsPortable()
    {
        Assert.Equal(InstallKind.Portable, InstallKindDetector.Detect(["2G-Connector.exe"], true));
    }

    [Fact]
    public void LoneExeInReadOnlyFolderCannotSelfUpdate()
    {
        Assert.Equal(InstallKind.ReadOnly, InstallKindDetector.Detect(["2G-Connector.exe"], false));
    }

    [Fact]
    public void FullPathsWork()
    {
        Assert.Equal(InstallKind.Installer,
            InstallKindDetector.Detect([@"C:\Programs\2G Connector\unins000.exe"], true));
    }

    [Fact]
    public void EachKindDownloadsTheRightAsset()
    {
        Assert.Equal("2G-Connector-Setup.exe", InstallKindDetector.AssetFor(InstallKind.Installer));
        Assert.Equal("2G-Connector.exe", InstallKindDetector.AssetFor(InstallKind.Portable));
        Assert.Null(InstallKindDetector.AssetFor(InstallKind.ReadOnly));
    }
}
