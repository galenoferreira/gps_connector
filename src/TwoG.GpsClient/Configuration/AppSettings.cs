namespace TwoG.GpsClient.Configuration;

/// <summary>Configurações persistidas em %APPDATA%\2G GPS Cliente\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Nome do dispositivo mostrado no EFB (prefixo das sentenças XGPS/XATT).</summary>
    public string DeviceName { get; set; } = "2G GPS";

    /// <summary>Porta UDP de destino (padrão XGPS: 49002).</summary>
    public int Port { get; set; } = 49002;

    /// <summary>Frequência de envio da sentença de posição XGPS, em Hz.</summary>
    public double XgpsHz { get; set; } = 5.0;

    /// <summary>Frequência de envio da sentença de atitude XATT, em Hz.</summary>
    public double XattHz { get; set; } = 5.0;

    /// <summary>
    /// IPs adicionais para envio unicast (além do broadcast), separados por vírgula.
    /// Útil quando o tablet está em outra sub-rede ou o roteador bloqueia broadcast.
    /// </summary>
    public string UnicastTargets { get; set; } = "";

    /// <summary>Registrar no EXE.xml do MSFS para iniciar junto com o simulador.</summary>
    public bool StartWithSim { get; set; } = true;

    /// <summary>Iniciar minimizado na bandeja (usado quando lançado junto com o simulador).</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Fechar a janela envia o app para a bandeja em vez de encerrar.</summary>
    public bool CloseToTray { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
