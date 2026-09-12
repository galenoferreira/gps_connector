using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core;

public enum PairingAttempt
{
    Accepted,

    /// <summary>Código errado ou expirado (<c>pairing_invalid</c>).</summary>
    Invalid,

    /// <summary>Nenhum código ativo, ou tentativas esgotadas (<c>pairing_locked</c>).</summary>
    Locked,
}

/// <summary>
/// Código de pareamento de 6 dígitos (spec 01, "Pareamento"). Só existe quando o piloto
/// pede, um de cada vez, vale 2 minutos, é de uso único e morre em 5 tentativas erradas,
/// somando todas as conexões. Com isso, a chance de força bruta é de 5 em um milhão por
/// código gerado.
/// </summary>
public sealed class PairingCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxFailures = 5;

    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private string? _code;
    private DateTime _expiresUtc;
    private int _failures;

    public PairingCodes(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Código ativo e validade, para a UI mostrar a contagem regressiva.</summary>
    public (string Code, DateTime ExpiresUtc)? Active
    {
        get
        {
            lock (_lock)
                return _code is null || _utcNow() >= _expiresUtc ? null : (_code, _expiresUtc);
        }
    }

    public string Generate()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        lock (_lock)
        {
            _code = code;
            _expiresUtc = _utcNow() + Lifetime;
            _failures = 0;
        }
        return code;
    }

    public void Cancel()
    {
        lock (_lock)
            _code = null;
    }

    public PairingAttempt TryConsume(string attempt)
    {
        lock (_lock)
        {
            if (_code is null)
                return PairingAttempt.Locked;

            if (_utcNow() >= _expiresUtc)
            {
                _code = null;
                return PairingAttempt.Invalid;
            }

            if (Matches(_code, attempt))
            {
                _code = null;
                return PairingAttempt.Accepted;
            }

            if (++_failures >= MaxFailures)
                _code = null;
            return PairingAttempt.Invalid;
        }
    }

    /// <summary>Comparação em tempo constante: o tempo de resposta não revela dígitos certos.</summary>
    private static bool Matches(string expected, string attempt)
    {
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(attempt);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
