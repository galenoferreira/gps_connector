using System.Net;

namespace TwoG.Connector.Core;

/// <summary>
/// Descoberta dos EFBs presentes na rede local, para o transmissor somar unicast
/// ao broadcast. Só acrescenta destinos — nunca dispensa o broadcast.
/// </summary>
public interface IEfbDiscovery : IDisposable
{
    /// <summary>EFBs vistos recentemente, do mais recente para o mais antigo.</summary>
    IReadOnlyList<DiscoveredEfb> Discovered { get; }

    /// <summary>Só os endereços — o que o transmissor precisa a cada envio.</summary>
    IReadOnlyList<IPAddress> Targets { get; }

    /// <summary>Quantos anúncios já chegaram na UDP 63093.</summary>
    long AnnouncementsReceived { get; }

    /// <summary>Falha que impede a descoberta, ou null quando está tudo bem.</summary>
    string? LastError { get; }

    void Start();
    void Stop();
}
