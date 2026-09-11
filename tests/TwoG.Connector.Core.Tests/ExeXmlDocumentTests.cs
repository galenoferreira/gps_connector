using System.Xml.Linq;

namespace TwoG.Connector.Core.Tests;

public class ExeXmlDocumentTests
{
    private const string NewExe = @"C:\Users\p\AppData\Local\Programs\2G Connector\2G-Connector.exe";

    /// <summary>EXE.xml real de quem tem FSUIPC e a v1.3.0 instalados.</summary>
    private const string WithLegacyAndFsuipc = """
        <?xml version="1.0" encoding="utf-8"?>
        <SimBase.Document Type="Launch" version="1,0">
          <Descr>Launch</Descr>
          <Filename>EXE.xml</Filename>
          <Disabled>False</Disabled>
          <Launch.Addon>
            <Name>FSUIPC7</Name>
            <Disabled>False</Disabled>
            <Path>C:\FSUIPC7\FSUIPC7.exe</Path>
          </Launch.Addon>
          <Launch.Addon>
            <Name>2G GPS Cliente</Name>
            <Disabled>False</Disabled>
            <Path>C:\Users\p\Downloads\2G-GPS-Cliente.exe</Path>
            <CommandLine>-minimized</CommandLine>
          </Launch.Addon>
        </SimBase.Document>
        """;

    private static List<string?> Names(XDocument doc) =>
        doc.Root!.Elements("Launch.Addon").Select(a => (string?)a.Element("Name")).ToList();

    private static XElement Ours(XDocument doc) =>
        doc.Root!.Elements("Launch.Addon").Single(a => (string?)a.Element("Name") == "2G Connector");

    [Fact]
    public void Register_OnEmptyDocument_AddsOurEntry()
    {
        var doc = ExeXmlDocument.CreateEmpty();

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        var ours = Ours(doc);
        Assert.Equal(NewExe, (string?)ours.Element("Path"));
        Assert.Equal("False", (string?)ours.Element("Disabled"));
        Assert.Equal("-minimized", (string?)ours.Element("CommandLine"));
    }

    [Fact]
    public void Register_ReplacesLegacyEntry_AndKeepsOtherAddons()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc);

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(["FSUIPC7", "2G Connector"], Names(doc));
        Assert.Equal(@"C:\FSUIPC7\FSUIPC7.exe",
            (string?)doc.Root!.Elements("Launch.Addon").First().Element("Path"));
    }

    [Fact]
    public void Register_LegacyNameMatchIsCaseInsensitive()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc.Replace("2G GPS Cliente", "2g gps cliente"));

        ExeXmlDocument.Register(doc, NewExe);

        Assert.DoesNotContain("2g gps cliente", Names(doc));
    }

    [Fact]
    public void Register_WhenAlreadyCurrent_IsUnchanged()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, NewExe);

        Assert.Equal(ExeXmlDocument.Outcome.Unchanged, ExeXmlDocument.Register(doc, NewExe));
    }

    [Fact]
    public void Register_WhenCurrentButLegacyCameBack_RemovesItAndIsChanged()
    {
        // Pingue-pongue: depois da migração a v1.3.0 recoloca a entrada legada,
        // e a nossa continua certa. Sem Changed o arquivo não é gravado e a
        // legada fica no EXE.xml para sempre.
        var doc = XDocument.Parse(WithLegacyAndFsuipc);
        ExeXmlDocument.Register(doc, NewExe);
        doc.Root!.Add(new XElement("Launch.Addon",
            new XElement("Name", "2G GPS Cliente"),
            new XElement("Disabled", "False"),
            new XElement("Path", @"C:\Users\p\Downloads\2G-GPS-Cliente.exe"),
            new XElement("CommandLine", "-minimized")));

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(["FSUIPC7", "2G Connector"], Names(doc));
    }

    [Fact]
    public void Register_WhenExeMoved_UpdatesPathInPlace()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, @"C:\antigo\2G-Connector.exe");

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(NewExe, (string?)Ours(doc).Element("Path"));
        Assert.Single(Names(doc));
    }

    [Fact]
    public void Register_CollapsesDuplicateEntriesOfOurs()
    {
        // A primeira já está certa: só a remoção da duplicata justifica gravar.
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, NewExe);
        doc.Root!.Add(
            new XElement("Launch.Addon", new XElement("Name", "2G Connector"), new XElement("Path", "b")));

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(NewExe, (string?)Ours(doc).Element("Path"));
    }

    [Fact]
    public void Register_ReenablesADisabledEntry()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, NewExe);
        Ours(doc).Element("Disabled")!.Value = "True";

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));
        Assert.Equal("False", (string?)Ours(doc).Element("Disabled"));
    }

    [Fact]
    public void Register_OnUnexpectedRoot_IsMalformed_AndDoesNotTouchIt()
    {
        var doc = XDocument.Parse("<Outra.Coisa><Launch.Addon><Name>X</Name></Launch.Addon></Outra.Coisa>");
        var before = doc.ToString();

        Assert.Equal(ExeXmlDocument.Outcome.Malformed, ExeXmlDocument.Register(doc, NewExe));
        Assert.Equal(before, doc.ToString());
    }

    [Fact]
    public void Unregister_RemovesOursAndLegacy_KeepsOthers()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc);
        ExeXmlDocument.Register(doc, NewExe);
        doc.Root!.Add(new XElement("Launch.Addon", new XElement("Name", "2G GPS Cliente")));

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Unregister(doc));

        Assert.Equal(["FSUIPC7"], Names(doc));
    }

    [Fact]
    public void Unregister_WithNothingOfOurs_IsUnchanged()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc.Replace("2G GPS Cliente", "Outro Addon"));

        Assert.Equal(ExeXmlDocument.Outcome.Unchanged, ExeXmlDocument.Unregister(doc));
    }
}
