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
    private SimConnectService? _sim;
    private XgpsBroadcaster? _broadcaster;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        _sim = new SimConnectService();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _broadcaster?.Dispose();
        _sim?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
