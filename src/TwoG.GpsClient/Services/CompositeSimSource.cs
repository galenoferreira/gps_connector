using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Roda todas as fontes de simulador ao mesmo tempo e expõe a que estiver ativa.
///
/// É o que dá a detecção automática sem o usuário escolher nada: procurar o MSFS
/// (retry do SimConnect) e escutar o beacon do X-Plane são ambos baratos e
/// inofensivos quando o simulador não está lá, então quem primeiro entregar dados
/// vence. Na prática só um simulador roda por vez.
/// </summary>
public sealed class CompositeSimSource : ISimSource
{
    private readonly IReadOnlyList<ISimSource> _sources;

    public CompositeSimSource(params ISimSource[] sources) => _sources = sources;

    /// <summary>
    /// Fonte mais "adiantada": prioriza quem está recebendo dados, depois quem
    /// está conectado e, em empate, o fix mais recente.
    /// </summary>
    private ISimSource? Active
    {
        get
        {
            ISimSource? best = null;
            var bestState = SimConnectionState.Searching;
            DateTime bestFix = DateTime.MinValue;

            foreach (var source in _sources)
            {
                var state = source.State;
                var fixTime = source.LatestFix?.Utc ?? DateTime.MinValue;

                if (best is null || state > bestState || (state == bestState && fixTime > bestFix))
                {
                    best = source;
                    bestState = state;
                    bestFix = fixTime;
                }
            }
            return bestState == SimConnectionState.Searching ? null : best;
        }
    }

    public SimConnectionState State => Active?.State ?? SimConnectionState.Searching;

    public string? SimulatorName => Active?.SimulatorName;

    public GpsFix? LatestFix => Active?.LatestFix;

    /// <summary>
    /// Só reporta erro quando nenhuma fonte conseguiu conectar — do contrário, uma
    /// falha do SimConnect apareceria na tela mesmo com o X-Plane funcionando.
    /// </summary>
    public string? LastError
    {
        get
        {
            if (Active is not null)
                return null;
            foreach (var source in _sources)
            {
                if (source.LastError is { Length: > 0 } error)
                    return error;
            }
            return null;
        }
    }

    public void Start()
    {
        foreach (var source in _sources)
            source.Start();
    }

    public void Stop()
    {
        foreach (var source in _sources)
            source.Stop();
    }

    public void Dispose()
    {
        foreach (var source in _sources)
            source.Dispose();
    }
}
