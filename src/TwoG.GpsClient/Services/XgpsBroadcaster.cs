using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TwoG.GpsClient.Configuration;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Transmite as sentenças XGPS (posição) e XATT (atitude) por UDP para os EFBs.
///
/// Formato (protocolo "X-Plane broadcast", porta 49002), uma sentença por datagrama,
/// ASCII, sem terminador, decimal com PONTO (InvariantCulture):
///   XGPS[nome],[lon],[lat],[alt m MSL],[curso verdadeiro],[vel. solo m/s]
///   XATT[nome],[proa verdadeira],[pitch],[roll],,,,,,,,,   (9 campos vazios p/ Garmin Pilot)
///
/// Envia broadcast dirigido por sub-rede de cada interface IPv4 ativa (mais confiável
/// que 255.255.255.255 em máquinas com VPN/múltiplos adaptadores) e, opcionalmente,
/// unicast para IPs configurados.
/// </summary>
public sealed class XgpsBroadcaster : IXgpsBroadcaster
{
    /// <summary>Idade máxima do fix para continuar transmitindo (simulador pausado/menu).</summary>
    private static readonly TimeSpan FixFreshness = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan EndpointRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly ISimSource _source;
    private readonly object _settingsLock = new();

    private UdpClient? _udp;
    private Timer? _xgpsTimer;
    private Timer? _xattTimer;

    private string _deviceName;
    private int _port;
    private double _xgpsHz;
    private double _xattHz;
    private IReadOnlyList<IPAddress> _unicastTargets;

    private volatile IPEndPoint[] _endpoints = [];
    private DateTime _endpointsRefreshedUtc = DateTime.MinValue;

    private long _packetsSent;
    private long _lastSendTicks;

    public XgpsBroadcaster(ISimSource source, AppSettings settings)
    {
        _source = source;
        _deviceName = settings.DeviceName;
        _port = settings.Port;
        _xgpsHz = settings.XgpsHz;
        _xattHz = settings.XattHz;
        _unicastTargets = ParseUnicast(settings.UnicastTargets);
    }

    public bool IsTransmitting
    {
        get
        {
            var fix = _source.LatestFix;
            return _xgpsTimer is not null
                   && fix is not null
                   && DateTime.UtcNow - fix.Utc < FixFreshness;
        }
    }

    public long PacketsSent => Interlocked.Read(ref _packetsSent);

    public DateTime? LastSendUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSendTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void Start()
    {
        lock (_settingsLock)
        {
            if (_xgpsTimer is not null)
                return;

            _udp = new UdpClient { EnableBroadcast = true };
            RestartTimersLocked();
        }
    }

    public void Stop()
    {
        lock (_settingsLock)
        {
            _xgpsTimer?.Dispose();
            _xattTimer?.Dispose();
            _xgpsTimer = null;
            _xattTimer = null;
            _udp?.Dispose();
            _udp = null;
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_settingsLock)
        {
            _deviceName = settings.DeviceName;
            _port = settings.Port;
            _xgpsHz = settings.XgpsHz;
            _xattHz = settings.XattHz;
            _unicastTargets = ParseUnicast(settings.UnicastTargets);
            _endpointsRefreshedUtc = DateTime.MinValue; // força recomputar endpoints

            if (_xgpsTimer is not null)
                RestartTimersLocked();
        }
    }

    public void Dispose() => Stop();

    // ── Envio ───────────────────────────────────────────────────────────

    private void RestartTimersLocked()
    {
        _xgpsTimer?.Dispose();
        _xattTimer?.Dispose();
        var xgpsPeriod = TimeSpan.FromSeconds(1.0 / _xgpsHz);
        var xattPeriod = TimeSpan.FromSeconds(1.0 / _xattHz);
        _xgpsTimer = new Timer(_ => SendXgps(), null, xgpsPeriod, xgpsPeriod);
        _xattTimer = new Timer(_ => SendXatt(), null, xattPeriod, xattPeriod);
    }

    private void SendXgps()
    {
        var fix = FreshFixOrNull();
        if (fix is null) return;
        SendToAll(XgpsSentences.FormatXgps(_deviceName, fix));
    }

    private void SendXatt()
    {
        var fix = FreshFixOrNull();
        if (fix is null) return;
        SendToAll(XgpsSentences.FormatXatt(_deviceName, fix));
    }

    private GpsFix? FreshFixOrNull()
    {
        var fix = _source.LatestFix;
        if (fix is null || DateTime.UtcNow - fix.Utc >= FixFreshness)
            return null;
        return fix;
    }

    private void SendToAll(string sentence)
    {
        var udp = _udp;
        if (udp is null) return;

        var payload = Encoding.ASCII.GetBytes(sentence);
        foreach (var endpoint in CurrentEndpoints())
        {
            try
            {
                udp.Send(payload, payload.Length, endpoint);
                Interlocked.Increment(ref _packetsSent);
                Interlocked.Exchange(ref _lastSendTicks, DateTime.UtcNow.Ticks);
            }
            catch (SocketException)
            {
                // Interface pode ter caído entre o refresh e o envio; próximo refresh corrige.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    // ── Descoberta de endpoints ─────────────────────────────────────────

    private IPEndPoint[] CurrentEndpoints()
    {
        if (DateTime.UtcNow - _endpointsRefreshedUtc < EndpointRefreshInterval)
            return _endpoints;

        lock (_settingsLock)
        {
            if (DateTime.UtcNow - _endpointsRefreshedUtc < EndpointRefreshInterval)
                return _endpoints;

            var targets = new HashSet<IPAddress>();

            foreach (var broadcast in EnumerateSubnetBroadcasts())
                targets.Add(broadcast);

            // Sem interface detectada: recua para o broadcast global.
            if (targets.Count == 0)
                targets.Add(IPAddress.Broadcast);

            foreach (var unicast in _unicastTargets)
                targets.Add(unicast);

            _endpoints = targets.Select(ip => new IPEndPoint(ip, _port)).ToArray();
            _endpointsRefreshedUtc = DateTime.UtcNow;
            return _endpoints;
        }
    }

    private static IEnumerable<IPAddress> EnumerateSubnetBroadcasts()
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
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                var broadcast = NetworkMath.DirectedBroadcast(addr.Address, addr.IPv4Mask);
                if (broadcast is not null)
                    yield return broadcast;
            }
        }
    }

    private static IReadOnlyList<IPAddress> ParseUnicast(string raw)
    {
        var list = new List<IPAddress>();
        foreach (var token in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Só IPv4: o socket é IPv4-only (broadcast dirigido é o caminho primário).
            if (IPAddress.TryParse(token, out var ip)
                && ip.AddressFamily == AddressFamily.InterNetwork)
                list.Add(ip);
        }
        return list;
    }
}
