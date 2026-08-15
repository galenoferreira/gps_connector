namespace TwoG.GpsClient.Core;

/// <summary>
/// Traduz o que o SimConnect informa em <c>SIMCONNECT_RECV_OPEN</c> num nome
/// legível de simulador.
///
/// Cuidado com o <c>szApplicationName</c>: o MSFS 2020 se identifica como
/// "KittyHawk" (codinome interno), então para a família Microsoft o sinal
/// confiável é o número de versão maior. Já o Prepar3D se identifica pelo nome,
/// e é por ele que o distinguimos.
/// </summary>
public static class SimulatorIdentity
{
    public static string Describe(uint applicationVersionMajor, string? applicationName)
    {
        var name = (applicationName ?? "").Trim();

        if (name.Contains("Prepar3D", StringComparison.OrdinalIgnoreCase))
            return applicationVersionMajor > 0
                ? $"Prepar3D v{applicationVersionMajor}"
                : "Prepar3D";

        if (name.Contains("FSX", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Flight Simulator X", StringComparison.OrdinalIgnoreCase))
            return "Microsoft Flight Simulator X";

        return applicationVersionMajor switch
        {
            12 => "Microsoft Flight Simulator 2024",
            11 => "Microsoft Flight Simulator 2020",
            // "KittyHawk" e afins não ajudam o usuário; só use o nome se for algo novo.
            _ => name.Length > 0 && !name.Contains("KittyHawk", StringComparison.OrdinalIgnoreCase)
                 ? name
                 : "Microsoft Flight Simulator",
        };
    }
}
