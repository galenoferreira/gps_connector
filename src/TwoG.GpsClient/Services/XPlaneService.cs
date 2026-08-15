using System.Net;
using System.Net.Sockets;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Lê posição e atitude do X-Plane 11/12 por UDP puro — sem plugin, sem DLL.
///
/// Fluxo: escuta o beacon multicast BECN (239.255.1.1:49707), com que o X-Plane
/// se anuncia na rede; assina os datarefs necessários via RREF na porta informada
/// pelo beacon; e recebe os valores no mesmo socket de onde assinou, já que o
/// X-Plane responde para a porta de origem.
///
/// Como o beacon dá a descoberta automática, a experiência é a mesma do MSFS:
/// não há endereço para o usuário configurar, e funciona também com o X-Plane
/// rodando em outra máquina da rede.
/// </summary>
public sealed class XPlaneService : ISimSource
{
    /// <summary>Frequência pedida ao X-Plane; acima da nossa taxa de envio, para o dado nunca ficar velho.</summary>
    private const int SubscriptionHz = 10;

    /// <summary>Sem valores por este tempo, considera-se que o X-Plane sumiu.</summary>
    private static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan BeaconTimeout = TimeSpan.FromSeconds(10);

    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly XPlaneFixAssembler _assembler = new();

    private Thread? _thread;

    private volatile SimConnectionState _state = SimConnectionState.Searching;
    private volatile string? _simulatorName;
    private volatile GpsFix? _latestFix;
    private volatile string? _lastError;

    public SimConnectionState State
    {
        get
        {
            var state = _state;
            if (state == SimConnectionState.Receiving)
            {
                var fix = _latestFix;
                if (fix is null || DateTime.UtcNow - fix.Utc > TimeSpan.FromSeconds(3))
                    return SimConnectionState.Connected;
            }
            return state;
        }
    }

    public string? SimulatorName => _simulatorName;

    public GpsFix? LatestFix => _latestFix;

    public string? LastError => _lastError;

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "X-Plane" };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread is null)
            return;
        _stopEvent.Set();
        _thread.Join(TimeSpan.FromSeconds(5));
        _thread = null;
        _stopEvent.Reset();
    }

    public void Dispose()
    {
        Stop();
        _stopEvent.Dispose();
    }

    // ── Thread de fundo ─────────────────────────────────────────────────

    private void ThreadMain()
    {
        while (!_stopEvent.WaitOne(TimeSpan.Zero))
        {
            try
            {
                var discovered = DiscoverViaBeacon();
                if (discovered is null)
                    continue;   // sem X-Plane na rede; tenta de novo

                var (endpoint, beacon) = discovered.Value;
                _simulatorName = beacon.Hostname.Length > 0
                    ? $"{beacon.DisplayName} ({beacon.Hostname})"
                    : beacon.DisplayName;
                _state = SimConnectionState.Connected;
                _lastError = null;

                StreamData(endpoint);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _stopEvent.WaitOne(TimeSpan.FromSeconds(3));
            }
            finally
            {
                ResetState();
            }
        }
    }

    private void ResetState()
    {
        _assembler.Reset();
        _latestFix = null;
        _simulatorName = null;
        if (_state != SimConnectionState.Searching)
            _state = SimConnectionState.Searching;
    }

    /// <summary>
    /// Espera o beacon multicast do X-Plane. Devolve o endereço de comandos e os
    /// dados anunciados, ou null se nada apareceu dentro do tempo limite.
    /// </summary>
    private (IPEndPoint Endpoint, XPlaneBeacon Beacon)? DiscoverViaBeacon()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, XPlaneProtocol.BeaconPort));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                new MulticastOption(IPAddress.Parse(XPlaneProtocol.BeaconMulticastGroup), IPAddress.Any));
            socket.ReceiveTimeout = 1000;
        }
        catch (SocketException ex)
        {
            // Porta ocupada por outro app que também escuta o X-Plane.
            _lastError = $"Não foi possível escutar o beacon do X-Plane: {ex.Message}";
            _stopEvent.WaitOne(TimeSpan.FromSeconds(5));
            return null;
        }

        var buffer = new byte[1500];
        var deadline = DateTime.UtcNow + BeaconTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_stopEvent.WaitOne(TimeSpan.Zero))
                return null;

            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try
            {
                received = socket.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException)
            {
                continue;   // timeout de 1 s: só volta ao laço
            }

            if (XPlaneProtocol.TryParseBeacon(buffer.AsSpan(0, received), out var beacon)
                && beacon is { IsMaster: true }
                && remote is IPEndPoint source)
            {
                return (new IPEndPoint(source.Address, beacon.Port), beacon);
            }
        }
        return null;
    }

    /// <summary>
    /// Assina os datarefs e consome os valores até o X-Plane parar de responder
    /// ou o app encerrar.
    /// </summary>
    private void StreamData(IPEndPoint xplane)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));   // o X-Plane responde nesta porta
        socket.ReceiveTimeout = 1000;

        Subscribe(socket, xplane, SubscriptionHz);
        try
        {
            var buffer = new byte[1500];
            Span<(int Index, float Value)> values = stackalloc (int, float)[256];
            var lastData = DateTime.UtcNow;

            while (!_stopEvent.WaitOne(TimeSpan.Zero))
            {
                if (DateTime.UtcNow - lastData > DataTimeout)
                    return;   // X-Plane sumiu: volta a procurar o beacon

                int received;
                try
                {
                    received = socket.Receive(buffer);
                }
                catch (SocketException)
                {
                    continue;   // timeout de 1 s
                }

                var count = XPlaneProtocol.ParseRrefResponse(buffer.AsSpan(0, received), values);
                if (count == 0)
                    continue;

                lastData = DateTime.UtcNow;
                for (var i = 0; i < count; i++)
                    _assembler.Set(values[i].Index, values[i].Value);

                var fix = _assembler.TryBuild(DateTime.UtcNow);
                if (fix is not null)
                {
                    _latestFix = fix;
                    _state = SimConnectionState.Receiving;
                }
            }
        }
        finally
        {
            // Cancela as assinaturas para o X-Plane não continuar transmitindo
            // para uma porta que deixou de existir.
            try
            {
                Subscribe(socket, xplane, 0);
            }
            catch (SocketException)
            {
                // Encerrando de qualquer forma.
            }
        }
    }

    private static void Subscribe(Socket socket, IPEndPoint xplane, int frequencyHz)
    {
        for (var index = 0; index < XPlaneFixAssembler.Datarefs.Length; index++)
        {
            var packet = XPlaneProtocol.BuildRrefRequest(frequencyHz, index, XPlaneFixAssembler.Datarefs[index]);
            socket.SendTo(packet, xplane);
        }
    }
}
