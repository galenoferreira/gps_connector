namespace TwoG.Connector.Core;

/// <summary>
/// Catálogo de rádios da primeira entrega (spec 02): nomes, tipos, faixas e espaçamento.
/// Todo comando passa por aqui ANTES de chegar ao simulador.
/// </summary>
public static class RadioCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "com1.active", "com1.standby", "com1.swap",
        "com2.active", "com2.standby", "com2.swap",
        "nav1.active", "nav1.standby", "nav1.swap",
        "nav2.active", "nav2.standby", "nav2.swap",
        "adf1.active",
        "xpdr.code", "xpdr.mode",
        "altimeter.baro",
    ];

    private static readonly Dictionary<RadioGroup, string[]> ByGroup = new()
    {
        [RadioGroup.Com1] = ["com1.active", "com1.standby", "com1.swap"],
        [RadioGroup.Com2] = ["com2.active", "com2.standby", "com2.swap"],
        [RadioGroup.Nav1] = ["nav1.active", "nav1.standby", "nav1.swap"],
        [RadioGroup.Nav2] = ["nav2.active", "nav2.standby", "nav2.swap"],
        [RadioGroup.Adf1] = ["adf1.active"],
        [RadioGroup.Transponder] = ["xpdr.code"],
        [RadioGroup.TransponderMode] = ["xpdr.mode"],
        [RadioGroup.Altimeter] = ["altimeter.baro"],
    };

    public static ControlKind? KindOf(string control) =>
        !All.Contains(control) ? null
        : control.EndsWith(".swap", StringComparison.Ordinal) ? ControlKind.Action
        : ControlKind.Set;

    /// <summary>Null quando o comando é válido; senão o código de erro do canal.</summary>
    public static string? Validate(string control, ControlKind kind, long value)
    {
        if (KindOf(control) != kind)
            return ControlErrors.Unsupported;
        if (kind == ControlKind.Action)
            return null;

        var valid = control switch
        {
            "com1.active" or "com1.standby" or "com2.active" or "com2.standby" => IsComChannel(value),
            "nav1.active" or "nav1.standby" or "nav2.active" or "nav2.standby" =>
                value is >= 108_000_000 and <= 117_950_000 && value % 50_000 == 0,
            "adf1.active" => value is >= 190_000 and <= 1_799_500 && value % 500 == 0,
            "xpdr.code" => IsTransponderCode(value),
            "xpdr.mode" => value is 0 or 1 or 3 or 4,
            "altimeter.baro" => value is >= 94_800 and <= 105_000,
            _ => false,
        };
        return valid ? null : ControlErrors.OutOfRange;
    }

    /// <summary>Controles dos grupos disponíveis, na ordem do catálogo.</summary>
    public static IReadOnlyList<string> ControlsFor(IEnumerable<RadioGroup> groups)
    {
        var wanted = groups.SelectMany(g => ByGroup[g]).ToHashSet(StringComparer.Ordinal);
        return All.Where(wanted.Contains).ToArray();
    }

    /// <summary>
    /// Canal de COM de 25 ou 8,33 kHz: kHz inteiro, e resto 0, 5, 10 ou 15 na divisão por
    /// 25 — exatamente os canais que existem (.020, .045, .070 e .095 não existem).
    /// </summary>
    private static bool IsComChannel(long hz)
    {
        if (hz is < 118_000_000 or > 136_990_000 || hz % 1000 != 0)
            return false;
        return (hz / 1000 % 25) is 0 or 5 or 10 or 15;
    }

    /// <summary>0 a 7777, com cada dígito decimal de 0 a 7 (é octal na prática).</summary>
    private static bool IsTransponderCode(long code)
    {
        if (code is < 0 or > 7777)
            return false;
        for (var rest = code; rest > 0; rest /= 10)
        {
            if (rest % 10 > 7)
                return false;
        }
        return true;
    }
}
