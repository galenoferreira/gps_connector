using System.Globalization;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Lê o formato .fms v11 do X-Plane (texto simples, UTF-8).
///
/// Cabeçalho seguido de uma linha por waypoint:
///   &lt;tipo&gt; &lt;identificador&gt; &lt;via&gt; &lt;altitude&gt; &lt;latitude&gt; &lt;longitude&gt;
/// com latitude e longitude já em graus decimais. Tipos: 1 aeroporto, 2 NDB, 3 VOR,
/// 11 fixo nomeado, 28 ponto lat/lon sem nome.
/// </summary>
public static class FmsFlightPlanParser
{
    public static FlightPlan? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        string? departure = null;
        string? destination = null;
        var waypoints = new List<FlightPlanWaypoint>();
        var sawVersion = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (fields[0].ToUpperInvariant())
            {
                case "ADEP" or "DEP" when fields.Length >= 2:
                    departure = fields[1];
                    continue;
                case "ADES" or "DES" when fields.Length >= 2:
                    destination = fields[1];
                    continue;
                case "NUMENR" or "CYCLE" or "DEPRWY" or "DESRWY"
                     or "SID" or "SIDTRANS" or "STAR" or "STARTRANS" or "APP" or "APPTRANS":
                    continue;
            }

            if (fields[0] is "1100" or "3" && fields.Length >= 2
                && fields[1].Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                sawVersion = true;
                continue;
            }

            var waypoint = ParseWaypointLine(fields);
            if (waypoint is not null)
                waypoints.Add(waypoint);
        }

        // Sem cabeçalho de versão nem waypoints, provavelmente não é um .fms.
        if (!sawVersion && waypoints.Count == 0)
            return null;

        return new FlightPlan(departure, destination, CruiseAltitudeFt: null, waypoints);
    }

    private static FlightPlanWaypoint? ParseWaypointLine(string[] fields)
    {
        // tipo, identificador, via, altitude, latitude, longitude
        if (fields.Length < 6 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
            return null;

        if (!TryParseInvariant(fields[4], out var latitude) || !TryParseInvariant(fields[5], out var longitude))
            return null;
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return null;

        double? altitude = TryParseInvariant(fields[3], out var parsedAltitude) && parsedAltitude > 0
            ? parsedAltitude
            : null;

        return new FlightPlanWaypoint(
            Id: fields[1],
            Kind: type switch
            {
                1 => WaypointKind.Airport,
                2 => WaypointKind.Ndb,
                3 => WaypointKind.Vor,
                11 => WaypointKind.Intersection,
                28 => WaypointKind.User,
                _ => WaypointKind.Unknown,
            },
            LatitudeDeg: latitude,
            LongitudeDeg: longitude,
            AltitudeFt: altitude);
    }

    private static bool TryParseInvariant(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
