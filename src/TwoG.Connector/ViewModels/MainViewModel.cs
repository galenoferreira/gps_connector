using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;
using TwoG.Connector.Services;

namespace TwoG.Connector.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly Brush Ok = (Brush)System.Windows.Application.Current.Resources["OkBrush"];
    private static readonly Brush Warn = (Brush)System.Windows.Application.Current.Resources["WarnBrush"];
    private static readonly Brush Err = (Brush)System.Windows.Application.Current.Resources["ErrBrush"];
    private static readonly Brush Dim = (Brush)System.Windows.Application.Current.Resources["DimBrush"];

    private readonly ISimSource _sim;
    private readonly IXgpsBroadcaster _broadcaster;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FlightPlanServer _flightPlanServer;
    private readonly IEfbDiscovery? _discovery;
    private readonly UpdateService? _updates;
    private readonly ControlService? _control;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _feedbackTimer;

    public MainViewModel(ISimSource sim, IXgpsBroadcaster broadcaster,
                         SettingsService settingsService, AppSettings settings,
                         FlightPlanServer flightPlanServer,
                         IEfbDiscovery? discovery = null,
                         UpdateService? updates = null,
                         ControlService? control = null)
    {
        _sim = sim;
        _broadcaster = broadcaster;
        _settingsService = settingsService;
        _settings = settings;
        _flightPlanServer = flightPlanServer;
        _discovery = discovery;
        _updates = updates;
        _control = control;

        LoadSettingsIntoInputs();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += (_, _) => Refresh();
        _uiTimer.Start();

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _feedbackTimer.Tick += (_, _) => { SettingsFeedback = ""; _feedbackTimer.Stop(); };

        Refresh();
    }

    public bool CloseToTray => _settings.CloseToTray;

    // ── Estado do simulador ─────────────────────────────────────────────
    [ObservableProperty] private string _simStatusText = "";
    [ObservableProperty] private string _simDetail = "";
    [ObservableProperty] private Brush _simStatusBrush = Dim;

    // ── Estado da transmissão ───────────────────────────────────────────
    [ObservableProperty] private string _txStatusText = "";
    [ObservableProperty] private string _txDetail = "";
    [ObservableProperty] private string _packetsText = "";
    [ObservableProperty] private Brush _txStatusBrush = Dim;

    // ── Posição ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _latText = "—";
    [ObservableProperty] private string _lonText = "—";
    [ObservableProperty] private string _altText = "—";
    [ObservableProperty] private string _gsText = "—";
    [ObservableProperty] private string _trkText = "—";
    [ObservableProperty] private string _hdgText = "—";
    [ObservableProperty] private string _pitchText = "—";
    [ObservableProperty] private string _rollText = "—";
    [ObservableProperty] private double _positionOpacity = 0.35;

    // ── Diagnóstico ─────────────────────────────────────────────────────
    [ObservableProperty] private string _diagDestinations = "—";
    [ObservableProperty] private string _diagInterfaces = "—";
    [ObservableProperty] private string _diagCounters = "—";
    [ObservableProperty] private string _diagLastError = "";
    [ObservableProperty] private bool _hasDiagError;

    // ── Apps descobertos ────────────────────────────────────────────────
    [ObservableProperty] private string _discoveryStatusText = "Nenhum app descoberto";
    [ObservableProperty] private string _discoveredApps = "";
    [ObservableProperty] private bool _hasDiscoveredApps;
    [ObservableProperty] private Brush _discoveryBrush = Dim;

    // ── Atualização ─────────────────────────────────────────────────────
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private bool _hasUpdateBanner;
    [ObservableProperty] private bool _canApplyUpdateNow;
    [ObservableProperty] private bool _canDownloadManually;
    [ObservableProperty] private string _diagUpdate = "—";

    public string DownloadPageUrl => ProductIdentity.LatestReleasePageUrl;

    // ── Controle pelo 2G Pilot ──────────────────────────────────────────
    [ObservableProperty] private string _controlStatusText = "";
    [ObservableProperty] private Brush _controlStatusBrush = Dim;
    [ObservableProperty] private string _controlDetail = "";
    [ObservableProperty] private string _pairingCodeText = "";
    [ObservableProperty] private string _pairingCountdownText = "";
    [ObservableProperty] private bool _hasPairingCode;
    [ObservableProperty] private bool _canStartPairing;
    [ObservableProperty] private bool _hasPairedDevices;
    [ObservableProperty] private string _diagControl = "—";

    public ObservableCollection<PairedDeviceItem> PairedDevices { get; } = [];

    // null = "reconstruir no próximo Refresh". Não pode ser "": é a assinatura da lista vazia,
    // e remover o último aparelho não limparia a lista.
    private string? _devicesSignature;

    // ── Configurações (campos de edição) ────────────────────────────────
    [ObservableProperty] private string _deviceNameInput = "";
    [ObservableProperty] private string _portInput = "";
    [ObservableProperty] private string _xgpsHzInput = "";
    [ObservableProperty] private string _xattHzInput = "";
    [ObservableProperty] private string _unicastInput = "";
    [ObservableProperty] private bool _startWithSimInput;
    [ObservableProperty] private bool _startMinimizedInput;
    [ObservableProperty] private bool _closeToTrayInput;
    [ObservableProperty] private bool _autoUpdateInput;
    [ObservableProperty] private bool _allowControlInput;
    [ObservableProperty] private string _controlPortInput = "";
    [ObservableProperty] private bool _canSyncFlightPlan;
    [ObservableProperty] private string _flightPlanFeedback = "";
    [ObservableProperty] private Brush _flightPlanFeedbackBrush = Dim;
    [ObservableProperty] private string _settingsFeedback = "";
    [ObservableProperty] private Brush _settingsFeedbackBrush = Dim;

    public string VersionText =>
        $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}  •  © 2026 2G";

    private void LoadSettingsIntoInputs()
    {
        DeviceNameInput = _settings.DeviceName;
        PortInput = _settings.Port.ToString(CultureInfo.InvariantCulture);
        XgpsHzInput = _settings.XgpsHz.ToString(CultureInfo.CurrentCulture);
        XattHzInput = _settings.XattHz.ToString(CultureInfo.CurrentCulture);
        UnicastInput = _settings.UnicastTargets;
        StartWithSimInput = _settings.StartWithSim;
        StartMinimizedInput = _settings.StartMinimized;
        CloseToTrayInput = _settings.CloseToTray;
        AutoUpdateInput = _settings.AutoUpdate;
        AllowControlInput = _settings.AllowControl;
        ControlPortInput = _settings.ControlPort.ToString(CultureInfo.InvariantCulture);
    }

    private int _refreshTick;
    private string _detectedInstalls = "";
    private string? _runningSimulator;
    private DateTime? _searchingSince;

    /// <summary>Depois disto, um simulador aberto que não conectou vira alerta.</summary>
    private static readonly TimeSpan ConnectPatience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Enquanto procura, distingue os três casos que exigem ações diferentes do
    /// usuário: nenhum simulador aberto, simulador abrindo, e simulador aberto que
    /// não aceita a conexão (o sintoma de incompatibilidade do SimConnect).
    /// </summary>
    private void UpdateSearchingStatus()
    {
        var error = _sim.LastError;
        if (error is not null)
        {
            SimStatusText = "Falha ao inicializar";
            SimDetail = error;
            SimStatusBrush = Err;
            return;
        }

        _searchingSince ??= DateTime.UtcNow;

        if (_runningSimulator is not null)
        {
            var waiting = DateTime.UtcNow - _searchingSince.Value;
            if (waiting > ConnectPatience)
            {
                SimStatusText = $"{_runningSimulator} não aceitou a conexão";
                SimDetail = "O simulador está aberto, mas o SimConnect não respondeu. "
                          + "Verifique se ele terminou de carregar; se persistir, "
                          + "pode ser incompatibilidade da versão do SimConnect.";
                SimStatusBrush = Err;
            }
            else
            {
                SimStatusText = $"Conectando ao {_runningSimulator}…";
                SimDetail = "Simulador em execução — estabelecendo a conexão SimConnect.";
                SimStatusBrush = Warn;
            }
            return;
        }

        SimStatusText = "Procurando simulador…";
        SimDetail = _detectedInstalls.Length > 0
            ? $"Instalado: {_detectedInstalls}. Abra o simulador para conectar."
            : "Conecta automaticamente ao abrir o MSFS ou o Prepar3D.";
        SimStatusBrush = Warn;
    }

    private void Refresh()
    {
        // Detecção de instalações e de processo é I/O leve; atualiza a cada ~10 s.
        if (_refreshTick++ % 40 == 0)
        {
            _detectedInstalls = string.Join(" e ",
                SimulatorInstallations.Detected().Select(i => i.DisplayName));
            _runningSimulator = SimulatorInstallations.RunningSimulator();
        }

        var state = _sim.State;
        switch (state)
        {
            case SimConnectionState.Searching:
                UpdateSearchingStatus();
                break;
            case SimConnectionState.Connected:
                _searchingSince = null;
                SimStatusText = "Conectado — aguardando voo";
                SimDetail = _sim.SimulatorName ?? "Simulador";
                SimStatusBrush = Ok;
                break;
            case SimConnectionState.Receiving:
                _searchingSince = null;
                SimStatusText = "Recebendo dados de voo";
                SimDetail = _sim.SimulatorName ?? "Simulador";
                SimStatusBrush = Ok;
                break;
        }

        if (_broadcaster.IsTransmitting)
        {
            TxStatusText = $"Transmitindo — UDP {_settings.Port}";
            TxStatusBrush = Ok;
        }
        else if (state == SimConnectionState.Searching)
        {
            TxStatusText = "Parado — sem simulador";
            TxStatusBrush = Dim;
        }
        else
        {
            TxStatusText = "Aguardando dados do simulador";
            TxStatusBrush = Warn;
        }

        TxDetail = $"XGPS {FormatHz(_settings.XgpsHz)} • XATT {FormatHz(_settings.XattHz)} • \"{_settings.DeviceName}\"";
        PacketsText = _broadcaster.PacketsSent == 0
            ? ""
            : $"{_broadcaster.PacketsSent.ToString("N0", CultureInfo.CurrentCulture)} pacotes";

        CanSyncFlightPlan = _sim.FlightPlans is { CanRead: true };

        UpdateDiagnostics();
        UpdateUpdaterStatus();
        UpdateControlStatus();

        var fix = _sim.LatestFix;
        if (fix is not null)
        {
            LatText = $"{Math.Abs(fix.LatitudeDeg).ToString("F5", CultureInfo.CurrentCulture)}° {(fix.LatitudeDeg >= 0 ? "N" : "S")}";
            LonText = $"{Math.Abs(fix.LongitudeDeg).ToString("F5", CultureInfo.CurrentCulture)}° {(fix.LongitudeDeg >= 0 ? "L" : "O")}";
            AltText = $"{(fix.AltitudeMslMeters * 3.28084).ToString("N0", CultureInfo.CurrentCulture)} ft";
            GsText = $"{(fix.GroundSpeedMps * 1.943844).ToString("N0", CultureInfo.CurrentCulture)} kt";
            TrkText = $"{NormalizeDeg(fix.TrackTrueDeg):000}°";
            HdgText = $"{NormalizeDeg(fix.HeadingTrueDeg):000}°";
            PitchText = $"{fix.PitchDegUp.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)}°";
            RollText = $"{fix.RollDegRight.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)}°";
            PositionOpacity = state == SimConnectionState.Receiving ? 1.0 : 0.5;
        }
        else
        {
            LatText = LonText = AltText = GsText = TrkText = HdgText = PitchText = RollText = "—";
            PositionOpacity = 0.35;
        }
    }

    /// <summary>
    /// Alimenta o painel de diagnóstico. Enumerar interfaces é I/O, então só
    /// acontece no mesmo tique lento (~10 s) da detecção de simuladores; os
    /// contadores e os destinos são leituras baratas e vão a cada tique.
    /// </summary>
    private void UpdateDiagnostics()
    {
        if (_refreshTick % 40 == 1 || DiagInterfaces == "—")
        {
            var nics = NetworkDiagnostics.ActiveIPv4Interfaces();
            DiagInterfaces = nics.Count == 0 ? "nenhuma interface IPv4 ativa" : string.Join("\n", nics);
        }

        var destinations = _broadcaster.Destinations;
        DiagDestinations = destinations.Count == 0
            ? "nenhum destino calculado"
            : string.Join("\n", destinations);

        var culture = CultureInfo.CurrentCulture;
        DiagCounters = string.Join("  •  ",
            $"enviados {_broadcaster.PacketsSent.ToString("N0", culture)}",
            $"falhas {_broadcaster.SendFailures.ToString("N0", culture)}",
            $"amostras inválidas {_sim.NonFiniteSamples.ToString("N0", culture)}");

        // Um erro de envio é a única prova local de que o pacote nem saiu da máquina.
        var sendError = _broadcaster.LastSendError;
        var discoveryError = _discovery?.LastError;
        // O Stop do servidor não limpa o LastError: com o controle desligado nas Configurações,
        // um erro de porta antigo acusaria um canal que o próprio piloto desligou.
        var controlError = _settings.AllowControl ? _control?.Server.LastError : null;
        DiagLastError = string.Join("  •  ",
            new[] { sendError, discoveryError, _updates?.LastError, controlError }.Where(e => e is { Length: > 0 }));
        HasDiagError = DiagLastError.Length > 0;

        UpdateDiscovery();
    }

    /// <summary>
    /// Mostra quantos apps foram descobertos e em quais IPs. Sem isso o piloto não
    /// tem como conferir se a descoberta funcionou, e um mecanismo que só funciona
    /// quando funciona não se depura em voo.
    /// </summary>
    private void UpdateDiscovery()
    {
        var found = _discovery?.Discovered ?? [];
        HasDiscoveredApps = found.Count > 0;

        if (found.Count == 0)
        {
            DiscoveryStatusText = "Nenhum app descoberto — transmitindo só em broadcast";
            DiscoveredApps = "";
            DiscoveryBrush = Dim;
            return;
        }

        DiscoveryStatusText = found.Count == 1
            ? "1 app descoberto — recebendo unicast"
            : $"{found.Count} apps descobertos — recebendo unicast";
        DiscoveryBrush = Ok;

        var now = DateTime.UtcNow;
        DiscoveredApps = string.Join("\n", found.Select(efb =>
        {
            var how = efb.Source == EfbDiscoverySource.Bonjour ? "Bonjour" : "anúncio";
            var age = (int)Math.Max(0, (now - efb.LastSeenUtc).TotalSeconds);
            return $"{efb.Address}  —  {efb.AppName} ({how}, há {age}s)";
        }));
    }

    /// <summary>
    /// Faixa abaixo do cabeçalho e linha no Diagnóstico. Leituras baratas: o serviço
    /// guarda o estado em memória, sem I/O por tique.
    /// </summary>
    private void UpdateUpdaterStatus()
    {
        var updates = _updates;
        if (updates is null || !updates.IsReleaseBuild)
        {
            HasUpdateBanner = false;
            CanApplyUpdateNow = false;
            CanDownloadManually = false;
            DiagUpdate = "desligado neste build (só builds de release se atualizam)";
            return;
        }

        var pending = updates.Pending;
        var latest = updates.LatestSeen;
        // Tentativas esgotadas: o TryApply não instala mais esta versão. Derivado da
        // pendência gravada, não do LastError, que a próxima verificação zeraria. Vai
        // para a faixa e, como o spec pede, para o Diagnóstico.
        var exhausted = pending is not null && pending.IsFor(updates.Kind) && pending.AttemptsExhausted
                        && !pending.IsStaleFor(updates.CurrentVersion)
            ? pending
            : null;

        // Mesma regra do TryApply: só oferece o que esta cópia vai de fato instalar.
        if (pending is not null && pending.CanBeInstalledBy(updates.Kind, updates.CurrentVersion))
        {
            HasUpdateBanner = true;
            CanApplyUpdateNow = true;
            CanDownloadManually = false;
            // Desligada, o TryApply recusa Startup e Exit: só o botão instala.
            UpdateBannerText = _settings.AutoUpdate
                ? $"v{pending.Version} pronta — instala ao reiniciar"
                : $"v{pending.Version} pronta — atualização automática desligada; use Atualizar agora";
        }
        else if (exhausted is not null)
        {
            HasUpdateBanner = true;
            CanApplyUpdateNow = false;
            CanDownloadManually = true;
            UpdateBannerText = $"A instalação da v{exhausted.Version} falhou {exhausted.Attempts} vezes — baixe manualmente";
        }
        else if (updates.Kind == InstallKind.ReadOnly && latest is not null && latest > updates.CurrentVersion)
        {
            HasUpdateBanner = true;
            CanApplyUpdateNow = false;
            CanDownloadManually = true;
            UpdateBannerText = $"v{latest} disponível — baixe manualmente (pasta do app sem permissão de escrita)";
        }
        else
        {
            HasUpdateBanner = false;
            CanApplyUpdateNow = false;
            CanDownloadManually = false;
        }

        // Vale nos dois ramos: as tentativas podem ter acabado antes de o piloto desligar a
        // automática, ou pelo botão, que instala (e conta tentativa) mesmo com ela desligada.
        var failed = exhausted is not null
            ? $"  •  instalação da v{exhausted.Version} falhou {exhausted.Attempts} vezes"
            : "";

        if (!_settings.AutoUpdate)
        {
            DiagUpdate = $"v{updates.CurrentVersion}  •  atualização automática desligada{failed}";
            return;
        }

        var check = updates.LastCheckUtc is { } at
            ? $"verificado há {FormatElapsed(DateTime.UtcNow - at)}"
            : "ainda não verificado";
        var seen = latest is not null ? $"  •  mais recente: v{latest}" : "";
        DiagUpdate = $"v{updates.CurrentVersion}  •  {check}{seen}{failed}";
    }

    /// <summary>Card "Controle pelo 2G Pilot" e linha no Diagnóstico.</summary>
    private void UpdateControlStatus()
    {
        var control = _control;
        if (control is null)
        {
            ControlStatusText = "Indisponível neste simulador";
            ControlStatusBrush = Dim;
            CanStartPairing = HasPairingCode = false;
            return;
        }

        if (!_settings.AllowControl)
        {
            ControlStatusText = "Desligado nas Configurações";
            ControlStatusBrush = Dim;
            ControlDetail = "";
            DiagControl = "desligado";
        }
        else if (!control.Server.IsRunning)
        {
            ControlStatusText = "Canal de controle indisponível";
            ControlStatusBrush = Err;
            ControlDetail = control.Server.LastError ?? "";
            DiagControl = control.Server.LastError ?? "servidor parado";
        }
        else
        {
            var paired = control.Server.Connected.Where(c => c.Paired).ToArray();
            ControlStatusText = paired.Length switch
            {
                0 => "Aguardando aparelho",
                1 => $"1 aparelho conectado: {paired[0].DeviceName}",
                var n => $"{n} aparelhos conectados",
            };
            ControlStatusBrush = paired.Length > 0 ? Ok : Warn;
            ControlDetail = $"TCP {control.Server.Port} • anúncio 2GCTL na UDP {_settings.Port}";
            DiagControl = $"conexões {control.Server.Connected.Count}"
                          + (control.Server.LastCommand is { } last ? $"  •  último comando: {last}" : "");
        }

        var active = control.Codes.Active;
        HasPairingCode = active is not null;
        if (active is { } code)
        {
            PairingCodeText = $"{code.Code[..3]} {code.Code[3..]}";
            PairingCountdownText = $"expira em {Math.Max(0, (int)(code.ExpiresUtc - DateTime.UtcNow).TotalSeconds)} s";
        }
        CanStartPairing = _settings.AllowControl && control.Server.IsRunning && !HasPairingCode;

        RefreshPairedDevices(control);
    }

    /// <summary>Recria a lista só quando algo mudou: a cada 250 ms seria piscar a tela à toa.</summary>
    private void RefreshPairedDevices(ControlService control)
    {
        var connected = control.Server.Connected.Where(c => c.Paired).Select(c => c.DeviceId).ToHashSet();
        var items = control.Devices.Devices
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new PairedDeviceItem(d.Id, d.Name,
                connected.Contains(d.Id) ? "conectado agora" : $"último acesso {d.LastSeenUtc.ToLocalTime():dd/MM HH:mm}"))
            .ToArray();

        var signature = string.Join("|", items.Select(i => $"{i.Id}:{i.Name}:{i.Detail}"));
        if (signature == _devicesSignature)
            return;
        _devicesSignature = signature;

        PairedDevices.Clear();
        foreach (var item in items)
            PairedDevices.Add(item);
        HasPairedDevices = items.Length > 0;
    }

    [RelayCommand]
    private void StartPairing()
    {
        _control?.Codes.Generate();
        Refresh();
    }

    [RelayCommand]
    private void CancelPairing()
    {
        _control?.Codes.Cancel();
        Refresh();
    }

    [RelayCommand]
    private void RemoveDevice(string? deviceId)
    {
        if (_control is null || deviceId is null)
            return;

        var name = _control.Devices.Devices.FirstOrDefault(d => d.Id == deviceId)?.Name ?? "este aparelho";
        var answer = System.Windows.MessageBox.Show(
            $"Remover \"{name}\"? Ele deixa de controlar o simulador e precisa parear de novo.",
            ProductIdentity.Name, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        _control.Devices.Remove(deviceId);   // derruba a conexão dele com 4001
        _devicesSignature = null;
        Refresh();
    }

    private static string FormatElapsed(TimeSpan span) =>
        span.TotalMinutes < 1 ? "menos de 1 min"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : $"{(int)span.TotalHours} h";

    [RelayCommand]
    private void ApplyUpdateNow()
    {
        if (System.Windows.Application.Current is not App app)
            return;

        var inFlight = _sim.State == SimConnectionState.Receiving;
        var pilotDeclined = false;
        var started = app.ApplyUpdateNow(inFlight, confirmInFlight: () =>
        {
            var confirmed = System.Windows.MessageBox.Show(
                "Você está em voo. O tablet fica sem posição por alguns segundos enquanto o "
                + $"{ProductIdentity.Name} se atualiza.\n\nAtualizar agora?",
                ProductIdentity.Name,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
            pilotDeclined = !confirmed;
            return confirmed;
        });

        // Caixa de mensagem, não texto na faixa: o próximo tique de 250 ms sobrescreveria
        // a faixa. Se quem recusou foi o piloto (Não na confirmação de voo), não há falha
        // a avisar — e o LastError estaria vazio.
        if (!started && !pilotDeclined)
            System.Windows.MessageBox.Show(
                _updates?.LastError ?? "Não foi possível instalar a atualização agora.",
                ProductIdentity.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
    }

    /// <summary>O "com o link" do spec: abre a página do release no navegador padrão.</summary>
    [RelayCommand]
    private void OpenDownloadPage()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(ProductIdentity.LatestReleasePageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Sem navegador associado: o endereço fica à vista para o piloto digitar.
            System.Windows.MessageBox.Show(
                $"Não foi possível abrir o navegador. Baixe a versão nova em:\n\n{ProductIdentity.LatestReleasePageUrl}",
                ProductIdentity.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    private static int NormalizeDeg(double deg)
    {
        var d = (int)Math.Round(deg) % 360;
        if (d < 0) d += 360;
        return d == 0 ? 360 : d;
    }

    private static string FormatHz(double hz) =>
        hz == Math.Floor(hz)
            ? $"{(int)hz} Hz"
            : $"{hz.ToString("0.#", CultureInfo.CurrentCulture)} Hz";

    /// <summary>
    /// Lê o plano ativo do simulador, publica no servidor local e avisa o EFB pela
    /// sentença 2GFPL no canal UDP que ele já escuta.
    /// </summary>
    [RelayCommand]
    private void SyncFlightPlan()
    {
        var source = _sim.FlightPlans;
        if (source is null || !source.CanRead)
        {
            ShowFlightPlanFeedback("Nenhum simulador conectado.", isError: true);
            return;
        }

        FlightPlanReadResult result;
        try
        {
            result = source.Read();
        }
        catch (Exception ex)
        {
            ShowFlightPlanFeedback($"Falha ao ler o plano: {ex.Message}", isError: true);
            return;
        }

        if (result.Plan is null)
        {
            ShowFlightPlanFeedback(result.Error ?? "Não foi possível ler o plano.", isError: true);
            return;
        }

        if (!_flightPlanServer.IsRunning)
        {
            ShowFlightPlanFeedback(
                _flightPlanServer.LastError ?? "Servidor de plano de voo indisponível.", isError: true);
            return;
        }

        var json = FlightPlanJson.Serialize(
            result.Plan, _sim.SimulatorName ?? _sim.Name, DateTime.UtcNow);
        _flightPlanServer.Publish(json);

        // O EFB descobre o IP do PC pela própria origem do datagrama, mas a URL
        // precisa ser absoluta para ele buscar — usamos o IP local da rota de saída.
        var url = $"http://{LocalAddressForEfb()}:{_flightPlanServer.Port}{FlightPlanServer.Path}";
        _broadcaster.SendNow(XgpsSentences.FormatFlightPlanAnnounce(
            _settings.DeviceName, FlightPlanJson.SchemaVersion, url));

        ShowFlightPlanFeedback($"Plano enviado — {result.Plan.Summary}", isError: false);
    }

    /// <summary>
    /// IP desta máquina na rede local. Descoberto abrindo um socket UDP para um
    /// destino externo: não envia nada, só faz o Windows escolher a interface de saída.
    /// </summary>
    private static string LocalAddressForEfb()
    {
        try
        {
            using var probe = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            probe.Connect("8.8.8.8", 65530);
            if (probe.LocalEndPoint is System.Net.IPEndPoint endpoint)
                return endpoint.Address.ToString();
        }
        catch (Exception)
        {
            // Sem rota externa: o EFB ainda pode tentar pelo hostname.
        }
        return System.Net.Dns.GetHostName();
    }

    private void ShowFlightPlanFeedback(string message, bool isError)
    {
        FlightPlanFeedback = message;
        FlightPlanFeedbackBrush = isError ? Err : Ok;
    }

    [RelayCommand]
    private void ApplySettings()
    {
        if (!int.TryParse(PortInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            ShowFeedback("Porta inválida (1–65535).", isError: true);
            return;
        }

        if (!TryParseHz(XgpsHzInput, 0.5, 10, out var xgpsHz))
        {
            ShowFeedback("Taxa XGPS inválida (0,5–10 Hz).", isError: true);
            return;
        }

        if (!TryParseHz(XattHzInput, 1, 10, out var xattHz))
        {
            ShowFeedback("Taxa XATT inválida (1–10 Hz).", isError: true);
            return;
        }

        if (!int.TryParse(ControlPortInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var controlPort)
            || controlPort is < 1 or > 65535 || controlPort == _settings.FlightPlanPort)
        {
            ShowFeedback("Porta do controle inválida (1–65535, diferente da do plano de voo).", isError: true);
            return;
        }

        var device = XgpsSentences.SanitizeDeviceName(DeviceNameInput);
        if (device.Length == 0)
        {
            ShowFeedback("Informe um nome de dispositivo (ASCII).", isError: true);
            return;
        }

        foreach (var target in SplitTargets(UnicastInput))
        {
            if (!System.Net.IPAddress.TryParse(target, out var ip)
                || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                ShowFeedback($"IP inválido (use IPv4): {target}", isError: true);
                return;
            }
        }

        _settings.DeviceName = device;
        _settings.Port = port;
        _settings.XgpsHz = xgpsHz;
        _settings.XattHz = xattHz;
        _settings.UnicastTargets = UnicastInput.Trim();
        _settings.StartWithSim = StartWithSimInput;
        _settings.StartMinimized = StartMinimizedInput;
        _settings.CloseToTray = CloseToTrayInput;
        var autoUpdateTurnedOn = AutoUpdateInput && !_settings.AutoUpdate;
        _settings.AutoUpdate = AutoUpdateInput;
        _settings.AllowControl = AllowControlInput;
        _settings.ControlPort = controlPort;

        _settingsService.Save(_settings);
        _broadcaster.UpdateSettings(_settings);
        _control?.Apply();
        DeviceNameInput = device;

        // O timer do UpdateService só volta a verificar em até 6 h, e a verificação
        // dos 60 s não fez nada se a opção estava desligada: verifica já. No pool, como
        // o timer: o UpdateChecker não usa ConfigureAwait(false) e o download inteiro
        // voltaria para o thread da UI.
        if (autoUpdateTurnedOn && _updates is { } updates)
            _ = Task.Run(updates.CheckNowAsync);

        var extra = SyncAutoStart();
        ShowFeedback($"Configurações aplicadas ✓{extra}", isError: false);
        Refresh();
    }

    /// <summary>Sincroniza o registro no EXE.xml e resume o resultado para o feedback.</summary>
    private string SyncAutoStart()
    {
        try
        {
            var results = new ExeXmlAutoStart().Sync(StartWithSimInput);
            if (results.Count == 0)
                return StartWithSimInput ? " — nenhum MSFS detectado ainda" : "";

            var failure = results.FirstOrDefault(r => !r.Success);
            return failure is not null
                ? $" — {failure.SimName}: {failure.Detail}"
                : "";
        }
        catch (Exception)
        {
            return " — falha ao atualizar EXE.xml";
        }
    }

    internal static IEnumerable<string> SplitTargets(string raw) =>
        raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseHz(string raw, double min, double max, out double hz)
    {
        raw = raw.Trim().Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out hz)
               && hz >= min && hz <= max;
    }

    private void ShowFeedback(string message, bool isError)
    {
        SettingsFeedback = message;
        SettingsFeedbackBrush = isError ? Err : Ok;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }
}

/// <summary>Uma linha da lista de aparelhos pareados.</summary>
public sealed record PairedDeviceItem(string Id, string Name, string Detail);
