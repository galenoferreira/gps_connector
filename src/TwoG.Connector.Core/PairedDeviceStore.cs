using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core;

public sealed record PairedDevice(string Id, string Name, string TokenSha256, DateTime PairedUtc, DateTime LastSeenUtc);

/// <summary>
/// Aparelhos pareados (spec 01, "Tokens"). O token vale até o piloto remover o aparelho,
/// e em disco fica SÓ o hash SHA-256: quem copiar o arquivo não consegue se passar pelo
/// iPad. Arquivo corrompido vira lista vazia — todos pareiam de novo, nada derruba o app.
/// </summary>
public sealed class PairedDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly List<PairedDevice> _devices;

    public PairedDeviceStore(string path, Func<DateTime>? utcNow = null)
    {
        _path = path;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _devices = Load(path);
    }

    /// <summary>Aparelho removido (id). O servidor fecha a conexão dele com 4001.</summary>
    public event Action<string>? Removed;

    /// <summary>A lista mudou (pareou ou removeu), para a UI se atualizar.</summary>
    public event Action? Changed;

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (_lock)
                return _devices.ToArray();
        }
    }

    /// <summary>Pareia (ou pareia de novo) o aparelho e devolve o token, que só o app guarda.</summary>
    public string Pair(string deviceId, string deviceName)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = _utcNow();
        lock (_lock)
        {
            // Parear de novo o mesmo aparelho substitui a entrada e invalida o token antigo.
            _devices.RemoveAll(d => d.Id == deviceId);
            _devices.Add(new PairedDevice(deviceId, deviceName, Hash(token), now, now));
            Save();
        }
        Changed?.Invoke();
        return token;
    }

    public PairedDevice? Authenticate(string token)
    {
        var presented = Encoding.ASCII.GetBytes(Hash(token));
        lock (_lock)
        {
            for (var i = 0; i < _devices.Count; i++)
            {
                if (!CryptographicOperations.FixedTimeEquals(presented, Encoding.ASCII.GetBytes(_devices[i].TokenSha256)))
                    continue;
                _devices[i] = _devices[i] with { LastSeenUtc = _utcNow() };
                Save();
                return _devices[i];
            }
        }
        return null;
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _devices.RemoveAll(d => d.Id == deviceId) > 0;
            if (removed)
                Save();
        }
        if (removed)
        {
            Removed?.Invoke(deviceId);
            Changed?.Invoke();
        }
        return removed;
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private sealed record FileModel(List<PairedDevice>? Devices);

    private static List<PairedDevice> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), JsonOptions);
            return model?.Devices?
                .Where(d => d is { Id.Length: > 0, Name: not null, TokenSha256.Length: 64 })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Chamado com o lock tomado. Escrita atômica; falha de disco não derruba o app.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new FileModel(_devices), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O pareamento continua valendo em memória até o app fechar.
        }
    }
}
