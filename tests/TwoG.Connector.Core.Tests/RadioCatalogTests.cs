namespace TwoG.Connector.Core.Tests;

public class RadioCatalogTests
{
    [Fact]
    public void CatalogHasTheSixteenControlsOfSpec02()
    {
        Assert.Equal(16, RadioCatalog.All.Count);
        Assert.Equal(RadioCatalog.All.Count, RadioCatalog.All.Distinct().Count());
    }

    [Theory]
    [InlineData("com1.swap", ControlKind.Action)]
    [InlineData("nav2.swap", ControlKind.Action)]
    [InlineData("com1.active", ControlKind.Set)]
    [InlineData("altimeter.baro", ControlKind.Set)]
    public void KnowsTheKindOfEachControl(string control, ControlKind kind)
    {
        Assert.Equal(kind, RadioCatalog.KindOf(control));
    }

    [Fact]
    public void UnknownControlHasNoKind()
    {
        Assert.Null(RadioCatalog.KindOf("ap.heading"));
    }

    [Theory]
    [InlineData(118_000_000L)]
    [InlineData(118_005_000L)]    // canal de 8,33
    [InlineData(118_010_000L)]
    [InlineData(118_015_000L)]
    [InlineData(118_025_000L)]
    [InlineData(121_900_000L)]
    [InlineData(136_990_000L)]    // 136990 % 25 == 15: canal de 8,33 válido
    public void AcceptsValidComChannels(long hz)
    {
        Assert.Null(RadioCatalog.Validate("com1.standby", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(117_995_000L)]    // abaixo da faixa
    [InlineData(137_000_000L)]    // acima da faixa
    [InlineData(118_020_000L)]    // .020 não existe na grade de 8,33
    [InlineData(118_045_000L)]
    [InlineData(118_070_000L)]
    [InlineData(118_095_000L)]
    [InlineData(118_001_000L)]    // não é canal
    [InlineData(118_000_500L)]    // não é múltiplo de kHz
    public void RejectsInvalidComChannels(long hz)
    {
        Assert.Equal(ControlErrors.OutOfRange, RadioCatalog.Validate("com2.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(108_000_000L, true)]
    [InlineData(117_950_000L, true)]
    [InlineData(110_300_000L, true)]
    [InlineData(108_025_000L, false)]
    [InlineData(107_950_000L, false)]
    [InlineData(118_000_000L, false)]
    public void ValidatesNav(long hz, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("nav1.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(190_000L, true)]
    [InlineData(350_000L, true)]
    [InlineData(1_799_500L, true)]
    [InlineData(189_500L, false)]
    [InlineData(1_800_000L, false)]
    [InlineData(350_250L, false)]
    public void ValidatesAdf(long hz, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("adf1.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1200L, true)]
    [InlineData(7700L, true)]
    [InlineData(7777L, true)]
    [InlineData(7800L, false)]
    [InlineData(7778L, false)]
    [InlineData(10_000L, false)]
    [InlineData(-1L, false)]
    public void ValidatesTransponderCode(long code, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("xpdr.code", ControlKind.Set, code));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(3L, true)]
    [InlineData(4L, true)]
    [InlineData(2L, false)]      // teste: fora de propósito
    [InlineData(5L, false)]
    public void ValidatesTransponderMode(long mode, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("xpdr.mode", ControlKind.Set, mode));
    }

    [Theory]
    [InlineData(94_800L, true)]
    [InlineData(101_325L, true)]
    [InlineData(105_000L, true)]
    [InlineData(94_799L, false)]
    [InlineData(105_001L, false)]
    public void ValidatesAltimeter(long pa, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("altimeter.baro", ControlKind.Set, pa));
    }

    [Fact]
    public void WrongKindOrUnknownControlIsUnsupported()
    {
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("com1.swap", ControlKind.Set, 1));
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("com1.active", ControlKind.Action, 0));
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("ap.heading", ControlKind.Set, 90));
    }

    [Fact]
    public void ActionsNeedNoValue()
    {
        Assert.Null(RadioCatalog.Validate("nav1.swap", ControlKind.Action, 0));
    }

    [Fact]
    public void ControlsForKeepsCatalogOrder()
    {
        var controls = RadioCatalog.ControlsFor([RadioGroup.Altimeter, RadioGroup.Com1]);

        Assert.Equal(["com1.active", "com1.standby", "com1.swap", "altimeter.baro"], controls);
    }

    [Fact]
    public void ControlsForSplitsTransponderCodeAndMode()
    {
        Assert.Equal(["xpdr.code"], RadioCatalog.ControlsFor([RadioGroup.Transponder]));
        Assert.Equal(["xpdr.mode"], RadioCatalog.ControlsFor([RadioGroup.TransponderMode]));
    }

    [Fact]
    public void AllGroupsGiveTheWholeCatalog()
    {
        Assert.Equal(RadioCatalog.All, RadioCatalog.ControlsFor(Enum.GetValues<RadioGroup>()));
        Assert.Empty(RadioCatalog.ControlsFor([]));
    }
}
