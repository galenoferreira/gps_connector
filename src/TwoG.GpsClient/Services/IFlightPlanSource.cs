using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Services;

/// <param name="Plan">O plano lido, ou null quando não foi possível.</param>
/// <param name="Error">Motivo em linguagem de usuário, quando <paramref name="Plan"/> é null.</param>
public sealed record FlightPlanReadResult(FlightPlan? Plan, string? Error)
{
    public static FlightPlanReadResult Ok(FlightPlan plan) => new(plan, null);
    public static FlightPlanReadResult Fail(string error) => new(null, error);
}

/// <summary>
/// Capacidade opcional de uma fonte de simulador: entregar o plano de voo ativo.
/// Nem todo simulador consegue — no X-Plane depende de o piloto ter salvado o plano.
/// </summary>
public interface IFlightPlanSource
{
    /// <summary>False quando não faz sentido nem oferecer o botão.</summary>
    bool CanRead { get; }

    /// <summary>Lê o plano agora. Chamado da thread da UI: não deve bloquear muito.</summary>
    FlightPlanReadResult Read();
}
