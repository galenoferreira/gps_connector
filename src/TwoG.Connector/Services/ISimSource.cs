using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

public enum SimConnectionState
{
    /// <summary>Procurando o simulador (tentativas periódicas de conexão).</summary>
    Searching,

    /// <summary>SimConnect aberto, mas ainda sem dados de voo recentes (menu, pausa, carregando).</summary>
    Connected,

    /// <summary>Recebendo posição do voo ativamente.</summary>
    Receiving,
}

/// <summary>Fonte de dados de posição de um simulador.</summary>
public interface ISimSource : IDisposable
{
    /// <summary>Nome curto do transporte, para rotular erros ("SimConnect", "X-Plane").</summary>
    string Name { get; }

    SimConnectionState State { get; }

    /// <summary>Nome/versão do simulador conectado (ex.: "Microsoft Flight Simulator 2024").</summary>
    string? SimulatorName { get; }

    /// <summary>Última amostra recebida (referência atômica; pode ser lida de qualquer thread).</summary>
    GpsFix? LatestFix { get; }

    /// <summary>
    /// Amostras descartadas por trazerem NaN/Infinity. Diagnóstico: se sobe
    /// enquanto o EFB congela, o simulador está mandando lixo — não é a rede.
    /// </summary>
    long NonFiniteSamples { get; }

    /// <summary>
    /// Falha persistente que impede a conexão (ex.: DLLs do SimConnect indisponíveis),
    /// ou null quando o único motivo de não estar conectado é o simulador estar fechado.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Leitura de plano de voo, quando a fonte souber fazer. Null quando não suporta.
    /// </summary>
    IFlightPlanSource? FlightPlans { get; }

    /// <summary>
    /// Comando do simulador pelo 2G Pilot, quando a fonte souber fazer. Null quando não
    /// suporta (X-Plane, até o spec 03).
    /// </summary>
    ISimControl? Control { get; }

    void Start();
    void Stop();
}
