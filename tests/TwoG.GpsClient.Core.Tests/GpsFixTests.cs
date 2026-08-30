using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

/// <summary>
/// Um valor não-finito vindo do simulador não pode chegar ao fio: a sentença
/// continua "bem formada" (campos separados por vírgula), mas os números viram
/// "NaN"/"Infinity" e o EFB descarta o datagrama em silêncio — a aeronave congela
/// na última posição boa enquanto o conector segue contando pacotes enviados.
/// </summary>
public class GpsFixTests
{
    private static GpsFix Fix(
        double lat = 34.55678, double lon = -80.11234, double altM = 365.8,
        double trk = 231.245, double gsMps = 57.2, double hdg = 180.2,
        double pitchUp = 0.1, double rollRight = 0.2) =>
        new(DateTime.UtcNow, lat, lon, altM, trk, gsMps, hdg, pitchUp, rollRight, OnGround: false);

    [Fact]
    public void IsFinite_TrueForANormalSample()
    {
        Assert.True(Fix().IsFinite);
    }

    public static TheoryData<string, GpsFix> NonFiniteFixes() => new()
    {
        { "latitude", Fix(lat: double.NaN) },
        { "longitude", Fix(lon: double.NaN) },
        { "altitude", Fix(altM: double.NaN) },
        { "curso", Fix(trk: double.NaN) },
        { "velocidade", Fix(gsMps: double.NaN) },
        { "proa", Fix(hdg: double.NaN) },
        { "arfagem", Fix(pitchUp: double.NaN) },
        { "rolagem", Fix(rollRight: double.NaN) },
        { "altitude +inf", Fix(altM: double.PositiveInfinity) },
        { "velocidade -inf", Fix(gsMps: double.NegativeInfinity) },
    };

    [Theory]
    [MemberData(nameof(NonFiniteFixes))]
    public void IsFinite_FalseWhenAnyFieldIsNotFinite(string field, GpsFix fix)
    {
        Assert.False(fix.IsFinite, $"campo não-finito não detectado: {field}");
    }

    /// <summary>
    /// Prova o sintoma que motivou a guarda: sem ela, a sentença sai com "NaN"
    /// no lugar do número e o EFB não tem como interpretá-la.
    /// </summary>
    [Fact]
    public void FormatXgps_WithNaN_WouldEmitAnUnparseableSentence()
    {
        var s = XgpsSentences.FormatXgps("2G GPS", Fix(trk: double.NaN));
        Assert.Contains("NaN", s);
    }

    /// <summary>
    /// A guarda de faixa sozinha não protege: toda comparação com NaN é falsa,
    /// então NaN passa por `is &lt; -90 or &gt; 90` sem ser barrado.
    /// </summary>
    [Fact]
    public void RangeChecksAloneDoNotRejectNaN()
    {
        double nan = double.NaN;
        Assert.False(nan is < -90 or > 90);
    }
}
