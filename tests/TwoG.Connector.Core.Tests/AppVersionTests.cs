namespace TwoG.Connector.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("v1.4.0", "1.4.0")]
    [InlineData("1.4.0+a1b2c3d", "1.4.0")]          // InformationalVersion do SDK
    [InlineData("1.4.1-rc.1", "1.4.1-rc.1")]
    [InlineData("1.0.0-ci.42", "1.0.0-ci.42")]      // builds de branch do CI
    [InlineData(" 2.0.0 ", "2.0.0")]
    public void ParsesAndNormalizes(string text, string expected)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(expected, v!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.4")]
    [InlineData("1.4.0.0")]
    [InlineData("01.4.0")]
    [InlineData("1.4.0-")]
    [InlineData("1.4.0-rc..1")]
    [InlineData("1.4.0-01")]
    [InlineData("1.4.0-rc_1")]
    [InlineData("abc")]
    [InlineData("-1.4.0")]
    public void RejectsInvalid(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    /// <summary>Ordem do exemplo da especificação SemVer 2.0, mais casos nossos.</summary>
    [Fact]
    public void FollowsSemVerPrecedence()
    {
        string[] ascending =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0",
            "1.4.0", "1.4.1-rc.1", "1.4.1", "1.10.0", "2.0.0",
        ];

        for (var i = 0; i < ascending.Length - 1; i++)
        {
            var lower = AppVersion.Parse(ascending[i]);
            var higher = AppVersion.Parse(ascending[i + 1]);
            Assert.True(lower < higher, $"{lower} deveria ser menor que {higher}");
            Assert.True(higher > lower, $"{higher} deveria ser maior que {lower}");
        }
    }

    [Fact]
    public void BuildMetadataDoesNotAffectEquality()
    {
        var a = AppVersion.Parse("1.4.0+aaa");
        var b = AppVersion.Parse("1.4.0+bbb");
        Assert.Equal(a, b);
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ParseThrowsOnInvalid()
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse("não é versão"));
    }
}
