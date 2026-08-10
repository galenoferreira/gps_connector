using System.Globalization;
using System.Net;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

public class XgpsSentencesTests
{
    private static GpsFix Fix(
        double lat = 34.55678, double lon = -80.11234, double altM = 365.8,
        double trk = 231.245, double gsMps = 57.2, double hdg = 180.2,
        double pitchUp = 0.1, double rollRight = 0.2) =>
        new(DateTime.UtcNow, lat, lon, altM, trk, gsMps, hdg, pitchUp, rollRight, OnGround: false);

    [Fact]
    public void Xgps_MatchesReferenceShape_LongitudeFirst()
    {
        var s = XgpsSentences.FormatXgps("2G GPS", Fix());
        Assert.Equal("XGPS2G GPS,-80.11234,34.55678,365.8,231.245,57.2", s);
    }

    [Fact]
    public void Xatt_HasThreeValues_AndNinePaddingFieldsForGarminPilot()
    {
        var s = XgpsSentences.FormatXatt("2G GPS", Fix());
        Assert.Equal("XATT2G GPS,180.2,0.1,0.2,,,,,,,,,", s);
        // 13 campos no total após o nome: 3 valores + 9 vazios ⇒ 12 vírgulas depois do nome.
        Assert.Equal(12, s.Count(c => c == ','));
    }

    [Fact]
    public void Sentences_UseInvariantDecimalPoint_UnderBrazilianCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var xgps = XgpsSentences.FormatXgps("2G GPS", Fix());
            var xatt = XgpsSentences.FormatXatt("2G GPS", Fix());
            Assert.Contains(".", xgps);
            Assert.DoesNotContain(",-80,", xgps); // vírgula decimal quebraria o protocolo
            Assert.Equal("XGPS2G GPS,-80.11234,34.55678,365.8,231.245,57.2", xgps);
            Assert.Equal("XATT2G GPS,180.2,0.1,0.2,,,,,,,,,", xatt);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Xgps_ZeroValues_FormatCompactly()
    {
        var s = XgpsSentences.FormatXgps("X", Fix(lat: 0, lon: 0, altM: 0, trk: 0, gsMps: 0));
        Assert.Equal("XGPSX,0,0,0,0,0", s);
    }

    [Fact]
    public void Xatt_NegativePitchAndRoll_KeepSign()
    {
        var s = XgpsSentences.FormatXatt("X", Fix(hdg: 359.96, pitchUp: -2.3, rollRight: -15.7));
        Assert.Equal("XATTX,360,-2.3,-15.7,,,,,,,,,", s);
    }

    [Theory]
    [InlineData(-10, 350)]
    [InlineData(370.5, 10.5)]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(-360, 0)]
    [InlineData(725, 5)]
    public void Normalize360_WrapsIntoRange(double input, double expected)
    {
        Assert.Equal(expected, XgpsSentences.Normalize360(input), precision: 6);
    }

    [Theory]
    [InlineData("João Pilão", "Joao Pilao")]     // acentos decompostos, não '?'
    [InlineData("2G GPS", "2G GPS")]
    [InlineData("a,b", "a b")]                    // vírgula é o separador do protocolo
    [InlineData("  éçã  ", "eca")]
    [InlineData("日本", "")]                      // sem equivalente ASCII → vazio
    public void SanitizeDeviceName_ProducesCleanAscii(string input, string expected)
    {
        Assert.Equal(expected, XgpsSentences.SanitizeDeviceName(input));
    }
}

public class NetworkMathTests
{
    [Theory]
    [InlineData("192.168.1.42", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.0.5", "255.0.0.0", "10.255.255.255")]
    [InlineData("172.16.31.7", "255.255.240.0", "172.16.31.255")]
    public void DirectedBroadcast_ComputesSubnetBroadcast(string ip, string mask, string expected)
    {
        var result = NetworkMath.DirectedBroadcast(IPAddress.Parse(ip), IPAddress.Parse(mask));
        Assert.Equal(IPAddress.Parse(expected), result);
    }

    [Fact]
    public void DirectedBroadcast_InvalidMaskOrFamily_ReturnsNull()
    {
        Assert.Null(NetworkMath.DirectedBroadcast(IPAddress.Parse("192.168.1.42"), null));
        Assert.Null(NetworkMath.DirectedBroadcast(IPAddress.Parse("192.168.1.42"), IPAddress.Parse("0.0.0.0")));
        Assert.Null(NetworkMath.DirectedBroadcast(IPAddress.Parse("::1"), IPAddress.Parse("255.255.255.0")));
    }

    [Fact]
    public void DirectedBroadcast_HostAndPointToPointMasks_ReturnNull()
    {
        // /32 (VPN) e /31 (ponto-a-ponto) não têm broadcast dirigido útil.
        Assert.Null(NetworkMath.DirectedBroadcast(IPAddress.Parse("100.101.102.103"), IPAddress.Parse("255.255.255.255")));
        Assert.Null(NetworkMath.DirectedBroadcast(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.255.255.254")));
    }
}
