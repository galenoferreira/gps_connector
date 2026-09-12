using System.Net;

namespace TwoG.Connector.Core;

/// <summary>Por onde um EFB foi encontrado, para a UI poder dizer ao piloto.</summary>
public enum EfbDiscoverySource
{
    /// <summary>Anúncio UDP na 63093, cujo IP de origem é o do app.</summary>
    Announcement,

    /// <summary>Serviço Bonjour <c>_2gpilot._udp.</c> resolvido por mDNS.</summary>
    Bonjour,
}

/// <summary>Um EFB visto na rede, com quando e por onde.</summary>
/// <param name="Address">IP do dispositivo que roda o app.</param>
/// <param name="AppName">Nome declarado pelo app.</param>
/// <param name="Source">Caminho pelo qual foi descoberto.</param>
/// <param name="LastSeenUtc">Última vez que se manifestou.</param>
/// <param name="Gdl90Port">Porta GDL 90 anunciada, quando houver.</param>
public sealed record DiscoveredEfb(
    IPAddress Address,
    string AppName,
    EfbDiscoverySource Source,
    DateTime LastSeenUtc,
    int? Gdl90Port);

/// <summary>
/// Os EFBs vistos na rede, mantidos enquanto continuarem se anunciando.
///
/// Existe para o Cliente poder mandar unicast direto ao app, o único caminho que
/// atravessa um roteador com isolamento de clientes ligado. NÃO substitui o
/// broadcast: se a descoberta falhar no meio de um voo, o piloto perderia
/// posição, e a banda de um envio a mais numa rede local não paga esse risco.
///
/// O tempo entra por parâmetro em vez de sair de <see cref="DateTime.UtcNow"/>
/// para a expiração ser testável sem esperar meio minuto.
/// </summary>
public sealed class DiscoveredEfbRegistry
{
    /// <summary>
    /// Sem sinal por este tempo, o destino é solto e o broadcast volta a ser o
    /// único caminho. O anúncio vem a cada 5 s, então 30 s tolera seis perdas
    /// seguidas antes de desistir — folga para um Wi-Fi ruim sem deixar um IP
    /// morto na lista para sempre.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Teto de destinos. Dois iPads na mesma cabine é caso real; uma dúzia não é,
    /// e seria sintoma de rede errada ou de alguém falando na nossa porta.
    /// </summary>
    public const int MaxTargets = 4;

    private readonly object _lock = new();
    private readonly Dictionary<IPAddress, DiscoveredEfb> _seen = new();

    /// <summary>
    /// Registra (ou renova) um EFB. Devolve true quando o endereço é novo, para
    /// quem chama poder reagir à descoberta sem varrer a lista.
    /// </summary>
    public bool Touch(IPAddress address, string appName, EfbDiscoverySource source, DateTime utcNow, int? gdl90Port = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_lock)
        {
            var isNew = !_seen.ContainsKey(address);

            if (isNew && CountActiveLocked(utcNow) >= MaxTargets)
            {
                // Cheio: um destino expirado ainda ocupando espaço sai para dar
                // lugar ao que está falando agora. Se todos estão vivos, o novo
                // é recusado — encher a lista com desconhecidos seria pior.
                if (!TryEvictExpiredLocked(utcNow))
                    return false;
            }

            _seen[address] = new DiscoveredEfb(address, appName, source, utcNow, gdl90Port);
            return isNew;
        }
    }

    /// <summary>
    /// Os EFBs ainda dentro do TTL, do mais recente para o mais antigo, limitados
    /// ao teto. Aproveita a leitura para descartar os vencidos.
    /// </summary>
    public IReadOnlyList<DiscoveredEfb> Active(DateTime utcNow)
    {
        lock (_lock)
        {
            PurgeLocked(utcNow);
            return _seen.Values
                .OrderByDescending(e => e.LastSeenUtc)
                .Take(MaxTargets)
                .ToArray();
        }
    }

    /// <summary>Só os endereços ativos — o que o transmissor precisa.</summary>
    public IReadOnlyList<IPAddress> ActiveAddresses(DateTime utcNow) =>
        Active(utcNow).Select(e => e.Address).ToArray();

    public void Clear()
    {
        lock (_lock)
            _seen.Clear();
    }

    private void PurgeLocked(DateTime utcNow)
    {
        foreach (var address in _seen
                     .Where(kv => utcNow - kv.Value.LastSeenUtc >= Ttl)
                     .Select(kv => kv.Key)
                     .ToArray())
        {
            _seen.Remove(address);
        }
    }

    private int CountActiveLocked(DateTime utcNow)
    {
        PurgeLocked(utcNow);
        return _seen.Count;
    }

    private bool TryEvictExpiredLocked(DateTime utcNow)
    {
        PurgeLocked(utcNow);
        if (_seen.Count < MaxTargets)
            return true;

        // Todos vivos: nada a despejar.
        return false;
    }
}
