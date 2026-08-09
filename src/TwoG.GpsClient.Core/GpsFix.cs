namespace TwoG.GpsClient.Core;

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
    bool OnGround);
