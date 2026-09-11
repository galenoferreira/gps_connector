using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TwoG.Connector.Services;

/// <summary>
/// Descrição das interfaces IPv4 ativas, para o painel de diagnóstico.
///
/// Existe para responder à pergunta que trava toda investigação de "o tablet não
/// recebe": o PC e o tablet estão na mesma sub-rede? Sem isto o usuário não tem
/// como comparar o IP que o iPad mostra com o que o conector está usando.
/// </summary>
public static class NetworkDiagnostics
{
    /// <summary>Uma linha por interface: "Wi-Fi — 192.168.1.42/24".</summary>
    public static IReadOnlyList<string> ActiveIPv4Interfaces()
    {
        var lines = new List<string>();

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return lines;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                lines.Add($"{nic.Name} — {addr.Address}/{PrefixLength(addr.IPv4Mask)}");
            }
        }
        return lines;
    }

    /// <summary>Converte a máscara em bits ("255.255.255.0" → 24).</summary>
    private static int PrefixLength(IPAddress? mask)
    {
        if (mask is null || mask.AddressFamily != AddressFamily.InterNetwork)
            return 0;

        var bits = 0;
        foreach (var b in mask.GetAddressBytes())
            bits += System.Numerics.BitOperations.PopCount(b);
        return bits;
    }
}
