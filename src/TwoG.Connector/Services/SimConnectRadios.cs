using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Rádios pelo SimConnect (spec 02). Comandos entram numa fila por <see cref="Submit"/>, de
/// qualquer thread, e são executados por <see cref="Drain"/> na thread do SimConnectService,
/// a única dona do objeto SimConnect. O estado vem de uma definição de dados POR GRUPO, com
/// o flag CHANGED: uma SimVar desconhecida (o modo do transponder no Prepar3D) derruba só o
/// grupo dela, e não os outros.
///
/// Todo método que recebe <see cref="SimConnect"/>, e os campos marcados "thread do
/// SimConnect", são tocados SÓ nessa thread.
/// </summary>
internal sealed class SimConnectRadios : ISimControl, IDisposable
{
    private const int MaxPending = 32;

    // IDs a partir de 100: definição, requisição e evento precisam ser únicos na conexão, e o
    // SimConnectService já usa de 0 a 9.
    private enum Definition { Com1 = 100, Com2, Nav1, Nav2, Adf1, Transponder, TransponderMode, Altimeter }

    private enum Request { Com1 = 100, Com2, Nav1, Nav2, Adf1, Transponder, TransponderMode, Altimeter }

    private enum Event
    {
        Com1Active = 100, Com1Standby, Com1Swap,
        Com2Active, Com2Standby, Com2Swap,
        Nav1Active, Nav1Standby, Nav1Swap,
        Nav2Active, Nav2Standby, Nav2Swap,
        Adf1Active, XpdrCode, AltimeterBaro,
    }

    /// <summary>SIMCONNECT_GROUP_PRIORITY_HIGHEST: com GROUPID_IS_PRIORITY, o evento vai direto.</summary>
    private enum Group { Highest = 1 }

    private static readonly (Event Event, string SimEvent, string Control)[] Events =
    [
        (Event.Com1Active, "COM_RADIO_SET_HZ", "com1.active"),
        (Event.Com1Standby, "COM_STBY_RADIO_SET_HZ", "com1.standby"),
        (Event.Com1Swap, "COM_STBY_RADIO_SWAP", "com1.swap"),
        (Event.Com2Active, "COM2_RADIO_SET_HZ", "com2.active"),
        (Event.Com2Standby, "COM2_STBY_RADIO_SET_HZ", "com2.standby"),
        (Event.Com2Swap, "COM2_RADIO_SWAP", "com2.swap"),
        (Event.Nav1Active, "NAV1_RADIO_SET_HZ", "nav1.active"),
        (Event.Nav1Standby, "NAV1_STBY_SET_HZ", "nav1.standby"),
        (Event.Nav1Swap, "NAV1_RADIO_SWAP", "nav1.swap"),
        (Event.Nav2Active, "NAV2_RADIO_SET_HZ", "nav2.active"),
        (Event.Nav2Standby, "NAV2_STBY_SET_HZ", "nav2.standby"),
        (Event.Nav2Swap, "NAV2_RADIO_SWAP", "nav2.swap"),
        (Event.Adf1Active, "ADF_COMPLETE_SET", "adf1.active"),
        (Event.XpdrCode, "XPNDR_SET", "xpdr.code"),
        (Event.AltimeterBaro, "KOHLSMAN_SET", "altimeter.baro"),
    ];

    private static readonly (RadioGroup Group, Definition Definition, Request Request)[] Groups =
    [
        (RadioGroup.Com1, Definition.Com1, Request.Com1),
        (RadioGroup.Com2, Definition.Com2, Request.Com2),
        (RadioGroup.Nav1, Definition.Nav1, Request.Nav1),
        (RadioGroup.Nav2, Definition.Nav2, Request.Nav2),
        (RadioGroup.Adf1, Definition.Adf1, Request.Adf1),
        (RadioGroup.Transponder, Definition.Transponder, Request.Transponder),
        (RadioGroup.TransponderMode, Definition.TransponderMode, Request.TransponderMode),
        (RadioGroup.Altimeter, Definition.Altimeter, Request.Altimeter),
    ];

    // A ordem dos campos TEM de ser a ordem dos AddToDataDefinition do grupo.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FrequencyPairData { public double Active; public double Standby; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AdfData { public double Active; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TransponderData { public int CodeBco16; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TransponderModeData { public int Mode; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AltimeterData { public double Millibars; }

    private readonly Func<string?> _simulatorName;
    private readonly ConcurrentQueue<ControlCommand> _pending = new();

    // Torna atômicos a checagem de _connected e o Enqueue do Submit contra o Reset. Sem ele,
    // um Submit que passou da checagem antes do TearDown enfileiraria depois da limpeza, com
    // o CommandSignal setado: o Drain logo depois do próximo TryConnect (antes até do
    // OnRecvOpen) executaria um comando velho na conexão nova.
    private readonly object _queueLock = new();

    // Thread do SimConnect.
    private readonly Dictionary<uint, RadioGroup> _packetToGroup = new();
    private readonly HashSet<RadioGroup> _failed = [];
    private readonly HashSet<RadioGroup> _available = [];
    private FrequencyPair? _com1, _com2, _nav1, _nav2;
    private long? _adf1;
    private int? _xpdrCode, _xpdrMode;
    private long? _baroPa;

    // Lidos de qualquer thread; trocados de uma vez (referências imutáveis).
    private volatile bool _connected;
    private volatile RadioState? _state;
    private volatile string[] _controls = [];

    public SimConnectRadios(Func<string?> simulatorName) => _simulatorName = simulatorName;

    /// <summary>Sinalizado quando há comando na fila: o SimConnectService acorda e chama <see cref="Drain"/>.</summary>
    public AutoResetEvent CommandSignal { get; } = new(false);

    public string? SimulatorName => _connected ? _simulatorName() : null;

    public IReadOnlyCollection<string> AvailableControls => _controls;

    public RadioState? State => _state;

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command)
    {
        lock (_queueLock)
        {
            if (!_connected)
                return ControlResult.Fail(command.Id, ControlErrors.SimNotConnected);
            if (_pending.Count >= MaxPending)
                return ControlResult.Fail(command.Id, ControlErrors.SimUnresponsive);

            _pending.Enqueue(command);
        }
        // Fora do lock: se o Reset esvaziar a fila entre o Enqueue e o Set, o sinal fica
        // setado com a fila vazia, e o Drain com fila vazia não faz nada.
        CommandSignal.Set();
        return ControlResult.Success(command.Id);
    }

    public void Dispose() => CommandSignal.Dispose();

    // ── Thread do SimConnect ────────────────────────────────────────────

    /// <summary>Chamado no OnRecvOpen: mapeia eventos, define os grupos e pede os dados.</summary>
    public void Register(SimConnect sim)
    {
        // Nada de uma sessão anterior sobrevive: a conexão nova começa com a fila vazia.
        _pending.Clear();
        ClearRadios();
        _packetToGroup.Clear();
        _failed.Clear();

        foreach (var (evt, simEvent, _) in Events)
            sim.MapClientEventToSimEvent(evt, simEvent);

        DefinePair(sim, RadioGroup.Com1, Definition.Com1, "COM", 1);
        DefinePair(sim, RadioGroup.Com2, Definition.Com2, "COM", 2);
        DefinePair(sim, RadioGroup.Nav1, Definition.Nav1, "NAV", 1);
        DefinePair(sim, RadioGroup.Nav2, Definition.Nav2, "NAV", 2);

        Define(sim, RadioGroup.Adf1, Definition.Adf1,
            ("ADF ACTIVE FREQUENCY:1", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ("ADF AVAILABLE:1", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<AdfData>(Definition.Adf1);

        Define(sim, RadioGroup.Transponder, Definition.Transponder,
            ("TRANSPONDER CODE:1", "BCO16", SIMCONNECT_DATATYPE.INT32),
            ("TRANSPONDER AVAILABLE:1", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<TransponderData>(Definition.Transponder);

        Define(sim, RadioGroup.TransponderMode, Definition.TransponderMode,
            ("TRANSPONDER STATE:1", "Enum", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<TransponderModeData>(Definition.TransponderMode);

        Define(sim, RadioGroup.Altimeter, Definition.Altimeter,
            ("KOHLSMAN SETTING MB:1", "millibars", SIMCONNECT_DATATYPE.FLOAT64));
        sim.RegisterDataDefineStruct<AltimeterData>(Definition.Altimeter);

        RequestAll(sim);
        _connected = true;
        Publish();
    }

    /// <summary>
    /// Troca de aeronave: um pedido novo zera a referência do CHANGED, e o simulador manda o
    /// retrato completo, com os AVAILABLE da aeronave nova.
    /// </summary>
    public void OnAircraftLoaded(SimConnect sim) => RequestAll(sim);

    /// <summary>Devolve true se o pacote era de um grupo de rádio.</summary>
    public bool OnData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwData is not { Length: > 0 })
            return false;

        // Grupo que falhou: o pedido do Register segue ativo e o pacote chega sem o campo
        // recusado (o resto da struct é lixo). É nosso, mas não entra no estado.
        foreach (var (group, _, request) in Groups)
        {
            if ((uint)request == data.dwRequestID && _failed.Contains(group))
                return true;
        }

        switch ((Request)data.dwRequestID)
        {
            case Request.Com1 when data.dwData[0] is FrequencyPairData d:
                _com1 = Pair(RadioGroup.Com1, d);
                break;
            case Request.Com2 when data.dwData[0] is FrequencyPairData d:
                _com2 = Pair(RadioGroup.Com2, d);
                break;
            case Request.Nav1 when data.dwData[0] is FrequencyPairData d:
                _nav1 = Pair(RadioGroup.Nav1, d);
                break;
            case Request.Nav2 when data.dwData[0] is FrequencyPairData d:
                _nav2 = Pair(RadioGroup.Nav2, d);
                break;
            case Request.Adf1 when data.dwData[0] is AdfData d:
                _adf1 = SetAvailable(RadioGroup.Adf1, d.Available != 0) ? RadioConversions.RoundHz(d.Active) : null;
                break;
            case Request.Transponder when data.dwData[0] is TransponderData d:
                _xpdrCode = SetAvailable(RadioGroup.Transponder, d.Available != 0)
                    ? RadioConversions.Bco16ToCode(unchecked((uint)d.CodeBco16))
                    : null;
                break;
            case Request.TransponderMode when data.dwData[0] is TransponderModeData d:
                SetAvailable(RadioGroup.TransponderMode, true);
                _xpdrMode = d.Mode;
                break;
            case Request.Altimeter when data.dwData[0] is AltimeterData d:
                SetAvailable(RadioGroup.Altimeter, true);
                _baroPa = RadioConversions.MillibarsToPa(d.Millibars);
                break;
            default:
                return false;
        }
        Publish();
        return true;
    }

    /// <summary>Exceção de um pedido dos rádios: o grupo dele fica indisponível. True se era nosso.</summary>
    public bool OnException(SIMCONNECT_RECV_EXCEPTION data)
    {
        if (!_packetToGroup.TryGetValue(data.dwSendID, out var group))
            return false;
        _failed.Add(group);
        _available.Remove(group);
        ClearGroup(group);   // um valor que já tinha chegado também sai do estado
        Publish();
        return true;
    }

    /// <summary>
    /// Executa os comandos da fila. Chamado quando <see cref="CommandSignal"/> dispara.
    /// A recusa do simulador (evento desconhecido, valor que ele não aceita) NÃO chega aqui:
    /// vem depois, como SIMCONNECT_RECV_EXCEPTION, e o estado continua mostrando o valor antigo.
    /// COMException aqui é a chamada nativa falhando, ou seja, o pipe caiu: ela sobe para o
    /// SimConnectService derrubar a conexão (TearDown, que também esvazia a fila por <see cref="Reset"/>).
    /// </summary>
    public void Drain(SimConnect sim)
    {
        while (_pending.TryDequeue(out var command))
            Execute(sim, command);
    }

    /// <summary>Conexão caiu (TearDown): nada de estado, nada de controles.</summary>
    public void Reset()
    {
        lock (_queueLock)
        {
            _connected = false;
            _pending.Clear();
        }
        ClearRadios();
        _packetToGroup.Clear();
        _failed.Clear();

        var hadSomething = _state is not null || _controls.Length > 0;
        _state = null;
        _controls = [];
        if (hadSomething)
            Changed?.Invoke();
    }

    private static void Execute(SimConnect sim, ControlCommand command)
    {
        if (command.Control == "xpdr.mode")
        {
            sim.SetDataOnSimObject(Definition.TransponderMode, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, new TransponderModeData { Mode = (int)command.Value });
            return;
        }

        var entry = Array.Find(Events, e => e.Control == command.Control);
        if (entry.SimEvent is null)
            return;   // o catálogo já barrou; não há o que fazer

        var parameter = command.Control switch
        {
            "adf1.active" => RadioConversions.HzToBcd32(command.Value),
            "xpdr.code" => RadioConversions.CodeToBcd16(command.Value),
            "altimeter.baro" => RadioConversions.PaToMillibars16(command.Value),
            _ => (uint)command.Value,   // Hz direto; nas ações o valor é 0
        };
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, entry.Event, parameter,
            Group.Highest, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private void DefinePair(SimConnect sim, RadioGroup group, Definition definition, string radio, int index)
    {
        Define(sim, group, definition,
            ($"{radio} ACTIVE FREQUENCY:{index}", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ($"{radio} STANDBY FREQUENCY:{index}", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ($"{radio} AVAILABLE:{index}", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<FrequencyPairData>(definition);
    }

    /// <summary>Acrescenta as SimVars e guarda o ID de cada pacote, para atribuir exceções ao grupo.</summary>
    private void Define(SimConnect sim, RadioGroup group, Definition definition,
                        params (string SimVar, string Unit, SIMCONNECT_DATATYPE Type)[] fields)
    {
        foreach (var (simVar, unit, type) in fields)
        {
            sim.AddToDataDefinition(definition, simVar, unit, type, 0f, SimConnect.SIMCONNECT_UNUSED);
            _packetToGroup[sim.GetLastSentPacketID()] = group;
        }
    }

    private void RequestAll(SimConnect sim)
    {
        foreach (var (group, definition, request) in Groups)
        {
            if (_failed.Contains(group))
                continue;
            // VISUAL_FRAME, e não SIM_FRAME: o estado continua chegando com o simulador pausado (V6).
            sim.RequestDataOnSimObject(request, definition, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _packetToGroup[sim.GetLastSentPacketID()] = group;
        }
    }

    private FrequencyPair? Pair(RadioGroup group, FrequencyPairData d) =>
        SetAvailable(group, d.Available != 0)
            ? new FrequencyPair(RadioConversions.RoundHz(d.Active), RadioConversions.RoundHz(d.Standby))
            : null;

    private bool SetAvailable(RadioGroup group, bool available)
    {
        if (available)
            _available.Add(group);
        else
            _available.Remove(group);
        return available;
    }

    private void ClearGroup(RadioGroup group)
    {
        switch (group)
        {
            case RadioGroup.Com1: _com1 = null; break;
            case RadioGroup.Com2: _com2 = null; break;
            case RadioGroup.Nav1: _nav1 = null; break;
            case RadioGroup.Nav2: _nav2 = null; break;
            case RadioGroup.Adf1: _adf1 = null; break;
            case RadioGroup.Transponder: _xpdrCode = null; break;
            case RadioGroup.TransponderMode: _xpdrMode = null; break;
            case RadioGroup.Altimeter: _baroPa = null; break;
        }
    }

    private void ClearRadios()
    {
        _available.Clear();
        _com1 = _com2 = _nav1 = _nav2 = null;
        _adf1 = null;
        _xpdrCode = _xpdrMode = null;
        _baroPa = null;
    }

    /// <summary>Monta o retrato e a lista de controles; avisa só se algo mudou.</summary>
    private void Publish()
    {
        var hasTransponder = _xpdrCode is not null;
        var modeAvailable = hasTransponder && _available.Contains(RadioGroup.TransponderMode);

        var state = new RadioState(_com1, _com2, _nav1, _nav2, _adf1,
            hasTransponder ? new TransponderState(_xpdrCode!.Value, modeAvailable ? _xpdrMode : null) : null,
            _baroPa);

        var groups = _available.Where(g => !_failed.Contains(g) && (g != RadioGroup.TransponderMode || hasTransponder));
        var controls = RadioCatalog.ControlsFor(groups).ToArray();

        var changed = !Equals(state, _state) || !controls.SequenceEqual(_controls);
        _state = state;
        _controls = controls;
        if (changed)
            Changed?.Invoke();
    }
}
