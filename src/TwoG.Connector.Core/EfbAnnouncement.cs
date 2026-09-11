using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>
/// Anúncio que um EFB emite na UDP 63093 para se dar a conhecer, no formato
/// estabelecido pelo ForeFlight e adotado pelo 2G Pilot:
///
///   {"App":"2G Pilot","GDL90":{"port":4000}}
///
/// O dado que importa não está no corpo: é o IP de ORIGEM do datagrama, que
/// revela onde o EFB está sem ninguém precisar adivinhar endereço. O corpo só
/// serve para confirmar que quem falou é mesmo um EFB, e não outro tráfego que
/// calhou de cair na porta.
/// </summary>
/// <param name="AppName">Nome declarado pelo app ("2G Pilot", "ForeFlight").</param>
/// <param name="Gdl90Port">
/// Porta em que o app quer receber GDL 90, quando declarada. O 2G Connector
/// ainda não fala GDL 90 — o campo é preservado para quando falar.
/// </param>
public sealed record EfbAnnouncement(string AppName, int? Gdl90Port)
{
    /// <summary>Porta em que os EFBs anunciam a si mesmos.</summary>
    public const int AnnouncePort = 63093;

    /// <summary>
    /// Um anúncio real tem algumas dezenas de bytes; qualquer coisa maior não é
    /// um deles e não vale o custo de tentar interpretar como JSON.
    /// </summary>
    private const int MaxPayloadBytes = 512;

    /// <summary>
    /// Interpreta o datagrama. Devolve false para qualquer coisa que não seja um
    /// anúncio de EFB — porta compartilhada recebe tráfego alheio, e o silêncio
    /// aqui é o comportamento certo.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> payload, out EfbAnnouncement announcement)
    {
        announcement = null!;

        if (payload.Length is 0 or > MaxPayloadBytes)
            return false;

        string json;
        try
        {
            json = Encoding.UTF8.GetString(payload);
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            // "App" é o único campo obrigatório: é ele que distingue um EFB de
            // qualquer outro JSON que apareça na porta.
            if (!document.RootElement.TryGetProperty("App", out var app)
                || app.ValueKind != JsonValueKind.String)
                return false;

            var name = app.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            announcement = new EfbAnnouncement(name.Trim(), ReadGdl90Port(document.RootElement));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? ReadGdl90Port(JsonElement root)
    {
        if (!root.TryGetProperty("GDL90", out var gdl90)
            || gdl90.ValueKind != JsonValueKind.Object
            || !gdl90.TryGetProperty("port", out var port)
            || !port.TryGetInt32(out var value)
            || value is < 1 or > 65535)
            return null;
        return value;
    }
}
