namespace TwoG.Connector.Core.Tests;

/// <summary>Os exemplos do spec 02 viram os casos de teste.</summary>
public class RadioConversionsTests
{
    [Theory]
    [InlineData(7700L, 0x7700u)]
    [InlineData(1200L, 0x1200u)]
    [InlineData(7777L, 0x7777u)]
    [InlineData(0L, 0x0000u)]
    [InlineData(21L, 0x0021u)]
    public void CodeToBcd16PutsEachDigitInANibble(long code, uint bcd)
    {
        Assert.Equal(bcd, RadioConversions.CodeToBcd16(code));
    }

    [Theory]
    [InlineData(0x7700u, 7700)]
    [InlineData(0x1200u, 1200)]
    [InlineData(0x0000u, 0)]
    [InlineData(0x0021u, 21)]
    public void Bco16ReadsBackAsDecimalCode(uint bco, int code)
    {
        Assert.Equal(code, RadioConversions.Bco16ToCode(bco));
    }

    [Theory]
    [InlineData(350_000L, 0x00350000u)]
    [InlineData(1_799_500L, 0x01799500u)]
    [InlineData(190_000L, 0x00190000u)]
    public void HzToBcd32EncodesTheHertzDigits(long hz, uint bcd)
    {
        Assert.Equal(bcd, RadioConversions.HzToBcd32(hz));
    }

    [Theory]
    [InlineData(101_325, 16212u)]
    [InlineData(94_800, 15168u)]
    [InlineData(105_000, 16800u)]
    public void PascalsBecomeMillibarsTimes16(long pa, uint mb16)
    {
        Assert.Equal(mb16, RadioConversions.PaToMillibars16(pa));
    }

    [Theory]
    [InlineData(1013.25, 101_325L)]
    [InlineData(1013.2, 101_320L)]
    [InlineData(948.0, 94_800L)]
    public void MillibarsBecomePascals(double mb, long pa)
    {
        Assert.Equal(pa, RadioConversions.MillibarsToPa(mb));
    }

    [Theory]
    [InlineData(118_499_999.6, 118_500_000L)]
    [InlineData(121_900_000.0, 121_900_000L)]
    [InlineData(350_000.4, 350_000L)]
    public void RoundHzRemovesFloatingNoise(double hz, long rounded)
    {
        Assert.Equal(rounded, RadioConversions.RoundHz(hz));
    }
}
