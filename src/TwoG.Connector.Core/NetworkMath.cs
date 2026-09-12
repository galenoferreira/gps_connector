using System.Net;

namespace TwoG.Connector.Core;

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

    /// <summary>
    /// IP da interface por onde <paramref name="destination"/> sai: a primeira cuja sub-rede
    /// contém o destino (o broadcast dirigido pertence à própria sub-rede). Null quando
    /// nenhuma contém — quem chama decide pela rota do sistema.
    /// </summary>
    public static IPAddress? SourceAddressFor(
        IPAddress destination, IEnumerable<(IPAddress Address, IPAddress Mask)> interfaces)
    {
        if (destination.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        var dest = destination.GetAddressBytes();
        foreach (var (address, mask) in interfaces)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || mask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                continue;

            var a = address.GetAddressBytes();
            var m = mask.GetAddressBytes();
            if (m is [0, 0, 0, 0])
                continue;

            var sameNetwork = true;
            for (var i = 0; i < 4; i++)
            {
                if ((a[i] & m[i]) != (dest[i] & m[i]))
                {
                    sameNetwork = false;
                    break;
                }
            }
            if (sameNetwork)
                return address;
        }
        return null;
    }
}
