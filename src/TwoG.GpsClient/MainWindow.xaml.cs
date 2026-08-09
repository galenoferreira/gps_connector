using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using TwoG.GpsClient.ViewModels;

namespace TwoG.GpsClient;

public partial class MainWindow : Window
{
    private bool _trayBalloonShown;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Encerramento real (menu Sair): não intercepta o Closing.</summary>
    public void ExitApplication()
    {
        _exiting = true;
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;   // truque para trazer à frente…
        Topmost = false;  // …sem ficar sempre visível
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableDarkTitleBar();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && Vm?.CloseToTray == true)
        {
            e.Cancel = true;
            Hide();
            if (!_trayBalloonShown)
            {
                _trayBalloonShown = true;
                TrayIcon.ShowBalloonTip("2G GPS Cliente",
                    "Continua transmitindo na bandeja do sistema.", BalloonIcon.Info);
            }
            return;
        }

        base.OnClosing(e);
        if (!_exiting)
        {
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    // ── Barra de título escura (Windows 10 20H1+) ───────────────────────
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TryEnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }
        catch
        {
            // Estético apenas; ignora em versões antigas do Windows.
        }
    }
}
