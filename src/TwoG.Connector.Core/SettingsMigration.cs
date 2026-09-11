namespace TwoG.Connector.Core;

/// <summary>
/// Leva a configuração da v1.3.0 para a pasta nova, uma vez. A antiga fica
/// intacta para que voltar à v1.3.0 continue funcionando.
/// </summary>
public static class SettingsMigration
{
    public const string FileName = "settings.json";

    /// <summary>
    /// Copia <c>settings.json</c> de <paramref name="legacyDir"/> para
    /// <paramref name="currentDir"/> quando só existe o antigo. Devolve true se copiou.
    /// </summary>
    public static bool CopyLegacyIfMissing(string currentDir, string legacyDir)
    {
        var current = Path.Combine(currentDir, FileName);
        var legacy = Path.Combine(legacyDir, FileName);
        if (File.Exists(current) || !File.Exists(legacy))
            return false;

        try
        {
            Directory.CreateDirectory(currentDir);
            File.Copy(legacy, current, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            // Outro processo criou o arquivo no meio do caminho: vale o que está lá.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
