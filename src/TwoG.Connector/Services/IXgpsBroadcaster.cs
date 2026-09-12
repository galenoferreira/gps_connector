using System.Net;
using TwoG.Connector.Configuration;

namespace TwoG.Connector.Services;

/// <summary>Transmissor UDP das sentenças XGPS/XATT para os EFBs da rede.</summary>
public interface IXgpsBroadcaster : IDisposable
{
    /// <summary>True quando há dados recentes do simulador sendo transmitidos agora.</summary>
    bool IsTransmitting { get; }

    long PacketsSent { get; }

    /// <summary>Momento (UTC) do último pacote enviado, ou null se nunca enviou.</summary>
    DateTime? LastSendUtc { get; }

    /// <summary>
    /// Destinos reais em uso agora ("192.168.1.255:49002"), broadcast dirigido de
    /// cada interface mais os unicast configurados. Diagnóstico: se a sub-rede do
    /// tablet não aparece aqui, o pacote nunca teve chance de chegar.
    /// </summary>
    IReadOnlyList<string> Destinations { get; }

    /// <summary>Envios que falharam no socket (interface caiu, rota sumiu).</summary>
    long SendFailures { get; }

    /// <summary>Texto do último erro de envio, ou null se nunca falhou.</summary>
    string? LastSendError { get; }

    void Start();
    void Stop();

    /// <summary>Aplica novas configurações (porta, taxas, nome do dispositivo, unicast).</summary>
    void UpdateSettings(AppSettings settings);

    /// <summary>Envia uma sentença avulsa imediatamente (ex.: anúncio de plano de voo).</summary>
    void SendNow(string sentence);

    /// <summary>
    /// Envia a cada destino atual uma sentença própria, montada por <paramref name="sentenceFor"/>.
    /// Destino para o qual a função devolve null é pulado. Usado pelo anúncio 2GCTL, cujo
    /// conteúdo depende da interface de saída. Não conta em <see cref="PacketsSent"/> nem em
    /// <see cref="LastSendUtc"/>, que ficam sendo a prova de que o XGPS está saindo; falhas
    /// de socket contam em <see cref="SendFailures"/>.
    /// </summary>
    void SendToEach(Func<IPEndPoint, string?> sentenceFor);
}
