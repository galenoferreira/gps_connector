using System.Security.Cryptography;

namespace TwoG.Connector.Core;

/// <summary>
/// Assinatura do manifesto de atualização: ECDSA P-256 sobre SHA-256, em DER,
/// codificada em Base64. É o que impede que um token ou conta do GitHub
/// comprometidos bastem para empurrar um binário a todos os usuários — a chave
/// privada só existe no secret do CI e no backup offline do mantenedor.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// True se <paramref name="signatureBase64"/> assina <paramref name="data"/> com
    /// alguma das chaves aceitas. Nunca lança: entrada ruim é simplesmente rejeitada.
    /// </summary>
    public static bool Verify(byte[] data, string signatureBase64, IReadOnlyList<string> acceptedPublicKeysPem)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        if (signature.Length == 0)
            return false;

        foreach (var pem in acceptedPublicKeysPem)
        {
            try
            {
                using var key = ECDsa.Create();
                key.ImportFromPem(pem);
                if (key.KeySize != 256)
                    continue;
                if (key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    return true;
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                // Chave ilegível ou assinatura malformada: tenta a próxima chave.
            }
        }
        return false;
    }

    /// <summary>
    /// Assina com a chave privada em PEM. Usado só pela ferramenta de release; o app
    /// instalado nunca tem chave privada.
    /// </summary>
    public static string Sign(byte[] data, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(
            key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }
}
