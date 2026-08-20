namespace TwoG.GpsClient.Core;

public enum WaypointKind
{
    Unknown,
    Airport,
    Ndb,
    Vor,
    Intersection,
    User,
}

/// <param name="Id">Identificador exibido (ICAO do aeroporto, nome do fixo, etc.).</param>
/// <param name="AltitudeFt">Altitude planejada em pés, ou null quando o plano não define.</param>
public sealed record FlightPlanWaypoint(
    string Id,
    WaypointKind Kind,
    double LatitudeDeg,
    double LongitudeDeg,
    double? AltitudeFt);

/// <summary>
/// Plano de voo já normalizado, independente do simulador de origem. É o que o
/// conector entrega ao EFB.
/// </summary>
public sealed record FlightPlan(
    string? DepartureId,
    string? DestinationId,
    double? CruiseAltitudeFt,
    IReadOnlyList<FlightPlanWaypoint> Waypoints)
{
    /// <summary>Um plano só é útil com pelo menos origem e destino.</summary>
    public bool IsUsable => Waypoints.Count >= 2;

    /// <summary>Ex.: "SBSP → SBRJ, 7 waypoints".</summary>
    public string Summary
    {
        get
        {
            var from = DepartureId ?? Waypoints.FirstOrDefault()?.Id ?? "?";
            var to = DestinationId ?? Waypoints.LastOrDefault()?.Id ?? "?";
            return $"{from} → {to}, {Waypoints.Count} waypoints";
        }
    }
}
