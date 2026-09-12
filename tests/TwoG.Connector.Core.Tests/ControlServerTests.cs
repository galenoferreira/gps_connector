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
    public async Task UnsupportedProtocolGetsAnErrorAndCloses4002()
    {
        var ws = await ConnectAsync();

        await SendAsync(ws, """{"type":"hello","protocol":2,"app":"2G Pilot","device":{"id":"ipad-1","name":"iPad de teste"}}""");

        Assert.Equal(ControlErrors.ProtocolUnsupported, (await ExpectAsync(ws, "error")).GetProperty("code").GetString());
        Assert.Equal(ControlSession.CloseProtocolUnsupported, await ExpectCloseAsync(ws));
    }

    /// <summary>Rajada de sets válidos, sem ler as respostas no meio.</summary>
    private static async Task SendSetsAsync(ClientWebSocket ws, int count)
    {
        for (var i = 0; i < count; i++)
            await SendAsync(ws, $$"""{"type":"set","id":"r{{i}}","control":"com1.standby","value":118500000}""");
    }

    [Fact]
    public async Task BurstAboveTheRateLimitGetsRateLimited()
    {
        var (ws, _) = await PairAsync();
        var burst = ControlSession.MaxCommandsPerSecond + 1;

        await SendSetsAsync(ws, burst);

        var errors = new List<string?>();
        for (var i = 0; i < burst; i++)
        {
            var result = await ExpectAsync(ws, "result");
            errors.Add(result.TryGetProperty("error", out var error) ? error.GetString() : null);
        }
        Assert.Contains(ControlErrors.RateLimited, errors);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task PersistentExcessCloses4008()
    {
        var (ws, _) = await PairAsync();

        // 20 aceitos no primeiro segundo; daí em diante, recusas. A 101ª em 10 s fecha.
        await SendSetsAsync(ws, ControlSession.MaxCommandsPerSecond + ControlSession.MaxRefusalsPer10Seconds + 10);

        Assert.Equal(ControlSession.CloseRateLimited, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task RemovingTheDeviceClosesItsConnectionWith4001()
    {
        var (ws, _) = await PairAsync();

        _devices.Remove("ipad-1");

        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task RemovedDeviceThatNeverAnswersIsReleasedAfterTwoSeconds()
    {
        var (ws, _) = await PairAsync();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.ConnectionsChanged += () =>
        {
            if (_server.Connected.Count == 0)
                released.TrySetResult();
        };

        _devices.Remove("ipad-1");

        // O app não lê o 4001 nem responde nada: sem o prazo, a conexão ficaria na lista,
        // segurando uma das 4 vagas, até o TCP cair.
        Assert.Same(released.Task, await Task.WhenAny(released.Task, Task.Delay(4000)));
        Assert.Empty(_server.Connected);
        GC.KeepAlive(ws);
    }

    [Fact]
    public async Task CommandInFlightAfterRemovalNeverReachesTheSimulator()
    {
        var (ws, _) = await PairAsync();

        _devices.Remove("ipad-1");
        // Sai do app antes de ele ler o 4001: o servidor já fechou e não pode mais executá-lo.
        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));

        // E continua esperando a resposta do app, em vez de soltar o TCP com o set.
        await Task.Delay(300);
        lock (_sim.Submitted)
            Assert.Empty(_sim.Submitted);
        Assert.Single(_server.Connected);

        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();
    }

    [Fact]
    public async Task StoppingWaitsForTheClientsCloseBeforeReleasingTheConnection()
    {
        var (ws, _) = await PairAsync();

        _server.Stop();
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(ws));

        // O 1000 já saiu, mas o app ainda não respondeu: a leitura não pode ter sido cancelada.
        await Task.Delay(300);
        Assert.Single(_server.Connected);

        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();
    }

    [Fact]
    public async Task StoppingGivesUpOnASilentClientAfterTwoSeconds()
    {
        var (ws, _) = await PairAsync();

        _server.Stop();
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(ws));

        await WaitForNoConnectionsAsync();   // o app nunca responde: o prazo de 2 s solta a conexão
    }

    private async Task WaitForNoConnectionsAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_server.Connected.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Empty(_server.Connected);
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
    public async Task OversizedMessageKeepsTheTcpOpenUntilTheClientsClose()
    {
        // Em TCP cru para ver o que o servidor faz com o socket. Largar o TCP logo após o
        // quadro 1009, com o resto da mensagem ainda não lido, manda RST no Windows, e o RST
        // pode apagar o quadro no app antes de ele ser lido.
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET /control HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));

        // Quadro de texto mascarado (máscara zero: o payload vai como está), 16 vezes o limite.
        var payload = ControlServer.MaxMessageBytes * 16;
        var frame = new List<byte> { 0x81, 0xFF };
        frame.AddRange(BitConverter.GetBytes((long)payload).Reverse());
        frame.AddRange(new byte[4]);
        frame.AddRange(Enumerable.Repeat((byte)'x', payload));
        await stream.WriteAsync(frame.ToArray(), Timeout());

        // Lê a resposta 101 e o quadro de fechamento inteiro.
        var received = new List<byte>();
        var buffer = new byte[1024];
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(received)) < 0 || received.Count < headerEnd + 2
               || received.Count < headerEnd + 2 + received[headerEnd + 1])
        {
            var read = await stream.ReadAsync(buffer, Timeout());
            Assert.NotEqual(0, read);
            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        Assert.Equal(0x88, received[headerEnd]);
        Assert.Equal((int)WebSocketCloseStatus.MessageTooBig, (received[headerEnd + 2] << 8) | received[headerEnd + 3]);

        // O servidor espera a resposta do app: nada de EOF por enquanto.
        var pending = stream.ReadAsync(buffer).AsTask();
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(300)));

        // Fechamento do cliente (mascarado, código 1000): agora sim o servidor solta o TCP.
        await stream.WriteAsync(new byte[] { 0x88, 0x82, 0, 0, 0, 0, 0x03, 0xE8 }, Timeout());
        Assert.Same(pending, await Task.WhenAny(pending, Task.Delay(5000)));
        Assert.Equal(0, await pending);
    }

    private static int IndexOfHeaderEnd(List<byte> bytes)
    {
        for (var i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                return i + 1;
        }
        return -1;
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
    public async Task StoppingClosesOpenConnectionsWith1000()
    {
        var (paired, _) = await PairAsync();
        var waitingHello = await ConnectAsync();

        _server.Stop();

        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(paired));
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(waitingHello));
    }

    [Fact]
    public async Task ThrowingSubscriberDoesNotLeakTheConnection()
    {
        _server.ConnectionsChanged += () => throw new InvalidOperationException("assinante com defeito");

        var (ws, _) = await PairAsync();   // o aviso do pareamento também não pode derrubar a conexão
        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        await ExpectAsync(ws, "result");

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_server.Connected.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Empty(_server.Connected);
    }

    [Fact]
    public async Task ConnectedListShowsThePairedDevice()
    {
        await PairAsync();

        var device = Assert.Single(_server.Connected);
        Assert.Equal("iPad de teste", device.DeviceName);
        Assert.True(device.Paired);
    }

    [Fact]
    public async Task NothingRunsOnTheCallersSynchronizationContext()
    {
        // No app, Start, Stop e a remoção do aparelho vêm da thread do WPF. Se o servidor
        // iniciasse trabalho assíncrono no contexto de quem chama, accept, recepção, Handle,
        // gravação do paired-devices.json e o envio de state cairiam todos no Dispatcher.
        var ui = new CountingSyncContext();
        RunOn(ui, () => _server.Start(0));   // o Start também passa pelo Stop do servidor já de pé
        Assert.True(_server.IsRunning, _server.LastError ?? "servidor não subiu");

        var (ws, _) = await PairAsync();
        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        await ExpectAsync(ws, "result");
        _sim.RaiseChanged();
        await ExpectAsync(ws, "state");

        RunOn(ui, () => _devices.Remove("ipad-1"));
        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();

        var other = await ConnectAsync();
        await SendAsync(other, Hello);
        await ExpectAsync(other, "pairing_required");
        RunOn(ui, () => _server.Stop());
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(other));
        await other.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();

        Assert.Equal(0, ui.Posts);
    }

    private static void RunOn(SynchronizationContext context, Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// Imita o Dispatcher: conta cada Post e roda o trabalho no pool com o próprio contexto
    /// instalado, como a thread da interface faria. Uma continuação que caia nele uma vez
    /// continua caindo, e tudo aparece na contagem.
    /// </summary>
    private sealed class CountingSyncContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => RunOn(this, () => d(state)));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            RunOn(this, () => d(state));
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
