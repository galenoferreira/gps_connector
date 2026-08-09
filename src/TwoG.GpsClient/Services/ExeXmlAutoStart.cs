using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Registra/remove o app no EXE.xml de cada instalação detectada do MSFS, para que
/// o simulador o lance automaticamente ao iniciar (mecanismo <c>Launch.Addon</c>,
/// o mesmo usado por FSUIPC7 e outros).
///
/// Regras de segurança (um EXE.xml corrompido impede o MSFS de abrir):
///  - sempre faz merge com parser XML de verdade — nunca concatena texto;
///  - preserva entradas de outros add-ons; mexe apenas no nó com o nosso Name;
///  - cria backup (.2g-backup) antes de cada gravação;
///  - arquivo ilegível/malformado é PULADO, nunca sobrescrito;
///  - grava UTF-8 SEM BOM (BOM é causa documentada de quebra).
/// </summary>
public sealed class ExeXmlAutoStart
{
    public const string AddonName = "2G GPS Cliente";
    private const string LaunchCommandLine = "-minimized";

    public sealed record SyncResult(string SimName, bool Success, string Detail);

    /// <summary>Caminho do executável a registrar (o do processo atual).</summary>
    private static string? ExePath => Environment.ProcessPath;

    /// <summary>
    /// Garante que o registro reflita <paramref name="enabled"/> em todas as
    /// instalações detectadas. Retorna o que foi feito em cada uma.
    /// </summary>
    public IReadOnlyList<SyncResult> Sync(bool enabled)
    {
        var results = new List<SyncResult>();
        foreach (var install in MsfsInstallations.Detected())
        {
            try
            {
                results.Add(enabled ? Register(install) : Unregister(install));
            }
            catch (Exception ex)
            {
                results.Add(new SyncResult(install.DisplayName, false, ex.Message));
            }
        }
        return results;
    }

    /// <summary>Remove o registro em todas as instalações (usado pelo desinstalador via "-unregister").</summary>
    public void UnregisterEverywhere()
    {
        foreach (var install in MsfsInstallations.Detected())
        {
            try
            {
                Unregister(install);
            }
            catch (Exception)
            {
                // Desinstalação não deve falhar por causa de um EXE.xml problemático.
            }
        }
    }

    private SyncResult Register(MsfsInstall install)
    {
        var exePath = ExePath;
        if (exePath is null)
            return new SyncResult(install.DisplayName, false, "caminho do executável indisponível");

        XDocument doc;
        if (File.Exists(install.ExeXmlPath))
        {
            var loaded = TryLoad(install.ExeXmlPath);
            if (loaded is null)
                return new SyncResult(install.DisplayName, false,
                    "EXE.xml existente está malformado — não modificado por segurança");
            doc = loaded;
        }
        else
        {
            doc = new XDocument(
                new XElement("SimBase.Document",
                    new XAttribute("Type", "Launch"),
                    new XAttribute("version", "1,0"),
                    new XElement("Descr", "Launch"),
                    new XElement("Filename", "EXE.xml"),
                    new XElement("Disabled", "False")));
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SimBase.Document")
            return new SyncResult(install.DisplayName, false,
                "EXE.xml existente tem estrutura inesperada — não modificado por segurança");

        var ours = FindOurAddon(root);
        if (ours is null)
        {
            ours = new XElement("Launch.Addon");
            root.Add(ours);
        }

        // Reescreve apenas o NOSSO nó, na ordem convencional (FSUIPC).
        ours.RemoveAll();
        ours.Add(
            new XElement("Name", AddonName),
            new XElement("Disabled", "False"),
            new XElement("Path", exePath),
            new XElement("CommandLine", LaunchCommandLine));

        Save(doc, install.ExeXmlPath);
        return new SyncResult(install.DisplayName, true, "registrado");
    }

    private SyncResult Unregister(MsfsInstall install)
    {
        if (!File.Exists(install.ExeXmlPath))
            return new SyncResult(install.DisplayName, true, "sem registro");

        var doc = TryLoad(install.ExeXmlPath);
        if (doc?.Root is null)
            return new SyncResult(install.DisplayName, false,
                "EXE.xml malformado — não modificado por segurança");

        var ours = FindOurAddon(doc.Root);
        if (ours is null)
            return new SyncResult(install.DisplayName, true, "sem registro");

        ours.Remove();
        Save(doc, install.ExeXmlPath);
        return new SyncResult(install.DisplayName, true, "registro removido");
    }

    private static XElement? FindOurAddon(XElement root) =>
        root.Elements("Launch.Addon")
            .FirstOrDefault(a =>
                string.Equals((string?)a.Element("Name"), AddonName, StringComparison.OrdinalIgnoreCase));

    private static XDocument? TryLoad(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Save(XDocument doc, string path)
    {
        if (File.Exists(path))
            File.Copy(path, path + ".2g-backup", overwrite: true);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }
}
