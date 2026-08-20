using System.IO;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Descobre o arquivo .PLN do voo em curso quando o SimConnect não informa o caminho.
///
/// O MSFS 2024 tem bug confirmado pela Asobo em que RequestSystemState("FlightPlan")
/// devolve string vazia ou apenas ".PLN". Nesses casos caímos para o caminho padrão do
/// voo personalizado, que é onde o simulador grava o plano ativo.
/// </summary>
public static class PlnFileLocator
{
    /// <summary>True se o caminho devolvido pelo SimConnect é utilizável.</summary>
    public static bool IsUsablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Trim() is var trimmed
        && trimmed.Length > ".PLN".Length
        && trimmed.EndsWith(".PLN", StringComparison.OrdinalIgnoreCase)
        && (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains('/'));

    /// <summary>
    /// Candidatos conhecidos, do mais recente para o mais antigo por data de escrita.
    /// </summary>
    public static IEnumerable<string> KnownCustomFlightPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // MSFS grava o voo personalizado em LocalState (não LocalCache, onde fica o UserCfg.opt).
        yield return Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe",
            "LocalState", "MISSIONS", "Custom", "CustomFlight", "CustomFlight.PLN");
        yield return Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe",
            "LocalState", "MISSIONS", "Custom", "CustomFlight", "CustomFlight.PLN");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator 2024",
            "MISSIONS", "Custom", "CustomFlight", "CustomFlight.PLN");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator",
            "MISSIONS", "Custom", "CustomFlight", "CustomFlight.PLN");

        // Prepar3D salva planos na pasta de documentos da versão.
        foreach (var version in (string[])["v6", "v5", "v4"])
        {
            var folder = Path.Combine(documents, $"Prepar3D {version} Files");
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in MostRecentPlns(folder))
                yield return file;
        }
    }

    /// <summary>Caminho existente mais recentemente escrito, ou null.</summary>
    public static string? MostRecentExisting()
    {
        string? best = null;
        var bestWrite = DateTime.MinValue;

        foreach (var candidate in KnownCustomFlightPaths())
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;
                var written = File.GetLastWriteTimeUtc(candidate);
                if (written > bestWrite)
                {
                    best = candidate;
                    bestWrite = written;
                }
            }
            catch (Exception)
            {
                // Caminho inacessível: tenta o próximo.
            }
        }
        return best;
    }

    private static IEnumerable<string> MostRecentPlns(string folder)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.pln", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var file in files.OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
            yield return file;
    }
}
