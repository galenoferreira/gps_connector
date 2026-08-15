namespace TwoG.GpsClient.Core;

/// <summary>
/// Monta um <see cref="GpsFix"/> a partir dos valores avulsos que o X-Plane envia
/// via RREF — cada pacote traz pares (índice, valor), possivelmente parciais.
///
/// Unidades: os datarefs do X-Plane já vêm exatamente no que o XGPS precisa
/// (metros MSL, m/s, graus), então não há conversão. E, ao contrário do MSFS,
/// **não há inversão de sinais**: theta positivo já é nariz para cima e phi
/// positivo já é asa direita para baixo — a mesma convenção do XATT, que foi
/// definido pelo próprio X-Plane a partir destes datarefs.
///
/// Precisão: o RREF transporta float32 mesmo para datarefs declarados double, o
/// que limita a posição a ~1,5 m. É da ordem do erro de um GPS real e aceitável
/// para EFB; melhorar exigiria um plugin XPLM.
/// </summary>
public sealed class XPlaneFixAssembler
{
    /// <summary>
    /// Datarefs assinados. A posição no array é o índice enviado no RREF e
    /// devolvido pelo X-Plane, então a ordem define o protocolo — não reordene.
    /// </summary>
    public static readonly string[] Datarefs =
    [
        "sim/flightmodel/position/latitude",      // 0 — graus
        "sim/flightmodel/position/longitude",     // 1 — graus
        "sim/flightmodel/position/elevation",     // 2 — metros MSL
        "sim/flightmodel/position/groundspeed",   // 3 — m/s
        "sim/flightmodel/position/hpath",         // 4 — curso sobre o solo, graus verdadeiros
        "sim/flightmodel/position/psi",           // 5 — proa verdadeira, graus
        "sim/flightmodel/position/theta",         // 6 — arfagem, graus (+ nariz p/ cima)
        "sim/flightmodel/position/phi",           // 7 — rolagem, graus (+ asa direita)
        "sim/flightmodel/failures/onground_any",  // 8 — em solo (0/1)
        "sim/time/paused",                        // 9 — pausado (0/1)
    ];

    private const int Latitude = 0;
    private const int Longitude = 1;
    private const int Elevation = 2;
    private const int GroundSpeed = 3;
    private const int Track = 4;
    private const int Heading = 5;
    private const int Pitch = 6;
    private const int Roll = 7;
    private const int OnGround = 8;
    private const int Paused = 9;

    /// <summary>Sem estes, não há sentença XGPS/XATT válida para enviar.</summary>
    private static readonly int[] Required =
        [Latitude, Longitude, Elevation, GroundSpeed, Track, Heading, Pitch, Roll];

    private readonly float?[] _values = new float?[Datarefs.Length];

    public void Set(int index, float value)
    {
        if ((uint)index < (uint)_values.Length)
            _values[index] = value;
    }

    public void Reset() => Array.Clear(_values);

    public bool IsPaused => _values[Paused] is > 0.5f;

    /// <summary>
    /// Devolve um fix quando todos os campos obrigatórios já chegaram e o
    /// simulador não está pausado; caso contrário, null.
    /// </summary>
    public GpsFix? TryBuild(DateTime utc)
    {
        if (IsPaused)
            return null;

        foreach (var field in Required)
        {
            if (_values[field] is null)
                return null;
        }

        var latitude = _values[Latitude]!.Value;
        var longitude = _values[Longitude]!.Value;

        // O X-Plane reporta 0,0 enquanto carrega um voo.
        if (latitude is 0 && longitude is 0)
            return null;
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return null;

        return new GpsFix(
            Utc: utc,
            LatitudeDeg: latitude,
            LongitudeDeg: longitude,
            AltitudeMslMeters: _values[Elevation]!.Value,
            TrackTrueDeg: _values[Track]!.Value,
            GroundSpeedMps: _values[GroundSpeed]!.Value,
            HeadingTrueDeg: _values[Heading]!.Value,
            PitchDegUp: _values[Pitch]!.Value,
            RollDegRight: _values[Roll]!.Value,
            OnGround: _values[OnGround] is > 0.5f);
    }
}
