using System.Net;
using System.Text;
using TwoG.Connector.Core;

namespace TwoG.Connector.Core.Tests;

public class EfbAnnouncementTests
{
    private static bool Parse(string json, out EfbAnnouncement announcement) =>
        EfbAnnouncement.TryParse(Encoding.UTF8.GetBytes(json), out announcement);

    /// <summary>O payload exato que o 2G Pilot emite.</summary>
    [Fact]
    public void TryParse_AcceptsTheExactPayloadFromTheApp()
    {
        Assert.True(Parse("""{"App":"2G Pilot","GDL90":{"port":4000}}""", out var a));
        Assert.Equal("2G Pilot", a.AppName);
        Assert.Equal(4000, a.Gdl90Port);
    }

    [Fact]
    public void TryParse_AcceptsForeFlightToo()
    {
        Assert.True(Parse("""{"App":"ForeFlight","GDL90":{"port":4000}}""", out var a));
        Assert.Equal("ForeFlight", a.AppName);
    }

    /// <summary>GDL 90 é opcional: sem ele o app ainda é um destino válido.</summary>
    [Fact]
    public void TryParse_AcceptsAnnouncementWithoutGdl90()
    {
        Assert.True(Parse("""{"App":"2G Pilot"}""", out var a));
        Assert.Equal("2G Pilot", a.AppName);
        Assert.Null(a.Gdl90Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("\"texto\"")]
    [InlineData("{}")]
    [InlineData("""{"app":"2G Pilot"}""")]          // chave é sensível a caixa
    [InlineData("""{"App":""}""")]
    [InlineData("""{"App":"   "}""")]
    [InlineData("""{"App":123}""")]
    [InlineData("XGPS2G GPS,-80.1,34.5,365,231,57")]  // nossa própria sentença
    public void TryParse_RejectsAnythingThatIsNotAnAnnouncement(string payload)
    {
        Assert.False(Parse(payload, out _));
    }

    /// <summary>Porta compartilhada recebe lixo; nada disso pode virar destino.</summary>
    [Fact]
    public void TryParse_RejectsOversizedPayload()
    {
        var padding = new string('x', 600);
        Assert.False(Parse($$"""{"App":"2G Pilot","pad":"{{padding}}"}""", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    [InlineData(-1)]
    public void TryParse_IgnoresOutOfRangeGdl90Port(int port)
    {
        Assert.True(Parse($$$"""{"App":"2G Pilot","GDL90":{"port":{{{port}}}}}""", out var a));
        Assert.Null(a.Gdl90Port);
    }
}

public class DiscoveredEfbRegistryTests
{
    private static readonly DateTime T0 = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static IPAddress Ip(int last) => IPAddress.Parse($"192.168.1.{last}");

    [Fact]
    public void Touch_ReportsFirstSightingAsNew()
    {
        var registry = new DiscoveredEfbRegistry();
        Assert.True(registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0));
        Assert.False(registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0.AddSeconds(5)));
        Assert.Single(registry.Active(T0.AddSeconds(5)));
    }

    /// <summary>Dois iPads na mesma cabine é caso real.</summary>
    [Fact]
    public void Active_KeepsSeveralAppsAtOnce()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0);
        registry.Touch(Ip(51), "2G Pilot", EfbDiscoverySource.Bonjour, T0);

        var active = registry.Active(T0);
        Assert.Equal(2, active.Count);
        Assert.Equal([Ip(51), Ip(50)], registry.ActiveAddresses(T0).OrderByDescending(a => a.ToString()).ToArray());
    }

    /// <summary>
    /// O requisito central da expiração: parou de anunciar, o destino é solto e o
    /// broadcast volta a ser o único caminho.
    /// </summary>
    [Fact]
    public void Active_DropsTargetAfterThirtySecondsOfSilence()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0);

        Assert.Single(registry.Active(T0.AddSeconds(29)));
        Assert.Empty(registry.Active(T0.AddSeconds(30)));
        Assert.Empty(registry.ActiveAddresses(T0.AddSeconds(45)));
    }

    [Fact]
    public void Touch_RenewsTheDeadline()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0);
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0.AddSeconds(25));

        // Sem a renovação teria morrido aos 30 s.
        Assert.Single(registry.Active(T0.AddSeconds(50)));
        Assert.Empty(registry.Active(T0.AddSeconds(56)));
    }

    [Fact]
    public void Touch_HonoursTheTargetCeiling()
    {
        var registry = new DiscoveredEfbRegistry();
        for (var i = 0; i < DiscoveredEfbRegistry.MaxTargets; i++)
            Assert.True(registry.Touch(Ip(50 + i), "2G Pilot", EfbDiscoverySource.Announcement, T0));

        Assert.False(registry.Touch(Ip(99), "Intruso", EfbDiscoverySource.Announcement, T0));
        Assert.Equal(DiscoveredEfbRegistry.MaxTargets, registry.Active(T0).Count);
        Assert.DoesNotContain(registry.ActiveAddresses(T0), a => a.Equals(Ip(99)));
    }

    /// <summary>Lista cheia de destinos mortos não pode barrar quem está falando agora.</summary>
    [Fact]
    public void Touch_ExpiredTargetYieldsItsSlot()
    {
        var registry = new DiscoveredEfbRegistry();
        for (var i = 0; i < DiscoveredEfbRegistry.MaxTargets; i++)
            registry.Touch(Ip(50 + i), "2G Pilot", EfbDiscoverySource.Announcement, T0);

        var later = T0.AddSeconds(31);
        Assert.True(registry.Touch(Ip(99), "2G Pilot", EfbDiscoverySource.Announcement, later));
        Assert.Equal([Ip(99)], registry.ActiveAddresses(later));
    }

    [Fact]
    public void Active_OrdersByMostRecentlySeen()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "primeiro", EfbDiscoverySource.Announcement, T0);
        registry.Touch(Ip(51), "segundo", EfbDiscoverySource.Announcement, T0.AddSeconds(2));

        Assert.Equal("segundo", registry.Active(T0.AddSeconds(2))[0].AppName);
    }

    [Fact]
    public void Touch_KeepsTheGdl90PortAndSource()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Bonjour, T0, gdl90Port: 4000);

        var efb = registry.Active(T0)[0];
        Assert.Equal(4000, efb.Gdl90Port);
        Assert.Equal(EfbDiscoverySource.Bonjour, efb.Source);
    }

    /// <summary>Bonjour e anúncio alimentam a mesma lista, sem duplicar o IP.</summary>
    [Fact]
    public void Touch_SameAddressFromBothPathsIsOneTarget()
    {
        var registry = new DiscoveredEfbRegistry();
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Bonjour, T0);
        registry.Touch(Ip(50), "2G Pilot", EfbDiscoverySource.Announcement, T0.AddSeconds(1));

        Assert.Single(registry.Active(T0.AddSeconds(1)));
    }
}
