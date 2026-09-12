namespace TwoG.Connector.Core;

public enum UpdateTrigger
{
    /// <summary>Ao abrir, antes de o simulador conectar.</summary>
    Startup,

    /// <summary>O piloto mandou encerrar o conector.</summary>
    Exit,

    /// <summary>Botão "Atualizar agora".</summary>
    Manual,

    /// <summary>Timer de verificação. Nunca instala.</summary>
    Periodic,
}

public enum UpdateDecision
{
    Apply,

    /// <summary>Em voo: só com confirmação explícita do piloto.</summary>
    ConfirmFirst,

    Skip,
}

/// <summary>
/// Quando uma atualização já baixada pode ser instalada. Reiniciar o conector no
/// meio de um voo é o piloto perdendo posição no tablet; por isso só há três
/// momentos, e nenhum deles acontece por conta própria durante a sessão.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>Tentativas por versão antes de desistir dela (evita ciclo a cada partida).</summary>
    public const int MaxAttempts = 2;

    public static UpdateDecision Decide(UpdateTrigger trigger, bool inFlight, int attemptsSoFar)
    {
        if (attemptsSoFar >= MaxAttempts)
            return UpdateDecision.Skip;

        return trigger switch
        {
            UpdateTrigger.Startup => UpdateDecision.Apply,
            UpdateTrigger.Exit => UpdateDecision.Apply,
            UpdateTrigger.Manual => inFlight ? UpdateDecision.ConfirmFirst : UpdateDecision.Apply,
            _ => UpdateDecision.Skip,
        };
    }
}
