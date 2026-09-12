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

    /// <summary>
    /// Só os destinos de loopback, isto é, os dos datagramas do próprio teste. Um
    /// EFB real aberto na rede do desenvolvedor (2G Pilot no iPad ou no simulador)
    /// também entra na lista, por anúncio ou Bonjour, e com razão: o serviço aceita
    /// qualquer host. Quem não pode presumir a rede vazia é o teste.
    /// </summary>
    private static IPAddress[] LoopbackTargets(EfbDiscoveryService discovery) =>
        discovery.Targets.Where(IPAddress.IsLoopback).ToArray();

    /// <summary>
    /// <see cref="EfbDiscoveryService.AnnouncementsReceived"/> soma anúncios de
    /// qualquer host e não dá para filtrar. Com a rede quieta tem de ficar em zero;
    /// se andou, foi um EFB real, que então está na lista fora do loopback.
    /// </summary>
    private static void AssertOnlyRealEfbsWereCounted(EfbDiscoveryService discovery)
    {
        if (discovery.AnnouncementsReceived == 0)
            return;

        // O contador sobe um instante antes do registro; esperar evita a corrida.
        Assert.True(
            WaitFor(() => discovery.Discovered.Any(e => !IPAddress.IsLoopback(e.Address)), 1000),
            $"{discovery.AnnouncementsReceived} anúncio(s) contado(s) sem nenhum EFB real na lista");
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
            return LoopbackTargets(discovery).Length > 0;
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
        Assert.Empty(LoopbackTargets(discovery));
        AssertOnlyRealEfbsWereCounted(discovery);
    }

    [Fact]
    public void Stop_ReleasesEveryDiscoveredTarget()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();

        Assert.True(WaitFor(() =>
        {
            SendAnnouncement(RealPayload);
            return LoopbackTargets(discovery).Length > 0;
        }));

        // Sem filtro de propósito: Stop solta tudo, EFBs reais inclusive, e com as
        // threads paradas a rede não tem como repor nada.
        discovery.Stop();
        Assert.Empty(discovery.Targets);
        Assert.Empty(discovery.Discovered);
    }

    /// <summary>Sem nenhum app anunciando daqui, a lista fica vazia e nada explode.</summary>
    [Fact]
    public void WithNoAppOnTheNetwork_ThereAreNoTargets()
    {
        using var discovery = new EfbDiscoveryService();
        discovery.Start();
        Thread.Sleep(400);

        Assert.Empty(LoopbackTargets(discovery));
        AssertOnlyRealEfbsWereCounted(discovery);
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
            return LoopbackTargets(discovery).Length > 0;
        }));

        Assert.Single(LoopbackTargets(discovery));
    }
}

/// <summary>
/// Os testes acima disputam a mesma porta UDP, então não podem correr em paralelo
/// entre si.
/// </summary>
[CollectionDefinition("EfbDiscovery", DisableParallelization = true)]
public class EfbDiscoveryCollection;
