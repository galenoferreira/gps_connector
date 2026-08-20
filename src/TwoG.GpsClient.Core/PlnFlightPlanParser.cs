using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Lê o formato .PLN (XML) usado pelo MSFS e pelo Prepar3D — herdado do FSX, idêntico
/// nos dois. É o único caminho prático para obter a rota completa: o SimConnect expõe
/// apenas o waypoint atual, o anterior e o próximo.
/// </summary>
public static class PlnFlightPlanParser
{
    /// <summary>
    /// Coordenada no formato do SDK: N52° 22' 42.75",E13° 31' 14.27",+000000.00
    /// Graus, minutos e segundos com hemisfério na frente, altitude em pés no fim.
    /// O separador decimal é sempre ponto, e o símbolo de grau às vezes vem ausente
    /// ou trocado dependendo de quem gerou o arquivo — daí o casamento tolerante.
    /// </summary>
    private static readonly Regex CoordinatePattern = new(
        @"^\s*(?<hemisphere>[NSEW])\s*(?<degrees>\d+(?:\.\d+)?)[^\d]+(?<minutes>\d+(?:\.\d+)?)[^\d]+(?<seconds>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FlightPlan? Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return null;
        }

        var plan = document.Root?.Element("FlightPlan.FlightPlan");
        if (plan is null)
            return null;

        var waypoints = new List<FlightPlanWaypoint>();
        foreach (var element in plan.Elements("ATCWaypoint"))
        {
            var waypoint = ParseWaypoint(element);
            if (waypoint is not null)
                waypoints.Add(waypoint);
        }

        return new FlightPlan(
            DepartureId: Trimmed(plan.Element("DepartureID")?.Value),
            DestinationId: Trimmed(plan.Element("DestinationID")?.Value),
            CruiseAltitudeFt: ParseDouble(plan.Element("CruisingAlt")?.Value),
            Waypoints: waypoints);
    }

    private static FlightPlanWaypoint? ParseWaypoint(XElement element)
    {
        var position = element.Element("WorldPosition")?.Value;
        if (position is null || !TryParseWorldPosition(position, out var latitude, out var longitude, out var altitude))
            return null;

        // O id do atributo é o que o simulador mostra; o ICAOIdent é o fallback.
        var id = Trimmed((string?)element.Attribute("id"))
                 ?? Trimmed(element.Element("ICAO")?.Element("ICAOIdent")?.Value)
                 ?? "";

        return new FlightPlanWaypoint(
            Id: id,
            Kind: ParseKind(element.Element("ATCWaypointType")?.Value),
            LatitudeDeg: latitude,
            LongitudeDeg: longitude,
            AltitudeFt: altitude);
    }

    /// <summary>
    /// Converte "N52° 22' 42.75",E13° 31' 14.27",+000000.00" em graus decimais.
    /// A altitude é o terceiro campo, em pés, e pode não existir.
    /// </summary>
    public static bool TryParseWorldPosition(string value, out double latitude, out double longitude, out double? altitude)
    {
        latitude = longitude = 0;
        altitude = null;

        var parts = value.Split(',');
        if (parts.Length < 2)
            return false;

        if (!TryParseCoordinate(parts[0], out latitude) || !TryParseCoordinate(parts[1], out longitude))
            return false;

        if (parts.Length >= 3)
            altitude = ParseDouble(parts[2]);
        return true;
    }

    private static bool TryParseCoordinate(string raw, out double degreesDecimal)
    {
        degreesDecimal = 0;
        var match = CoordinatePattern.Match(raw);
        if (!match.Success)
            return false;

        var degrees = double.Parse(match.Groups["degrees"].Value, CultureInfo.InvariantCulture);
        var minutes = double.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);

        degreesDecimal = degrees + minutes / 60.0 + seconds / 3600.0;
        if (match.Groups["hemisphere"].Value is "S" or "W")
            degreesDecimal = -degreesDecimal;
        return true;
    }

    private static WaypointKind ParseKind(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "airport" => WaypointKind.Airport,
        "ndb" => WaypointKind.Ndb,
        "vor" => WaypointKind.Vor,
        "intersection" => WaypointKind.Intersection,
        "user" => WaypointKind.User,
        _ => WaypointKind.Unknown,
    };

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
