using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core;

public sealed record ControlServerOptions
{
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PushInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record ConnectedDevice(string? DeviceId, string? DeviceName, bool Paired);

/// <summary>
/// Servidor do canal de controle (spec 01): WebSocket sobre um TcpListener, com o
/// upgrade HTTP feito à mão. Pelo mesmo motivo do FlightPlanServer, nada de HttpListener:
/// escutar em todas as interfaces exigiria reserva de URL ou administrador.
/// Uma falha aqui nunca derruba o app nem afeta o XGPS: tudo roda em tarefas próprias e
/// só fala com o simulador pelo <see cref="ISimControl"/>.
/// </summary>
public sealed class ControlServer : IDisposable
{
    public const string Path = "/control";
    public const int MaxConnections = 4;
    public const int MaxMessageBytes = 4096;
    public const int CloseRevoked = 4001;
    public const int CloseBusy = 4003;

    private const int MaxRequestBytes = 8192;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly ISimControl _control;
    private readonly PairedDeviceStore _devices;
    private readonly PairingCodes _codes;
    private readonly string _connectorVersion;
    private readonly ControlServerOptions _options;
    private readonly ConcurrentDictionary<Connection, byte> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private int _slots;
    private volatile bool _dirty;
    private long _changeGeneration;   // conta os avisos do simulador; ver o pareamento no laço de recepção
    private string? _lastSimulatorName;   // só o laço de envio lê e grava depois do construtor

    public ControlServer(ISimControl control, PairedDeviceStore devices, PairingCodes codes,
                         string connectorVersion, ControlServerOptions? options = null)
    {
        _control = control;
        _devices = devices;
        _codes = codes;
        _connectorVersion = connectorVersion;
        _options = options ?? new ControlServerOptions();
        _lastSimulatorName = control.SimulatorName;
        _control.Changed += () =>
        {
            Interlocked.Increment(ref _changeGeneration);
            _dirty = true;
        };
        _devices.Removed += OnDeviceRemoved;
    }

    public int Port { get; private set; }

    public bool IsRunning => _listener is not null;

    /// <summary>Erro que impediu o servidor de subir (porta ocupada), ou null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Resumo do último comando recebido de qualquer aparelho, para o Diagnóstico.</summary>
    public string? LastCommand { get; private set; }

    public IReadOnlyList<ConnectedDevice> Connected =>
        _connections.Keys
            .Select(c => new ConnectedDevice(c.Session.DeviceId, c.Session.DeviceName, c.Session.Phase == SessionPhase.Paired))
            .ToArray();

    /// <summary>
    /// Uma conexão abriu, fechou ou pareou. Disparado de threads do pool, nunca da interface;
    /// uma exceção do assinante é ignorada e não afeta a conexão.
    /// </summary>
    public event Action? ConnectionsChanged;

    public void Start(int port)
    {
        Stop();
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            LastError = null;
            // Quem chama é a thread da interface. Iniciados direto, os laços capturariam o
            // contexto dela, e accept, recepção, Handle, gravação dos aparelhos e o envio de
            // state rodariam todos no Dispatcher. O Task.Run os põe no pool desde o começo.
            _ = Task.Run(() => AcceptLoopAsync(listener, token));
            _ = Task.Run(() => PushLoopAsync(listener, token));
        }
        catch (SocketException ex)
        {
            LastError = $"Porta {port} indisponível: {ex.Message}";
            _listener = null;
        }
    }

    public void Stop()
    {
        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Já parado.
        }
        _listener = null;

        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null)
            return;

        // Fecha com 1000 ANTES de cancelar: cada conexão tem uma leitura pendente com este
        // token, e cancelar uma leitura aborta o WebSocket — o quadro de fechamento não sairia,
        // nem a resposta do app seria lida (soltar o TCP com ela em trânsito manda RST).
        // Nada espera aqui (quem chama é a thread da interface): o Task.Run tira os fechamentos
        // do contexto dela, e o cancelamento vem quando o laço de recepção de cada uma recebeu
        // o Close do app e a soltou, ou em 2 s, o que vier antes (um app que não responde, ou
        // um envio travado, que segura o lock de envio até o cancelamento). A marca de
        // fechamento vem antes, aqui mesmo: um quadro que chegue enquanto o 1000 ainda não saiu
        // também não vira comando.
        var connections = _connections.Keys.ToArray();
        foreach (var connection in connections)
            connection.MarkClosing();
        var closing = Task.Run(async () =>
        {
            await Task.WhenAll(connections.Select(
                c => c.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connector encerrando")));
            await Task.WhenAll(connections.Select(c => c.Finished));
        });
        _ = Task.WhenAny(closing, Task.Delay(CloseTimeout)).ContinueWith(_ =>
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    public void Dispose() => Stop();

    private void OnDeviceRemoved(string deviceId)
    {
        // Quem chama é o botão Remover da interface: o fechamento e o prazo rodam no pool.
        // A marca vem antes, aqui mesmo, para que um quadro que chegue enquanto o 4001 ainda
        // não saiu também não vire comando.
        foreach (var connection in _connections.Keys.Where(c => c.Session.DeviceId == deviceId))
        {
            connection.MarkClosing();
            _ = Task.Run(() => RevokeAsync(connection));
        }
    }

    /// <summary>
    /// Manda o 4001 e dá ao app o mesmo prazo do <see cref="Stop"/> para responder. Um app que
    /// não responde nem manda quadro deixa o laço de recepção parado no ReceiveAsync, e a
    /// conexão seguraria uma das 4 vagas até o TCP cair: passado o prazo, o socket é abortado,
    /// a leitura pendente falha e o laço solta a conexão.
    /// </summary>
    private async Task RevokeAsync(Connection connection)
    {
        var closing = connection.CloseAsync((WebSocketCloseStatus)CloseRevoked, "aparelho removido");
        await Task.WhenAny(connection.Finished, Task.Delay(CloseTimeout));
        if (_connections.ContainsKey(connection))
            connection.Socket.Abort();
        await closing;   // o abort também solta um envio travado que segurava o lock do fechamento
    }

    private void RaiseConnectionsChanged()
    {
        try
        {
            ConnectionsChanged?.Invoke();
        }
        catch (Exception)
        {
            // Defeito do assinante não pode deixar conexão fantasma nem vaga presa.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.OperationAborted
                                             && !token.IsCancellationRequested
                                             && ReferenceEquals(_listener, listener))
            {
                // Conexão da fila desfeita antes do accept (no Windows, ConnectionReset): um
                // iPad que desistiu no meio do connect. Sair aqui deixaria o servidor surdo com
                // IsRunning verdadeiro, e nada o reiniciaria.
                continue;
            }
            catch (Exception)
            {
                return;   // servidor parado
            }
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            var reserved = false;
            try
            {
                var stream = client.GetStream();
                var key = await ReadUpgradeAsync(stream, token);
                if (key is null)
                    return;

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\n"
                    + "Connection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\n\r\n"), token);

                using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
                {
                    IsServer = true,
                    KeepAliveInterval = _options.KeepAliveInterval,
                    KeepAliveTimeout = _options.KeepAliveTimeout,
                });
                var connection = new Connection(socket, new ControlSession(_control, _devices, _codes, _connectorVersion));

                // Reserva a vaga ANTES de aceitar: checar e incluir separado deixaria duas
                // conexões simultâneas passarem juntas pelo limite.
                reserved = Interlocked.Increment(ref _slots) <= MaxConnections;
                if (!reserved)
                {
                    Interlocked.Decrement(ref _slots);
                    await connection.SendAsync(ControlProtocol.Error(ControlErrors.Busy), token);
                    await connection.CloseAndDrainAsync((WebSocketCloseStatus)CloseBusy, "conexões esgotadas");
                    return;
                }

                try
                {
                    _connections.TryAdd(connection, 0);
                    RaiseConnectionsChanged();
                    await ReceiveLoopAsync(connection, token);
                }
                finally
                {
                    _connections.TryRemove(connection, out _);
                    Interlocked.Decrement(ref _slots);
                    RaiseConnectionsChanged();
                    connection.MarkFinished();
                }
            }
            catch (Exception)
            {
                // Cliente sumiu no meio: nada a fazer além de liberar a vaga (finally acima).
            }
        }
    }

    /// <summary>
    /// Lê o pedido HTTP de upgrade e devolve o Sec-WebSocket-Key. Pedido inválido já recebe a
    /// resposta de erro aqui, e o retorno é null.
    /// </summary>
    private static async Task<string?> ReadUpgradeAsync(NetworkStream stream, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        var bytes = new List<byte>(512);
        var one = new byte[1];
        while (bytes.Count < MaxRequestBytes)
        {
            if (await stream.ReadAsync(one, deadline.Token) == 0)
                return null;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                break;
        }

        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var method = requestLine.Length > 0 ? requestLine[0] : "";
        var target = requestLine.Length > 1 ? requestLine[1].Split('?')[0] : "";

        string? error =
            !method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "405 Method Not Allowed"
            : !target.Equals(Path, StringComparison.Ordinal) ? "404 Not Found"
            : !headers.TryGetValue("Upgrade", out var upgrade) || !upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase)
              || !headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13"
              || !headers.ContainsKey("Sec-WebSocket-Key") ? "400 Bad Request"
            : null;

        if (error is not null)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {error}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
            return null;
        }
        return headers["Sec-WebSocket-Key"];
    }

    private async Task ReceiveLoopAsync(Connection connection, CancellationToken token)
    {
        var buffer = new byte[MaxMessageBytes];
        using var helloDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        helloDeadline.CancelAfter(_options.HelloTimeout);

        while (connection.Socket.State == WebSocketState.Open)
        {
            var receiveToken = connection.Session.Phase == SessionPhase.AwaitingHello ? helloDeadline.Token : token;
            var count = 0;
            ValueWebSocketReceiveResult received;
            try
            {
                do
                {
                    received = await connection.Socket.ReceiveAsync(buffer.AsMemory(count), receiveToken);
                    count += received.Count;
                    if (!received.EndOfMessage && count == buffer.Length)
                    {
                        await connection.CloseAndDrainAsync(WebSocketCloseStatus.MessageTooBig, "mensagem maior que 4 KB");
                        return;
                    }
                }
                while (!received.EndOfMessage);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Prazo do hello: cancelar a leitura já aborta o WebSocket.
                return;
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "");
                return;
            }

            if (connection.Closing || connection.Socket.State != WebSocketState.Open)
            {
                // O servidor já decidiu fechar (Stop, aparelho removido) e este quadro saiu do
                // app antes de ele ver o fechamento: não vira comando, senão o set de um aparelho
                // removido chegaria ao simulador. Descarta até o Close do app, por no máximo 2 s,
                // mesmo que o quadro de fechamento ainda esteja saindo numa tarefa do pool.
                await connection.DrainAsync();
                return;
            }

            if (received.MessageType != WebSocketMessageType.Text)
            {
                await connection.SendAsync(ControlProtocol.Error(ControlErrors.InvalidMessage), token);
                continue;
            }

            var wasPaired = connection.Session.Phase == SessionPhase.Paired;
            var generation = Interlocked.Read(ref _changeGeneration);
            var lastBefore = connection.Session.LastCommand;
            var output = connection.Session.Handle(Encoding.UTF8.GetString(buffer, 0, count));
            // Antes de enviar: quem recebe o result já pode ler o LastCommand.
            // Só copia se ESTA mensagem foi um comando: a sessão guarda o último dela, e um
            // type desconhecido, uma mensagem inválida ou um rate_limited deste aparelho
            // trariam de volta o comando antigo dele por cima do mais novo de outro aparelho.
            // Cada comando gera uma string nova na sessão, então a referência basta.
            if (connection.Session.LastCommand is { } last && !ReferenceEquals(last, lastBefore))
                LastCommand = last;

            foreach (var message in output.Messages)
                await connection.SendAsync(message, token);

            if (!wasPaired && connection.Session.Phase == SessionPhase.Paired)
            {
                // O app tem a lista que foi no pacote de boas-vindas, não a de agora: se a
                // aeronave mudou depois do Handle, o laço de envio vê a diferença e manda a nova.
                connection.LastControls = output.Messages.First(
                    m => m.StartsWith("""{"type":"controls",""", StringComparison.Ordinal));
                // Só agora o laço de envio fala com este aparelho: antes, controls e state
                // chegariam na frente de paired/welcome. Se o simulador avisou mudança desde
                // o Handle, o ciclo que a tratou pode ter pulado esta conexão: marca de novo.
                // Sem mudança nada é marcado, e nenhum state extra vai para a rede.
                connection.Session.MarkWelcomeSent();
                if (Interlocked.Read(ref _changeGeneration) != generation)
                    _dirty = true;
                RaiseConnectionsChanged();
            }

            if (output.CloseCode is int code)
            {
                await connection.CloseAndDrainAsync((WebSocketCloseStatus)code, output.CloseReason ?? "");
                return;
            }
        }
    }

    /// <summary>
    /// Laço único de envio: a cada <see cref="ControlServerOptions.PushInterval"/>, se o
    /// simulador avisou mudança, manda a lista de controles (se mudou) e o estado para cada
    /// aparelho pareado. Mudanças próximas viram um só state: no máximo 10 por segundo.
    /// O nome do simulador é conferido a cada tick, porque pode mudar sem Changed: uma fonte
    /// sem ISimControl (o X-Plane) conecta ou cai e só o nome do state muda. Sem isso, um app
    /// pareado antes de o X-Plane abrir ficaria com null, e um pareado com ele aberto ficaria
    /// com o nome depois de ele fechar.
    /// </summary>
    private async Task PushLoopAsync(TcpListener listener, CancellationToken token)
    {
        using var timer = new PeriodicTimer(_options.PushInterval);
        try
        {
            // Sai assim que o servidor para, sem esperar o cancelamento (que o Stop adia até os
            // fechamentos): num Stop seguido de Start, dois laços mandariam state em dobro.
            while (await timer.WaitForNextTickAsync(token) && ReferenceEquals(_listener, listener))
            {
                // Conta como aviso do simulador: um pareamento em curso vê o contador andar.
                var name = _control.SimulatorName;
                if (name != _lastSimulatorName)
                {
                    _lastSimulatorName = name;
                    Interlocked.Increment(ref _changeGeneration);
                    _dirty = true;
                }

                if (!_dirty)
                    continue;
                _dirty = false;

                foreach (var connection in _connections.Keys.Where(c => c.Session.ReadyForPush))
                {
                    var controls = connection.Session.ControlsMessage();
                    if (controls != connection.LastControls)
                    {
                        connection.LastControls = controls;
                        await connection.SendAsync(controls, token);
                    }
                    await connection.SendAsync(connection.Session.StateMessage(), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Servidor parado.
        }
    }

    private sealed class Connection(WebSocket socket, ControlSession session)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _closing;

        public WebSocket Socket { get; } = socket;

        /// <summary>
        /// O servidor decidiu fechar esta conexão (Stop, aparelho removido). Marcado por quem
        /// chama, antes de o quadro de fechamento sair numa tarefa do pool: um quadro de dados
        /// que chegue nessa janela também não vira comando.
        /// </summary>
        public bool Closing => _closing;

        public void MarkClosing() => _closing = true;

        public ControlSession Session { get; } = session;

        /// <summary>Última lista de controles enviada a este aparelho.</summary>
        public string? LastControls { get; set; }

        /// <summary>Completa quando o laço de recepção terminou e a conexão saiu da lista.</summary>
        public Task Finished => _finished.Task;

        public void MarkFinished() => _finished.TrySetResult();

        public async Task SendAsync(string json, CancellationToken token)
        {
            try
            {
                await _sendLock.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // A conexão caiu; o laço de recepção dela vai perceber e encerrar.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Manda o quadro de fechamento sem esperar a resposta. Serve quando o laço de recepção
        /// continua lendo (Stop, aparelho removido) e é ele quem recebe a resposta do app: um
        /// quadro de dados que chegue antes dela é descartado (<see cref="DrainAsync"/>).
        /// </summary>
        public async Task CloseAsync(WebSocketCloseStatus status, string reason)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(CloseTimeout);
                    await Socket.CloseOutputAsync(status, reason, timeout.Token);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Já fechada ou abortada.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Fecha por iniciativa do servidor quando ninguém mais vai ler a conexão (1009, 4002,
        /// 4003, 4008): manda o quadro e descarta o que chegar até o fechamento do app, por no
        /// máximo 2 s. Fechar o TCP com bytes não lidos (o resto da mensagem grande, um hello já
        /// enviado) manda RST, e o RST pode fazer o app perder o código logo antes dele.
        /// </summary>
        public async Task CloseAndDrainAsync(WebSocketCloseStatus status, string reason)
        {
            await CloseAsync(status, reason);
            await DrainAsync();
        }

        /// <summary>
        /// Com o fechamento decidido, descarta o que chegar até o Close do app, por no máximo 2 s.
        /// Lê também com o estado ainda Open: no Stop e na remoção o quadro de fechamento sai
        /// numa tarefa do pool e pode não ter sido escrito quando o laço de recepção chega aqui.
        /// </summary>
        public async Task DrainAsync()
        {
            var discard = new byte[1024];
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                while (Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                {
                    var received = await Socket.ReceiveAsync(discard.AsMemory(), timeout.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (Exception)
            {
                // Prazo esgotado ou conexão caída: não há mais o que esperar.
            }
        }
    }
}
