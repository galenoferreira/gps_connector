using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>Um arquivo do release, como declarado no manifesto.</summary>
public sealed record UpdateFile(string Sha256, long Size)
{
    public static UpdateFile FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return new UpdateFile(Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }
}

/// <summary>
/// Manifesto publicado em cada release (update.json). Só deve ser interpretado
/// DEPOIS de a assinatura ter sido verificada — ver <see cref="UpdateSignature"/>.
/// </summary>
public sealed record UpdateManifest(AppVersion Version, IReadOnlyDictionary<string, UpdateFile> Files)
{
    public static bool TryParse(byte[] json, [NotNullWhen(true)] out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "manifesto não é um objeto JSON";
                return false;
            }

            if (!root.TryGetProperty("version", out var versionEl)
                || versionEl.ValueKind != JsonValueKind.String
                || !AppVersion.TryParse(versionEl.GetString(), out var version))
            {
                error = "campo \"version\" ausente ou inválido";
                return false;
            }

            if (!root.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Object)
            {
                error = "campo \"files\" ausente";
                return false;
            }

            var files = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in filesEl.EnumerateObject())
            {
                if (!TryReadFile(entry.Value, out var file))
                {
                    error = $"entrada inválida para \"{entry.Name}\"";
                    return false;
                }
                files[entry.Name] = file;
            }

            if (files.Count == 0)
            {
                error = "manifesto sem arquivos";
                return false;
            }

            manifest = new UpdateManifest(version, files);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON inválido: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadFile(JsonElement el, [NotNullWhen(true)] out UpdateFile? file)
    {
        file = null;
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty("sha256", out var hashEl) || hashEl.ValueKind != JsonValueKind.String
            || !el.TryGetProperty("size", out var sizeEl) || sizeEl.ValueKind != JsonValueKind.Number
            || !sizeEl.TryGetInt64(out var size) || size <= 0)
            return false;

        var hash = hashEl.GetString();
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            return false;

        file = new UpdateFile(hash.ToLowerInvariant(), size);
        return true;
    }

    /// <summary>Gera o update.json (usado pela ferramenta de release).</summary>
    public static byte[] Serialize(AppVersion version, IReadOnlyDictionary<string, UpdateFile> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("version", version.ToString());
            writer.WriteStartObject("files");
            foreach (var (name, file) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(name);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteNumber("size", file.Size);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
