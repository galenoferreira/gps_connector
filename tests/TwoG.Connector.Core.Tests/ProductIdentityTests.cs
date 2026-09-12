namespace TwoG.Connector.Core.Tests;

public class ProductIdentityTests
{
    /// <summary>
    /// Mudar estes nomes faz a v1.3.0 e a versão atual subirem juntas e
    /// transmitirem em dobro. Se este teste falhar, releia o spec antes de "corrigir".
    /// </summary>
    [Fact]
    public void LegacyWindowsObjectNamesArePinned()
    {
        Assert.Equal(@"Local\TwoG.GpsClient.SingleInstance", ProductIdentity.SingleInstanceMutexName);
        Assert.Equal(@"Local\TwoG.GpsClient.ShowWindow", ProductIdentity.ShowWindowEventName);
    }

    [Fact]
    public void LegacyNamesMatchWhatV130WroteToDisk()
    {
        Assert.Equal("2G GPS Cliente", ProductIdentity.LegacyName);
        Assert.Equal("2G GPS Cliente", ProductIdentity.LegacyDataFolderName);
    }

    [Fact]
    public void DefaultDeviceNameSurvivesSanitization()
    {
        Assert.Equal(ProductIdentity.DefaultDeviceName,
            XgpsSentences.SanitizeDeviceName(ProductIdentity.DefaultDeviceName));
    }

    /// <summary>A faixa "baixe manualmente" aponta para cá: a página, não um arquivo.</summary>
    [Fact]
    public void LatestReleasePageIsTheRepositoryReleasesLatest()
    {
        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/latest",
            ProductIdentity.LatestReleasePageUrl);
    }
}
