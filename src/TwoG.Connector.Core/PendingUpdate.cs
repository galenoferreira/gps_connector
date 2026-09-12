using System.Security.Cryptography;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>Atualização baixada e verificada, aguardando um momento seguro para instalar.</summary>
public sealed record PendingUpdate(string Version, string AssetName, string FilePath, string Sha256, int Attempts);

/// <summary>Persiste a atualização pendente em <c>updates/pending.json</c>.</summary>
public static class PendingUpdateStore
{
    public const string StateFileName = "pending.json";

    /// <summary>Estado ausente, corrompido ou ilegível vira null. Não lança por I/O.</summary>
    public static PendingUpdate? Load(string updatesDir)
    {
        var path = Path.Combine(updatesDir, StateFileName);
        try
        {
            if (!File.Exists(path))
                return null;
            var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(path));
            return pending is not null && AppVersion.TryParse(pending.Version, out _) ? pending : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Grava o estado. Ao contrário de Load, Clear e FileIsIntact, deixa escapar
    /// IOException e UnauthorizedAccessException (somente leitura, ACL, disco cheio):
    /// quem chama precisa saber que nada foi gravado.
    /// </summary>
    public static void Save(string updatesDir, PendingUpdate pending)
    {
        Directory.CreateDirectory(updatesDir);
        var path = Path.Combine(updatesDir, StateFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(pending));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Conta mais uma tentativa e grava, antes de instalar. Lança como <see cref="Save"/>;
    /// se lançar, não instale: o limite de <see cref="UpdatePolicy.MaxAttempts"/> depende
    /// deste contador, e sem ele uma instalação que falha se repetiria a cada partida.
    /// </summary>
    public static PendingUpdate RecordAttempt(string updatesDir, PendingUpdate pending)
    {
        var next = pending with { Attempts = pending.Attempts + 1 };
        Save(updatesDir, next);
        return next;
    }

    /// <summary>Apaga estado e downloads. Melhor esforço.</summary>
    public static void Clear(string updatesDir)
    {
        try
        {
            if (Directory.Exists(updatesDir))
                Directory.Delete(updatesDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Um arquivo preso não pode impedir o app de subir.
        }
    }

    /// <summary>
    /// Confere o arquivo em disco de novo antes de instalar: pega download truncado,
    /// disco com defeito ou troca local desde a verificação.
    /// </summary>
    public static bool FileIsIntact(PendingUpdate pending)
    {
        try
        {
            if (!File.Exists(pending.FilePath))
                return false;
            using var stream = File.OpenRead(pending.FilePath);
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == pending.Sha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
