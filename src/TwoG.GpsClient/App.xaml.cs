using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using TwoG.GpsClient.Configuration;
using TwoG.GpsClient.Services;
using TwoG.GpsClient.ViewModels;

namespace TwoG.GpsClient;

public partial class App : Application
{
    private const string MutexName = @"Local\TwoG.GpsClient.SingleInstance";
    private const string ShowEventName = @"Local\TwoG.GpsClient.ShowWindow";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private ISimSource? _sim;
    private XgpsBroadcaster? _broadcaster;

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

        // Chamado pelo desinstalador: remove nossas entradas dos EXE.xml e sai.
        if (e.Args.Any(a => string.Equals(a, "-unregister", StringComparison.OrdinalIgnoreCase)))
        {
            new ExeXmlAutoStart().UnregisterEverywhere();
            Shutdown();
            return;
        }

        var startMinimizedArg = e.Args.Any(a =>
            string.Equals(a, "-minimized", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
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

        // Extrai as DLLs do SimConnect (recursos embutidos) antes de tocar em
        // qualquer tipo do SimConnect.
        SimConnectRuntime.Ensure();

        _sim = CreateSimSource();
        _broadcaster = new XgpsBroadcaster(_sim, settings);
        var viewModel = new MainViewModel(_sim, _broadcaster, settingsService, settings);

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
        _broadcaster.Start();

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
                "2G GPS Cliente");
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
                "2G GPS Cliente", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Se nem MessageBox funciona, não há mais o que fazer.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _broadcaster?.Dispose();
        _sim?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
