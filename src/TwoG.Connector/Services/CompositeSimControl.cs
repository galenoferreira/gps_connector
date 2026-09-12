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

    public string? SimulatorName => _active()?.Control?.SimulatorName;

    public IReadOnlyCollection<string> AvailableControls => _active()?.Control?.AvailableControls ?? [];

    public RadioState? State => _active()?.Control?.State;

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command) =>
        _active()?.Control is { } control
            ? control.Submit(command)
            : ControlResult.Fail(command.Id, ControlErrors.SimNotConnected);
}
