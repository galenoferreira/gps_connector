using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Emite o anúncio 2GCTL a cada 5 s pelos mesmos destinos do XGPS (broadcast dirigido de
/// cada interface e EFBs descobertos), com o host da URL certo para cada um.
/// Melhor esforço: nenhuma falha aqui interrompe o XGPS.
/// </summary>
internal sealed class ControlAnnouncer : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly IXgpsBroadcaster _broadcaster;
    private readonly Func<string> _deviceName;
    private readonly Func<int> _port;
    private Timer? _timer;

    public ControlAnnouncer(IXgpsBroadcaster broadcaster, Func<string> deviceName, Func<int> port)
    {
        _broadcaster = broadcaster;
        _deviceName = deviceName;
        _port = port;
    }

    public void Start() => _timer ??= new Timer(_ => Announce(), null, TimeSpan.Zero, Interval);

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private void Announce()
    {
        try
        {
            var interfaces = LocalInterfaces();
            var name = _deviceName();
            var port = _port();
            _broadcaster.SendToEach(endpoint =>
                ControlAnnouncement.SentenceFor(endpoint.Address, name, port, interfaces, RouteSource));
        }
        catch (Exception)
        {
            // Anúncio é melhor esforço; o próximo tique tenta de novo.
        }
    }

    private static IReadOnlyList<(IPAddress Address, IPAddress Mask)> LocalInterfaces()
    {
        var list = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    list.Add((unicast.Address, unicast.IPv4Mask));
            }
        }
        return list;
    }

    /// <summary>IP de saída que o Windows escolheria para o destino. Não envia nada.</summary>
    private static IPAddress? RouteSource(IPAddress destination)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(destination, 9);
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
