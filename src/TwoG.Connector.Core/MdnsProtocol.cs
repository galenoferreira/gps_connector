using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace TwoG.Connector.Core;

/// <summary>O que uma resposta mDNS nos disse sobre o serviço procurado.</summary>
/// <param name="MentionsService">A resposta fala do serviço perguntado.</param>
/// <param name="InstanceName">Nome da instância ("2G Pilot"), quando veio um PTR.</param>
/// <param name="Port">Porta do SRV, quando veio.</param>
/// <param name="Addresses">Endereços IPv4 dos registros A.</param>
public sealed record MdnsResponse(
    bool MentionsService,
    string? InstanceName,
    int? Port,
    IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// O mínimo de DNS-SD sobre multicast (RFC 6762/6763) para achar um serviço na
/// rede local: montar uma pergunta PTR e ler a resposta.
///
/// Escrito à mão, sem biblioteca, pelo mesmo motivo do <see cref="XPlaneProtocol"/>:
/// é parsing binário determinístico, cabe em pouca coisa e fica testável byte a
/// byte no Core. As opções de mercado estão paradas há anos e arrastariam
/// dependências (inclusive de Linux) para dentro de um .exe único.
///
/// A pergunta é "one-shot" (RFC 6762 §5.1): sai de uma porta efêmera com o bit
/// QU ligado, pedindo resposta unicast. Isso evita disputar a porta 5353 com o
/// responder mDNS que o próprio Windows 10/11 já mantém — e traz um bônus: o IP
/// de origem da resposta já é o do dispositivo, sem depender de registro A.
/// </summary>
public static class MdnsProtocol
{
    public const string MulticastGroup = "224.0.0.251";
    public const int MulticastPort = 5353;

    /// <summary>Serviço que o app 2G Pilot publica enquanto está escutando.</summary>
    public const string PilotServiceName = "_2gpilot._udp.local";

    private const ushort TypePtr = 12;
    private const ushort TypeA = 1;
    private const ushort TypeSrv = 33;

    /// <summary>Classe IN com o bit QU (unicast response) ligado.</summary>
    private const ushort ClassInUnicastResponse = 0x8001;

    private const int HeaderBytes = 12;

    /// <summary>
    /// Monta a pergunta "quem oferece este serviço?". O ID vai zerado, como manda
    /// o mDNS — a correspondência é pelo nome, não pelo identificador.
    /// </summary>
    public static byte[] BuildPtrQuery(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var body = new List<byte>(64);

        // Cabeçalho: ID 0, flags 0 (query padrão), 1 pergunta, nenhum registro.
        body.AddRange([0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0]);

        WriteName(body, serviceName);
        AppendUInt16(body, TypePtr);
        AppendUInt16(body, ClassInUnicastResponse);

        return [.. body];
    }

    /// <summary>
    /// Lê uma resposta e extrai o que interessa sobre <paramref name="serviceName"/>.
    /// Devolve false para datagrama malformado ou que não seja resposta — a porta
    /// recebe tráfego multicast de todo tipo e o silêncio é o certo.
    /// </summary>
    public static bool TryParseResponse(ReadOnlySpan<byte> message, string serviceName, out MdnsResponse response)
    {
        response = null!;
        if (message.Length < HeaderBytes)
            return false;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        if ((flags & 0x8000) == 0)
            return false;   // é pergunta, não resposta

        var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        var records = BinaryPrimitives.ReadUInt16BigEndian(message[6..])
                    + BinaryPrimitives.ReadUInt16BigEndian(message[8..])
                    + BinaryPrimitives.ReadUInt16BigEndian(message[10..]);

        var offset = HeaderBytes;

        for (var i = 0; i < questions; i++)
        {
            if (!TrySkipName(message, ref offset) || !Advance(message, ref offset, 4))
                return false;
        }

        var wanted = serviceName.TrimEnd('.');
        var mentions = false;
        string? instance = null;
        int? port = null;
        var addresses = new List<IPAddress>();

        for (var i = 0; i < records; i++)
        {
            if (!TryReadName(message, ref offset, out var owner))
                return false;
            if (offset + 10 > message.Length)
                return false;

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
            offset += 10;

            if (offset + dataLength > message.Length)
                return false;

            var dataStart = offset;

            if (NameEquals(owner, wanted))
                mentions = true;

            switch (type)
            {
                case TypePtr when NameEquals(owner, wanted):
                {
                    var pointer = dataStart;
                    if (TryReadName(message, ref pointer, out var target))
                    {
                        mentions = true;
                        instance ??= InstanceLabel(target, wanted);
                    }
                    break;
                }

                case TypeSrv when dataLength >= 6:
                    port ??= BinaryPrimitives.ReadUInt16BigEndian(message[(dataStart + 4)..]);
                    break;

                case TypeA when dataLength == 4:
                    addresses.Add(new IPAddress(message.Slice(dataStart, 4).ToArray()));
                    break;
            }

            offset = dataStart + dataLength;
        }

        response = new MdnsResponse(mentions, instance, port, addresses);
        return true;
    }

    // ── Nomes DNS ───────────────────────────────────────────────────────

    private static void WriteName(List<byte> destination, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            destination.Add((byte)bytes.Length);
            destination.AddRange(bytes);
        }
        destination.Add(0);   // raiz
    }

    private static void AppendUInt16(List<byte> destination, ushort value)
    {
        destination.Add((byte)(value >> 8));
        destination.Add((byte)(value & 0xFF));
    }

    /// <summary>
    /// Lê um nome, seguindo os ponteiros de compressão (0xC0). O limite de saltos
    /// existe porque uma mensagem hostil pode encadear ponteiros em ciclo.
    /// </summary>
    private static bool TryReadName(ReadOnlySpan<byte> message, ref int offset, out string name)
    {
        name = "";
        var labels = new List<string>();
        var jumps = 0;
        var cursor = offset;
        var followed = false;

        while (true)
        {
            if (cursor >= message.Length)
                return false;

            var length = message[cursor];

            if (length == 0)
            {
                cursor++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length || ++jumps > 16)
                    return false;

                var target = ((length & 0x3F) << 8) | message[cursor + 1];
                if (!followed)
                {
                    offset = cursor + 2;
                    followed = true;
                }
                cursor = target;
                continue;
            }

            if ((length & 0xC0) != 0 || cursor + 1 + length > message.Length)
                return false;

            labels.Add(Encoding.UTF8.GetString(message.Slice(cursor + 1, length)));
            cursor += 1 + length;
        }

        if (!followed)
            offset = cursor;

        name = string.Join('.', labels);
        return true;
    }

    private static bool TrySkipName(ReadOnlySpan<byte> message, ref int offset) =>
        TryReadName(message, ref offset, out _);

    private static bool Advance(ReadOnlySpan<byte> message, ref int offset, int count)
    {
        if (offset + count > message.Length)
            return false;
        offset += count;
        return true;
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "2G Pilot._2gpilot._udp.local" → "2G Pilot". O rótulo da instância é o que
    /// vai para a tela; o resto é o nome do serviço, igual para todo mundo.
    /// </summary>
    private static string? InstanceLabel(string fullName, string serviceName)
    {
        var suffix = "." + serviceName.TrimEnd('.');
        return fullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fullName[..^suffix.Length]
            : null;
    }
}
