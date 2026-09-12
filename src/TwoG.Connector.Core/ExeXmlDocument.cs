using System.Xml.Linq;

namespace TwoG.Connector.Core;

/// <summary>
/// Transformação pura do EXE.xml do MSFS (lista de add-ons lançados com o
/// simulador). Separada do I/O para ser testável: um EXE.xml quebrado impede o
/// MSFS de abrir, e a migração da entrada da v1.3.0 mexe justamente nele.
///
/// Só toca nós <c>Launch.Addon</c> com o nosso nome ou com o nome legado;
/// entradas de outros add-ons nunca são alteradas.
/// </summary>
public static class ExeXmlDocument
{
    public const string AddonName = ProductIdentity.Name;

    /// <summary>Nomes com que versões anteriores se registraram.</summary>
    public static readonly IReadOnlyList<string> LegacyAddonNames = [ProductIdentity.LegacyName];

    public const string LaunchCommandLine = "-minimized";

    public enum Outcome
    {
        /// <summary>O documento foi alterado e precisa ser gravado.</summary>
        Changed,

        /// <summary>Já estava como deveria: não grave (evita backup e escrita à toa).</summary>
        Unchanged,

        /// <summary>Estrutura inesperada: não grave de jeito nenhum.</summary>
        Malformed,
    }

    /// <summary>Documento novo, para quando o EXE.xml ainda não existe.</summary>
    public static XDocument CreateEmpty() =>
        new(new XElement("SimBase.Document",
            new XAttribute("Type", "Launch"),
            new XAttribute("version", "1,0"),
            new XElement("Descr", "Launch"),
            new XElement("Filename", "EXE.xml"),
            new XElement("Disabled", "False")));

    /// <summary>
    /// Garante uma única entrada nossa, habilitada, apontando para
    /// <paramref name="exePath"/>, e remove as entradas legadas.
    /// </summary>
    public static Outcome Register(XDocument doc, string exePath)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SimBase.Document")
            return Outcome.Malformed;

        var changed = RemoveAddons(root, LegacyAddonNames);

        var ours = FindAddons(root, [AddonName]).ToList();
        // Duplicatas nossas (edição manual, versões antigas) viram uma só.
        foreach (var extra in ours.Skip(1))
        {
            extra.Remove();
            changed = true;
        }

        var current = ours.FirstOrDefault();
        if (current is not null && IsUpToDate(current, exePath))
            return changed ? Outcome.Changed : Outcome.Unchanged;

        if (current is null)
        {
            current = new XElement("Launch.Addon");
            root.Add(current);
        }

        // Reescreve apenas o NOSSO nó, na ordem convencional (FSUIPC).
        current.RemoveAll();
        current.Add(
            new XElement("Name", AddonName),
            new XElement("Disabled", "False"),
            new XElement("Path", exePath),
            new XElement("CommandLine", LaunchCommandLine));
        return Outcome.Changed;
    }

    /// <summary>Remove a nossa entrada e as legadas (usado pelo desinstalador).</summary>
    public static Outcome Unregister(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SimBase.Document")
            return Outcome.Malformed;

        return RemoveAddons(root, [AddonName, .. LegacyAddonNames])
            ? Outcome.Changed
            : Outcome.Unchanged;
    }

    private static bool IsUpToDate(XElement addon, string exePath) =>
        string.Equals((string?)addon.Element("Path"), exePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals((string?)addon.Element("Disabled"), "False", StringComparison.OrdinalIgnoreCase)
        && (string?)addon.Element("CommandLine") == LaunchCommandLine;

    private static IEnumerable<XElement> FindAddons(XElement root, IReadOnlyCollection<string> names) =>
        root.Elements("Launch.Addon").Where(a =>
            names.Contains((string?)a.Element("Name") ?? "", StringComparer.OrdinalIgnoreCase));

    private static bool RemoveAddons(XElement root, IReadOnlyCollection<string> names)
    {
        var matches = FindAddons(root, names).ToList();
        foreach (var addon in matches)
            addon.Remove();
        return matches.Count > 0;
    }
}
