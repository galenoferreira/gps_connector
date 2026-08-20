using System.IO;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Conecta a um simulador que fale SimConnect — MSFS 2020/2024 e, pela mesma API,
/// Prepar3D v4+ — e mantém <see cref="LatestFix"/> atualizado.
///
/// As SimVars e as convenções de sinal são herdadas do FSX e valem igualmente para
/// o Prepar3D, então não há caminho de código separado por simulador; o que muda é
/// apenas o nome exibido, derivado do SIMCONNECT_RECV_OPEN.
///
/// Arquitetura: uma única thread de fundo é dona do objeto SimConnect do início ao
/// fim (criação, callbacks via ReceiveMessage e descarte), usando o padrão de
/// event-handle — funciona sem janela/message pump e evita qualquer acesso
/// multi-thread ao SimConnect. Quando o simulador não está aberto, o construtor
/// lança COMException imediatamente; tentamos de novo a cada 3 s (padrão da
/// comunidade: fs2ff, Little Navmap).
///
/// Sinais de "voo ativo": evento "Sim" (1 = voando) e "Pause_EX1" (0 = sem pausa).
/// O MSFS 2024 tem bug conhecido (devsupport t/11572): não envia Sim=0 ao voltar
/// para o menu principal — mas envia Pause_EX1=9, e a entrega de dados estanca,
/// então o watchdog de frescor do broadcaster cobre o resto.
/// </summary>
public sealed class SimConnectService : ISimSource, IFlightPlanSource
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    private const string ClientName = "2G GPS Cliente";

    private enum DEFINITION { Position }
    private enum REQUEST { Position, FlightPlan }
    private enum EVENT_ID { Sim, PauseEx1, Crashed, CrashReset, FlightPlanActivated, FlightPlanDeactivated }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PositionStruct
    {
        public double LatitudeDeg;      // PLANE LATITUDE, degrees
        public double LongitudeDeg;     // PLANE LONGITUDE, degrees
        public double AltitudeMslM;     // PLANE ALTITUDE, meters
        public double TrackTrueDeg;     // GPS GROUND TRUE TRACK, degrees
        public double GroundSpeedMps;   // GPS GROUND SPEED, meters per second
        public double HeadingTrueDeg;   // PLANE HEADING DEGREES TRUE, degrees
        public double PitchDeg;         // PLANE PITCH DEGREES (positivo = nariz p/ BAIXO)
        public double BankDeg;          // PLANE BANK DEGREES (positivo = asa ESQUERDA)
        public int OnGround;            // SIM ON GROUND, Bool
    }

    private readonly EventWaitHandle _simEvent = new(false, EventResetMode.AutoReset);
    private readonly ManualResetEvent _stopEvent = new(false);

    private Thread? _thread;
    private SimConnect? _sim;          // acessado SOMENTE pela thread de fundo
    private bool _simRunning;          // idem
    private bool _paused;              // idem
    private bool _crashed;             // idem

    private volatile SimConnectionState _state = SimConnectionState.Searching;
    private volatile string? _simulatorName;
    private volatile GpsFix? _latestFix;
    private volatile string? _lastError;
    private volatile string? _flightPlanPath;

    public string Name => "SimConnect";

    public SimConnectionState State
    {
        get
        {
            // "Recebendo" exige dados frescos; caso contrário rebaixa para "Conectado".
            var s = _state;
            if (s == SimConnectionState.Receiving)
            {
                var fix = _latestFix;
                if (fix is null || DateTime.UtcNow - fix.Utc > TimeSpan.FromSeconds(3))
                    return SimConnectionState.Connected;
            }
            return s;
        }
    }

    public string? SimulatorName => _simulatorName;

    public GpsFix? LatestFix => _latestFix;

    public string? LastError => SimConnectRuntime.Error ?? _lastError;

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "SimConnect" };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread is null)
            return;
        _stopEvent.Set();
        _thread.Join(TimeSpan.FromSeconds(5));
        _thread = null;
        _stopEvent.Reset();
    }

    public void Dispose()
    {
        Stop();
        _simEvent.Dispose();
        _stopEvent.Dispose();
    }

    // ── Thread de fundo ─────────────────────────────────────────────────

    private void ThreadMain()
    {
        var waitConnected = new WaitHandle[] { _stopEvent, _simEvent };
        while (true)
        {
            if (_sim is null)
            {
                TryConnect();
                if (_sim is null)
                {
                    if (_stopEvent.WaitOne(RetryInterval))
                        break;
                    continue;
                }
            }

            var signaled = WaitHandle.WaitAny(waitConnected, TimeSpan.FromSeconds(1));
            if (signaled == 0)
                break;
            if (signaled == 1)
            {
                try
                {
                    _sim.ReceiveMessage();
                }
                catch (COMException)
                {
                    // Pipe caiu (simulador fechou/travou): volta a procurar.
                    TearDown();
                }
            }
        }
        TearDown();
    }

    private void TryConnect()
    {
        try
        {
            var sim = new SimConnect(ClientName, IntPtr.Zero, 0, _simEvent, 0);
            sim.OnRecvOpen += OnRecvOpen;
            sim.OnRecvQuit += OnRecvQuit;
            sim.OnRecvEvent += OnRecvEvent;
            sim.OnRecvSimobjectData += OnRecvSimobjectData;
            sim.OnRecvSystemState += OnRecvSystemState;
            _sim = sim;
            _lastError = null;
        }
        catch (COMException)
        {
            // Simulador não está aberto (0x80040108); a próxima tentativa vem em 3 s.
            _sim = null;
        }
        catch (Exception ex)
        {
            // Falha estrutural (DLLs do SimConnect ausentes/inválidas): registra para
            // a UI em vez de derrubar a thread — e segue tentando.
            _sim = null;
            _lastError = ex.Message;
        }
    }

    private void TearDown()
    {
        try
        {
            _sim?.Dispose();
        }
        catch (Exception)
        {
            // Descarte de um handle já inválido não pode derrubar a thread.
        }
        _sim = null;
        _simRunning = false;
        _paused = false;
        _crashed = false;
        _simulatorName = null;
        _flightPlanPath = null;
        _state = SimConnectionState.Searching;
    }

    // ── Callbacks (sempre na thread de fundo, via ReceiveMessage) ───────

    private void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        _simulatorName = SimulatorIdentity.Describe(
            data.dwApplicationVersionMajor, data.szApplicationName);
        _state = SimConnectionState.Connected;

        // Unidades pedidas já convertidas pelo SimConnect (nativas são radianos!).
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE ALTITUDE", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "GPS GROUND TRUE TRACK", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "GPS GROUND SPEED", "meters per second", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.AddToDataDefinition(DEFINITION.Position, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE.INT32, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.RegisterDataDefineStruct<PositionStruct>(DEFINITION.Position);

        sender.RequestDataOnSimObject(REQUEST.Position, DEFINITION.Position,
            SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME,
            SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

        sender.SubscribeToSystemEvent(EVENT_ID.Sim, "Sim");
        sender.SubscribeToSystemEvent(EVENT_ID.PauseEx1, "Pause_EX1");
        sender.SubscribeToSystemEvent(EVENT_ID.Crashed, "Crashed");
        sender.SubscribeToSystemEvent(EVENT_ID.CrashReset, "CrashReset");

        // Caminho do .PLN ativo. Chega de forma assincrona em OnRecvSystemState e fica
        // em cache, porque o botao da UI nao pode tocar no SimConnect (afinidade de thread).
        sender.SubscribeToSystemEvent(EVENT_ID.FlightPlanActivated, "FlightPlanActivated");
        sender.SubscribeToSystemEvent(EVENT_ID.FlightPlanDeactivated, "FlightPlanDeactivated");
        RequestFlightPlanPath(sender);
    }

    private static void RequestFlightPlanPath(SimConnect sender)
    {
        try
        {
            sender.RequestSystemState(REQUEST.FlightPlan, "FlightPlan");
        }
        catch (COMException)
        {
            // Sem o caminho, o locator por caminho conhecido cobre.
        }
    }

    private void OnRecvSystemState(SimConnect sender, SIMCONNECT_RECV_SYSTEM_STATE data)
    {
        if ((REQUEST)data.dwRequestID != REQUEST.FlightPlan)
            return;

        // O MSFS 2024 devolve "" ou ".PLN" enquanto o voo nao esta carregado (bug
        // confirmado pela Asobo); nesses casos preserva o cache e deixa o fallback agir.
        _flightPlanPath = PlnFileLocator.IsUsablePath(data.szString) ? data.szString.Trim() : null;
    }

    private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data) => TearDown();

    private void OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((EVENT_ID)data.uEventID)
        {
            case EVENT_ID.Sim:
                _simRunning = data.dwData != 0;
                // CrashReset só dispara ao fim da cut-scene de crash; quem volta ao
                // menu e inicia novo voo nunca o recebe — solta a trava aqui também.
                if (_simRunning)
                    _crashed = false;
                break;
            case EVENT_ID.PauseEx1:
                _paused = data.dwData != 0;
                break;
            case EVENT_ID.Crashed:
                _crashed = true;
                break;
            case EVENT_ID.CrashReset:
                _crashed = false;
                break;
            case EVENT_ID.FlightPlanActivated:
                RequestFlightPlanPath(sender);
                break;
            case EVENT_ID.FlightPlanDeactivated:
                _flightPlanPath = null;
                break;
        }
    }

    private void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwRequestID != (uint)REQUEST.Position)
            return;
        if (!_simRunning || _paused || _crashed)
            return;
        if (data.dwData is not { Length: > 0 } || data.dwData[0] is not PositionStruct p)
            return;

        // Descarta amostras claramente inválidas (menu/carregamento envia zeros).
        if (p.LatitudeDeg is 0 && p.LongitudeDeg is 0 && p.AltitudeMslM is 0)
            return;
        if (p.LatitudeDeg is < -90 or > 90 || p.LongitudeDeg is < -180 or > 180)
            return;

        _latestFix = new GpsFix(
            Utc: DateTime.UtcNow,
            LatitudeDeg: p.LatitudeDeg,
            LongitudeDeg: p.LongitudeDeg,
            AltitudeMslMeters: p.AltitudeMslM,
            TrackTrueDeg: p.TrackTrueDeg,
            GroundSpeedMps: p.GroundSpeedMps,
            HeadingTrueDeg: p.HeadingTrueDeg,
            // Convenção MSFS é invertida vs. X-Plane/aviação nos DOIS eixos:
            // pitch positivo = nariz p/ baixo; bank positivo = asa esquerda. Negamos ambos.
            PitchDegUp: -p.PitchDeg,
            RollDegRight: -p.BankDeg,
            OnGround: p.OnGround != 0);
        _state = SimConnectionState.Receiving;
    }

    // ── Plano de voo ────────────────────────────────────────────────────

    public IFlightPlanSource? FlightPlans => this;

    /// <summary>Só oferece o plano com o simulador conectado.</summary>
    public bool CanRead => _state != SimConnectionState.Searching;

    public FlightPlanReadResult Read()
    {
        // Preferência para o caminho que o próprio simulador informou; senão, o
        // local padrão do voo personalizado.
        var path = _flightPlanPath;
        if (!PlnFileLocator.IsUsablePath(path))
            path = PlnFileLocator.MostRecentExisting();

        if (path is null)
            return FlightPlanReadResult.Fail(
                "Nenhum plano de voo encontrado. Crie a rota no simulador antes de sincronizar.");

        string xml;
        try
        {
            xml = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return FlightPlanReadResult.Fail($"Não foi possível ler {Path.GetFileName(path)}: {ex.Message}");
        }

        var plan = PlnFlightPlanParser.Parse(xml);
        if (plan is null)
            return FlightPlanReadResult.Fail($"Arquivo de plano em formato inesperado: {Path.GetFileName(path)}");
        if (!plan.IsUsable)
            return FlightPlanReadResult.Fail("O plano de voo tem menos de dois waypoints.");

        return FlightPlanReadResult.Ok(plan);
    }
}
