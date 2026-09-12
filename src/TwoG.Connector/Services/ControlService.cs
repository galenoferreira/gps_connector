using System.IO;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Junta as peças do canal de controle — servidor, código de pareamento, aparelhos e
/// anúncio — e as liga ou desliga conforme "Permitir controle pelo 2G Pilot".
/// Desligado: a porta 49004 não abre, o 2GCTL não é anunciado e as conexões são fechadas.
/// Os pareamentos continuam guardados.
/// </summary>
// Público como os demais serviços: o MainViewModel, público, o recebe no construtor
// (internal daria CS0051 — o mesmo caso do UpdateService na v1.4).
public sealed class ControlService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ControlAnnouncer _announcer;

    public ControlService(ISimControl control, IXgpsBroadcaster broadcaster, AppSettings settings, string connectorVersion)
    {
        _settings = settings;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductIdentity.DataFolderName);
        Devices = new PairedDeviceStore(Path.Combine(dir, "paired-devices.json"));
        Codes = new PairingCodes();
        Server = new ControlServer(control, Devices, Codes, connectorVersion);
        _announcer = new ControlAnnouncer(broadcaster, () => _settings.DeviceName, () => Server.Port);
    }

    public PairedDeviceStore Devices { get; }

    public PairingCodes Codes { get; }

    public ControlServer Server { get; }

    /// <summary>Aplica as Configurações atuais. Chamado na partida e ao clicar em Aplicar.</summary>
    public void Apply()
    {
        if (!_settings.AllowControl)
        {
            _announcer.Stop();
            Codes.Cancel();
            Server.Stop();
            return;
        }

        if (!Server.IsRunning || Server.Port != _settings.ControlPort)
            Server.Start(_settings.ControlPort);

        // Sem servidor (porta ocupada), anunciar seria convidar o app a uma porta fechada.
        if (Server.IsRunning)
            _announcer.Start();
        else
            _announcer.Stop();
    }

    public void Dispose()
    {
        _announcer.Dispose();
        Server.Dispose();
    }
}
