namespace TwoG.Connector.Core;

/// <summary>
/// Uma amostra imutável de posição/atitude vinda do simulador.
/// Unidades já normalizadas para o protocolo XGPS:
/// altitude em metros MSL, velocidade em m/s, ângulos em graus,
/// pitch positivo = nariz para cima, roll positivo = asa direita para baixo.
/// </summary>
public sealed record GpsFix(
    DateTime Utc,
    double LatitudeDeg,
    double LongitudeDeg,
    double AltitudeMslMeters,
    double TrackTrueDeg,
    double GroundSpeedMps,
    double HeadingTrueDeg,
    double PitchDegUp,
    double RollDegRight,
    bool OnGround)
{
    /// <summary>
    /// Todos os campos numéricos são finitos, ou seja, a amostra pode virar sentença.
    ///
    /// Um NaN ou Infinity vindo do simulador não é barrado pelas guardas de faixa
    /// (toda comparação com NaN é falsa) e chega ao fio como o texto "NaN" ou
    /// "Infinity" no lugar do número: o datagrama continua com o formato certo, mas
    /// o EFB não consegue interpretá-lo e o descarta em silêncio — a aeronave
    /// congela na última posição válida enquanto o conector segue contando envios.
    /// </summary>
    public bool IsFinite =>
        double.IsFinite(LatitudeDeg)
        && double.IsFinite(LongitudeDeg)
        && double.IsFinite(AltitudeMslMeters)
        && double.IsFinite(TrackTrueDeg)
        && double.IsFinite(GroundSpeedMps)
        && double.IsFinite(HeadingTrueDeg)
        && double.IsFinite(PitchDegUp)
        && double.IsFinite(RollDegRight);
}
