using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>Mensagem vinda do app, já lida e validada na forma (não no conteúdo).</summary>
public abstract record ClientMessage;

public sealed record HelloMessage(int Protocol, string DeviceId, string DeviceName, string? Token) : ClientMessage;

public sealed record PairMessage(string Code) : ClientMessage;

public sealed record CommandMessage(ControlCommand Command) : ClientMessage;

/// <summary>Tipo desconhecido: o canal ignora. É o que permite estender a versão 1.</summary>
public sealed record UnknownMessage(string Type) : ClientMessage;

/// <summary>
/// Mensagens do canal de controle (spec 01, "Mensagens"): JSON, um por quadro, campos em
/// camelCase. A leitura nunca lança — mensagem malformada devolve false e vira
/// <c>invalid_message</c> na sessão.
/// </summary>
public static class ControlProtocol
{
    public const int Version = 1;
    public const int MaxIdLength = 64;
    public const int MaxDeviceIdLength = 128;
    public const int MaxDeviceNameLength = 64;

    public static bool TryParse(string json, [NotNullWhen(true)] out ClientMessage? message)
    {
        message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryString(root, "type", out var type))
                return false;

            message = type switch
            {
                "hello" => ParseHello(root),
                "pair" => TryString(root, "code", out var code) ? new PairMessage(code) : null,
                "set" => ParseCommand(root, ControlKind.Set),
                "action" => ParseCommand(root, ControlKind.Action),
                _ => new UnknownMessage(type),
            };
            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static HelloMessage? ParseHello(JsonElement root)
    {
        if (!root.TryGetProperty("protocol", out var protocolEl)
            || protocolEl.ValueKind != JsonValueKind.Number
            || !protocolEl.TryGetInt32(out var protocol))
            return null;

        if (!root.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object
            || !TryString(device, "id", out var id) || id.Length == 0 || id.Length > MaxDeviceIdLength
            || !TryString(device, "name", out var name))
            return null;

        name = name.Trim();
        if (name.Length == 0)
            return null;
        if (name.Length > MaxDeviceNameLength)
            name = name[..MaxDeviceNameLength];

        var token = TryString(root, "token", out var t) && t.Length > 0 ? t : null;
        return new HelloMessage(protocol, id, name, token);
    }

    private static CommandMessage? ParseCommand(JsonElement root, ControlKind kind)
    {
        if (!TryString(root, "id", out var id) || id.Length == 0 || id.Length > MaxIdLength
            || !TryString(root, "control", out var control) || control.Length == 0)
            return null;

        long value = 0;
        if (kind == ControlKind.Set
            && (!root.TryGetProperty("value", out var valueEl)
                || valueEl.ValueKind != JsonValueKind.Number
                || !valueEl.TryGetInt64(out value)))
            return null;   // valor ausente, texto ou decimal: o fio só aceita inteiro

        return new CommandMessage(new ControlCommand(id, control, kind, value));
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString()!;
            return true;
        }
        value = "";
        return false;
    }

    // ── Mensagens do Connector ─────────────────────────────────────────

    public static string Welcome(string connectorVersion, string? simulator) => Write(w =>
    {
        w.WriteString("type", "welcome");
        w.WriteNumber("protocol", Version);
        w.WriteString("connector", connectorVersion);
        WriteNullable(w, "simulator", simulator);
    });

    public static string PairingRequired() => Write(w => w.WriteString("type", "pairing_required"));

    public static string Paired(string token) => Write(w =>
    {
        w.WriteString("type", "paired");
        w.WriteString("token", token);
    });

    public static string Controls(IEnumerable<string> controls) => Write(w =>
    {
        w.WriteString("type", "controls");
        w.WriteStartArray("controls");
        foreach (var control in controls)
            w.WriteStringValue(control);
        w.WriteEndArray();
    });

    public static string Result(ControlResult result) => Write(w =>
    {
        w.WriteString("type", "result");
        w.WriteString("id", result.Id);
        w.WriteBoolean("ok", result.Ok);
        if (result.Error is not null)
            w.WriteString("error", result.Error);
    });

    public static string Error(string code) => Write(w =>
    {
        w.WriteString("type", "error");
        w.WriteString("code", code);
    });

    /// <summary>Sempre o retrato completo; rádio ausente não aparece (spec 01, "state").</summary>
    public static string State(long seq, string? simulator, RadioState? state) => Write(w =>
    {
        w.WriteString("type", "state");
        w.WriteNumber("seq", seq);
        WriteNullable(w, "simulator", simulator);
        w.WriteStartObject("radios");
        if (state is not null)
        {
            WritePair(w, "com1", state.Com1);
            WritePair(w, "com2", state.Com2);
            WritePair(w, "nav1", state.Nav1);
            WritePair(w, "nav2", state.Nav2);
            if (state.Adf1Active is long adf)
            {
                w.WriteStartObject("adf1");
                w.WriteNumber("active", adf);
                w.WriteEndObject();
            }
            if (state.Xpdr is { } xpdr)
            {
                w.WriteStartObject("xpdr");
                w.WriteNumber("code", xpdr.Code);
                if (xpdr.Mode is int mode)
                    w.WriteNumber("mode", mode);
                w.WriteEndObject();
            }
            if (state.AltimeterPa is long pa)
            {
                w.WriteStartObject("altimeter");
                w.WriteNumber("baroPa", pa);
                w.WriteEndObject();
            }
        }
        w.WriteEndObject();
    });

    private static void WritePair(Utf8JsonWriter w, string name, FrequencyPair? pair)
    {
        if (pair is null)
            return;
        w.WriteStartObject(name);
        w.WriteNumber("active", pair.Active);
        w.WriteNumber("standby", pair.Standby);
        w.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.WriteString(name, value);
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
