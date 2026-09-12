using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

public class ControlSessionTests : IDisposable
{
    private const string Hello = """{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad de teste"}}""";

    private readonly TestTempDir _tmp = new();
    private readonly FakeSimControl _sim = new();
    private readonly PairingCodes _codes;
    private readonly PairedDeviceStore _devices;
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    public ControlSessionTests()
    {
        _codes = new PairingCodes(() => _now);
        _devices = new PairedDeviceStore(_tmp.Sub("paired.json"), () => _now);
    }

    public void Dispose() => _tmp.Dispose();

    private ControlSession NewSession() => new(_sim, _devices, _codes, "1.5.0", () => _now);

    private static string[] Types(SessionOutput output) =>
        output.Messages.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("type").GetString()!).ToArray();

    private static JsonElement Json(string message) => JsonDocument.Parse(message).RootElement.Clone();

    /// <summary>Sessão já pareada, pronta para comandos.</summary>
    private ControlSession PairedSession()
    {
        var session = NewSession();
        session.Handle(Hello);
        session.Handle($$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        Assert.Equal(SessionPhase.Paired, session.Phase);
        return session;
    }

    private static string Set(string id, string control, long value) =>
        $$"""{"type":"set","id":"{{id}}","control":"{{control}}","value":{{value}}}""";

    [Fact]
    public void HelloWithoutTokenAsksForPairing()
    {
        var session = NewSession();

        Assert.Equal(["pairing_required"], Types(session.Handle(Hello)));
        Assert.Equal(SessionPhase.Unpaired, session.Phase);
    }

    [Fact]
    public void OtherProtocolIsRefusedAndClosedWith4002()
    {
        var output = NewSession().Handle("""{"type":"hello","protocol":2,"device":{"id":"a","name":"b"}}""");

        Assert.Equal(["error"], Types(output));
        Assert.Equal("protocol_unsupported", Json(output.Messages[0]).GetProperty("code").GetString());
        Assert.Equal(ControlSession.CloseProtocolUnsupported, output.CloseCode);
    }

    [Fact]
    public void AnythingBeforeHelloIsInvalid()
    {
        var output = NewSession().Handle(Set("a", "com1.active", 121_900_000));
        Assert.Equal("invalid_message", Json(output.Messages[0]).GetProperty("code").GetString());
    }

    [Fact]
    public void InvalidJsonIsInvalidMessage_AndTheSessionGoesOn()
    {
        var session = NewSession();
        Assert.Equal(["error"], Types(session.Handle("{")));
        Assert.Equal(["pairing_required"], Types(session.Handle(Hello)));
    }

    [Fact]
    public void UnknownTypeIsIgnored()
    {
        Assert.Empty(NewSession().Handle("""{"type":"future"}""").Messages);
    }

    [Fact]
    public void WrongCodeIsPairingInvalid()
    {
        var session = NewSession();
        session.Handle(Hello);
        var code = _codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        var output = session.Handle($$"""{"type":"pair","code":"{{wrong}}"}""");

        Assert.Equal("pairing_invalid", Json(output.Messages[0]).GetProperty("code").GetString());
        Assert.Equal(SessionPhase.Unpaired, session.Phase);
    }

    [Fact]
    public void PairingWithoutActiveCodeIsLocked()
    {
        var session = NewSession();
        session.Handle(Hello);

        var output = session.Handle("""{"type":"pair","code":"123456"}""");

        Assert.Equal("pairing_locked", Json(output.Messages[0]).GetProperty("code").GetString());
    }

    [Fact]
    public void RightCodePairsAndSendsWelcomeControlsState()
    {
        var session = NewSession();
        session.Handle(Hello);

        var output = session.Handle($$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");

        Assert.Equal(["paired", "welcome", "controls", "state"], Types(output));
        var token = Json(output.Messages[0]).GetProperty("token").GetString()!;
        Assert.Equal("ipad-1", _devices.Authenticate(token)!.Id);
        Assert.Equal("ipad-1", session.DeviceId);
        Assert.Equal("iPad de teste", session.DeviceName);
    }

    [Fact]
    public void HelloWithValidTokenSkipsPairing()
    {
        var token = _devices.Pair("ipad-1", "iPad de teste");
        var session = NewSession();

        var output = session.Handle($$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        Assert.Equal(["welcome", "controls", "state"], Types(output));
        Assert.Equal(SessionPhase.Paired, session.Phase);
        Assert.Equal("1.5.0", Json(output.Messages[0]).GetProperty("connector").GetString());
    }

    [Fact]
    public void HelloWithRevokedTokenAsksForPairing()
    {
        var token = _devices.Pair("ipad-1", "iPad");
        _devices.Remove("ipad-1");

        var output = NewSession().Handle($$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        Assert.Equal(["pairing_required"], Types(output));
    }

    [Fact]
    public void CommandBeforePairingIsNotPaired()
    {
        var session = NewSession();
        session.Handle(Hello);

        var result = Json(session.Handle(Set("a1", "com1.active", 121_900_000)).Messages[0]);

        Assert.Equal("not_paired", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void ValidCommandReachesTheSimulator()
    {
        var session = PairedSession();

        var result = Json(session.Handle(Set("a1", "com1.standby", 118_500_000)).Messages[0]);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(new ControlCommand("a1", "com1.standby", ControlKind.Set, 118_500_000), Assert.Single(_sim.Submitted));
        Assert.Contains("com1.standby", session.LastCommand);
    }

    [Fact]
    public void OutOfRangeNeverReachesTheSimulator()
    {
        var result = Json(PairedSession().Handle(Set("a1", "com1.standby", 118_020_000)).Messages[0]);

        Assert.Equal("out_of_range", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void ControlMissingFromThisAircraftIsUnsupported()
    {
        _sim.AvailableControls = ["com1.active", "com1.standby", "com1.swap"];

        var result = Json(PairedSession().Handle(Set("a1", "com2.active", 121_900_000)).Messages[0]);

        Assert.Equal("unsupported", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void SimulatorRefusalIsPassedThrough()
    {
        _sim.FailWith = ControlErrors.SimNotConnected;

        var result = Json(PairedSession().Handle(Set("a1", "com1.active", 121_900_000)).Messages[0]);

        Assert.Equal("sim_not_connected", result.GetProperty("error").GetString());
    }

    [Fact]
    public void TwentyFirstCommandInOneSecondIsRateLimited()
    {
        var session = PairedSession();
        for (var i = 0; i < ControlSession.MaxCommandsPerSecond; i++)
            Assert.True(Json(session.Handle(Set($"c{i}", "com1.active", 121_900_000)).Messages[0]).GetProperty("ok").GetBoolean());

        var refused = Json(session.Handle(Set("extra", "com1.active", 121_900_000)).Messages[0]);
        Assert.Equal("rate_limited", refused.GetProperty("error").GetString());

        _now = _now.AddSeconds(1.1);
        Assert.True(Json(session.Handle(Set("later", "com1.active", 121_900_000)).Messages[0]).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void MoreThanOneHundredRefusalsInTenSecondsCloses4008()
    {
        var session = PairedSession();
        SessionOutput output = SessionOutput.None;
        var sent = 0;
        while (output.CloseCode is null && sent < 1000)
        {
            output = session.Handle(Set($"c{sent}", "com1.active", 121_900_000));
            sent++;
        }

        Assert.Equal(ControlSession.CloseRateLimited, output.CloseCode);
        Assert.Equal(ControlSession.MaxCommandsPerSecond + ControlSession.MaxRefusalsPer10Seconds + 1, sent);
    }

    [Fact]
    public void StateSequenceGrows()
    {
        var session = PairedSession();
        var first = Json(session.StateMessage()).GetProperty("seq").GetInt64();
        var second = Json(session.StateMessage()).GetProperty("seq").GetInt64();

        Assert.True(second > first);
    }

    [Fact]
    public void ControlsMessageFollowsTheAircraft()
    {
        var session = PairedSession();
        _sim.AvailableControls = ["xpdr.code"];

        var controls = Json(session.ControlsMessage()).GetProperty("controls").EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert.Equal(["xpdr.code"], controls);
    }
}
