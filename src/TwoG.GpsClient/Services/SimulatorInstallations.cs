using System.Diagnostics;
using System.IO;

namespace TwoG.GpsClient.Services;

public enum SimFamily
{
    /// <summary>Microsoft Flight Simulator 2020/2024.</summary>
    Msfs,

    /// <summary>Lockheed Martin Prepar3D v4/v5/v6.</summary>
    Prepar3D,
}

/// <param name="MarkerFile">
/// Arquivo que só existe depois que o simulador rodou pelo menos uma vez; é o que
/// prova a instalação (o MSFS usa UserCfg.opt, o P3D usa Prepar3D.cfg).
/// </param>
public sealed record SimInstall(string DisplayName, string ConfigDir, SimFamily Family, string MarkerFile)
{
    public string MarkerPath => Path.Combine(ConfigDir, MarkerFile);

    /// <summary>
    /// EXE.xml de autostart. P3D herdou o mesmo mecanismo &lt;Launch.Addon&gt; do FSX,
    /// no mesmo formato e na pasta de configuração do usuário.
    /// </summary>
    public string ExeXmlPath => Path.Combine(ConfigDir, "EXE.xml");
}

/// <summary>
/// Localiza instalações dos simuladores suportados pelo método padrão da comunidade
/// (FlyByWire, FSUIPC7, Contrail): existência do arquivo de configuração por usuário
/// de cada edição. A pasta só passa a existir depois da primeira execução do simulador.
/// </summary>
public static class SimulatorInstallations
{
    public static IReadOnlyList<SimInstall> AllKnown()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            new SimInstall("MSFS 2024 (Microsoft Store)",
                Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache"),
                SimFamily.Msfs, "UserCfg.opt"),
            new SimInstall("MSFS 2024 (Steam)",
                Path.Combine(roaming, "Microsoft Flight Simulator 2024"),
                SimFamily.Msfs, "UserCfg.opt"),
            new SimInstall("MSFS 2020 (Microsoft Store)",
                Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache"),
                SimFamily.Msfs, "UserCfg.opt"),
            new SimInstall("MSFS 2020 (Steam)",
                Path.Combine(roaming, "Microsoft Flight Simulator"),
                SimFamily.Msfs, "UserCfg.opt"),

            new SimInstall("Prepar3D v6",
                Path.Combine(roaming, "Lockheed Martin", "Prepar3D v6"),
                SimFamily.Prepar3D, "Prepar3D.cfg"),
            new SimInstall("Prepar3D v5",
                Path.Combine(roaming, "Lockheed Martin", "Prepar3D v5"),
                SimFamily.Prepar3D, "Prepar3D.cfg"),
            new SimInstall("Prepar3D v4",
                Path.Combine(roaming, "Lockheed Martin", "Prepar3D v4"),
                SimFamily.Prepar3D, "Prepar3D.cfg"),
        ];
    }

    public static IReadOnlyList<SimInstall> Detected()
    {
        try
        {
            return AllKnown().Where(i => File.Exists(i.MarkerPath)).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Executáveis dos simuladores, na ordem de prioridade em que os procuramos.
    /// Store e Steam compartilham o mesmo nome de processo em ambas as famílias.
    /// </summary>
    private static readonly (string ProcessName, string DisplayName)[] KnownProcesses =
    [
        ("FlightSimulator2024", "Microsoft Flight Simulator 2024"),
        ("FlightSimulator", "Microsoft Flight Simulator 2020"),
        ("Prepar3D", "Prepar3D"),
        ("X-Plane", "X-Plane"),
    ];

    /// <summary>
    /// Nome do simulador atualmente em execução, ou null se nenhum estiver aberto.
    /// Serve para distinguir "nenhum simulador aberto" de "simulador aberto mas o
    /// SimConnect não conectou" — diagnósticos bem diferentes para o usuário.
    /// </summary>
    public static string? RunningSimulator()
    {
        foreach (var (processName, displayName) in KnownProcesses)
        {
            try
            {
                if (Process.GetProcessesByName(processName).Length > 0)
                    return displayName;
            }
            catch (Exception)
            {
                // Enumerar processos pode falhar por permissão; segue para o próximo.
            }
        }
        return null;
    }
}
