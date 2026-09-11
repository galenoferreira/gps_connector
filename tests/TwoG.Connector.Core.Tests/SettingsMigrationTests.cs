namespace TwoG.Connector.Core.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void CopiesLegacySettingsWhenOnlyTheyExist()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("2G GPS Cliente");
        var current = tmp.Sub("2G Connector");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"DeviceName":"2G GPS"}""");

        Assert.True(SettingsMigration.CopyLegacyIfMissing(current, legacy));
        Assert.Equal("""{"DeviceName":"2G GPS"}""", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void LeavesLegacyFileIntactForRollback()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("old");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        SettingsMigration.CopyLegacyIfMissing(tmp.Sub("new"), legacy);

        Assert.True(File.Exists(Path.Combine(legacy, "settings.json")));
    }

    [Fact]
    public void NeverOverwritesExistingSettings()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("old");
        var current = tmp.Sub("new");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"DeviceName":"antigo"}""");
        File.WriteAllText(Path.Combine(current, "settings.json"), """{"DeviceName":"novo"}""");

        Assert.False(SettingsMigration.CopyLegacyIfMissing(current, legacy));
        Assert.Equal("""{"DeviceName":"novo"}""", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void DoesNothingOnFreshInstall()
    {
        using var tmp = new TestTempDir();
        var current = tmp.Sub("new");

        Assert.False(SettingsMigration.CopyLegacyIfMissing(current, tmp.Sub("old")));
        Assert.False(Directory.Exists(current));
    }

    /// <summary>
    /// Até a v1.3.0 a pasta era criada a cada início, mas o settings.json só era
    /// gravado ao clicar em Aplicar. Quem nunca mexeu em nada transmitia "2G GPS".
    /// </summary>
    [Fact]
    public void UpgradeWithoutSavedSettingsKeepsLegacyDeviceName()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("2G GPS Cliente");
        Directory.CreateDirectory(legacy);

        Assert.False(SettingsMigration.CopyLegacyIfMissing(tmp.Sub("2G Connector"), legacy));
        Assert.Equal("2G GPS", SettingsMigration.DefaultDeviceName(legacy));
    }

    [Fact]
    public void FreshInstallGetsNewDeviceName()
    {
        using var tmp = new TestTempDir();

        Assert.Equal(ProductIdentity.DefaultDeviceName, SettingsMigration.DefaultDeviceName(tmp.Sub("2G GPS Cliente")));
    }
}
