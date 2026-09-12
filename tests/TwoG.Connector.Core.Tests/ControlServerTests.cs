using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

/// <summary>
/// Integração de verdade: o servidor numa porta efêmera e um ClientWebSocket — o mesmo
/// caminho que o URLSessionWebSocketTask do iPad vai percorrer. O simulador é falso.
/// </summary>
public class ControlServerTests : IDisposable
{
    private const string Hello = """{"type":"hello","protocol":1,"app":"2G Pilot","device":{"id":"ipad-1","name":"iPad de teste"}}""";

    private readonly TestTempDir _tmp = new();
    private readonly FakeSimControl _sim = new();
    private readonly PairingCodes _codes = new();
    private readonly PairedDeviceStore _devices;
    private readonly ControlServer _server;

    public ControlServerTests()
    {
        _devices = new PairedDeviceStore(_tmp.Sub("paired.json"));
        _server = new ControlServer(_sim, _devices, _codes, "1.5.0", new ControlServerOptions
        {
            HelloTimeout = TimeSpan.FromMilliseconds(500),
            PushInterval = TimeSpan.FromMilliseconds(20),
        });
        _server.Start(0);
        Assert.True(_server.IsRunning, _server.LastError ?? "servidor não subiu");
    }

    public void Dispose()
    {
        _server.Dispose();
        _tmp.Dispose();
    }

    private static CancellationToken Timeout(int ms = 5000) => new CancellationTokenSource(ms).Token;

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}{ControlServer.Path}"), Timeout());
        return ws;
    }

    private static Task SendAsync(ClientWebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, Timeout());

    private static async Task<JsonElement> ExpectAsync(ClientWebSocket ws, string type)
    {
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(buffer, Timeout());
        Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
        var message = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
        Assert.Equal(type, message.GetProperty("type").GetString());
        return message;
    }

    /// <summary>Espera o fechamento e devolve o código; null se a conexão caiu sem quadro de fechamento.</summary>
    private static async Task<int?> ExpectCloseAsync(ClientWebSocket ws)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, Timeout());
                if (result.MessageType == WebSocketMessageType.Close)
                    return (int?)result.CloseStatus;
            }
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    private async Task<(ClientWebSocket Ws, string Token)> PairAsync()
    {
        var ws = await ConnectAsync();
        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");
        await SendAsync(ws, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        var token = (await ExpectAsync(ws, "paired")).GetProperty("token").GetString()!;
        await ExpectAsync(ws, "welcome");
        await ExpectAsync(ws, "controls");
        await ExpectAsync(ws, "state");
        return (ws, token);
    }

    [Fact]
    public async Task PairingFlowEndToEnd()
    {
        var ws = await ConnectAsync();
        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");

        await SendAsync(ws, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");

        await ExpectAsync(ws, "paired");
        Assert.Equal("1.5.0", (await ExpectAsync(ws, "welcome")).GetProperty("connector").GetString());
        Assert.Contains("com1.active", (await ExpectAsync(ws, "controls")).GetProperty("controls").EnumerateArray().Select(e => e.GetString()));
        var state = await ExpectAsync(ws, "state");
        Assert.Equal(121_900_000, state.GetProperty("radios").GetProperty("com1").GetProperty("active").GetInt64());
    }

    [Fact]
    public async Task ReconnectingWithTheTokenSkipsPairing()
    {
        var (first, token) = await PairAsync();
        first.Abort();

        var ws = await ConnectAsync();
        await SendAsync(ws, $$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        await ExpectAsync(ws, "welcome");
        await ExpectAsync(ws, "controls");
        await ExpectAsync(ws, "state");
    }

    [Fact]
    public async Task SetReachesTheSimulatorAndGetsAResult()
    {
        var (ws, _) = await PairAsync();

        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");

        var result = await ExpectAsync(ws, "result");
        Assert.Equal("a1", result.GetProperty("id").GetString());
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("com1.standby", Assert.Single(_sim.Submitted).Control);
        Assert.Contains("com1.standby", _server.LastCommand);
    }

    [Fact]
    public async Task LastCommandOnlyChangesWhenTheMessageIsACommand()
    {
        var (a, _) = await PairAsync();

        // Segundo aparelho, com id próprio: o PairAsync usa sempre o hello do ipad-1.
        var b = await ConnectAsync();
        await SendAsync(b, """{"type":"hello","protocol":1,"app":"2G Pilot","device":{"id":"ipad-2","name":"iPad B"}}""");
        await ExpectAsync(b, "pairing_required");
        await SendAsync(b, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        await ExpectAsync(b, "paired");
        await ExpectAsync(b, "welcome");
        await ExpectAsync(b, "controls");
        await ExpectAsync(b, "state");

        await SendAsync(a, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        await ExpectAsync(a, "result");
        await SendAsync(b, """{"type":"set","id":"b1","control":"com2.standby","value":121500000}""");
        await ExpectAsync(b, "result");
        Assert.Contains("com2.standby", _server.LastCommand);

        // O type desconhecido não tem resposta. A mensagem inválida logo atrás tem, e a
        // conexão trata na ordem: quando o error chega, o xyz já passou pelo Handle.
        await SendAsync(a, """{"type":"xyz"}""");
        await SendAsync(a, "{");
        await ExpectAsync(a, "error");

        Assert.Contains("com2.standby", _server.LastCommand);
    }

    [Fact]
    public async Task StateIsPushedWhenTheSimulatorChanges()
    {
        var (ws, _) = await PairAsync();

        _sim.State = _sim.State! with { Com1 = new FrequencyPair(121_900_000, 124_350_000) };
        _sim.RaiseChanged();

        var state = await ExpectAsync(ws, "state");
        Assert.Equal(124_350_000, state.GetProperty("radios").GetProperty("com1").GetProperty("standby").GetInt64());
        Assert.True(state.GetProperty("seq").GetInt64() > 1);
    }

    [Fact]
    public async Task ControlsArePushedWhenTheAircraftChanges()
    {
        var (ws, _) = await PairAsync();

        _sim.AvailableControls = ["com1.active", "com1.standby", "com1.swap"];
        _sim.RaiseChanged();

        var controls = await ExpectAsync(ws, "controls");
        Assert.Equal(3, controls.GetProperty("controls").GetArrayLength());
        await ExpectAsync(ws, "state");
    }

    [Fact]
    public async Task FifthConnectionGetsBusyAndCloses4003()
    {
        // As quatro mandam hello: sem ele, o prazo curto do teste as fecharia antes da quinta.
        var open = new List<ClientWebSocket>();
        for (var i = 0; i < ControlServer.MaxConnections; i++)
        {
            var ws = await ConnectAsync();
            await SendAsync(ws, Hello);
            await ExpectAsync(ws, "pairing_required");
            open.Add(ws);
        }

        var fifth = await ConnectAsync();

        Assert.Equal("busy", (await ExpectAsync(fifth, "error")).GetProperty("code").GetString());
        Assert.Equal(ControlServer.CloseBusy, await ExpectCloseAsync(fifth));
    }

    [Fact]
    public async Task RemovingTheDeviceClosesItsConnectionWith4001()
    {
        var (ws, _) = await PairAsync();

        _devices.Remove("ipad-1");

        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task SilentConnectionIsClosedAfterTheHelloTimeout()
    {
        var ws = await ConnectAsync();

        await ExpectCloseAsync(ws);   // não pode ficar pendurada: Timeout() é de 5 s e o prazo do hello é 500 ms

        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task OversizedMessageCloses1009()
    {
        var ws = await ConnectAsync();

        await SendAsync(ws, new string('x', ControlServer.MaxMessageBytes + 100));

        Assert.Equal((int)WebSocketCloseStatus.MessageTooBig, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task InvalidJsonKeepsTheConnectionOpen()
    {
        var ws = await ConnectAsync();

        await SendAsync(ws, "{");
        Assert.Equal("invalid_message", (await ExpectAsync(ws, "error")).GetProperty("code").GetString());

        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");
    }

    [Theory]
    [InlineData("GET /control HTTP/1.1\r\nHost: x\r\n\r\n", "400")]
    [InlineData("GET /outra HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\n\r\n", "404")]
    [InlineData("POST /control HTTP/1.1\r\nHost: x\r\n\r\n", "405")]
    public async Task NonWebSocketRequestsAreRejected(string request, string status)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, Timeout());

        Assert.StartsWith($"HTTP/1.1 {status}", Encoding.ASCII.GetString(buffer, 0, read));
    }

    [Fact]
    public async Task ConnectedListShowsThePairedDevice()
    {
        await PairAsync();

        var device = Assert.Single(_server.Connected);
        Assert.Equal("iPad de teste", device.DeviceName);
        Assert.True(device.Paired);
    }
}
