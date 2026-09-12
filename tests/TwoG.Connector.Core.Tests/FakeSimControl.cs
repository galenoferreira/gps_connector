namespace TwoG.Connector.Core.Tests;

/// <summary>Simulador falso para sessão e servidor: registra comandos e deixa disparar mudanças.</summary>
internal sealed class FakeSimControl : ISimControl
{
    public string? SimulatorName { get; set; } = "Microsoft Flight Simulator 2024";

    public IReadOnlyCollection<string> AvailableControls { get; set; } = RadioCatalog.All;

    public RadioState? State { get; set; } = new(
        new FrequencyPair(121_900_000, 118_500_000), null, null, null,
        null, new TransponderState(2000, 4), 101_325);

    /// <summary>Quando definido, todo Submit falha com este código.</summary>
    public string? FailWith { get; set; }

    public List<ControlCommand> Submitted { get; } = [];

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command)
    {
        lock (Submitted)
            Submitted.Add(command);
        return FailWith is null ? ControlResult.Success(command.Id) : ControlResult.Fail(command.Id, FailWith);
    }

    public void RaiseChanged() => Changed?.Invoke();
}
