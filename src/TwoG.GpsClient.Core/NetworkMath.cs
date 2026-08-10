using System.Net;

namespace TwoG.GpsClient.Core;

public static class NetworkMath
{
    /// <summary>
    /// Calcula o endereço de broadcast dirigido da sub-rede (ex.: 192.168.1.42/24 → 192.168.1.255).
    /// Retorna null para máscaras inválidas (0.0.0.0) ou endereços não-IPv4.
    /// </summary>
    public static IPAddress? DirectedBroadcast(IPAddress address, IPAddress? mask)
    {
        if (mask is null
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || mask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        var ip = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        // /0 é inválida; /32 e /31 não têm broadcast dirigido útil (seria o próprio
        // IP da interface ou o par ponto-a-ponto — típico de adaptadores VPN).
        if (m is [0, 0, 0, 0] or [255, 255, 255, 255] or [255, 255, 255, 254])
            return null;

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
            broadcast[i] = (byte)(ip[i] | ~m[i]);
        return new IPAddress(broadcast);
    }
}
