using System.Buffers.Binary;
using System.Text;

namespace TwoG.GpsClient.Core;

/// <param name="Port">Porta em que o X-Plane recebe comandos (padrão 49000).</param>
/// <param name="VersionNumber">Ex.: 121400 para o X-Plane 12.14.</param>
/// <param name="Role">1 = máquina mestre (a que simula). Só ela nos interessa.</param>
public sealed record XPlaneBeacon(int Port, int VersionNumber, uint Role, string Hostname)
{
    public bool IsMaster => Role == 1;

    /// <summary>Ex.: "X-Plane 12".</summary>
    public string DisplayName
    {
        get
        {
            var major = VersionNumber / 10000;
            return major is >= 9 and <= 99 ? $"X-Plane {major}" : "X-Plane";
        }
    }
}

/// <summary>
/// Protocolo UDP do X-Plane: descoberta por beacon multicast (BECN) e assinatura de
/// datarefs (RREF). Não exige plugin nem DLL — só sockets.
///
/// Tudo aqui é little-endian e sem estado, para ser testável sem o simulador.
/// </summary>
public static class XPlaneProtocol
{
    /// <summary>Grupo multicast em que o X-Plane anuncia sua presença.</summary>
    public const string BeaconMulticastGroup = "239.255.1.1";
    public const int BeaconPort = 49707;

    /// <summary>Porta padrão de comandos, usada se o beacon não informar outra.</summary>
    public const int DefaultCommandPort = 49000;

    /// <summary>Tamanho fixo do pacote RREF de assinatura.</summary>
    public const int RrefRequestSize = 413;

    private const int DatarefFieldSize = 400;

    private static ReadOnlySpan<byte> BeaconHeader => "BECN\0"u8;
    private static ReadOnlySpan<byte> RrefRequestHeader => "RREF\0"u8;
    private static ReadOnlySpan<byte> RrefResponseHeader => "RREF,"u8;

    /// <summary>
    /// Monta o pacote de assinatura de um dataref. Frequência 0 cancela a assinatura.
    /// Layout: "RREF\0" + frequência (int32) + índice (int32) + caminho (400 bytes).
    /// </summary>
    public static byte[] BuildRrefRequest(int frequencyHz, int index, string dataref)
    {
        ArgumentNullException.ThrowIfNull(dataref);
        if (frequencyHz < 0)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));

        var pathBytes = Encoding.ASCII.GetBytes(dataref);
        if (pathBytes.Length >= DatarefFieldSize)
            throw new ArgumentException($"Dataref excede {DatarefFieldSize - 1} bytes.", nameof(dataref));

        var packet = new byte[RrefRequestSize];
        RrefRequestHeader.CopyTo(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5, 4), frequencyHz);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(9, 4), index);
        pathBytes.CopyTo(packet.AsSpan(13));
        // O resto já é zero: o caminho fica terminado em \0 e preenchido.
        return packet;
    }

    /// <summary>
    /// Lê um pacote de valores do X-Plane: cabeçalho "RREF," seguido de pares
    /// (índice int32, valor float32) de 8 bytes. Índices são os que enviamos na
    /// assinatura. Retorna quantos pares foram lidos.
    /// </summary>
    public static int ParseRrefResponse(ReadOnlySpan<byte> packet, Span<(int Index, float Value)> destination)
    {
        if (packet.Length < RrefResponseHeader.Length
            || !packet[..RrefResponseHeader.Length].SequenceEqual(RrefResponseHeader))
            return 0;

        var body = packet[RrefResponseHeader.Length..];
        var count = Math.Min(body.Length / 8, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var entry = body.Slice(i * 8, 8);
            destination[i] = (
                BinaryPrimitives.ReadInt32LittleEndian(entry[..4]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[4..]));
        }
        return count;
    }

    /// <summary>
    /// Lê o beacon BECN. Layout após o cabeçalho de 5 bytes:
    /// versão maior (byte), versão menor (byte), id da aplicação (int32),
    /// versão do X-Plane (int32), papel (uint32), porta (uint16), hostname (ASCIIZ).
    /// </summary>
    public static bool TryParseBeacon(ReadOnlySpan<byte> packet, out XPlaneBeacon? beacon)
    {
        beacon = null;
        const int headerSize = 5;
        const int fixedSize = 16;

        if (packet.Length < headerSize + fixedSize
            || !packet[..headerSize].SequenceEqual(BeaconHeader))
            return false;

        var body = packet[headerSize..];
        var majorVersion = body[0];
        var applicationHostId = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(2, 4));

        // 1 = X-Plane. Versões futuras do beacon podem crescer, mas a maior tem de bater.
        if (majorVersion != 1 || applicationHostId != 1)
            return false;

        var versionNumber = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(6, 4));
        var role = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(10, 4));
        var port = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(14, 2));

        var hostBytes = body[fixedSize..];
        var terminator = hostBytes.IndexOf((byte)0);
        var hostname = Encoding.ASCII.GetString(terminator >= 0 ? hostBytes[..terminator] : hostBytes).Trim();

        beacon = new XPlaneBeacon(port == 0 ? DefaultCommandPort : port, versionNumber, role, hostname);
        return true;
    }
}
