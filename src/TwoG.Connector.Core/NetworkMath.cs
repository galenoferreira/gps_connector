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
    /// IP da interface cuja sub-rede contém <paramref name="destination"/> (o broadcast
    /// dirigido pertence à própria sub-rede). Entre várias que contêm, vence a de máscara
    /// mais longa, como na tabela de rotas: numa VPN 10.0.0.0/8 ao lado da LAN 10.0.0.0/24,
    /// 10.0.0.50 sai pela LAN, qualquer que seja a ordem das interfaces. Empate de máscara
    /// (duas placas na mesma sub-rede) quem desfaz é a métrica, que só a rota do sistema
    /// conhece — por isso isto é a reserva dela, não o contrário. Null quando nenhuma contém.
    /// </summary>
    public static IPAddress? SourceAddressFor(
        IPAddress destination, IEnumerable<(IPAddress Address, IPAddress Mask)> interfaces)
    {
        if (destination.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        var dest = destination.GetAddressBytes();
        IPAddress? best = null;
        var bestPrefix = -1;
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
            if (!sameNetwork)
                continue;

            var prefix = 0;
            foreach (var b in m)
                prefix += System.Numerics.BitOperations.PopCount(b);
            if (prefix > bestPrefix)
            {
                best = address;
                bestPrefix = prefix;
            }
        }
        return best;
    }
}
