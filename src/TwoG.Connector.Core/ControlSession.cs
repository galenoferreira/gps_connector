namespace TwoG.Connector.Core;

public enum SessionPhase
{
    AwaitingHello,
    Unpaired,
    Paired,
}

/// <summary>O que enviar em resposta a uma mensagem, e se a conexão deve fechar.</summary>
public sealed record SessionOutput(IReadOnlyList<string> Messages, int? CloseCode = null, string? CloseReason = null)
{
    public static readonly SessionOutput None = new([]);
}

/// <summary>
/// Máquina de estados de UMA conexão (spec 01, "Conexão" e "Pareamento"): aguardando
/// hello → não pareada → pareada. Não conhece socket: recebe texto e devolve mensagens.
/// Todo comando é validado aqui (catálogo e controles da aeronave) antes de chegar ao
/// simulador. <see cref="Handle"/> roda só no laço de recepção da conexão;
/// <see cref="StateMessage"/> e <see cref="ControlsMessage"/> podem vir do laço de envio.
/// </summary>
public sealed class ControlSession
{
    public const int MaxCommandsPerSecond = 20;
    public const int MaxRefusalsPer10Seconds = 100;
    public const int CloseProtocolUnsupported = 4002;
    public const int CloseRateLimited = 4008;

    private readonly ISimControl _control;
    private readonly PairedDeviceStore _devices;
    private readonly PairingCodes _codes;
    private readonly string _connectorVersion;
    private readonly Func<DateTime> _utcNow;
    private readonly Queue<DateTime> _recentCommands = new();
    private readonly Queue<DateTime> _recentRefusals = new();
    private long _stateSeq;
    private volatile SessionPhase _phase = SessionPhase.AwaitingHello;
    private string? _helloDeviceId;
    private string? _helloDeviceName;

    public ControlSession(ISimControl control, PairedDeviceStore devices, PairingCodes codes,
                          string connectorVersion, Func<DateTime>? utcNow = null)
    {
        _control = control;
        _devices = devices;
        _codes = codes;
        _connectorVersion = connectorVersion;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public SessionPhase Phase => _phase;

    /// <summary>Aparelho pareado desta conexão; null antes do pareamento.</summary>
    public string? DeviceId { get; private set; }

    public string? DeviceName { get; private set; }

    /// <summary>Resumo do último comando, para o Diagnóstico ("com1.standby = 118500000 → ok").</summary>
    public string? LastCommand { get; private set; }

    public SessionOutput Handle(string json)
    {
        if (!ControlProtocol.TryParse(json, out var message))
            return Send(ControlProtocol.Error(ControlErrors.InvalidMessage));

        return (_phase, message) switch
        {
            (_, UnknownMessage) => SessionOutput.None,
            (SessionPhase.AwaitingHello, HelloMessage hello) => OnHello(hello),
            (SessionPhase.Unpaired, PairMessage pair) => OnPair(pair),
            (SessionPhase.Unpaired, CommandMessage command) =>
                Send(ControlProtocol.Result(ControlResult.Fail(command.Command.Id, ControlErrors.NotPaired))),
            (SessionPhase.Paired, CommandMessage command) => OnCommand(command.Command),
            _ => Send(ControlProtocol.Error(ControlErrors.InvalidMessage)),
        };
    }

    public string StateMessage() =>
        ControlProtocol.State(Interlocked.Increment(ref _stateSeq), _control.SimulatorName, _control.State);

    public string ControlsMessage() => ControlProtocol.Controls(_control.AvailableControls);

    private SessionOutput OnHello(HelloMessage hello)
    {
        if (hello.Protocol != ControlProtocol.Version)
            return new SessionOutput(
                [ControlProtocol.Error(ControlErrors.ProtocolUnsupported)],
                CloseProtocolUnsupported, "protocolo não suportado");

        // O token identifica o aparelho; o id do hello só vale para um pareamento novo.
        if (hello.Token is not null && _devices.Authenticate(hello.Token) is { } device)
            return BecomePaired(device.Id, device.Name, prefix: null);

        _helloDeviceId = hello.DeviceId;
        _helloDeviceName = hello.DeviceName;
        _phase = SessionPhase.Unpaired;
        return Send(ControlProtocol.PairingRequired());
    }

    private SessionOutput OnPair(PairMessage pair)
    {
        switch (_codes.TryConsume(pair.Code))
        {
            case PairingAttempt.Accepted:
                var token = _devices.Pair(_helloDeviceId!, _helloDeviceName!);
                return BecomePaired(_helloDeviceId!, _helloDeviceName!, prefix: ControlProtocol.Paired(token));
            case PairingAttempt.Invalid:
                return Send(ControlProtocol.Error(ControlErrors.PairingInvalid));
            default:
                return Send(ControlProtocol.Error(ControlErrors.PairingLocked));
        }
    }

    private SessionOutput BecomePaired(string deviceId, string deviceName, string? prefix)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        _phase = SessionPhase.Paired;

        var messages = new List<string>(4);
        if (prefix is not null)
            messages.Add(prefix);
        messages.Add(ControlProtocol.Welcome(_connectorVersion, _control.SimulatorName));
        messages.Add(ControlsMessage());
        messages.Add(StateMessage());
        return new SessionOutput(messages);
    }

    private SessionOutput OnCommand(ControlCommand command)
    {
        var now = _utcNow();
        Prune(_recentCommands, now - TimeSpan.FromSeconds(1));

        if (_recentCommands.Count >= MaxCommandsPerSecond)
        {
            Prune(_recentRefusals, now - TimeSpan.FromSeconds(10));
            _recentRefusals.Enqueue(now);
            var refused = ControlProtocol.Result(ControlResult.Fail(command.Id, ControlErrors.RateLimited));
            return _recentRefusals.Count > MaxRefusalsPer10Seconds
                ? new SessionOutput([refused], CloseRateLimited, "excesso de comandos")
                : Send(refused);
        }
        _recentCommands.Enqueue(now);

        var error = RadioCatalog.Validate(command.Control, command.Kind, command.Value);
        if (error is null && !_control.AvailableControls.Contains(command.Control))
            error = ControlErrors.Unsupported;

        var result = error is null ? _control.Submit(command) : ControlResult.Fail(command.Id, error);

        LastCommand = command.Kind == ControlKind.Set
            ? $"{command.Control} = {command.Value} → {result.Error ?? "ok"}"
            : $"{command.Control} → {result.Error ?? "ok"}";
        return Send(ControlProtocol.Result(result));
    }

    private static void Prune(Queue<DateTime> queue, DateTime olderThan)
    {
        while (queue.Count > 0 && queue.Peek() <= olderThan)
            queue.Dequeue();
    }

    private static SessionOutput Send(string message) => new([message]);
}
