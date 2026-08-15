using System.Buffers.Binary;
using System.Text;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

public class XPlaneProtocolTests
{
    [Fact]
    public void RrefRequest_HasExactWireLayout()
    {
        var packet = XPlaneProtocol.BuildRrefRequest(frequencyHz: 10, index: 3,
            dataref: "sim/flightmodel/position/latitude");

        Assert.Equal(413, packet.Length);
        Assert.Equal("RREF\0", Encoding.ASCII.GetString(packet, 0, 5));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(5, 4)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(9, 4)));

        var path = Encoding.ASCII.GetString(packet, 13, packet.Length - 13).TrimEnd('\0');
        Assert.Equal("sim/flightmodel/position/latitude", path);
        // O campo do caminho precisa estar terminado em \0 dentro dos 400 bytes.
        Assert.Equal(0, packet[13 + path.Length]);
    }

    [Fact]
    public void RrefRequest_FrequencyZero_UnsubscribesAndStaysValid()
    {
        var packet = XPlaneProtocol.BuildRrefRequest(0, 7, "sim/time/paused");
        Assert.Equal(413, packet.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(5, 4)));
    }

    [Fact]
    public void RrefRequest_RejectsOversizedDataref()
    {
        Assert.Throws<ArgumentException>(() =>
            XPlaneProtocol.BuildRrefRequest(1, 0, new string('x', 400)));
    }

    [Fact]
    public void RrefResponse_ParsesIndexValuePairs()
    {
        var packet = BuildRrefResponse((0, 34.55678f), (1, -80.11234f), (2, 365.8f));

        Span<(int Index, float Value)> parsed = stackalloc (int, float)[8];
        var count = XPlaneProtocol.ParseRrefResponse(packet, parsed);

        Assert.Equal(3, count);
        Assert.Equal(0, parsed[0].Index);
        Assert.Equal(34.55678f, parsed[0].Value, precision: 4);
        Assert.Equal(1, parsed[1].Index);
        Assert.Equal(-80.11234f, parsed[1].Value, precision: 4);
        Assert.Equal(2, parsed[2].Index);
        Assert.Equal(365.8f, parsed[2].Value, precision: 3);
    }

    [Fact]
    public void RrefResponse_IgnoresForeignPackets()
    {
        Span<(int, float)> parsed = stackalloc (int, float)[4];
        Assert.Equal(0, XPlaneProtocol.ParseRrefResponse("DATA@..."u8, parsed));
        Assert.Equal(0, XPlaneProtocol.ParseRrefResponse([], parsed));
        Assert.Equal(0, XPlaneProtocol.ParseRrefResponse("RR"u8, parsed));
    }

    [Fact]
    public void RrefResponse_TruncatesToDestinationCapacity()
    {
        var packet = BuildRrefResponse((0, 1f), (1, 2f), (2, 3f), (3, 4f));
        Span<(int, float)> parsed = stackalloc (int, float)[2];
        Assert.Equal(2, XPlaneProtocol.ParseRrefResponse(packet, parsed));
    }

    [Fact]
    public void Beacon_ParsesMasterAnnouncement()
    {
        var packet = BuildBeacon(versionNumber: 121400, role: 1, port: 49000, hostname: "SIM-PC");

        Assert.True(XPlaneProtocol.TryParseBeacon(packet, out var beacon));
        Assert.NotNull(beacon);
        Assert.Equal(49000, beacon!.Port);
        Assert.Equal(121400, beacon.VersionNumber);
        Assert.True(beacon.IsMaster);
        Assert.Equal("SIM-PC", beacon.Hostname);
        Assert.Equal("X-Plane 12", beacon.DisplayName);
    }

    [Fact]
    public void Beacon_ExternalVisualIsNotMaster()
    {
        // role 2 = máquina de visual externo; não é quem simula.
        var packet = BuildBeacon(121400, role: 2, port: 49000, hostname: "VISUAL");
        Assert.True(XPlaneProtocol.TryParseBeacon(packet, out var beacon));
        Assert.False(beacon!.IsMaster);
    }

    [Fact]
    public void Beacon_RejectsOtherApplicationsAndGarbage()
    {
        Assert.False(XPlaneProtocol.TryParseBeacon("BECN\0"u8, out _));      // curto demais
        Assert.False(XPlaneProtocol.TryParseBeacon("RREF,xxxxxxxx"u8, out _)); // outro protocolo
        Assert.False(XPlaneProtocol.TryParseBeacon([], out _));

        var wrongApp = BuildBeacon(121400, 1, 49000, "X", applicationHostId: 2);
        Assert.False(XPlaneProtocol.TryParseBeacon(wrongApp, out _));
    }

    [Fact]
    public void Beacon_ZeroPortFallsBackToDefault()
    {
        var packet = BuildBeacon(121400, 1, port: 0, hostname: "PC");
        Assert.True(XPlaneProtocol.TryParseBeacon(packet, out var beacon));
        Assert.Equal(XPlaneProtocol.DefaultCommandPort, beacon!.Port);
    }

    [Theory]
    [InlineData(121400, "X-Plane 12")]
    [InlineData(115000, "X-Plane 11")]
    [InlineData(0, "X-Plane")]
    public void Beacon_DisplayNameFromVersion(int versionNumber, string expected)
    {
        var packet = BuildBeacon(versionNumber, 1, 49000, "PC");
        Assert.True(XPlaneProtocol.TryParseBeacon(packet, out var beacon));
        Assert.Equal(expected, beacon!.DisplayName);
    }

    // ── Auxiliares que montam pacotes como o X-Plane os emite ───────────

    private static byte[] BuildRrefResponse(params (int Index, float Value)[] values)
    {
        var packet = new byte[5 + values.Length * 8];
        "RREF,"u8.CopyTo(packet);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5 + i * 8, 4), values[i].Index);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(9 + i * 8, 4), values[i].Value);
        }
        return packet;
    }

    private static byte[] BuildBeacon(int versionNumber, uint role, ushort port, string hostname,
                                      int applicationHostId = 1)
    {
        var host = Encoding.ASCII.GetBytes(hostname);
        var packet = new byte[5 + 16 + host.Length + 1];
        "BECN\0"u8.CopyTo(packet);
        packet[5] = 1;   // versão maior do beacon
        packet[6] = 2;   // versão menor
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), applicationHostId);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), versionNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15, 4), role);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(19, 2), port);
        host.CopyTo(packet, 21);
        return packet;
    }
}

public class XPlaneFixAssemblerTests
{
    private static XPlaneFixAssembler Filled(float paused = 0, float onGround = 0)
    {
        var a = new XPlaneFixAssembler();
        a.Set(0, 34.55678f);    // latitude
        a.Set(1, -80.11234f);   // longitude
        a.Set(2, 365.8f);       // elevação, metros MSL
        a.Set(3, 57.2f);        // velocidade de solo, m/s
        a.Set(4, 231.245f);     // curso verdadeiro
        a.Set(5, 180.2f);       // proa verdadeira
        a.Set(6, 2.5f);         // arfagem
        a.Set(7, -8.0f);        // rolagem
        a.Set(8, onGround);
        a.Set(9, paused);
        return a;
    }

    [Fact]
    public void TryBuild_MapsUnitsDirectlyAndKeepsSigns()
    {
        var fix = Filled().TryBuild(DateTime.UtcNow);

        Assert.NotNull(fix);
        Assert.Equal(34.55678, fix!.LatitudeDeg, precision: 4);
        Assert.Equal(-80.11234, fix.LongitudeDeg, precision: 4);
        Assert.Equal(365.8, fix.AltitudeMslMeters, precision: 2);
        Assert.Equal(57.2, fix.GroundSpeedMps, precision: 2);
        // Sem inversão de sinal: ao contrário do MSFS, o X-Plane já usa a
        // convenção do XATT (arfagem + = nariz p/ cima, rolagem + = asa direita).
        Assert.Equal(2.5, fix.PitchDegUp, precision: 3);
        Assert.Equal(-8.0, fix.RollDegRight, precision: 3);
        Assert.False(fix.OnGround);
    }

    [Fact]
    public void TryBuild_ProducesTheExpectedSentences()
    {
        var fix = Filled().TryBuild(DateTime.UtcNow)!;
        Assert.Equal("XGPS2G GPS,-80.11234,34.55678,365.8,231.245,57.2",
            XgpsSentences.FormatXgps("2G GPS", fix));
        Assert.Equal("XATT2G GPS,180.2,2.5,-8,,,,,,,,,",
            XgpsSentences.FormatXatt("2G GPS", fix));
    }

    [Fact]
    public void TryBuild_WaitsForEveryRequiredField()
    {
        var a = new XPlaneFixAssembler();
        a.Set(0, 34.5f);
        a.Set(1, -80.1f);
        Assert.Null(a.TryBuild(DateTime.UtcNow));   // faltam os demais
    }

    [Fact]
    public void TryBuild_SuppressedWhilePaused()
    {
        Assert.Null(Filled(paused: 1).TryBuild(DateTime.UtcNow));
        Assert.True(Filled(paused: 1).IsPaused);
    }

    [Fact]
    public void TryBuild_RejectsNullIslandAndOutOfRange()
    {
        var loading = Filled();
        loading.Set(0, 0);
        loading.Set(1, 0);
        Assert.Null(loading.TryBuild(DateTime.UtcNow));

        var bogus = Filled();
        bogus.Set(0, 91f);
        Assert.Null(bogus.TryBuild(DateTime.UtcNow));
    }

    [Fact]
    public void OnGroundFlag_IsCarried()
    {
        Assert.True(Filled(onGround: 1).TryBuild(DateTime.UtcNow)!.OnGround);
    }

    [Fact]
    public void Reset_ClearsAccumulatedValues()
    {
        var a = Filled();
        a.Reset();
        Assert.Null(a.TryBuild(DateTime.UtcNow));
    }

    [Fact]
    public void DatarefOrder_IsTheWireContract()
    {
        // Os índices são enviados ao X-Plane e voltam nas respostas: reordenar o
        // array quebraria o mapeamento silenciosamente.
        Assert.Equal("sim/flightmodel/position/latitude", XPlaneFixAssembler.Datarefs[0]);
        Assert.Equal("sim/flightmodel/position/longitude", XPlaneFixAssembler.Datarefs[1]);
        Assert.Equal("sim/flightmodel/position/elevation", XPlaneFixAssembler.Datarefs[2]);
        Assert.Equal("sim/flightmodel/position/groundspeed", XPlaneFixAssembler.Datarefs[3]);
        Assert.Equal("sim/flightmodel/position/hpath", XPlaneFixAssembler.Datarefs[4]);
        Assert.Equal("sim/flightmodel/position/psi", XPlaneFixAssembler.Datarefs[5]);
        Assert.Equal("sim/flightmodel/position/theta", XPlaneFixAssembler.Datarefs[6]);
        Assert.Equal("sim/flightmodel/position/phi", XPlaneFixAssembler.Datarefs[7]);
        Assert.Equal(10, XPlaneFixAssembler.Datarefs.Length);
    }
}
