using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// ISimControl que vive o app inteiro e repassa para a fonte ativa. O servidor do canal
/// guarda UMA referência, mas a fonte ativa muda (MSFS fecha, X-Plane abre): este objeto
/// absorve a troca e retransmite as mudanças de todas as fontes.
/// </summary>
internal sealed class CompositeSimControl : ISimControl
{
    private readonly Func<ISimSource?> _active;

    public CompositeSimControl(Func<ISimSource?> active, IEnumerable<ISimSource> sources)
    {
        _active = active;
        foreach (var source in sources)
        {
            if (source.Control is { } control)
                control.Changed += () => Changed?.Invoke();
        }
    }

    /// <summary>
    /// Nome da fonte ativa, tenha ela Control ou não: o X-Plane conectado aparece pelo nome,
    /// com controls [] e radios {}. O null fica para "sem simulador conectado" (spec 01).
    /// </summary>
    public string? SimulatorName => _active()?.SimulatorName;

    public IReadOnlyCollection<string> AvailableControls => _active()?.Control?.AvailableControls ?? [];

    public RadioState? State => _active()?.Control?.State;

    public event Action? Changed;

    /// <summary>
    /// A sessão já barra como unsupported o que está fora de AvailableControls; aqui só chega
    /// quem a fonte ativa trocou entre a checagem e o envio. Fonte ativa sem Control (X-Plane)
    /// é unsupported, como a sessão diria; sim_not_connected só quando não há fonte ativa.
    /// </summary>
    public ControlResult Submit(ControlCommand command) =>
        _active() switch
        {
            null => ControlResult.Fail(command.Id, ControlErrors.SimNotConnected),
            { Control: { } control } => control.Submit(command),
            _ => ControlResult.Fail(command.Id, ControlErrors.Unsupported),
        };
}
