namespace TwoG.Connector.Core;

public enum InstallKind
{
    /// <summary>Instalado pelo Inno Setup: atualiza rodando o instalador novo.</summary>
    Installer,

    /// <summary>Exe avulso numa pasta com escrita: atualiza trocando o arquivo.</summary>
    Portable,

    /// <summary>Exe avulso sem permissão de escrita: só avisa.</summary>
    ReadOnly,
}

public static class InstallKindDetector
{
    /// <summary>
    /// Instalação pelo Inno tem um desinstalador unins???.exe ao lado do exe; sem ele
    /// é o exe avulso, que só dá para trocar se a pasta aceitar escrita.
    /// </summary>
    public static InstallKind Detect(IEnumerable<string> fileNamesInExeDir, bool exeDirWritable)
    {
        if (fileNamesInExeDir.Any(IsInnoUninstaller))
            return InstallKind.Installer;
        return exeDirWritable ? InstallKind.Portable : InstallKind.ReadOnly;
    }

    public static string? AssetFor(InstallKind kind) => kind switch
    {
        InstallKind.Installer => ProductIdentity.SetupFileName,
        InstallKind.Portable => ProductIdentity.ExeFileName,
        _ => null,
    };

    private static bool IsInnoUninstaller(string path)
    {
        // Aceita "\" e "/" em qualquer SO: o nome pode vir de um caminho do Windows.
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Length == "unins000.exe".Length
               && name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
               && name.AsSpan(5, 3).ToArray().All(char.IsAsciiDigit);
    }
}
