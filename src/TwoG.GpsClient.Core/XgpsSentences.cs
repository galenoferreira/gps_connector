using System.Globalization;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Formatação das sentenças do protocolo "X-Plane broadcast" (XGPS/XATT), como
/// consumido por ForeFlight, Garmin Pilot, SkyDemon, 2G Pilot e outros EFBs.
///
/// Regras (spec ForeFlight + implementação de referência fs2ff):
///  - uma sentença por datagrama UDP, ASCII, sem terminador de linha;
///  - decimal SEMPRE com ponto (InvariantCulture);
///  - XGPS: longitude ANTES da latitude; altitude em METROS MSL; velocidade em m/s;
///    curso em graus verdadeiros;
///  - XATT: proa VERDADEIRA, pitch positivo = nariz p/ cima, roll positivo = asa
///    direita p/ baixo; 9 campos vazios ao final (Garmin Pilot exige 13 campos,
///    ForeFlight ignora os extras).
/// </summary>
public static class XgpsSentences
{
    public static string FormatXgps(string deviceName, GpsFix fix) =>
        string.Create(CultureInfo.InvariantCulture,
            $"XGPS{deviceName},{fix.LongitudeDeg:0.#####},{fix.LatitudeDeg:0.#####},{fix.AltitudeMslMeters:0.#},{Normalize360(fix.TrackTrueDeg):0.###},{fix.GroundSpeedMps:0.#}");

    public static string FormatXatt(string deviceName, GpsFix fix) =>
        string.Create(CultureInfo.InvariantCulture,
            $"XATT{deviceName},{Normalize360(fix.HeadingTrueDeg):0.#},{fix.PitchDegUp:0.#},{fix.RollDegRight:0.#},,,,,,,,,");

    /// <summary>Normaliza um ângulo para o intervalo [0, 360).</summary>
    public static double Normalize360(double deg)
    {
        var d = deg % 360.0;
        return d < 0 ? d + 360.0 : d;
    }
}
