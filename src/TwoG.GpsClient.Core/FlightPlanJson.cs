using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Contrato JSON entregue ao 2G Pilot EFB. É a interface pública entre o conector e o
/// EFB: mudar nome ou tipo de campo quebra o outro lado, então trate como versionado.
/// </summary>
public static class FlightPlanJson
{
    /// <summary>Suba junto com qualquer mudança incompatível no formato.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(FlightPlan plan, string source, DateTime generatedUtc) =>
        JsonSerializer.Serialize(new FlightPlanDto
        {
            SchemaVersion = SchemaVersion,
            Source = source,
            GeneratedUtc = generatedUtc,
            Departure = plan.DepartureId,
            Destination = plan.DestinationId,
            CruiseAltitudeFt = plan.CruiseAltitudeFt,
            Waypoints = plan.Waypoints.Select(w => new WaypointDto
            {
                Id = w.Id,
                Type = w.Kind.ToString(),
                Lat = Math.Round(w.LatitudeDeg, 6),
                Lon = Math.Round(w.LongitudeDeg, 6),
                AltFt = w.AltitudeFt is null ? null : Math.Round(w.AltitudeFt.Value, 1),
            }).ToList(),
        }, Options);

    private sealed class FlightPlanDto
    {
        public int SchemaVersion { get; set; }
        public string Source { get; set; } = "";
        public DateTime GeneratedUtc { get; set; }
        public string? Departure { get; set; }
        public string? Destination { get; set; }
        public double? CruiseAltitudeFt { get; set; }
        public List<WaypointDto> Waypoints { get; set; } = [];
    }

    private sealed class WaypointDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double? AltFt { get; set; }
    }
}
