using System.Security.Cryptography;

namespace TwoG.Connector.Core;

/// <summary>Onde buscar o manifesto e como montar a URL de cada arquivo.</summary>
public sealed record UpdateFeed(Uri ManifestUrl, Func<AppVersion, string, Uri> AssetUrl)
{
    public Uri SignatureUrl => new(ManifestUrl.AbsoluteUri + ".sig");

    /// <summary>
    /// Feed oficial: o manifesto pelo link permanente do "latest" (sem API do GitHub,
    /// sem limite de requisições) e os arquivos pela URL COM versão — senão um
    /// release publicado durante o download misturaria o manifesto de uma versão
    /// com o binário de outra.
    /// </summary>
    public static UpdateFeed GitHubLatest(string repository) => new(
        new Uri($"https://github.com/{repository}/releases/latest/download/update.json"),
        (version, asset) => new Uri($"https://github.com/{repository}/releases/download/v{version}/{asset}"));

    /// <summary>
    /// Feed de teste (TWOG_UPDATE_FEED): manifesto numa pasta fixa, arquivos ao lado.
    /// A assinatura continua obrigatória, então isto não enfraquece nada.
    /// </summary>
    public static UpdateFeed Beside(Uri manifestUrl) => new(manifestUrl, (_, asset) => new Uri(manifestUrl, asset));
}

public enum UpdateCheckStatus
{
    UpToDate,

    /// <summary>Há versão nova, mas não foi baixada (instalação sem permissão de escrita).</summary>
    Available,

    Downloaded,
    AlreadyPending,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, AppVersion? Latest = null, string? Error = null);

/// <summary>
/// Busca o manifesto, verifica a assinatura e a versão, baixa o arquivo e confere
/// tamanho e hash — nesta ordem. Só o que passou por tudo vira
/// <see cref="PendingUpdate"/>. Não instala nada: quando instalar é decisão de
/// <see cref="UpdatePolicy"/>. Nunca lança por falha de rede ou de conteúdo.
/// </summary>
public sealed class UpdateChecker(HttpClient http, UpdateFeed feed, string updatesDir, IReadOnlyList<string> acceptedKeys)
{
    private const int CopyBufferSize = 81920;

    public async Task<UpdateCheckResult> CheckAsync(AppVersion current, string? assetName, CancellationToken ct = default)
    {
        byte[] manifestBytes;
        string signature;
        try
        {
            manifestBytes = await http.GetByteArrayAsync(feed.ManifestUrl, ct);
            signature = await http.GetStringAsync(feed.SignatureUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            return Failed($"sem acesso ao feed de atualização: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("tempo esgotado ao buscar o manifesto");
        }

        if (!UpdateSignature.Verify(manifestBytes, signature, acceptedKeys))
            return Failed("assinatura inválida — atualização descartada");

        if (!UpdateManifest.TryParse(manifestBytes, out var manifest, out var error))
            return Failed($"manifesto inválido: {error}");

        var latest = manifest.Version;
        if (latest.CompareTo(current) <= 0)
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest);

        if (assetName is null)
            return new UpdateCheckResult(UpdateCheckStatus.Available, latest);

        if (!manifest.Files.TryGetValue(assetName, out var expected))
            return Failed($"o release {latest} não traz {assetName}", latest);

        var pending = PendingUpdateStore.Load(updatesDir);
        if (pending is not null
            && pending.Version == latest.ToString()
            && string.Equals(pending.AssetName, assetName, StringComparison.OrdinalIgnoreCase)
            && pending.Sha256 == expected.Sha256
            && PendingUpdateStore.FileIsIntact(pending))
            return new UpdateCheckResult(UpdateCheckStatus.AlreadyPending, latest);

        // Versão nova substitui qualquer pendência anterior.
        PendingUpdateStore.Clear(updatesDir);
        var dir = Path.Combine(updatesDir, latest.ToString());
        var finalPath = Path.Combine(dir, assetName);
        var partialPath = finalPath + ".partial";

        try
        {
            Directory.CreateDirectory(dir);
            long written;
            using (var response = await http.GetAsync(feed.AssetUrl(latest, assetName), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(partialPath);
                written = await CopyBoundedAsync(source, target, expected.Size, ct);
            }

            if (written != expected.Size)
            {
                PendingUpdateStore.Clear(updatesDir);
                return Failed($"tamanho do download não confere com o manifesto ({expected.Size} bytes esperados)", latest);
            }

            string actual;
            await using (var stream = File.OpenRead(partialPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));

            if (actual != expected.Sha256)
            {
                PendingUpdateStore.Clear(updatesDir);
                return Failed("SHA256 do download não confere com o manifesto — descartado", latest);
            }

            File.Move(partialPath, finalPath, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed($"falha no download: {ex.Message}", latest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed($"falha ao gravar o download: {ex.Message}", latest);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed("tempo esgotado no download", latest);
        }

        PendingUpdateStore.Save(updatesDir,
            new PendingUpdate(latest.ToString(), assetName, finalPath, expected.Sha256, Attempts: 0));
        return new UpdateCheckResult(UpdateCheckStatus.Downloaded, latest);
    }

    /// <summary>
    /// Copia até <paramref name="maxBytes"/> + 1: se o servidor mandar mais do que o
    /// manifesto declara, para ali em vez de encher o disco. Devolve o total lido.
    /// </summary>
    private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return total;
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }

    private static UpdateCheckResult Failed(string error, AppVersion? latest = null) =>
        new(UpdateCheckStatus.Failed, latest, error);
}
