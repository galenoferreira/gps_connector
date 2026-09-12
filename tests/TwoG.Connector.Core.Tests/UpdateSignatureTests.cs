using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core.Tests;

/// <summary>
/// Pares de chaves gerados aqui mesmo e descartados: a chave privada real nunca
/// entra num teste.
/// </summary>
public class UpdateSignatureTests
{
    private static readonly byte[] Manifest = Encoding.UTF8.GetBytes("""{"version":"1.5.0","files":{}}""");

    /// <summary>Mesmo formato que o openssl ecparam produz: SEC1 "EC PRIVATE KEY".</summary>
    private static (string PrivatePem, string PublicPem) NewKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportECPrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void ValidSignatureIsAccepted()
    {
        var (priv, pub) = NewKeyPair();
        var sig = UpdateSignature.Sign(Manifest, priv);

        Assert.True(UpdateSignature.Verify(Manifest, sig, [pub]));
    }

    [Fact]
    public void OneAlteredByteIsRejected()
    {
        var (priv, pub) = NewKeyPair();
        var sig = UpdateSignature.Sign(Manifest, priv);
        var tampered = (byte[])Manifest.Clone();
        tampered[^2] ^= 0x01;

        Assert.False(UpdateSignature.Verify(tampered, sig, [pub]));
    }

    [Fact]
    public void SignatureFromAnotherKeyIsRejected()
    {
        var (attackerPriv, _) = NewKeyPair();
        var (_, ourPub) = NewKeyPair();

        Assert.False(UpdateSignature.Verify(Manifest, UpdateSignature.Sign(Manifest, attackerPriv), [ourPub]));
    }

    [Fact]
    public void AnyAcceptedKeyIsEnough_ForRotation()
    {
        var (_, oldPub) = NewKeyPair();
        var (newPriv, newPub) = NewKeyPair();

        Assert.True(UpdateSignature.Verify(Manifest, UpdateSignature.Sign(Manifest, newPriv), [oldPub, newPub]));
    }

    [Theory]
    [InlineData("isto não é base64")]
    [InlineData("")]
    [InlineData("AAAA")]
    public void GarbageSignatureIsRejected(string signature)
    {
        var (_, pub) = NewKeyPair();
        Assert.False(UpdateSignature.Verify(Manifest, signature, [pub]));
    }

    [Fact]
    public void UnreadableKeyIsSkipped_NotFatal()
    {
        var (priv, pub) = NewKeyPair();
        Assert.True(UpdateSignature.Verify(Manifest, UpdateSignature.Sign(Manifest, priv), ["não é PEM", pub]));
    }

    [Fact]
    public void EmptyKeyListRejectsEverything()
    {
        var (priv, _) = NewKeyPair();
        Assert.False(UpdateSignature.Verify(Manifest, UpdateSignature.Sign(Manifest, priv), []));
    }

    [Fact]
    public void ShipsExactlyTheMaintainerKey()
    {
        var key = Assert.Single(UpdateKeys.Accepted);
        Assert.Contains("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEkU39e98bOMDbzZsTK1VvFMqqFum/", key);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(key);
        Assert.Equal(256, ecdsa.KeySize);
    }

    [Fact]
    public void ARandomKeyDoesNotPassAsTheMaintainers()
    {
        var (priv, _) = NewKeyPair();
        Assert.False(UpdateSignature.Verify(Manifest, UpdateSignature.Sign(Manifest, priv), UpdateKeys.Accepted));
    }
}
