namespace TwoG.Connector.Core;

public enum ControlKind
{
    /// <summary>Define um valor absoluto ("COM1 standby = 118.500").</summary>
    Set,

    /// <summary>Ação sem valor ("trocar COM1").</summary>
    Action,
}

/// <summary>Um comando vindo do app. <see cref="Value"/> é 0 nas ações.</summary>
public sealed record ControlCommand(string Id, string Control, ControlKind Kind, long Value);

/// <summary>
/// Resposta a um comando. <c>Ok</c> significa "entregue ao simulador", não "aplicado":
/// a confirmação de que o cockpit mudou é o estado (spec 01).
/// </summary>
public sealed record ControlResult(string Id, bool Ok, string? Error)
{
    public static ControlResult Success(string id) => new(id, true, null);

    public static ControlResult Fail(string id, string error) => new(id, false, error);
}

/// <summary>Códigos de erro do canal (spec 01, "Códigos de erro").</summary>
public static class ControlErrors
{
    public const string OutOfRange = "out_of_range";
    public const string Unsupported = "unsupported";
    public const string SimNotConnected = "sim_not_connected";
    public const string NotPaired = "not_paired";
    public const string RateLimited = "rate_limited";
    public const string SimUnresponsive = "sim_unresponsive";
    public const string NotApplied = "not_applied";
    public const string InvalidMessage = "invalid_message";
    public const string ProtocolUnsupported = "protocol_unsupported";
    public const string PairingInvalid = "pairing_invalid";
    public const string PairingLocked = "pairing_locked";
    public const string Busy = "busy";
}

public sealed record FrequencyPair(long Active, long Standby);

/// <summary><see cref="Mode"/> é null onde o simulador não modela o modo (Prepar3D).</summary>
public sealed record TransponderState(int Code, int? Mode);

/// <summary>
/// Retrato dos rádios reportado pelo simulador. Um rádio que a aeronave não tem fica null
/// e não aparece no <c>state</c>. Valores em Hz, Pa e código decimal (spec 02).
/// </summary>
public sealed record RadioState(
    FrequencyPair? Com1,
    FrequencyPair? Com2,
    FrequencyPair? Nav1,
    FrequencyPair? Nav2,
    long? Adf1Active,
    TransponderState? Xpdr,
    long? AltimeterPa)
{
    public static readonly RadioState Empty = new(null, null, null, null, null, null, null);
}

/// <summary>Grupos de rádio: cada um vira uma definição de dados no simulador (spec 02).</summary>
public enum RadioGroup
{
    Com1,
    Com2,
    Nav1,
    Nav2,
    Adf1,
    Transponder,
    TransponderMode,
    Altimeter,
}

/// <summary>
/// Capacidade de comandar o simulador. Fica no Core para que sessão e servidor sejam
/// testados sem simulador; a implementação real é do app (SimConnectRadios).
/// </summary>
public interface ISimControl
{
    /// <summary>Nome do simulador conectado, ou null.</summary>
    string? SimulatorName { get; }

    /// <summary>Controles que a aeronave atual aceita, na ordem do catálogo.</summary>
    IReadOnlyCollection<string> AvailableControls { get; }

    /// <summary>Último estado reportado, ou null sem simulador.</summary>
    RadioState? State { get; }

    /// <summary>Estado, controles disponíveis ou simulador mudaram. Pode vir de qualquer thread.</summary>
    event Action? Changed;

    /// <summary>
    /// Enfileira um comando JÁ VALIDADO pelo catálogo. Não bloqueia: a execução é na
    /// thread do simulador. Devolve <c>sim_not_connected</c> ou <c>sim_unresponsive</c>
    /// quando não dá para enfileirar.
    /// </summary>
    ControlResult Submit(ControlCommand command);
}
