using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

public class ControlProtocolTests
{
    private static ClientMessage Parse(string json)
    {
        Assert.True(ControlProtocol.TryParse(json, out var message), $"deveria ler: {json}");
        return message!;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ReadsHelloWithToken()
    {
        var hello = Assert.IsType<HelloMessage>(Parse(
            """{"type":"hello","protocol":1,"app":"2G Pilot","appVersion":"3.2.0","device":{"id":"abc","name":"  iPad do Galeno  "},"token":"tok"}"""));

        Assert.Equal(1, hello.Protocol);
        Assert.Equal("abc", hello.DeviceId);
        Assert.Equal("iPad do Galeno", hello.DeviceName);   // espaços aparados
        Assert.Equal("tok", hello.Token);
    }

    [Fact]
    public void HelloWithoutTokenHasNullToken()
    {
        var hello = Assert.IsType<HelloMessage>(Parse("""{"type":"hello","protocol":1,"device":{"id":"abc","name":"iPad"}}"""));
        Assert.Null(hello.Token);
    }

    [Fact]
    public void LongDeviceNameIsCutTo64()
    {
        var name = new string('x', 100);
        var hello = Assert.IsType<HelloMessage>(Parse($$$"""{"type":"hello","protocol":1,"device":{"id":"a","name":"{{{name}}}"}}"""));
        Assert.Equal(64, hello.DeviceName.Length);
    }

    [Fact]
    public void ReadsPair()
    {
        Assert.Equal("482913", Assert.IsType<PairMessage>(Parse("""{"type":"pair","code":"482913"}""")).Code);
    }

    [Fact]
    public void ReadsSetWithIntegerValue()
    {
        var command = Assert.IsType<CommandMessage>(Parse("""{"type":"set","id":"a1","control":"com1.standby","value":118500000}""")).Command;
        Assert.Equal(new ControlCommand("a1", "com1.standby", ControlKind.Set, 118_500_000), command);
    }

    [Fact]
    public void ReadsActionWithoutValue()
    {
        var command = Assert.IsType<CommandMessage>(Parse("""{"type":"action","id":"a2","control":"com1.swap"}""")).Command;
        Assert.Equal(new ControlCommand("a2", "com1.swap", ControlKind.Action, 0), command);
    }

    [Fact]
    public void UnknownTypeIsKeptAsUnknown_SoTheChannelCanIgnoreIt()
    {
        Assert.Equal("future", Assert.IsType<UnknownMessage>(Parse("""{"type":"future","x":1}""")).Type);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        Assert.IsType<PairMessage>(Parse("""{"type":"pair","code":"1","extra":{"nested":true}}"""));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"code":"1"}""")]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"type":"hello","protocol":"1","device":{"id":"a","name":"b"}}""")]
    [InlineData("""{"type":"hello","protocol":1,"device":{"name":"b"}}""")]
    [InlineData("""{"type":"hello","protocol":1,"device":{"id":"a","name":"   "}}""")]
    [InlineData("""{"type":"hello","protocol":1}""")]
    [InlineData("""{"type":"pair"}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active","value":1.5}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active","value":"118500000"}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active"}""")]
    [InlineData("""{"type":"set","control":"com1.active","value":1}""")]
    [InlineData("""{"type":"action","id":"","control":"com1.swap"}""")]
    public void RejectsMalformedMessagesWithoutThrowing(string json)
    {
        Assert.False(ControlProtocol.TryParse(json, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void RejectsIdLongerThan64()
    {
        var id = new string('i', 65);
        Assert.False(ControlProtocol.TryParse($$"""{"type":"action","id":"{{id}}","control":"com1.swap"}""", out _));
    }

    [Fact]
    public void WritesWelcome()
    {
        var json = Json(ControlProtocol.Welcome("1.5.0", "Microsoft Flight Simulator 2024"));
        Assert.Equal("welcome", json.GetProperty("type").GetString());
        Assert.Equal(1, json.GetProperty("protocol").GetInt32());
        Assert.Equal("1.5.0", json.GetProperty("connector").GetString());
        Assert.Equal("Microsoft Flight Simulator 2024", json.GetProperty("simulator").GetString());
    }

    [Fact]
    public void WelcomeWithoutSimulatorWritesNull()
    {
        Assert.Equal(JsonValueKind.Null, Json(ControlProtocol.Welcome("1.5.0", null)).GetProperty("simulator").ValueKind);
    }

    [Fact]
    public void WritesSmallMessages()
    {
        Assert.Equal("pairing_required", Json(ControlProtocol.PairingRequired()).GetProperty("type").GetString());
        Assert.Equal("tok", Json(ControlProtocol.Paired("tok")).GetProperty("token").GetString());
        Assert.Equal("busy", Json(ControlProtocol.Error("busy")).GetProperty("code").GetString());
        Assert.Equal(["com1.active", "xpdr.code"],
            Json(ControlProtocol.Controls(["com1.active", "xpdr.code"])).GetProperty("controls").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void WritesResults()
    {
        var ok = Json(ControlProtocol.Result(ControlResult.Success("a1")));
        Assert.Equal("a1", ok.GetProperty("id").GetString());
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.False(ok.TryGetProperty("error", out _));

        var fail = Json(ControlProtocol.Result(ControlResult.Fail("a2", "out_of_range")));
        Assert.False(fail.GetProperty("ok").GetBoolean());
        Assert.Equal("out_of_range", fail.GetProperty("error").GetString());
    }

    [Fact]
    public void WritesFullState()
    {
        var state = new RadioState(
            new FrequencyPair(121_900_000, 118_500_000), new FrequencyPair(122_800_000, 124_350_000),
            new FrequencyPair(110_300_000, 113_900_000), null,
            350_000, new TransponderState(2000, 4), 101_320);

        var json = Json(ControlProtocol.State(1042, "MSFS 2024", state));
        var radios = json.GetProperty("radios");

        Assert.Equal(1042, json.GetProperty("seq").GetInt64());
        Assert.Equal(121_900_000, radios.GetProperty("com1").GetProperty("active").GetInt64());
        Assert.Equal(118_500_000, radios.GetProperty("com1").GetProperty("standby").GetInt64());
        Assert.False(radios.TryGetProperty("nav2", out _));            // rádio ausente não aparece
        Assert.Equal(350_000, radios.GetProperty("adf1").GetProperty("active").GetInt64());
        Assert.Equal(2000, radios.GetProperty("xpdr").GetProperty("code").GetInt32());
        Assert.Equal(4, radios.GetProperty("xpdr").GetProperty("mode").GetInt32());
        Assert.Equal(101_320, radios.GetProperty("altimeter").GetProperty("baroPa").GetInt64());
    }

    [Fact]
    public void TransponderWithoutModeOmitsMode()
    {
        var state = RadioState.Empty with { Xpdr = new TransponderState(1200, null) };
        var xpdr = Json(ControlProtocol.State(1, "Prepar3D v5", state)).GetProperty("radios").GetProperty("xpdr");
        Assert.False(xpdr.TryGetProperty("mode", out _));
    }

    [Fact]
    public void StateWithoutSimulatorHasEmptyRadios()
    {
        var json = Json(ControlProtocol.State(3, null, null));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("simulator").ValueKind);
        Assert.Empty(json.GetProperty("radios").EnumerateObject());
    }
}
