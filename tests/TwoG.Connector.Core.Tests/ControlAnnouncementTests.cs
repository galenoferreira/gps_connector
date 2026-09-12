using System.Net;

namespace TwoG.Connector.Core.Tests;

public class ControlAnnouncementTests
{
    private static readonly (IPAddress Address, IPAddress Mask)[] WifiAndEthernet =
    [
        (IPAddress.Parse("192.168.68.102"), IPAddress.Parse("255.255.255.0")),
        (IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.0.0.0")),
    ];

    private static IPAddress? NoRoute(IPAddress _) => null;

    [Theory]
    [InlineData("192.168.68.255", "192.168.68.102")]   // broadcast dirigido da Wi-Fi
    [InlineData("192.168.68.101", "192.168.68.102")]   // iPad na Wi-Fi, em unicast
    [InlineData("10.255.255.255", "10.0.0.5")]         // broadcast da Ethernet
    public void SourceIsTheInterfaceOfTheDestinationSubnet(string destination, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), NetworkMath.SourceAddressFor(IPAddress.Parse(destination), WifiAndEthernet));
    }

    [Fact]
    public void DestinationOutsideEverySubnetHasNoInterface()
    {
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.Parse("172.16.0.9"), WifiAndEthernet));
    }

    [Fact]
    public void ZeroMaskAndIpv6AreIgnored()
    {
        (IPAddress, IPAddress)[] odd = [(IPAddress.Parse("192.168.1.2"), IPAddress.Any), (IPAddress.IPv6Loopback, IPAddress.Parse("255.255.255.0"))];
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.Parse("192.168.1.9"), odd));
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.IPv6Loopback, WifiAndEthernet));
    }

    [Fact]
    public void SentenceFollowsTheSpec()
    {
        var url = ControlAnnouncement.Url(IPAddress.Parse("192.168.68.102"), 49004);

        Assert.Equal("ws://192.168.68.102:49004/control", url);
        Assert.Equal("2GCTL2G Connector,1,ws://192.168.68.102:49004/control", ControlAnnouncement.Sentence("2G Connector", url));
    }

    [Fact]
    public void CommaInTheDeviceNameCannotBreakTheSentence()
    {
        Assert.StartsWith("2GCTLMeu PC 1,1,", ControlAnnouncement.Sentence("Meu PC,1","ws://h:1/control"));
    }

    [Theory]
    [InlineData("10.0.0.255", "10.0.0.7")]    // broadcast da LAN /24
    [InlineData("10.0.0.50", "10.0.0.7")]     // EFB descoberto na LAN
    [InlineData("10.200.0.1", "10.8.0.2")]    // só a VPN /8 contém
    public void OverlappingSubnetsPickTheLongestMaskWhateverTheOrder(string destination, string expected)
    {
        (IPAddress, IPAddress) vpn = (IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.0.0.0"));
        (IPAddress, IPAddress) lan = (IPAddress.Parse("10.0.0.7"), IPAddress.Parse("255.255.255.0"));

        Assert.Equal(IPAddress.Parse(expected), NetworkMath.SourceAddressFor(IPAddress.Parse(destination), [vpn, lan]));
        Assert.Equal(IPAddress.Parse(expected), NetworkMath.SourceAddressFor(IPAddress.Parse(destination), [lan, vpn]));
    }

    [Fact]
    public void TheRouteDecidesBetweenTwoCardsOnTheSameSubnet()
    {
        // Wi-Fi enumerada antes da Ethernet, as duas na mesma /24: o broadcast sai pela de
        // menor métrica, e só a rota do sistema sabe qual é.
        (IPAddress, IPAddress)[] wifiThenEthernet =
        [
            (IPAddress.Parse("192.168.1.20"), IPAddress.Parse("255.255.255.0")),
            (IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0")),
        ];

        var sentence = ControlAnnouncement.SentenceFor(IPAddress.Parse("192.168.1.255"), "X", 49004, wifiThenEthernet,
            _ => IPAddress.Parse("192.168.1.10"));

        Assert.Equal("2GCTLX,1,ws://192.168.1.10:49004/control", sentence);
    }

    [Fact]
    public void WithoutRouteTheSubnetOfTheDestinationDecides()
    {
        var sentence = ControlAnnouncement.SentenceFor(IPAddress.Parse("10.255.255.255"), "2G Connector", 49004, WifiAndEthernet, NoRoute);
        Assert.Equal("2GCTL2G Connector,1,ws://10.0.0.5:49004/control", sentence);
    }

    [Fact]
    public void SentenceForUsesTheRouteOutsideEverySubnet()
    {
        var sentence = ControlAnnouncement.SentenceFor(IPAddress.Parse("172.16.0.9"), "X", 49004, WifiAndEthernet,
            _ => IPAddress.Parse("172.16.0.1"));
        Assert.Equal("2GCTLX,1,ws://172.16.0.1:49004/control", sentence);
    }

    [Fact]
    public void WithoutInterfaceOrRouteThereIsNoSentence()
    {
        Assert.Null(ControlAnnouncement.SentenceFor(IPAddress.Parse("172.16.0.9"), "X", 49004, WifiAndEthernet, NoRoute));
    }
}
