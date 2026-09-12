using System.Net;

namespace TwoG.Connector.Core;

/// <summary>
/// Anúncio do canal de controle na UDP 49002 (spec 01, "Descoberta"):
/// <c>2GCTL&lt;nome&gt;,&lt;protocolo&gt;,&lt;url&gt;</c>, no estilo do 2GFPL. O host da URL é o IP da
/// interface por onde AQUELE anúncio sai — num PC com Wi-Fi e Ethernet, cada interface
/// anuncia o próprio endereço. (O 2GFPL usa só a rota padrão; ver o problema conhecido no 00.)
/// </summary>
public static class ControlAnnouncement
{
    public const string Prefix = "2GCTL";

    public static string Url(IPAddress host, int port) => $"ws://{host}:{port}{ControlServer.Path}";

    public static string Sentence(string deviceName, string url) =>
        $"{Prefix}{XgpsSentences.SanitizeDeviceName(deviceName)},{ControlProtocol.Version},{url}";

    /// <summary>Sentença para um destino, ou null se não houver por onde ele saia.</summary>
    public static string? SentenceFor(
        IPAddress destination, string deviceName, int port,
        IReadOnlyList<(IPAddress Address, IPAddress Mask)> interfaces,
        Func<IPAddress, IPAddress?> routeSource)
    {
        var host = NetworkMath.SourceAddressFor(destination, interfaces) ?? routeSource(destination);
        return host is null ? null : Sentence(deviceName, Url(host, port));
    }
}
