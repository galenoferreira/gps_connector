using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

public enum SimConnectionState
{
    /// <summary>Procurando o simulador (tentativas periódicas de conexão).</summary>
    Searching,

    /// <summary>SimConnect aberto, mas ainda sem dados de voo recentes (menu, pausa, carregando).</summary>
    Connected,

    /// <summary>Recebendo posição do voo ativamente.</summary>
    Receiving,
}

/// <summary>Fonte de dados de posição do simulador (SimConnect).</summary>
public interface ISimSource : IDisposable
{
    SimConnectionState State { get; }

    /// <summary>Nome/versão do simulador conectado (ex.: "Microsoft Flight Simulator 2024").</summary>
    string? SimulatorName { get; }

    /// <summary>Última amostra recebida (referência atômica; pode ser lida de qualquer thread).</summary>
    GpsFix? LatestFix { get; }

    void Start();
    void Stop();
}
