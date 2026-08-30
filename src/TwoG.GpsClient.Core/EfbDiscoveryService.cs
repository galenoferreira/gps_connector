using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Descobre os EFBs da rede para o transmissor poder falar unicast com eles.
///
/// Dois caminhos, alimentando a mesma lista:
///
///  1. Bonjour — pergunta por <c>_2gpilot._udp.local</c> a cada 10 s. É o mais
///     robusto, porque acha o app sem depender de ele ter recebido nada antes.
///  2. Anúncio na UDP 63093 — o app manda o JSON dele de volta para quem
///     transmitiu, e o IP de origem do datagrama é a resposta que queremos.
///
/// Nenhum dos dois substitui o broadcast: eles só acrescentam destinos. Se a
/// descoberta falhar em pleno voo, o piloto não pode perder posição.
/// </summary>
public sealed class EfbDiscoveryService : IEfbDiscovery
{
    /// <summary>
    /// Intervalo entre perguntas Bonjour. O anúncio da 63093 chega a cada 5 s e
    /// segura o TTL sozinho; o mDNS existe para o primeiro contato e para o caso
    /// de o app nunca ter recebido nada nosso, então não precisa ser rápido.
    /// </summary>
    private static readonly TimeSpan BonjourInterval = TimeSpan.FromSeconds(10);

    /// <summary>Janela de escuta após cada pergunta Bonjour.</summary>
    private static readonly TimeSpan BonjourListenWindow = TimeSpan.FromSeconds(2);

    private readonly DiscoveredEfbRegistry _registry = new();
    private readonly ManualResetEvent _stopEvent = new(false);

    private Thread? _announceThread;
    private Thread? _bonjourThread;

    private volatile string? _announceError;
    private volatile string? _bonjourError;
    private long _announcementsReceived;

    public IReadOnlyList<DiscoveredEfb> Discovered => _registry.Active(DateTime.UtcNow);

    public IReadOnlyList<IPAddress> Targets => _registry.ActiveAddresses(DateTime.UtcNow);

    public long AnnouncementsReceived => Interlocked.Read(ref _announcementsReceived);

    /// <summary>
    /// Falhas dos dois caminhos, rotuladas. Um Bonjour bloqueado não pode parecer
    /// problema do anúncio, e vice-versa.
    /// </summary>
    public string? LastError
    {
        get
        {
            var failures = new[]
                {
                    _announceError is { Length: > 0 } a ? $"anúncio: {a}" : null,
                    _bonjourError is { Length: > 0 } b ? $"Bonjour: {b}" : null,
                }
                .Where(f => f is not null)
                .ToArray();
            return failures.Length == 0 ? null : string.Join("  •  ", failures);
        }
    }

    public void Start()
    {
        if (_announceThread is not null)
            return;

        _announceThread = new Thread(AnnounceListenerMain) { IsBackground = true, Name = "EFB announce" };
        _bonjourThread = new Thread(BonjourMain) { IsBackground = true, Name = "EFB Bonjour" };
        _announceThread.Start();
        _bonjourThread.Start();
    }

    public void Stop()
    {
        if (_announceThread is null)
            return;

        _stopEvent.Set();
        _announceThread.Join(TimeSpan.FromSeconds(3));
        _bonjourThread?.Join(TimeSpan.FromSeconds(3));
        _announceThread = null;
        _bonjourThread = null;
        _stopEvent.Reset();
        _registry.Clear();
    }

    public void Dispose()
    {
        Stop();
        _stopEvent.Dispose();
    }

    // ── Caminho 1: anúncio na UDP 63093 ─────────────────────────────────

    private void AnnounceListenerMain()
    {
        while (!_stopEvent.WaitOne(TimeSpan.Zero))
        {
            using var socket = TryBindAnnouncePort();
            if (socket is null)
            {
                // Porta ocupada ou negada: espera e tenta de novo, em vez de
                // matar a thread e perder a descoberta pelo resto do voo.
                if (_stopEvent.WaitOne(TimeSpan.FromSeconds(5)))
                    return;
                continue;
            }

            var buffer = new byte[1500];
            while (!_stopEvent.WaitOne(TimeSpan.Zero))
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int received;
                try
                {
                    received = socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (SocketException ex)
                {
                    _announceError = ex.Message;
                    break;   // recria o socket
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (remote is not IPEndPoint source
                    || source.AddressFamily != AddressFamily.InterNetwork
                    || !EfbAnnouncement.TryParse(buffer.AsSpan(0, received), out var announcement))
                    continue;

                Interlocked.Increment(ref _announcementsReceived);
                _registry.Touch(source.Address, announcement.AppName,
                    EfbDiscoverySource.Announcement, DateTime.UtcNow, announcement.Gdl90Port);
            }
        }
    }

    private Socket? TryBindAnnouncePort()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Outro conector de GPS pode escutar a mesma porta; compartilhar é
            // melhor do que um dos dois ficar sem descoberta.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, EfbAnnouncement.AnnouncePort));
            socket.ReceiveTimeout = 1000;
            _announceError = null;
            return socket;
        }
        catch (SocketException ex)
        {
            _announceError = $"não foi possível escutar a UDP {EfbAnnouncement.AnnouncePort}: {ex.Message}";
            socket.Dispose();
            return null;
        }
    }

    // ── Caminho 2: Bonjour ──────────────────────────────────────────────

    private void BonjourMain()
    {
        do
        {
            try
            {
                QueryBonjourOnce();
                _bonjourError = null;
            }
            catch (SocketException ex)
            {
                _bonjourError = ex.Message;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
        while (!_stopEvent.WaitOne(BonjourInterval));
    }

    /// <summary>
    /// Pergunta e escuta numa porta efêmera. O bit QU da pergunta pede resposta
    /// unicast, então não precisamos disputar a 5353 com o responder mDNS que o
    /// Windows 10/11 já mantém — e o IP de origem da resposta já é o do aparelho.
    /// </summary>
    private void QueryBonjourOnce()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        socket.ReceiveTimeout = 500;

        var query = MdnsProtocol.BuildPtrQuery(MdnsProtocol.PilotServiceName);
        var group = new IPEndPoint(IPAddress.Parse(MdnsProtocol.MulticastGroup), MdnsProtocol.MulticastPort);

        // Uma pergunta por interface: numa máquina com Wi-Fi, Ethernet e adaptadores
        // virtuais, deixar a rota escolher sozinha costuma sair pela interface errada.
        var sent = false;
        foreach (var local in LocalIPv4Addresses())
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    local.GetAddressBytes());
                socket.SendTo(query, group);
                sent = true;
            }
            catch (SocketException)
            {
                // Interface sem multicast; as outras seguem valendo.
            }
        }

        if (!sent)
            socket.SendTo(query, group);

        var buffer = new byte[4096];
        var deadline = DateTime.UtcNow + BonjourListenWindow;
        while (DateTime.UtcNow < deadline)
        {
            if (_stopEvent.WaitOne(TimeSpan.Zero))
                return;

            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try
            {
                received = socket.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException)
            {
                continue;   // janela de 500 ms vencida
            }

            if (remote is not IPEndPoint source
                || source.AddressFamily != AddressFamily.InterNetwork
                || !MdnsProtocol.TryParseResponse(buffer.AsSpan(0, received), MdnsProtocol.PilotServiceName, out var reply)
                || !reply.MentionsService)
                continue;

            var name = reply.InstanceName is { Length: > 0 } instance ? instance : "2G Pilot";

            // O IP de origem é o do aparelho e vale mais que o registro A, que
            // pode trazer endereços de interfaces por onde não há caminho até nós.
            _registry.Touch(source.Address, name, EfbDiscoverySource.Bonjour, DateTime.UtcNow);
        }
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !nic.SupportsMulticast)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return addr.Address;
            }
        }
    }
}
