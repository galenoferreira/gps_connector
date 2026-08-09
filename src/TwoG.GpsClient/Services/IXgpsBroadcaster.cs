using TwoG.GpsClient.Configuration;

namespace TwoG.GpsClient.Services;

/// <summary>Transmissor UDP das sentenças XGPS/XATT para os EFBs da rede.</summary>
public interface IXgpsBroadcaster : IDisposable
{
    /// <summary>True quando há dados recentes do simulador sendo transmitidos agora.</summary>
    bool IsTransmitting { get; }

    long PacketsSent { get; }

    /// <summary>Momento (UTC) do último pacote enviado, ou null se nunca enviou.</summary>
    DateTime? LastSendUtc { get; }

    void Start();
    void Stop();

    /// <summary>Aplica novas configurações (porta, taxas, nome do dispositivo, unicast).</summary>
    void UpdateSettings(AppSettings settings);
}
