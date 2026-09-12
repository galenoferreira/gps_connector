using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;
using TwoG.Connector.Services;
using TwoG.Connector.ViewModels;

namespace TwoG.Connector;

public partial class App : Application
{
    private const string MutexName = ProductIdentity.SingleInstanceMutexName;
    private const string ShowEventName = ProductIdentity.ShowWindowEventName;

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private ISimSource? _sim;
    private XgpsBroadcaster? _broadcaster;
    private FlightPlanServer? _flightPlanServer;
    private EfbDiscoveryService? _discovery;
    private UpdateService? _updates;
    private bool _userExit;
    private bool _updateLaunched;
    private bool _sessionEnding;

    internal UpdateService? Updates => _updates;

    /// <summary>
    /// Encerramento que não veio do piloto: atualização iniciada ou fim da sessão do
    /// Windows. O Shutdown do WPF dispara o Closing da janela mesmo assim; ela não
    /// deve ir para a bandeja nem marcar saída do piloto.
    /// </summary>
    internal bool IsClosingWithoutUser => _updateLaunched || _sessionEnding;

    /// <summary>
    /// Isolado e sem inline de propósito: o JIT deste método é o primeiro ponto que
    /// carrega tipos do SimConnect, e ele só acontece na chamada — depois de
    /// <see cref="SimConnectRuntime.Ensure"/> ter extraído e registrado as DLLs.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ISimSource CreateSimSource() =>
        new CompositeSimSource(new SimConnectService(), new XPlaneService());

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sem isto, qualquer exceção não tratada fecha o app sem deixar rastro.
        DispatcherUnhandledException += (_, args) =>
        {
            ReportFatal(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportFatal(args.ExceptionObject as Exception);

        // Logoff/desligamento: o WPF chama Shutdown depois deste evento, e o Windows
        // pode matar o processo no meio de uma instalação. Nunca instala aqui.
        SessionEnding += (_, _) => _sessionEnding = true;

        // Lançado da pasta de updates pela versão anterior: troca o exe avulso e sai.
        if (UpdateInstaller.IsReplaceRequest(e.Args))
        {
            UpdateInstaller.RunReplace(e.Args, AcquireSingleInstance, ReleaseSingleInstance);
            Shutdown();
            return;
        }

        // Chamado pelo desinstalador: remove nossas entradas dos EXE.xml e sai.
        if (e.Args.Any(a => string.Equals(a, "-unregister", StringComparison.OrdinalIgnoreCase)))
        {
            new ExeXmlAutoStart().UnregisterEverywhere();
            Shutdown();
            return;
        }

        // Chamado pelo instalador: registra nos EXE.xml, se configurado, e sai.
        if (e.Args.Any(a => string.Equals(a, "-register", StringComparison.OrdinalIgnoreCase)))
        {
            RegisterFromInstaller();
            Shutdown();
            return;
        }

        var startMinimizedArg = e.Args.Any(IsMinimizedArg);

        var updatedArg = e.Args.Any(a =>
            string.Equals(a, UpdateInstaller.UpdatedArg, StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance && updatedArg)
        {
            // Relançado pelo updater: a versão anterior está saindo agora. Espera o
            // mutex em vez de sair como segunda instância.
            isFirstInstance = WaitForMutex(_singleInstanceMutex, TimeSpan.FromSeconds(10));
        }

        if (!isFirstInstance)
        {
            // Já existe uma instância. Se o relançamento foi deliberado (usuário),
            // pede que ela se mostre; se veio do autostart do MSFS (-minimized),
            // não rouba o foco do simulador.
            if (!startMinimizedArg)
            {
                try
                {
                    EventWaitHandle.OpenExisting(ShowEventName).Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (UnauthorizedAccessException) { }
            }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        UpdateInstaller.CleanupAfterUpdate(Environment.ProcessPath);
        _updates = UpdateService.Create(settings);

        // Momento seguro nº 1: antes de o simulador conectar não há voo a interromper.
        if (_updates.TryApply(UpdateTrigger.Startup, inFlight: false, RelaunchArgs(e.Args),
                              relaunch: true, ReleaseSingleInstance))
        {
            _updateLaunched = true;
            Shutdown();
            return;
        }

        // Extrai as DLLs do SimConnect (recursos embutidos) antes de tocar em
        // qualquer tipo do SimConnect.
        SimConnectRuntime.Ensure();

        _sim = CreateSimSource();
        _discovery = new EfbDiscoveryService();
        _broadcaster = new XgpsBroadcaster(_sim, settings, _discovery);
        _flightPlanServer = new FlightPlanServer();
        _flightPlanServer.Start(settings.FlightPlanPort);
        var viewModel = new MainViewModel(_sim, _broadcaster, settingsService, settings, _flightPlanServer, _discovery);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;

        var startMinimized = settings.StartMinimized || startMinimizedArg;
        if (!startMinimized)
            window.Show();

        // Segunda instância lançada → traz a janela existente à frente.
        var showListener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        })
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };
        showListener.Start();

        _sim.Start();
        _discovery.Start();
        _broadcaster.Start();
        _updates.Start();

        // Auto-reparo do auto-start: updates do MSFS às vezes apagam o EXE.xml.
        if (settings.StartWithSim)
        {
            Task.Run(() =>
            {
                try
                {
                    new ExeXmlAutoStart().Sync(enabled: true);
                }
                catch (Exception)
                {
                    // Melhor esforço; o usuário pode ressincronizar pela UI.
                }
            });
        }
    }

    /// <summary>
    /// "-register": o mesmo autorreparo do EXE.xml da abertura, sem UI. Na
    /// atualização sobre a v1.3.0 o instalador apaga o exe antigo; sem isto a entrada
    /// legada ficaria apontando para ele até o app novo ser aberto, e o simulador
    /// deixaria de lançar o conector. Respeita "iniciar com o simulador".
    /// </summary>
    private static void RegisterFromInstaller()
    {
        try
        {
            if (new SettingsService().Load().StartWithSim)
                new ExeXmlAutoStart().Sync(enabled: true);
        }
        catch (Exception)
        {
            // Melhor esforço, sem deixar exceção escapar: o ReportFatal abriria uma
            // mensagem, e o instalador ficaria parado esperando este processo.
            // A próxima abertura do app repara.
        }
    }

    /// <summary>
    /// Registra a falha em disco e avisa o usuário, em vez de sumir da tela.
    /// </summary>
    private static void ReportFatal(Exception? ex)
    {
        if (ex is null)
            return;

        var logPath = "";
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductIdentity.DataFolderName);
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "erro.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Sem log é ruim, mas não impede o aviso na tela.
        }

        try
        {
            var detail = logPath.Length > 0 ? $"{Environment.NewLine}{Environment.NewLine}Detalhes em: {logPath}" : "";
            MessageBox.Show($"Ocorreu um erro inesperado:{Environment.NewLine}{Environment.NewLine}{ex.Message}{detail}",
                ProductIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Se nem MessageBox funciona, não há mais o que fazer.
        }
    }

    private static bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;    // a anterior saiu sem liberar: agora é nosso
        }
    }

    /// <summary>Pega o mutex de instância única, esperando até <paramref name="timeout"/>.</summary>
    private bool AcquireSingleInstance(TimeSpan timeout)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out var owned);
        return owned || WaitForMutex(_singleInstanceMutex, timeout);
    }

    private static bool IsMinimizedArg(string arg) =>
        string.Equals(arg, "-minimized", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase);

    /// <summary>Argumentos para reabrir o app depois de atualizar, sem o marcador do updater.</summary>
    private static string[] RelaunchArgs(IEnumerable<string> args) =>
        args.Where(a => !string.Equals(a, UpdateInstaller.UpdatedArg, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>Solta o mutex de instância única. Idempotente.</summary>
    internal void ReleaseSingleInstance()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Não éramos donos (ex.: segunda instância saindo).
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    /// <summary>Encerramento pedido pelo piloto (menu Sair ou fechar sem bandeja).</summary>
    internal void BeginUserExit() => _userExit = true;

    /// <summary>Botão "Atualizar agora". Devolve false se não iniciou a instalação.</summary>
    internal bool ApplyUpdateNow(bool inFlight, Func<bool> confirmInFlight)
    {
        if (_updates is null)
            return false;
        // O botão foi clicado com a janela à vista: a versão nova volta à vista, mesmo
        // que esta tenha sido aberta com -minimized pelo MSFS ou pela chave Run.
        var args = RelaunchArgs(Environment.GetCommandLineArgs().Skip(1).Where(a => !IsMinimizedArg(a)));
        if (!_updates.TryApply(UpdateTrigger.Manual, inFlight, args, relaunch: true,
                               ReleaseSingleInstance, confirmInFlight))
            return false;

        _updateLaunched = true;
        Shutdown();
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _flightPlanServer?.Dispose();
        _broadcaster?.Dispose();
        _discovery?.Dispose();
        _sim?.Dispose();

        // Momento seguro nº 2: o piloto mandou encerrar. Instala sem reabrir. Logoff
        // e desligamento do Windows não instalam (_sessionEnding, via SessionEnding).
        if (_userExit && !_updateLaunched && !_sessionEnding)
            _updates?.TryApply(UpdateTrigger.Exit, inFlight: false, [], relaunch: false, ReleaseSingleInstance);

        _updates?.Dispose();
        ReleaseSingleInstance();
        base.OnExit(e);
    }
}
