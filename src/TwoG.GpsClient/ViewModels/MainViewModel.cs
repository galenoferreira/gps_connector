using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwoG.GpsClient.Configuration;
using TwoG.GpsClient.Core;
using TwoG.GpsClient.Services;

namespace TwoG.GpsClient.ViewModels;

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
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _feedbackTimer;

    public MainViewModel(ISimSource sim, IXgpsBroadcaster broadcaster,
                         SettingsService settingsService, AppSettings settings)
    {
        _sim = sim;
        _broadcaster = broadcaster;
        _settingsService = settingsService;
        _settings = settings;

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

    // ── Configurações (campos de edição) ────────────────────────────────
    [ObservableProperty] private string _deviceNameInput = "";
    [ObservableProperty] private string _portInput = "";
    [ObservableProperty] private string _xgpsHzInput = "";
    [ObservableProperty] private string _xattHzInput = "";
    [ObservableProperty] private string _unicastInput = "";
    [ObservableProperty] private bool _startWithSimInput;
    [ObservableProperty] private bool _startMinimizedInput;
    [ObservableProperty] private bool _closeToTrayInput;
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
    }

    private int _refreshTick;
    private string _detectedInstalls = "";

    private void Refresh()
    {
        // Detecção de instalações é I/O leve; atualiza a cada ~10 s.
        if (_refreshTick++ % 40 == 0)
        {
            _detectedInstalls = string.Join(" e ",
                MsfsInstallations.Detected().Select(i => i.DisplayName));
        }

        var state = _sim.State;
        switch (state)
        {
            case SimConnectionState.Searching:
                SimStatusText = "Procurando simulador…";
                SimDetail = _detectedInstalls.Length > 0
                    ? $"Instalado: {_detectedInstalls}. Abra o simulador para conectar."
                    : "Conecta automaticamente quando o MSFS estiver aberto.";
                SimStatusBrush = Warn;
                break;
            case SimConnectionState.Connected:
                SimStatusText = "Conectado — aguardando voo";
                SimDetail = _sim.SimulatorName ?? "Microsoft Flight Simulator";
                SimStatusBrush = Ok;
                break;
            case SimConnectionState.Receiving:
                SimStatusText = "Recebendo dados de voo";
                SimDetail = _sim.SimulatorName ?? "Microsoft Flight Simulator";
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

        var device = DeviceNameInput.Replace(",", " ").Trim();
        if (device.Length == 0)
        {
            ShowFeedback("Informe um nome de dispositivo.", isError: true);
            return;
        }

        foreach (var target in SplitTargets(UnicastInput))
        {
            if (!System.Net.IPAddress.TryParse(target, out _))
            {
                ShowFeedback($"IP inválido: {target}", isError: true);
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

        _settingsService.Save(_settings);
        _broadcaster.UpdateSettings(_settings);
        DeviceNameInput = device;

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
