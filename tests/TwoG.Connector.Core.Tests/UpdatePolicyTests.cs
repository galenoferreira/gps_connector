namespace TwoG.Connector.Core.Tests;

public class UpdatePolicyTests
{
    [Theory]
    [InlineData(UpdateTrigger.Startup, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Startup, true, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Exit, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Exit, true, UpdateDecision.Apply)]        // o piloto mandou encerrar
    [InlineData(UpdateTrigger.Manual, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Manual, true, UpdateDecision.ConfirmFirst)]
    [InlineData(UpdateTrigger.Periodic, false, UpdateDecision.Skip)]    // nunca por timer
    [InlineData(UpdateTrigger.Periodic, true, UpdateDecision.Skip)]
    public void DecidesByTriggerAndFlightState(UpdateTrigger trigger, bool inFlight, UpdateDecision expected)
    {
        Assert.Equal(expected, UpdatePolicy.Decide(trigger, inFlight, attemptsSoFar: 0));
    }

    [Theory]
    [InlineData(UpdateTrigger.Startup)]
    [InlineData(UpdateTrigger.Exit)]
    [InlineData(UpdateTrigger.Manual)]
    public void GivesUpAfterMaxAttempts_ToAvoidALoopOnEveryStart(UpdateTrigger trigger)
    {
        Assert.Equal(UpdateDecision.Apply, UpdatePolicy.Decide(trigger, false, UpdatePolicy.MaxAttempts - 1));
        Assert.Equal(UpdateDecision.Skip, UpdatePolicy.Decide(trigger, false, UpdatePolicy.MaxAttempts));
    }
}
