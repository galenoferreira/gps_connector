using System.Text.Json;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

public class PlnFlightPlanParserTests
{
    private const string SamplePln = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SimBase.Document Type="AceXML" version="1,0">
          <Descr>AceXML Document</Descr>
          <FlightPlan.FlightPlan>
            <Title>SBSP to SBRJ</Title>
            <DepartureID>SBSP</DepartureID>
            <DestinationID>SBRJ</DestinationID>
            <CruisingAlt>12000.000</CruisingAlt>
            <ATCWaypoint id="SBSP">
              <ATCWaypointType>Airport</ATCWaypointType>
              <WorldPosition>S23° 37' 36.00",W46° 39' 22.00",+002630.00</WorldPosition>
              <ICAO><ICAOIdent>SBSP</ICAOIdent></ICAO>
            </ATCWaypoint>
            <ATCWaypoint id="ROSAL">
              <ATCWaypointType>Intersection</ATCWaypointType>
              <WorldPosition>S23° 10' 00.00",W45° 30' 00.00",+012000.00</WorldPosition>
            </ATCWaypoint>
            <ATCWaypoint id="SBRJ">
              <ATCWaypointType>Airport</ATCWaypointType>
              <WorldPosition>S22° 54' 37.00",W43° 09' 47.00",+000011.00</WorldPosition>
              <ICAO><ICAOIdent>SBRJ</ICAOIdent></ICAO>
            </ATCWaypoint>
          </FlightPlan.FlightPlan>
        </SimBase.Document>
        """;

    [Fact]
    public void Parse_ReadsRouteEndsAndCruise()
    {
        var plan = PlnFlightPlanParser.Parse(SamplePln);

        Assert.NotNull(plan);
        Assert.Equal("SBSP", plan!.DepartureId);
        Assert.Equal("SBRJ", plan.DestinationId);
        Assert.Equal(12000, plan.CruiseAltitudeFt);
        Assert.Equal(3, plan.Waypoints.Count);
        Assert.True(plan.IsUsable);
        Assert.Equal("SBSP → SBRJ, 3 waypoints", plan.Summary);
    }

    [Fact]
    public void Parse_ConvertsSexagesimalToDecimalDegrees()
    {
        var plan = PlnFlightPlanParser.Parse(SamplePln)!;
        var origin = plan.Waypoints[0];

        // S23 37' 36" = -(23 + 37/60 + 36/3600) = -23.626667
        Assert.Equal(-23.626667, origin.LatitudeDeg, precision: 5);
        Assert.Equal(-46.656111, origin.LongitudeDeg, precision: 5);
        Assert.Equal(2630, origin.AltitudeFt);
        Assert.Equal(WaypointKind.Airport, origin.Kind);
        Assert.Equal(WaypointKind.Intersection, plan.Waypoints[1].Kind);
    }

    [Theory]
    // Os quatro hemisférios: sinal correto é o que impede o EFB de plotar do outro lado do mundo.
    [InlineData("N52° 22' 42.75\",E13° 31' 14.27\",+000000.00", 52.378542, 13.520631)]
    [InlineData("S33° 52' 04.00\",E151° 12' 36.00\",+000021.00", -33.867778, 151.210000)]
    [InlineData("N40° 38' 23.00\",W073° 46' 44.00\",+000013.00", 40.639722, -73.778889)]
    [InlineData("S23° 37' 36.00\",W046° 39' 22.00\",+002630.00", -23.626667, -46.656111)]
    public void TryParseWorldPosition_HandlesEveryHemisphere(string raw, double expectedLat, double expectedLon)
    {
        Assert.True(PlnFlightPlanParser.TryParseWorldPosition(raw, out var lat, out var lon, out _));
        Assert.Equal(expectedLat, lat, precision: 5);
        Assert.Equal(expectedLon, lon, precision: 5);
    }

    [Fact]
    public void TryParseWorldPosition_RejectsGarbage()
    {
        Assert.False(PlnFlightPlanParser.TryParseWorldPosition("", out _, out _, out _));
        Assert.False(PlnFlightPlanParser.TryParseWorldPosition("nao é coordenada", out _, out _, out _));
        Assert.False(PlnFlightPlanParser.TryParseWorldPosition("N52° 22' 42.75\"", out _, out _, out _));
    }

    [Fact]
    public void Parse_DirectRouteWithoutIntermediateWaypointsIsUsable()
    {
        var xml = SamplePln.Replace("""
            <ATCWaypoint id="ROSAL">
              <ATCWaypointType>Intersection</ATCWaypointType>
              <WorldPosition>S23° 10' 00.00",W45° 30' 00.00",+012000.00</WorldPosition>
            </ATCWaypoint>
        """, "");

        var plan = PlnFlightPlanParser.Parse(xml)!;
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.True(plan.IsUsable);
    }

    [Fact]
    public void Parse_SkipsWaypointsWithoutPosition()
    {
        var xml = SamplePln.Replace(
            "<WorldPosition>S23° 10' 00.00\",W45° 30' 00.00\",+012000.00</WorldPosition>", "");
        var plan = PlnFlightPlanParser.Parse(xml)!;
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.DoesNotContain(plan.Waypoints, w => w.Id == "ROSAL");
    }

    [Fact]
    public void Parse_ReturnsNullForNonPlnContent()
    {
        Assert.Null(PlnFlightPlanParser.Parse("xml quebrado <<<"));
        Assert.Null(PlnFlightPlanParser.Parse("<SimBase.Document><Outro/></SimBase.Document>"));
    }
}

public class FmsFlightPlanParserTests
{
    // Exemplo da documentação oficial do X-Plane.
    private const string SampleFms = """
        I
        1100 Version
        CYCLE 1710
        ADEP KCUB
        DEPRWY RW13
        ADES KRDU
        DESRWY RW05L
        APP I05L
        NUMENR 4
        1 KCUB ADEP 0.000000 33.970470 -80.995247
        3 CTF DRCT 0.000000 34.650497 -80.274918
        11 NOMOE V155 0.000000 34.880920 -79.996437
        1 KRDU ADES 435.000000 35.877640 -78.787476
        """;

    [Fact]
    public void Parse_ReadsHeaderAndWaypoints()
    {
        var plan = FmsFlightPlanParser.Parse(SampleFms);

        Assert.NotNull(plan);
        Assert.Equal("KCUB", plan!.DepartureId);
        Assert.Equal("KRDU", plan.DestinationId);
        Assert.Equal(4, plan.Waypoints.Count);
        Assert.True(plan.IsUsable);
    }

    [Fact]
    public void Parse_MapsTypesAndDecimalCoordinates()
    {
        var plan = FmsFlightPlanParser.Parse(SampleFms)!;

        Assert.Equal(WaypointKind.Airport, plan.Waypoints[0].Kind);
        Assert.Equal(WaypointKind.Vor, plan.Waypoints[1].Kind);
        Assert.Equal(WaypointKind.Intersection, plan.Waypoints[2].Kind);

        Assert.Equal(33.970470, plan.Waypoints[0].LatitudeDeg, precision: 5);
        Assert.Equal(-80.995247, plan.Waypoints[0].LongitudeDeg, precision: 5);
        // Altitude 0 no .fms significa "sem restrição", não nível do mar.
        Assert.Null(plan.Waypoints[0].AltitudeFt);
        Assert.Equal(435, plan.Waypoints[3].AltitudeFt);
    }

    [Fact]
    public void Parse_ToleratesWindowsLineEndings()
    {
        var plan = FmsFlightPlanParser.Parse(SampleFms.Replace("\n", "\r\n"));
        Assert.NotNull(plan);
        Assert.Equal(4, plan!.Waypoints.Count);
    }

    [Fact]
    public void Parse_IgnoresMalformedWaypointLines()
    {
        var plan = FmsFlightPlanParser.Parse(SampleFms + "\n11 QUEBRADO DRCT\n99 FORA 999 0 91.0 0.0")!;
        Assert.Equal(4, plan.Waypoints.Count);   // linha curta e latitude inválida descartadas
    }

    [Fact]
    public void Parse_ReturnsNullForForeignContent()
    {
        Assert.Null(FmsFlightPlanParser.Parse(""));
        Assert.Null(FmsFlightPlanParser.Parse("isto aqui não é plano nenhum"));
    }
}

public class FlightPlanJsonTests
{
    [Fact]
    public void Serialize_ProducesTheAgreedContract()
    {
        var plan = new FlightPlan("SBSP", "SBRJ", 12000,
        [
            new FlightPlanWaypoint("SBSP", WaypointKind.Airport, -23.626667, -46.656111, 2630),
            new FlightPlanWaypoint("SBRJ", WaypointKind.Airport, -22.910278, -43.163056, null),
        ]);

        var json = FlightPlanJson.Serialize(plan, "MSFS 2024", new DateTime(2026, 8, 15, 19, 40, 0, DateTimeKind.Utc));
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("MSFS 2024", root.GetProperty("source").GetString());
        Assert.Equal("SBSP", root.GetProperty("departure").GetString());
        Assert.Equal("SBRJ", root.GetProperty("destination").GetString());
        Assert.Equal(12000, root.GetProperty("cruiseAltitudeFt").GetDouble());

        var waypoints = root.GetProperty("waypoints");
        Assert.Equal(2, waypoints.GetArrayLength());
        Assert.Equal("SBSP", waypoints[0].GetProperty("id").GetString());
        Assert.Equal("Airport", waypoints[0].GetProperty("type").GetString());
        Assert.Equal(-23.626667, waypoints[0].GetProperty("lat").GetDouble(), precision: 6);
        Assert.Equal(2630, waypoints[0].GetProperty("altFt").GetDouble());
        // Altitude ausente não vira 0 — o campo simplesmente não aparece.
        Assert.False(waypoints[1].TryGetProperty("altFt", out _));
    }

    [Fact]
    public void Serialize_UsesInvariantNumbersRegardlessOfCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var plan = new FlightPlan("A", "B", null,
                [new FlightPlanWaypoint("A", WaypointKind.User, -23.5, -46.6, null)]);
            Assert.Contains("-23.5", FlightPlanJson.Serialize(plan, "X", DateTime.UtcNow));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}

public class FlightPlanAnnounceTests
{
    [Fact]
    public void Announce_HasKeywordNameAndUrl()
    {
        var sentence = XgpsSentences.FormatFlightPlanAnnounce(
            "2G GPS", 1, "http://192.168.1.10:49003/flightplan");
        Assert.Equal("2GFPL2G GPS,1,http://192.168.1.10:49003/flightplan", sentence);
        // Mesma convenção do XGPS: nome colado no keyword, campos por vírgula.
        Assert.StartsWith("2GFPL", sentence);
    }

    [Fact]
    public void Announce_FitsInOneDatagram()
    {
        var sentence = XgpsSentences.FormatFlightPlanAnnounce(
            "2G GPS", FlightPlanJson.SchemaVersion, "http://255.255.255.255:65535/flightplan");
        Assert.True(System.Text.Encoding.ASCII.GetByteCount(sentence) < 1472);
    }
}
