using System.IO;

namespace TwoG.GpsClient.Services;

public sealed record MsfsInstall(string DisplayName, string ConfigDir)
{
    public string UserCfgPath => Path.Combine(ConfigDir, "UserCfg.opt");
    public string ExeXmlPath => Path.Combine(ConfigDir, "EXE.xml");
}

/// <summary>
/// Localiza instalações do MSFS 2020/2024 (Microsoft Store e Steam) pelo método
/// padrão da comunidade (FlyByWire, FSUIPC7, Contrail): existência do UserCfg.opt
/// na pasta de configuração por usuário de cada edição. A pasta só existe depois
/// que o simulador rodou ao menos uma vez.
/// </summary>
public static class MsfsInstallations
{
    public static IReadOnlyList<MsfsInstall> AllKnown()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            new MsfsInstall("MSFS 2024 (Microsoft Store)",
                Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache")),
            new MsfsInstall("MSFS 2024 (Steam)",
                Path.Combine(roaming, "Microsoft Flight Simulator 2024")),
            new MsfsInstall("MSFS 2020 (Microsoft Store)",
                Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache")),
            new MsfsInstall("MSFS 2020 (Steam)",
                Path.Combine(roaming, "Microsoft Flight Simulator")),
        ];
    }

    public static IReadOnlyList<MsfsInstall> Detected()
    {
        try
        {
            return AllKnown().Where(i => File.Exists(i.UserCfgPath)).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
