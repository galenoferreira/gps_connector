namespace TwoG.Connector.Core.Tests;

public class PairingCodesTests
{
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private PairingCodes NewCodes() => new(() => _now);

    [Fact]
    public void GeneratesSixDigits()
    {
        var code = NewCodes().Generate();
        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void CodesAreSpreadOut()
    {
        var codes = NewCodes();
        var seen = Enumerable.Range(0, 200).Select(_ => codes.Generate()).ToHashSet();
        Assert.True(seen.Count > 190, $"só {seen.Count} códigos distintos em 200");
    }

    [Fact]
    public void CorrectCodeIsAcceptedOnce()
    {
        var codes = NewCodes();
        var code = codes.Generate();

        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(code));
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));    // uso único
    }

    [Fact]
    public void WithoutCodeEverythingIsLocked()
    {
        Assert.Equal(PairingAttempt.Locked, NewCodes().TryConsume("123456"));
    }

    [Fact]
    public void FourMistakesStillAllowTheRightCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
            Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(wrong));

        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(code));
    }

    [Fact]
    public void FiveMistakesInvalidateTheCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
            Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(wrong));

        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }

    [Fact]
    public void ExpiredCodeIsInvalidThenLocked()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        _now += PairingCodes.Lifetime;

        Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(code));
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }

    [Fact]
    public void NewCodeReplacesTheOldOne()
    {
        var codes = NewCodes();
        var first = codes.Generate();
        string second;
        do { second = codes.Generate(); } while (second == first);

        Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(first));
        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(second));
    }

    [Fact]
    public void ActiveShowsTheCodeUntilItExpires()
    {
        var codes = NewCodes();
        var code = codes.Generate();

        Assert.Equal(code, codes.Active!.Value.Code);
        Assert.Equal(_now + PairingCodes.Lifetime, codes.Active!.Value.ExpiresUtc);

        _now += PairingCodes.Lifetime;
        Assert.Null(codes.Active);
    }

    [Fact]
    public void CancelRemovesTheCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        codes.Cancel();

        Assert.Null(codes.Active);
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }
}
