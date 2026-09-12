using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TwoG.Connector.Core;

/// <summary>
/// Versão SemVer 2.0 (maior.menor.correção[-pré-release][+build]), com a regra de
/// precedência da especificação. O "+build" é ignorado: o SDK do .NET acrescenta o
/// hash do commit à InformationalVersion, e ele não diz nada sobre ordem.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Identificadores de pré-release ("rc", "1"); vazio numa versão final.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    private AppVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versão inválida: {text}");

    public static bool TryParse(string? text, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;

        var parts = core.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
            return false;

        string[] preRelease = [];
        if (dash >= 0)
        {
            preRelease = s[(dash + 1)..].Split('.');
            if (!preRelease.All(IsValidIdentifier))
                return false;
        }

        version = new AppVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>Número sem sinal e sem zero à esquerda ("0" vale, "01" não).</summary>
    private static bool TryParseNumber(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && (s.Length == 1 || s[0] != '0');

    private static bool IsValidIdentifier(string id)
    {
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return false;
        // Identificador numérico com zero à esquerda é inválido na SemVer.
        return !(id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit));
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;

        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A versão final é maior que qualquer pré-release dela.
        if (!IsPreRelease)
            return other.IsPreRelease ? 1 : 0;
        if (!other.IsPreRelease)
            return -1;

        for (var i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aIsNumber = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
        var bIsNumber = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
        if (aIsNumber && bIsNumber) return an.CompareTo(bn);
        if (aIsNumber) return -1;   // numérico tem precedência menor que alfanumérico
        if (bIsNumber) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as AppVersion);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}"
            : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
}
