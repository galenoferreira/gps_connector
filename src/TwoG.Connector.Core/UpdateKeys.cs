namespace TwoG.Connector.Core;

/// <summary>
/// Chaves públicas aceitas para assinar atualizações, vindas dos arquivos
/// UpdateKeys/*.pubkey embutidos no assembly. É uma lista, e não uma chave só,
/// para permitir rotação: publica-se uma versão que aceite a antiga e a nova antes
/// de trocar o secret do CI.
/// </summary>
public static class UpdateKeys
{
    private const string ResourcePrefix = "TwoG.Connector.Core.UpdateKeys.";

    public static IReadOnlyList<string> Accepted { get; } = Load();

    private static string[] Load()
    {
        var assembly = typeof(UpdateKeys).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".pubkey", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            })
            .ToArray();
    }
}
