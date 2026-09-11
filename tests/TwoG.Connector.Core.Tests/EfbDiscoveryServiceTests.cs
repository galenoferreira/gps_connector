using System.Net;
using System.Net.Sockets;
using System.Text;
using TwoG.Connector.Core;

namespace TwoG.Connector.Core.Tests;

/// <summary>
/// Integração de verdade: sobe o serviço, manda datagramas reais na UDP 63093 e
/// confere o que ele descobriu. É o único jeito de provar o caminho inteiro —
/// socket, parser e registro — sem um iPad na mesa.
///
/// Os datagramas saem do loopback, então o IP descoberto é 127.0.0.1.
/// </summary>
[Collection("EfbDiscovery")]
public class EfbDiscoveryServiceTests
{
    private const string RealPayload = """{"App":"2G Pilot","GDL90":{"port":4000}}""";

    private static void SendAnnouncement(string payload)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var bytes = Encoding.UTF8.GetBytes(payload);
        udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, EfbAnnouncement.AnnouncePort));
    }

    /// <summary>Espera a condição em vez de dormir um tempo fixo e torcer.</summary>
    private static bool WaitFor(Func<bool> condition, int timeoutMs = 4000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    [Fact]
    public void Announcement_MakesTheAppShowUpAsAUnicastTarget()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();

        // O socket leva um instante para subir; repetir o anúncio imita o app,
        // que emite a cada 5 s de qualquer forma.
        Assert.True(WaitFor(() =>
        {
            SendAnnouncement(RealPayload);
            return discovery.Targets.Count > 0;
        }), $"nenhum destino descoberto. Erro: {discovery.LastError}");

        Assert.Contains(IPAddress.Loopback, discovery.Targets);

        var efb = discovery.Discovered.Single(e => e.Address.Equals(IPAddress.Loopback));
        Assert.Equal("2G Pilot", efb.AppName);
        Assert.Equal(EfbDiscoverySource.Announcement, efb.Source);
        Assert.Equal(4000, efb.Gdl90Port);
        Assert.True(discovery.AnnouncementsReceived > 0);
    }

    /// <summary>
    /// Tráfego alheio na porta compartilhada não pode virar destino de transmissão.
    /// </summary>
    [Fact]
    public void Announcement_GarbageOnThePortIsIgnored()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();

        Assert.True(WaitFor(() =>
        {
            SendAnnouncement("isto não é um anúncio");
            SendAnnouncement("{}");
            SendAnnouncement("XGPS2G GPS,-80.1,34.5,365,231,57");
            return discovery.LastError is null;
        }));

        Thread.Sleep(300);
        Assert.Empty(discovery.Targets);
        Assert.Equal(0, discovery.AnnouncementsReceived);
    }

    [Fact]
    public void Stop_ReleasesEveryDiscoveredTarget()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();

        Assert.True(WaitFor(() =>
        {
            SendAnnouncement(RealPayload);
            return discovery.Targets.Count > 0;
        }));

        discovery.Stop();
        Assert.Empty(discovery.Targets);
        Assert.Empty(discovery.Discovered);
    }

    /// <summary>Sem nenhum app na rede, a lista fica vazia e nada explode.</summary>
    [Fact]
    public void WithNoAppOnTheNetwork_ThereAreNoTargets()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();
        Thread.Sleep(400);

        Assert.Empty(discovery.Targets);
        Assert.Equal(0, discovery.AnnouncementsReceived);
    }

    /// <summary>Start repetido não pode duplicar threads nem socket.</summary>
    [Fact]
    public void Start_IsIdempotent()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();
        discovery.Start();

        Assert.True(WaitFor(() =>
        {
            SendAnnouncement(RealPayload);
            return discovery.Targets.Count > 0;
        }));

        Assert.Single(discovery.Targets);
    }
}

/// <summary>
/// Os testes acima disputam a mesma porta UDP, então não podem correr em paralelo
/// entre si.
/// </summary>
[CollectionDefinition("EfbDiscovery", DisableParallelization = true)]
public class EfbDiscoveryCollection;
