using System.Net;
using System.Text;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

public class MdnsProtocolTests
{
    private const string Service = "_2gpilot._udp.local";

    // ── Montagem de mensagens, para os testes exercitarem bytes reais ──

    private static void WriteName(List<byte> to, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            to.Add((byte)bytes.Length);
            to.AddRange(bytes);
        }
        to.Add(0);
    }

    private static void WriteUInt16(List<byte> to, int value)
    {
        to.Add((byte)(value >> 8));
        to.Add((byte)(value & 0xFF));
    }

    private static void WriteRecordHeader(List<byte> to, string owner, int type, int dataLength)
    {
        WriteName(to, owner);
        WriteUInt16(to, type);
        WriteUInt16(to, 1);            // classe IN
        WriteUInt16(to, 0);            // TTL alto
        WriteUInt16(to, 120);          // TTL baixo
        WriteUInt16(to, dataLength);
    }

    /// <summary>Resposta como um iPad devolve: PTR + SRV + A na mesma mensagem.</summary>
    private static byte[] FullResponse(string instance = "2G Pilot", int port = 49002, string ip = "192.168.1.50")
    {
        var full = $"{instance}.{Service}";
        var host = "ipad.local";

        var ptr = new List<byte>();
        WriteName(ptr, full);

        var srv = new List<byte>();
        WriteUInt16(srv, 0);           // prioridade
        WriteUInt16(srv, 0);           // peso
        WriteUInt16(srv, port);
        WriteName(srv, host);

        var message = new List<byte>();
        WriteUInt16(message, 0);       // ID
        WriteUInt16(message, 0x8400);  // resposta autoritativa
        WriteUInt16(message, 0);       // perguntas
        WriteUInt16(message, 1);       // respostas
        WriteUInt16(message, 0);       // autoridade
        WriteUInt16(message, 2);       // adicionais

        WriteRecordHeader(message, Service, 12, ptr.Count);
        message.AddRange(ptr);

        WriteRecordHeader(message, full, 33, srv.Count);
        message.AddRange(srv);

        WriteRecordHeader(message, host, 1, 4);
        message.AddRange(IPAddress.Parse(ip).GetAddressBytes());

        return [.. message];
    }

    // ── Pergunta ────────────────────────────────────────────────────────

    [Fact]
    public void BuildPtrQuery_HasOneQuestionAndNoRecords()
    {
        var q = MdnsProtocol.BuildPtrQuery(Service);

        Assert.Equal(0, q[0]);                       // ID zerado, como manda o mDNS
        Assert.Equal(0, q[1]);
        Assert.Equal(0, q[2] | q[3]);                // flags de pergunta
        Assert.Equal(1, (q[4] << 8) | q[5]);         // uma pergunta
        Assert.Equal(0, (q[6] << 8) | q[7]);         // nenhuma resposta
    }

    [Fact]
    public void BuildPtrQuery_EncodesTheServiceAsDnsLabels()
    {
        var q = MdnsProtocol.BuildPtrQuery(Service);
        var body = q.AsSpan(12);

        Assert.Equal(8, body[0]);                    // "_2gpilot" tem 8 bytes
        Assert.Equal("_2gpilot", Encoding.UTF8.GetString(body.Slice(1, 8)));
        Assert.Equal(4, body[9]);                    // "_udp"
        Assert.Equal("_udp", Encoding.UTF8.GetString(body.Slice(10, 4)));
        Assert.Equal(5, body[14]);                   // "local"
        Assert.Equal("local", Encoding.UTF8.GetString(body.Slice(15, 5)));
        Assert.Equal(0, body[20]);                   // raiz
    }

    /// <summary>
    /// O bit QU pede resposta unicast, que é o que permite perguntar de uma porta
    /// efêmera em vez de disputar a 5353 com o responder do Windows.
    /// </summary>
    [Fact]
    public void BuildPtrQuery_AsksForUnicastResponse()
    {
        var q = MdnsProtocol.BuildPtrQuery(Service);
        var type = (q[^4] << 8) | q[^3];
        var klass = (q[^2] << 8) | q[^1];

        Assert.Equal(12, type);                      // PTR
        Assert.Equal(0x8001, klass);                 // IN com bit QU
    }

    // ── Resposta ────────────────────────────────────────────────────────

    [Fact]
    public void TryParseResponse_ReadsInstancePortAndAddress()
    {
        Assert.True(MdnsProtocol.TryParseResponse(FullResponse(), Service, out var r));

        Assert.True(r.MentionsService);
        Assert.Equal("2G Pilot", r.InstanceName);
        Assert.Equal(49002, r.Port);
        Assert.Equal([IPAddress.Parse("192.168.1.50")], r.Addresses);
    }

    [Fact]
    public void TryParseResponse_IgnoresAnotherServiceOnTheWire()
    {
        // Uma impressora respondendo na mesma rede não pode virar destino.
        Assert.True(MdnsProtocol.TryParseResponse(FullResponse(), "_ipp._tcp.local", out var r));
        Assert.False(r.MentionsService);
        Assert.Null(r.InstanceName);
    }

    [Fact]
    public void TryParseResponse_RejectsQueries()
    {
        Assert.False(MdnsProtocol.TryParseResponse(MdnsProtocol.BuildPtrQuery(Service), Service, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(11)]
    public void TryParseResponse_RejectsTruncatedHeader(int length)
    {
        Assert.False(MdnsProtocol.TryParseResponse(new byte[length], Service, out _));
    }

    /// <summary>Datagrama cortado no meio de um registro não pode derrubar nada.</summary>
    [Fact]
    public void TryParseResponse_RejectsTruncatedBody()
    {
        var full = FullResponse();
        Assert.False(MdnsProtocol.TryParseResponse(full.AsSpan(0, full.Length - 6), Service, out _));
    }

    /// <summary>
    /// Respostas reais comprimem nomes com ponteiros 0xC0; sem seguir o ponteiro
    /// o parser leria lixo.
    /// </summary>
    [Fact]
    public void TryParseResponse_FollowsNameCompressionPointers()
    {
        var full = $"2G Pilot.{Service}";

        var ptr = new List<byte>();
        WriteName(ptr, full);

        var message = new List<byte>();
        WriteUInt16(message, 0);
        WriteUInt16(message, 0x8400);
        WriteUInt16(message, 1);       // uma pergunta, para o nome ficar no offset 12
        WriteUInt16(message, 1);
        WriteUInt16(message, 0);
        WriteUInt16(message, 0);

        WriteName(message, Service);   // pergunta, no offset 12
        WriteUInt16(message, 12);
        WriteUInt16(message, 1);

        // Resposta cujo dono é um ponteiro para o offset 12.
        message.Add(0xC0);
        message.Add(12);
        WriteUInt16(message, 12);
        WriteUInt16(message, 1);
        WriteUInt16(message, 0);
        WriteUInt16(message, 120);
        WriteUInt16(message, ptr.Count);
        message.AddRange(ptr);

        Assert.True(MdnsProtocol.TryParseResponse(message.ToArray(), Service, out var r));
        Assert.True(r.MentionsService);
        Assert.Equal("2G Pilot", r.InstanceName);
    }

    /// <summary>Ponteiro que aponta para si mesmo não pode travar a thread.</summary>
    [Fact]
    public void TryParseResponse_SurvivesPointerLoop()
    {
        var message = new List<byte>();
        WriteUInt16(message, 0);
        WriteUInt16(message, 0x8400);
        WriteUInt16(message, 0);
        WriteUInt16(message, 1);
        WriteUInt16(message, 0);
        WriteUInt16(message, 0);
        message.Add(0xC0);
        message.Add(12);               // aponta para si mesmo

        Assert.False(MdnsProtocol.TryParseResponse(message.ToArray(), Service, out _));
    }

    [Fact]
    public void TryParseResponse_HandlesResponseWithoutSrvOrAddress()
    {
        var ptr = new List<byte>();
        WriteName(ptr, $"2G Pilot.{Service}");

        var message = new List<byte>();
        WriteUInt16(message, 0);
        WriteUInt16(message, 0x8400);
        WriteUInt16(message, 0);
        WriteUInt16(message, 1);
        WriteUInt16(message, 0);
        WriteUInt16(message, 0);
        WriteRecordHeader(message, Service, 12, ptr.Count);
        message.AddRange(ptr);

        Assert.True(MdnsProtocol.TryParseResponse(message.ToArray(), Service, out var r));
        Assert.True(r.MentionsService);
        Assert.Null(r.Port);
        Assert.Empty(r.Addresses);
    }

    [Fact]
    public void PilotServiceName_IsWhatTheAppPublishes()
    {
        Assert.Equal("_2gpilot._udp.local", MdnsProtocol.PilotServiceName);
    }
}
