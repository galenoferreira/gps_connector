# Canal de controle e rádios pelo SimConnect — Plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans para executar este plano tarefa a tarefa. Os passos usam checkbox (`- [ ]`).

**Objetivo:** o 2G Pilot passa a comandar os rádios do MSFS 2020/2024 e do Prepar3D pelo 2G Connector, com pareamento, estado de volta e anúncio automático na rede.

**Arquitetura:** tudo que não depende do Windows vai para o `TwoG.Connector.Core` e é testado no macOS, inclusive o servidor WebSocket, com um `ClientWebSocket` de verdade contra um simulador falso. Isso cobre catálogo, conversões, protocolo, pareamento, tokens, sessão, servidor e anúncio. O app WPF fica com a parte que só roda no Windows: a tradução para o SimConnect, numa classe própria alimentada por fila e drenada pela thread do SimConnect que já existe, e com a interface. O servidor é um `TcpListener` com upgrade HTTP feito à mão e `WebSocket.CreateFromStream`, pelo mesmo motivo do `FlightPlanServer`: sem `HttpListener`, sem admin.

**Stack:** .NET 10 (Core `net10.0`, app `net10.0-windows` WPF x64), `System.Net.WebSockets`, `System.Text.Json`, `System.Security.Cryptography`, xUnit, SimConnect (DLL vendorizada do SDK 0.24.3).

**Specs:** [01-canal-de-controle.md](01-canal-de-controle.md) e [02-radios-simconnect.md](02-radios-simconnect.md), com a visão geral no [00-visao-geral.md](00-visao-geral.md).

## Restrições globais

- **Invariante:** o app envia só ID do catálogo e inteiro. Nenhuma mensagem do canal carrega nome de variável, de evento ou código.
- Porta TCP padrão `49004`, caminho `/control`. Anúncio `2GCTL<nome>,<protocolo>,<url>` na UDP `49002`, a cada 5 s, com o host da URL sendo o IP da interface por onde cada anúncio sai.
- Protocolo `1`. Mensagens JSON em quadros de texto, UTF-8, campos em camelCase, `type` obrigatório; `type` e campos desconhecidos são ignorados.
- Limites exatos: 4 conexões (a 5ª recebe `busy` e fecha com 4003); mensagem de até 4096 bytes (acima, fecha com 1009); 20 comandos/s por conexão (acima, `rate_limited`; mais de 100 recusas em 10 s, fecha com 4008); `hello` em até 10 s; ping a cada 15 s e fechamento sem pong em 30 s (45 s no total); `state` no máximo 10 por segundo; fila de comandos com até 32 pendentes (acima, `sim_unresponsive`).
- Códigos de fechamento: 1000, 1009, 4001 (aparelho removido), 4002 (protocolo), 4003 (lotado), 4008 (excesso).
- Pareamento: código de 6 dígitos, válido por 2 min, uso único, 5 tentativas no total, só um ativo por vez, comparação em tempo constante. Token de 32 bytes em base64url (43 caracteres); o arquivo `%APPDATA%\2G Connector\paired-devices.json` guarda **só o SHA-256**.
- Frequências em **Hz inteiros**, pressão em **Pa inteiros**. Faixas e espaçamentos exatamente como na tabela do 02.
- **Só a thread do `SimConnectService` toca o objeto `SimConnect`.** Os IDs de definição, requisição e evento dos rádios começam em **100**, longe dos que o `SimConnectService` já usa (0 a 9).
- Uma falha no canal nunca afeta o XGPS/XATT.
- Passe **sempre** o caminho do `.csproj` nos comandos `dotnet` (volume exFAT, arquivos `._*`).
- Comentários e commits em português, no estilo do repositório; todo commit termina com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Nenhum push, tag ou release sem autorização explícita do mantenedor.
- Linha de base: `PROD` em `189fde5`, 288 testes passando.

### Desvios conscientes do spec 01

- **`ISimControl` mora no Core**, não no app. É o que permite testar `ControlSession` e `ControlServer` no macOS contra um simulador falso. A implementação real (`SimConnectRadios`) continua no app.
- **`ISimControl` ganha `SimulatorName`**, que o `welcome` e o `state` precisam e o esboço do 01 não tinha.

## Mapa de arquivos

**Core — `src/TwoG.Connector.Core/`**

| Arquivo | Responsabilidade |
|---|---|
| `ControlTypes.cs` | `ControlKind`, `ControlCommand`, `ControlResult`, `ControlErrors`, `FrequencyPair`, `TransponderState`, `RadioState`, `RadioGroup`, `ISimControl` |
| `RadioCatalog.cs` | Os 16 controles, tipo de cada um, validação de faixa e espaçamento, lista de controles por grupo disponível |
| `RadioConversions.cs` | BCD16, BCO16, BCD32, Pa ↔ milibares × 16, arredondamento de Hz |
| `ControlProtocol.cs` | Ler as mensagens do app e gravar as do Connector |
| `PairingCodes.cs` | Código de 6 dígitos com validade, uso único e limite de tentativas |
| `PairedDeviceStore.cs` | Aparelhos pareados e tokens (só o hash em disco) |
| `ControlSession.cs` | Máquina de estados de uma conexão, com limite de taxa |
| `ControlServer.cs` | `TcpListener`, upgrade WebSocket, limites, envio de estado, revogação |
| `ControlAnnouncement.cs` | Sentença `2GCTL` e URL |
| `NetworkMath.cs` (modificar) | `SourceAddressFor`: IP da interface por onde sai cada destino |

**App — `src/TwoG.Connector/`**

| Arquivo | Mudança |
|---|---|
| `Services/SimConnectRadios.cs` | Novo: `ISimControl` pelo SimConnect (fila, grupos de dados, eventos) |
| `Services/CompositeSimControl.cs` | Novo: `ISimControl` estável que repassa para a fonte ativa |
| `Services/ControlService.cs` | Novo: junta servidor, códigos, aparelhos e anúncio; liga e desliga pelas Configurações |
| `Services/ISimSource.cs`, `CompositeSimSource.cs`, `XPlaneService.cs`, `SimConnectService.cs` | Capacidade `Control` e integração com a thread do SimConnect |
| `Services/IXgpsBroadcaster.cs`, `XgpsBroadcaster.cs` | `SendToEach`: sentença diferente por destino |
| `Configuration/AppSettings.cs`, `SettingsService.cs` | `AllowControl`, `ControlPort` |
| `App.xaml.cs`, `ViewModels/MainViewModel.cs`, `MainWindow.xaml` | Ligação e interface |

**Testes — `tests/TwoG.Connector.Core.Tests/`:** `RadioCatalogTests.cs`, `RadioConversionsTests.cs`, `ControlProtocolTests.cs`, `PairingCodesTests.cs`, `PairedDeviceStoreTests.cs`, `ControlSessionTests.cs`, `ControlServerTests.cs`, `ControlAnnouncementTests.cs`, e o helper `FakeSimControl.cs`.

---

### Tarefa 1: Tipos do canal e catálogo de rádios

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ControlTypes.cs`
- Criar: `src/TwoG.Connector.Core/RadioCatalog.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/RadioCatalogTests.cs`

**Interfaces:**
- Produz:
  - `enum ControlKind { Set, Action }`
  - `sealed record ControlCommand(string Id, string Control, ControlKind Kind, long Value)`
  - `sealed record ControlResult(string Id, bool Ok, string? Error)` com `Success(string id)` e `Fail(string id, string error)`
  - `static class ControlErrors` (constantes dos códigos do 01)
  - `sealed record FrequencyPair(long Active, long Standby)`, `sealed record TransponderState(int Code, int? Mode)`
  - `sealed record RadioState(FrequencyPair? Com1, FrequencyPair? Com2, FrequencyPair? Nav1, FrequencyPair? Nav2, long? Adf1Active, TransponderState? Xpdr, long? AltimeterPa)` com `RadioState.Empty`
  - `enum RadioGroup { Com1, Com2, Nav1, Nav2, Adf1, Transponder, TransponderMode, Altimeter }`
  - `interface ISimControl { string? SimulatorName; IReadOnlyCollection<string> AvailableControls; RadioState? State; event Action? Changed; ControlResult Submit(ControlCommand command); }`
  - `RadioCatalog.All`, `RadioCatalog.KindOf(string) : ControlKind?`, `RadioCatalog.Validate(string control, ControlKind kind, long value) : string?` (null = válido; senão o código de erro), `RadioCatalog.ControlsFor(IEnumerable<RadioGroup>) : IReadOnlyList<string>`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/RadioCatalogTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class RadioCatalogTests
{
    [Fact]
    public void CatalogHasTheSixteenControlsOfSpec02()
    {
        Assert.Equal(16, RadioCatalog.All.Count);
        Assert.Equal(RadioCatalog.All.Count, RadioCatalog.All.Distinct().Count());
    }

    [Theory]
    [InlineData("com1.swap", ControlKind.Action)]
    [InlineData("nav2.swap", ControlKind.Action)]
    [InlineData("com1.active", ControlKind.Set)]
    [InlineData("altimeter.baro", ControlKind.Set)]
    public void KnowsTheKindOfEachControl(string control, ControlKind kind)
    {
        Assert.Equal(kind, RadioCatalog.KindOf(control));
    }

    [Fact]
    public void UnknownControlHasNoKind()
    {
        Assert.Null(RadioCatalog.KindOf("ap.heading"));
    }

    [Theory]
    [InlineData(118_000_000L)]
    [InlineData(118_005_000L)]    // canal de 8,33
    [InlineData(118_010_000L)]
    [InlineData(118_015_000L)]
    [InlineData(118_025_000L)]
    [InlineData(121_900_000L)]
    [InlineData(136_990_000L)]    // 136990 % 25 == 15: canal de 8,33 válido
    public void AcceptsValidComChannels(long hz)
    {
        Assert.Null(RadioCatalog.Validate("com1.standby", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(117_995_000L)]    // abaixo da faixa
    [InlineData(137_000_000L)]    // acima da faixa
    [InlineData(118_020_000L)]    // .020 não existe na grade de 8,33
    [InlineData(118_045_000L)]
    [InlineData(118_070_000L)]
    [InlineData(118_095_000L)]
    [InlineData(118_001_000L)]    // não é canal
    [InlineData(118_000_500L)]    // não é múltiplo de kHz
    public void RejectsInvalidComChannels(long hz)
    {
        Assert.Equal(ControlErrors.OutOfRange, RadioCatalog.Validate("com2.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(108_000_000L, true)]
    [InlineData(117_950_000L, true)]
    [InlineData(110_300_000L, true)]
    [InlineData(108_025_000L, false)]
    [InlineData(107_950_000L, false)]
    [InlineData(118_000_000L, false)]
    public void ValidatesNav(long hz, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("nav1.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(190_000L, true)]
    [InlineData(350_000L, true)]
    [InlineData(1_799_500L, true)]
    [InlineData(189_500L, false)]
    [InlineData(1_800_000L, false)]
    [InlineData(350_250L, false)]
    public void ValidatesAdf(long hz, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("adf1.active", ControlKind.Set, hz));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1200L, true)]
    [InlineData(7700L, true)]
    [InlineData(7777L, true)]
    [InlineData(7800L, false)]
    [InlineData(7778L, false)]
    [InlineData(10_000L, false)]
    [InlineData(-1L, false)]
    public void ValidatesTransponderCode(long code, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("xpdr.code", ControlKind.Set, code));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(3L, true)]
    [InlineData(4L, true)]
    [InlineData(2L, false)]      // teste: fora de propósito
    [InlineData(5L, false)]
    public void ValidatesTransponderMode(long mode, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("xpdr.mode", ControlKind.Set, mode));
    }

    [Theory]
    [InlineData(94_800L, true)]
    [InlineData(101_325L, true)]
    [InlineData(105_000L, true)]
    [InlineData(94_799L, false)]
    [InlineData(105_001L, false)]
    public void ValidatesAltimeter(long pa, bool valid)
    {
        Assert.Equal(valid ? null : ControlErrors.OutOfRange, RadioCatalog.Validate("altimeter.baro", ControlKind.Set, pa));
    }

    [Fact]
    public void WrongKindOrUnknownControlIsUnsupported()
    {
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("com1.swap", ControlKind.Set, 1));
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("com1.active", ControlKind.Action, 0));
        Assert.Equal(ControlErrors.Unsupported, RadioCatalog.Validate("ap.heading", ControlKind.Set, 90));
    }

    [Fact]
    public void ActionsNeedNoValue()
    {
        Assert.Null(RadioCatalog.Validate("nav1.swap", ControlKind.Action, 0));
    }

    [Fact]
    public void ControlsForKeepsCatalogOrder()
    {
        var controls = RadioCatalog.ControlsFor([RadioGroup.Altimeter, RadioGroup.Com1]);

        Assert.Equal(["com1.active", "com1.standby", "com1.swap", "altimeter.baro"], controls);
    }

    [Fact]
    public void ControlsForSplitsTransponderCodeAndMode()
    {
        Assert.Equal(["xpdr.code"], RadioCatalog.ControlsFor([RadioGroup.Transponder]));
        Assert.Equal(["xpdr.mode"], RadioCatalog.ControlsFor([RadioGroup.TransponderMode]));
    }

    [Fact]
    public void AllGroupsGiveTheWholeCatalog()
    {
        Assert.Equal(RadioCatalog.All, RadioCatalog.ControlsFor(Enum.GetValues<RadioGroup>()));
        Assert.Empty(RadioCatalog.ControlsFor([]));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `RadioCatalog`, `ControlKind` etc. não existem.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/ControlTypes.cs`:

```csharp
namespace TwoG.Connector.Core;

public enum ControlKind
{
    /// <summary>Define um valor absoluto ("COM1 standby = 118.500").</summary>
    Set,

    /// <summary>Ação sem valor ("trocar COM1").</summary>
    Action,
}

/// <summary>Um comando vindo do app. <see cref="Value"/> é 0 nas ações.</summary>
public sealed record ControlCommand(string Id, string Control, ControlKind Kind, long Value);

/// <summary>
/// Resposta a um comando. <c>Ok</c> significa "entregue ao simulador", não "aplicado":
/// a confirmação de que o cockpit mudou é o estado (spec 01).
/// </summary>
public sealed record ControlResult(string Id, bool Ok, string? Error)
{
    public static ControlResult Success(string id) => new(id, true, null);

    public static ControlResult Fail(string id, string error) => new(id, false, error);
}

/// <summary>Códigos de erro do canal (spec 01, "Códigos de erro").</summary>
public static class ControlErrors
{
    public const string OutOfRange = "out_of_range";
    public const string Unsupported = "unsupported";
    public const string SimNotConnected = "sim_not_connected";
    public const string NotPaired = "not_paired";
    public const string RateLimited = "rate_limited";
    public const string SimUnresponsive = "sim_unresponsive";
    public const string NotApplied = "not_applied";
    public const string InvalidMessage = "invalid_message";
    public const string ProtocolUnsupported = "protocol_unsupported";
    public const string PairingInvalid = "pairing_invalid";
    public const string PairingLocked = "pairing_locked";
    public const string Busy = "busy";
}

public sealed record FrequencyPair(long Active, long Standby);

/// <summary><see cref="Mode"/> é null onde o simulador não modela o modo (Prepar3D).</summary>
public sealed record TransponderState(int Code, int? Mode);

/// <summary>
/// Retrato dos rádios reportado pelo simulador. Um rádio que a aeronave não tem fica null
/// e não aparece no <c>state</c>. Valores em Hz, Pa e código decimal (spec 02).
/// </summary>
public sealed record RadioState(
    FrequencyPair? Com1,
    FrequencyPair? Com2,
    FrequencyPair? Nav1,
    FrequencyPair? Nav2,
    long? Adf1Active,
    TransponderState? Xpdr,
    long? AltimeterPa)
{
    public static readonly RadioState Empty = new(null, null, null, null, null, null, null);
}

/// <summary>Grupos de rádio: cada um vira uma definição de dados no simulador (spec 02).</summary>
public enum RadioGroup
{
    Com1,
    Com2,
    Nav1,
    Nav2,
    Adf1,
    Transponder,
    TransponderMode,
    Altimeter,
}

/// <summary>
/// Capacidade de comandar o simulador. Fica no Core para que sessão e servidor sejam
/// testados sem simulador; a implementação real é do app (SimConnectRadios).
/// </summary>
public interface ISimControl
{
    /// <summary>Nome do simulador conectado, ou null.</summary>
    string? SimulatorName { get; }

    /// <summary>Controles que a aeronave atual aceita, na ordem do catálogo.</summary>
    IReadOnlyCollection<string> AvailableControls { get; }

    /// <summary>Último estado reportado, ou null sem simulador.</summary>
    RadioState? State { get; }

    /// <summary>Estado, controles disponíveis ou simulador mudaram. Pode vir de qualquer thread.</summary>
    event Action? Changed;

    /// <summary>
    /// Enfileira um comando JÁ VALIDADO pelo catálogo. Não bloqueia: a execução é na
    /// thread do simulador. Devolve <c>sim_not_connected</c> ou <c>sim_unresponsive</c>
    /// quando não dá para enfileirar.
    /// </summary>
    ControlResult Submit(ControlCommand command);
}
```

`src/TwoG.Connector.Core/RadioCatalog.cs`:

```csharp
namespace TwoG.Connector.Core;

/// <summary>
/// Catálogo de rádios da primeira entrega (spec 02): nomes, tipos, faixas e espaçamento.
/// Todo comando passa por aqui ANTES de chegar ao simulador.
/// </summary>
public static class RadioCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "com1.active", "com1.standby", "com1.swap",
        "com2.active", "com2.standby", "com2.swap",
        "nav1.active", "nav1.standby", "nav1.swap",
        "nav2.active", "nav2.standby", "nav2.swap",
        "adf1.active",
        "xpdr.code", "xpdr.mode",
        "altimeter.baro",
    ];

    private static readonly Dictionary<RadioGroup, string[]> ByGroup = new()
    {
        [RadioGroup.Com1] = ["com1.active", "com1.standby", "com1.swap"],
        [RadioGroup.Com2] = ["com2.active", "com2.standby", "com2.swap"],
        [RadioGroup.Nav1] = ["nav1.active", "nav1.standby", "nav1.swap"],
        [RadioGroup.Nav2] = ["nav2.active", "nav2.standby", "nav2.swap"],
        [RadioGroup.Adf1] = ["adf1.active"],
        [RadioGroup.Transponder] = ["xpdr.code"],
        [RadioGroup.TransponderMode] = ["xpdr.mode"],
        [RadioGroup.Altimeter] = ["altimeter.baro"],
    };

    public static ControlKind? KindOf(string control) =>
        !All.Contains(control) ? null
        : control.EndsWith(".swap", StringComparison.Ordinal) ? ControlKind.Action
        : ControlKind.Set;

    /// <summary>Null quando o comando é válido; senão o código de erro do canal.</summary>
    public static string? Validate(string control, ControlKind kind, long value)
    {
        if (KindOf(control) != kind)
            return ControlErrors.Unsupported;
        if (kind == ControlKind.Action)
            return null;

        var valid = control switch
        {
            "com1.active" or "com1.standby" or "com2.active" or "com2.standby" => IsComChannel(value),
            "nav1.active" or "nav1.standby" or "nav2.active" or "nav2.standby" =>
                value is >= 108_000_000 and <= 117_950_000 && value % 50_000 == 0,
            "adf1.active" => value is >= 190_000 and <= 1_799_500 && value % 500 == 0,
            "xpdr.code" => IsTransponderCode(value),
            "xpdr.mode" => value is 0 or 1 or 3 or 4,
            "altimeter.baro" => value is >= 94_800 and <= 105_000,
            _ => false,
        };
        return valid ? null : ControlErrors.OutOfRange;
    }

    /// <summary>Controles dos grupos disponíveis, na ordem do catálogo.</summary>
    public static IReadOnlyList<string> ControlsFor(IEnumerable<RadioGroup> groups)
    {
        var wanted = groups.SelectMany(g => ByGroup[g]).ToHashSet(StringComparer.Ordinal);
        return All.Where(wanted.Contains).ToArray();
    }

    /// <summary>
    /// Canal de COM de 25 ou 8,33 kHz: kHz inteiro, e resto 0, 5, 10 ou 15 na divisão por
    /// 25 — exatamente os canais que existem (.020, .045, .070 e .095 não existem).
    /// </summary>
    private static bool IsComChannel(long hz)
    {
        if (hz is < 118_000_000 or > 136_990_000 || hz % 1000 != 0)
            return false;
        return (hz / 1000 % 25) is 0 or 5 or 10 or 15;
    }

    /// <summary>0 a 7777, com cada dígito decimal de 0 a 7 (é octal na prática).</summary>
    private static bool IsTransponderCode(long code)
    {
        if (code is < 0 or > 7777)
            return false;
        for (var rest = code; rest > 0; rest /= 10)
        {
            if (rest % 10 > 7)
                return false;
        }
        return true;
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS, com os testes novos e os 288 da linha de base.

- [ ] **Passo 5: Commit**

```bash
git add src/TwoG.Connector.Core/ControlTypes.cs src/TwoG.Connector.Core/RadioCatalog.cs tests/TwoG.Connector.Core.Tests/RadioCatalogTests.cs
git commit -m "Tipos do canal de controle e catálogo de rádios

Os 16 controles do spec 02, com tipo, faixa e espaçamento, e a interface
ISimControl que o servidor usa. Todo comando é validado aqui antes de
chegar ao simulador; o COM aceita exatamente os canais de 25 e 8,33 kHz
que existem.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 2: Conversões para o SimConnect

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/RadioConversions.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/RadioConversionsTests.cs`

**Interfaces:**
- Produz: `RadioConversions.CodeToBcd16(long code) : uint`, `Bco16ToCode(uint bco) : int`, `HzToBcd32(long hz) : uint`, `PaToMillibars16(long pa) : uint`, `MillibarsToPa(double mb) : long`, `RoundHz(double hz) : long`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/RadioConversionsTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

/// <summary>Os exemplos do spec 02 viram os casos de teste.</summary>
public class RadioConversionsTests
{
    [Theory]
    [InlineData(7700L, 0x7700u)]
    [InlineData(1200L, 0x1200u)]
    [InlineData(7777L, 0x7777u)]
    [InlineData(0L, 0x0000u)]
    [InlineData(21L, 0x0021u)]
    public void CodeToBcd16PutsEachDigitInANibble(long code, uint bcd)
    {
        Assert.Equal(bcd, RadioConversions.CodeToBcd16(code));
    }

    [Theory]
    [InlineData(0x7700u, 7700)]
    [InlineData(0x1200u, 1200)]
    [InlineData(0x0000u, 0)]
    [InlineData(0x0021u, 21)]
    public void Bco16ReadsBackAsDecimalCode(uint bco, int code)
    {
        Assert.Equal(code, RadioConversions.Bco16ToCode(bco));
    }

    [Theory]
    [InlineData(350_000L, 0x03500000u)]
    [InlineData(1_799_500L, 0x17995000u)]
    [InlineData(190_000L, 0x01900000u)]
    [InlineData(1_234_500L, 0x12345000u)]
    public void HzToBcd32UsesTheAdfBcd32Layout(long hz, uint bcd)
    {
        Assert.Equal(bcd, RadioConversions.HzToBcd32(hz));
    }

    [Theory]
    [InlineData(101_325, 16212u)]
    [InlineData(94_800, 15168u)]
    [InlineData(105_000, 16800u)]
    public void PascalsBecomeMillibarsTimes16(long pa, uint mb16)
    {
        Assert.Equal(mb16, RadioConversions.PaToMillibars16(pa));
    }

    [Theory]
    [InlineData(1013.25, 101_325L)]
    [InlineData(1013.2, 101_320L)]
    [InlineData(948.0, 94_800L)]
    public void MillibarsBecomePascals(double mb, long pa)
    {
        Assert.Equal(pa, RadioConversions.MillibarsToPa(mb));
    }

    [Theory]
    [InlineData(118_499_999.6, 118_500_000L)]
    [InlineData(121_900_000.0, 121_900_000L)]
    [InlineData(350_000.4, 350_000L)]
    public void RoundHzRemovesFloatingNoise(double hz, long rounded)
    {
        Assert.Equal(rounded, RadioConversions.RoundHz(hz));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `RadioConversions` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/RadioConversions.cs`:

```csharp
namespace TwoG.Connector.Core;

/// <summary>
/// Conversões entre os valores do canal (Hz, Pa, código decimal) e os formatos que o
/// SimConnect espera ou devolve (spec 02, "Mapeamento para o SimConnect").
/// </summary>
public static class RadioConversions
{
    /// <summary>Código de transponder para BCD16: 7700 → 0x7700 (XPNDR_SET).</summary>
    public static uint CodeToBcd16(long code) => DecimalToBcd(code, digits: 4);

    /// <summary>TRANSPONDER CODE em BCO16 para código decimal: 0x7700 → 7700.</summary>
    public static int Bco16ToCode(uint bco)
    {
        var code = 0;
        var scale = 1;
        for (var i = 0; i < 4; i++)
        {
            code += (int)((bco >> (4 * i)) & 0xF) * scale;
            scale *= 10;
        }
        return code;
    }

    /// <summary>
    /// Hz em BCD32 para o ADF_COMPLETE_SET (unidade Frequency ADF BCD32): quatro dígitos
    /// de kHz, o décimo e três nibbles zero, ou seja, BCD de Hz × 10.
    /// 350.000 Hz → 0x03500000; 1.234,5 kHz → 0x12345000. A confirmação em simulador
    /// é o item V4 do spec 02.
    /// </summary>
    public static uint HzToBcd32(long hz) => DecimalToBcd(hz * 10, digits: 8);

    /// <summary>Pa para milibares × 16 (KOHLSMAN_SET): 101.325 → 16.212.</summary>
    public static uint PaToMillibars16(long pa) =>
        (uint)Math.Round(pa * 16 / 100.0, MidpointRounding.AwayFromZero);

    /// <summary>KOHLSMAN SETTING MB para Pa inteiro: 1013,25 → 101.325.</summary>
    public static long MillibarsToPa(double mb) =>
        (long)Math.Round(mb * 100, MidpointRounding.AwayFromZero);

    /// <summary>Frequência em Hz pedida como FLOAT64 ao simulador, sem o ruído de ponto flutuante.</summary>
    public static long RoundHz(double hz) =>
        (long)Math.Round(hz, MidpointRounding.AwayFromZero);

    private static uint DecimalToBcd(long value, int digits)
    {
        uint bcd = 0;
        var rest = value;
        for (var i = 0; i < digits && rest > 0; i++)
        {
            bcd |= (uint)(rest % 10) << (4 * i);
            rest /= 10;
        }
        return bcd;
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Commit**

```bash
git add src/TwoG.Connector.Core/RadioConversions.cs tests/TwoG.Connector.Core.Tests/RadioConversionsTests.cs
git commit -m "Conversões dos rádios para os formatos do SimConnect

BCD16 do transponder, BCO16 de volta, BCD32 do ADF, milibares x 16 do
altímetro e arredondamento de Hz, com os exemplos do spec 02 como casos
de teste.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 3: Protocolo — ler e gravar as mensagens

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ControlProtocol.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ControlProtocolTests.cs`

**Interfaces:**
- Consome: `ControlCommand`, `ControlKind`, `ControlResult`, `RadioState` (Tarefa 1).
- Produz:
  - `abstract record ClientMessage`; `HelloMessage(int Protocol, string DeviceId, string DeviceName, string? Token)`, `PairMessage(string Code)`, `CommandMessage(ControlCommand Command)`, `UnknownMessage(string Type)`
  - `ControlProtocol.Version = 1`, `ControlProtocol.TryParse(string json, out ClientMessage? message) : bool`
  - Gravadores, todos devolvendo `string` JSON: `Welcome(string connectorVersion, string? simulator)`, `PairingRequired()`, `Paired(string token)`, `Controls(IEnumerable<string>)`, `Result(ControlResult)`, `Error(string code)`, `State(long seq, string? simulator, RadioState? state)`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ControlProtocolTests.cs`:

```csharp
using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

public class ControlProtocolTests
{
    private static ClientMessage Parse(string json)
    {
        Assert.True(ControlProtocol.TryParse(json, out var message), $"deveria ler: {json}");
        return message!;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ReadsHelloWithToken()
    {
        var hello = Assert.IsType<HelloMessage>(Parse(
            """{"type":"hello","protocol":1,"app":"2G Pilot","appVersion":"3.2.0","device":{"id":"abc","name":"  iPad do Galeno  "},"token":"tok"}"""));

        Assert.Equal(1, hello.Protocol);
        Assert.Equal("abc", hello.DeviceId);
        Assert.Equal("iPad do Galeno", hello.DeviceName);   // espaços aparados
        Assert.Equal("tok", hello.Token);
    }

    [Fact]
    public void HelloWithoutTokenHasNullToken()
    {
        var hello = Assert.IsType<HelloMessage>(Parse("""{"type":"hello","protocol":1,"device":{"id":"abc","name":"iPad"}}"""));
        Assert.Null(hello.Token);
    }

    [Fact]
    public void LongDeviceNameIsCutTo64()
    {
        var name = new string('x', 100);
        var hello = Assert.IsType<HelloMessage>(Parse($$"""{"type":"hello","protocol":1,"device":{"id":"a","name":"{{name}}"}}"""));
        Assert.Equal(64, hello.DeviceName.Length);
    }

    [Fact]
    public void ReadsPair()
    {
        Assert.Equal("482913", Assert.IsType<PairMessage>(Parse("""{"type":"pair","code":"482913"}""")).Code);
    }

    [Fact]
    public void ReadsSetWithIntegerValue()
    {
        var command = Assert.IsType<CommandMessage>(Parse("""{"type":"set","id":"a1","control":"com1.standby","value":118500000}""")).Command;
        Assert.Equal(new ControlCommand("a1", "com1.standby", ControlKind.Set, 118_500_000), command);
    }

    [Fact]
    public void ReadsActionWithoutValue()
    {
        var command = Assert.IsType<CommandMessage>(Parse("""{"type":"action","id":"a2","control":"com1.swap"}""")).Command;
        Assert.Equal(new ControlCommand("a2", "com1.swap", ControlKind.Action, 0), command);
    }

    [Fact]
    public void UnknownTypeIsKeptAsUnknown_SoTheChannelCanIgnoreIt()
    {
        Assert.Equal("future", Assert.IsType<UnknownMessage>(Parse("""{"type":"future","x":1}""")).Type);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        Assert.IsType<PairMessage>(Parse("""{"type":"pair","code":"1","extra":{"nested":true}}"""));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"code":"1"}""")]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"type":"hello","protocol":"1","device":{"id":"a","name":"b"}}""")]
    [InlineData("""{"type":"hello","protocol":1,"device":{"name":"b"}}""")]
    [InlineData("""{"type":"hello","protocol":1,"device":{"id":"a","name":"   "}}""")]
    [InlineData("""{"type":"hello","protocol":1}""")]
    [InlineData("""{"type":"pair"}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active","value":1.5}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active","value":"118500000"}""")]
    [InlineData("""{"type":"set","id":"a","control":"com1.active"}""")]
    [InlineData("""{"type":"set","control":"com1.active","value":1}""")]
    [InlineData("""{"type":"action","id":"","control":"com1.swap"}""")]
    public void RejectsMalformedMessagesWithoutThrowing(string json)
    {
        Assert.False(ControlProtocol.TryParse(json, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void RejectsIdLongerThan64()
    {
        var id = new string('i', 65);
        Assert.False(ControlProtocol.TryParse($$"""{"type":"action","id":"{{id}}","control":"com1.swap"}""", out _));
    }

    [Fact]
    public void WritesWelcome()
    {
        var json = Json(ControlProtocol.Welcome("1.5.0", "Microsoft Flight Simulator 2024"));
        Assert.Equal("welcome", json.GetProperty("type").GetString());
        Assert.Equal(1, json.GetProperty("protocol").GetInt32());
        Assert.Equal("1.5.0", json.GetProperty("connector").GetString());
        Assert.Equal("Microsoft Flight Simulator 2024", json.GetProperty("simulator").GetString());
    }

    [Fact]
    public void WelcomeWithoutSimulatorWritesNull()
    {
        Assert.Equal(JsonValueKind.Null, Json(ControlProtocol.Welcome("1.5.0", null)).GetProperty("simulator").ValueKind);
    }

    [Fact]
    public void WritesSmallMessages()
    {
        Assert.Equal("pairing_required", Json(ControlProtocol.PairingRequired()).GetProperty("type").GetString());
        Assert.Equal("tok", Json(ControlProtocol.Paired("tok")).GetProperty("token").GetString());
        Assert.Equal("busy", Json(ControlProtocol.Error("busy")).GetProperty("code").GetString());
        Assert.Equal(["com1.active", "xpdr.code"],
            Json(ControlProtocol.Controls(["com1.active", "xpdr.code"])).GetProperty("controls").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void WritesResults()
    {
        var ok = Json(ControlProtocol.Result(ControlResult.Success("a1")));
        Assert.Equal("a1", ok.GetProperty("id").GetString());
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.False(ok.TryGetProperty("error", out _));

        var fail = Json(ControlProtocol.Result(ControlResult.Fail("a2", "out_of_range")));
        Assert.False(fail.GetProperty("ok").GetBoolean());
        Assert.Equal("out_of_range", fail.GetProperty("error").GetString());
    }

    [Fact]
    public void WritesFullState()
    {
        var state = new RadioState(
            new FrequencyPair(121_900_000, 118_500_000), new FrequencyPair(122_800_000, 124_350_000),
            new FrequencyPair(110_300_000, 113_900_000), null,
            350_000, new TransponderState(2000, 4), 101_320);

        var json = Json(ControlProtocol.State(1042, "MSFS 2024", state));
        var radios = json.GetProperty("radios");

        Assert.Equal(1042, json.GetProperty("seq").GetInt64());
        Assert.Equal(121_900_000, radios.GetProperty("com1").GetProperty("active").GetInt64());
        Assert.Equal(118_500_000, radios.GetProperty("com1").GetProperty("standby").GetInt64());
        Assert.False(radios.TryGetProperty("nav2", out _));            // rádio ausente não aparece
        Assert.Equal(350_000, radios.GetProperty("adf1").GetProperty("active").GetInt64());
        Assert.Equal(2000, radios.GetProperty("xpdr").GetProperty("code").GetInt32());
        Assert.Equal(4, radios.GetProperty("xpdr").GetProperty("mode").GetInt32());
        Assert.Equal(101_320, radios.GetProperty("altimeter").GetProperty("baroPa").GetInt64());
    }

    [Fact]
    public void TransponderWithoutModeOmitsMode()
    {
        var state = RadioState.Empty with { Xpdr = new TransponderState(1200, null) };
        var xpdr = Json(ControlProtocol.State(1, "Prepar3D v5", state)).GetProperty("radios").GetProperty("xpdr");
        Assert.False(xpdr.TryGetProperty("mode", out _));
    }

    [Fact]
    public void StateWithoutSimulatorHasEmptyRadios()
    {
        var json = Json(ControlProtocol.State(3, null, null));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("simulator").ValueKind);
        Assert.Empty(json.GetProperty("radios").EnumerateObject());
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `ControlProtocol` e as mensagens não existem.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/ControlProtocol.cs`:

```csharp
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
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Commit**

```bash
git add src/TwoG.Connector.Core/ControlProtocol.cs tests/TwoG.Connector.Core.Tests/ControlProtocolTests.cs
git commit -m "Protocolo do canal de controle: leitura e gravação das mensagens

Lê hello, pair, set e action sem nunca lançar: mensagem malformada, valor
decimal ou em texto viram recusa, e tipo desconhecido é mantido para ser
ignorado. Grava welcome, pairing_required, paired, controls, result,
error e state, este sempre como retrato completo.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 4: Código de pareamento

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/PairingCodes.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/PairingCodesTests.cs`

**Interfaces:**
- Produz: `enum PairingAttempt { Accepted, Invalid, Locked }`; `sealed class PairingCodes(Func<DateTime>? utcNow = null)` com `Lifetime` (2 min), `MaxFailures` (5), `Generate() : string`, `Cancel()`, `Active : (string Code, DateTime ExpiresUtc)?`, `TryConsume(string attempt) : PairingAttempt`

Semântica, do spec 01: código errado ou expirado → `Invalid`; nenhum código ativo ou tentativas esgotadas → `Locked`. A quinta tentativa errada ainda responde `Invalid`, e invalida o código: a partir daí é `Locked`.

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/PairingCodesTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class PairingCodesTests
{
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private PairingCodes NewCodes() => new(() => _now);

    [Fact]
    public void GeneratesSixDigits()
    {
        var code = NewCodes().Generate();
        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void CodesAreSpreadOut()
    {
        var codes = NewCodes();
        var seen = Enumerable.Range(0, 200).Select(_ => codes.Generate()).ToHashSet();
        Assert.True(seen.Count > 190, $"só {seen.Count} códigos distintos em 200");
    }

    [Fact]
    public void CorrectCodeIsAcceptedOnce()
    {
        var codes = NewCodes();
        var code = codes.Generate();

        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(code));
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));    // uso único
    }

    [Fact]
    public void WithoutCodeEverythingIsLocked()
    {
        Assert.Equal(PairingAttempt.Locked, NewCodes().TryConsume("123456"));
    }

    [Fact]
    public void FourMistakesStillAllowTheRightCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
            Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(wrong));

        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(code));
    }

    [Fact]
    public void FiveMistakesInvalidateTheCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
            Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(wrong));

        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }

    [Fact]
    public void ExpiredCodeIsInvalidThenLocked()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        _now += PairingCodes.Lifetime;

        Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(code));
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }

    [Fact]
    public void NewCodeReplacesTheOldOne()
    {
        var codes = NewCodes();
        var first = codes.Generate();
        string second;
        do { second = codes.Generate(); } while (second == first);

        Assert.Equal(PairingAttempt.Invalid, codes.TryConsume(first));
        Assert.Equal(PairingAttempt.Accepted, codes.TryConsume(second));
    }

    [Fact]
    public void ActiveShowsTheCodeUntilItExpires()
    {
        var codes = NewCodes();
        var code = codes.Generate();

        Assert.Equal(code, codes.Active!.Value.Code);
        Assert.Equal(_now + PairingCodes.Lifetime, codes.Active!.Value.ExpiresUtc);

        _now += PairingCodes.Lifetime;
        Assert.Null(codes.Active);
    }

    [Fact]
    public void CancelRemovesTheCode()
    {
        var codes = NewCodes();
        var code = codes.Generate();
        codes.Cancel();

        Assert.Null(codes.Active);
        Assert.Equal(PairingAttempt.Locked, codes.TryConsume(code));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `PairingCodes` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/PairingCodes.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core;

public enum PairingAttempt
{
    Accepted,

    /// <summary>Código errado ou expirado (<c>pairing_invalid</c>).</summary>
    Invalid,

    /// <summary>Nenhum código ativo, ou tentativas esgotadas (<c>pairing_locked</c>).</summary>
    Locked,
}

/// <summary>
/// Código de pareamento de 6 dígitos (spec 01, "Pareamento"). Só existe quando o piloto
/// pede, um de cada vez, vale 2 minutos, é de uso único e morre em 5 tentativas erradas,
/// somando todas as conexões. Com isso, a chance de força bruta é de 5 em um milhão por
/// código gerado.
/// </summary>
public sealed class PairingCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxFailures = 5;

    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private string? _code;
    private DateTime _expiresUtc;
    private int _failures;

    public PairingCodes(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Código ativo e validade, para a UI mostrar a contagem regressiva.</summary>
    public (string Code, DateTime ExpiresUtc)? Active
    {
        get
        {
            lock (_lock)
                return _code is null || _utcNow() >= _expiresUtc ? null : (_code, _expiresUtc);
        }
    }

    public string Generate()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        lock (_lock)
        {
            _code = code;
            _expiresUtc = _utcNow() + Lifetime;
            _failures = 0;
        }
        return code;
    }

    public void Cancel()
    {
        lock (_lock)
            _code = null;
    }

    public PairingAttempt TryConsume(string attempt)
    {
        lock (_lock)
        {
            if (_code is null)
                return PairingAttempt.Locked;

            if (_utcNow() >= _expiresUtc)
            {
                _code = null;
                return PairingAttempt.Invalid;
            }

            if (Matches(_code, attempt))
            {
                _code = null;
                return PairingAttempt.Accepted;
            }

            if (++_failures >= MaxFailures)
                _code = null;
            return PairingAttempt.Invalid;
        }
    }

    /// <summary>Comparação em tempo constante: o tempo de resposta não revela dígitos certos.</summary>
    private static bool Matches(string expected, string attempt)
    {
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(attempt);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Commit**

```bash
git add src/TwoG.Connector.Core/PairingCodes.cs tests/TwoG.Connector.Core.Tests/PairingCodesTests.cs
git commit -m "Código de pareamento de 6 dígitos

Gerado com gerador criptográfico só quando o piloto pede, um por vez,
válido por 2 minutos, de uso único e invalidado em 5 tentativas erradas.
Comparação em tempo constante.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 5: Aparelhos pareados e tokens

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/PairedDeviceStore.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/PairedDeviceStoreTests.cs`

**Interfaces:**
- Consome: `TestTempDir` (helper de teste existente).
- Produz: `sealed record PairedDevice(string Id, string Name, string TokenSha256, DateTime PairedUtc, DateTime LastSeenUtc)`; `sealed class PairedDeviceStore(string path, Func<DateTime>? utcNow = null)` com `Devices : IReadOnlyList<PairedDevice>`, `Pair(string deviceId, string deviceName) : string` (o token), `Authenticate(string token) : PairedDevice?`, `Remove(string deviceId) : bool`, eventos `Removed : Action<string>` (id) e `Changed : Action`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/PairedDeviceStoreTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class PairedDeviceStoreTests
{
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private PairedDeviceStore NewStore(TestTempDir tmp) => new(tmp.Sub("paired-devices.json"), () => _now);

    [Fact]
    public void TokenIs32BytesInBase64Url()
    {
        using var tmp = new TestTempDir();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", NewStore(tmp).Pair("ipad-1", "iPad"));
    }

    [Fact]
    public void FileKeepsOnlyTheHash()
    {
        using var tmp = new TestTempDir();
        var token = NewStore(tmp).Pair("ipad-1", "iPad do Galeno");
        var file = File.ReadAllText(tmp.Sub("paired-devices.json"));

        Assert.DoesNotContain(token, file);
        Assert.Contains("tokenSha256", file);
        Assert.Contains("iPad do Galeno", file);
    }

    [Fact]
    public void KnownTokenAuthenticatesAndUpdatesLastSeen()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var token = store.Pair("ipad-1", "iPad");
        _now = _now.AddHours(1);

        var device = store.Authenticate(token);

        Assert.Equal("ipad-1", device!.Id);
        Assert.Equal(_now, device.LastSeenUtc);
    }

    [Fact]
    public void UnknownTokenDoesNotAuthenticate()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        store.Pair("ipad-1", "iPad");

        Assert.Null(store.Authenticate("não-é-um-token"));
    }

    [Fact]
    public void PairingTheSameDeviceAgainRevokesTheOldToken()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var old = store.Pair("ipad-1", "iPad");
        var fresh = store.Pair("ipad-1", "iPad renomeado");

        Assert.Null(store.Authenticate(old));
        Assert.Equal("iPad renomeado", store.Authenticate(fresh)!.Name);
        Assert.Single(store.Devices);
    }

    [Fact]
    public void RemoveRevokesAndRaisesRemoved()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var token = store.Pair("ipad-1", "iPad");
        string? removed = null;
        store.Removed += id => removed = id;

        Assert.True(store.Remove("ipad-1"));

        Assert.Equal("ipad-1", removed);
        Assert.Null(store.Authenticate(token));
        Assert.False(store.Remove("ipad-1"));
    }

    [Fact]
    public void ChangedFiresOnPairAndRemove()
    {
        using var tmp = new TestTempDir();
        var store = NewStore(tmp);
        var changes = 0;
        store.Changed += () => changes++;

        store.Pair("ipad-1", "iPad");
        store.Remove("ipad-1");

        Assert.Equal(2, changes);
    }

    [Fact]
    public void PairingsSurviveARestart()
    {
        using var tmp = new TestTempDir();
        var token = NewStore(tmp).Pair("ipad-1", "iPad");

        Assert.Equal("ipad-1", NewStore(tmp).Authenticate(token)!.Id);
    }

    [Fact]
    public void CorruptFileMeansNoPairings_NotACrash()
    {
        using var tmp = new TestTempDir();
        File.WriteAllText(tmp.Sub("paired-devices.json"), "{ corrompido");

        Assert.Empty(NewStore(tmp).Devices);
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `PairedDeviceStore` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/PairedDeviceStore.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core;

public sealed record PairedDevice(string Id, string Name, string TokenSha256, DateTime PairedUtc, DateTime LastSeenUtc);

/// <summary>
/// Aparelhos pareados (spec 01, "Tokens"). O token vale até o piloto remover o aparelho,
/// e em disco fica SÓ o hash SHA-256: quem copiar o arquivo não consegue se passar pelo
/// iPad. Arquivo corrompido vira lista vazia — todos pareiam de novo, nada derruba o app.
/// </summary>
public sealed class PairedDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly List<PairedDevice> _devices;

    public PairedDeviceStore(string path, Func<DateTime>? utcNow = null)
    {
        _path = path;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _devices = Load(path);
    }

    /// <summary>Aparelho removido (id). O servidor fecha a conexão dele com 4001.</summary>
    public event Action<string>? Removed;

    /// <summary>A lista mudou (pareou ou removeu), para a UI se atualizar.</summary>
    public event Action? Changed;

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (_lock)
                return _devices.ToArray();
        }
    }

    /// <summary>Pareia (ou pareia de novo) o aparelho e devolve o token, que só o app guarda.</summary>
    public string Pair(string deviceId, string deviceName)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = _utcNow();
        lock (_lock)
        {
            // Parear de novo o mesmo aparelho substitui a entrada e invalida o token antigo.
            _devices.RemoveAll(d => d.Id == deviceId);
            _devices.Add(new PairedDevice(deviceId, deviceName, Hash(token), now, now));
            Save();
        }
        Changed?.Invoke();
        return token;
    }

    public PairedDevice? Authenticate(string token)
    {
        var presented = Encoding.ASCII.GetBytes(Hash(token));
        lock (_lock)
        {
            for (var i = 0; i < _devices.Count; i++)
            {
                if (!CryptographicOperations.FixedTimeEquals(presented, Encoding.ASCII.GetBytes(_devices[i].TokenSha256)))
                    continue;
                _devices[i] = _devices[i] with { LastSeenUtc = _utcNow() };
                Save();
                return _devices[i];
            }
        }
        return null;
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _devices.RemoveAll(d => d.Id == deviceId) > 0;
            if (removed)
                Save();
        }
        if (removed)
        {
            Removed?.Invoke(deviceId);
            Changed?.Invoke();
        }
        return removed;
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private sealed record FileModel(List<PairedDevice>? Devices);

    private static List<PairedDevice> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), JsonOptions);
            return model?.Devices?
                .Where(d => d is { Id.Length: > 0, Name: not null, TokenSha256.Length: 64 })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Chamado com o lock tomado. Escrita atômica; falha de disco não derruba o app.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new FileModel(_devices), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O pareamento continua valendo em memória até o app fechar.
        }
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Commit**

```bash
git add src/TwoG.Connector.Core/PairedDeviceStore.cs tests/TwoG.Connector.Core.Tests/PairedDeviceStoreTests.cs
git commit -m "Aparelhos pareados com o token guardado só como hash

Token de 32 bytes em base64url; em disco fica apenas o SHA-256. Parear de
novo o mesmo aparelho revoga o token antigo, remover avisa o servidor para
fechar a conexão, e arquivo corrompido vira lista vazia sem derrubar o app.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 6: Sessão — a máquina de estados de uma conexão

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ControlSession.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/FakeSimControl.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ControlSessionTests.cs`

**Interfaces:**
- Consome: `ISimControl`, `ControlCommand`, `ControlResult`, `ControlErrors`, `RadioCatalog` (Tarefa 1); `ControlProtocol` e as mensagens (Tarefa 3); `PairingCodes`, `PairingAttempt` (Tarefa 4); `PairedDeviceStore` (Tarefa 5).
- Produz:
  - `enum SessionPhase { AwaitingHello, Unpaired, Paired }`
  - `sealed record SessionOutput(IReadOnlyList<string> Messages, int? CloseCode = null, string? CloseReason = null)` com `SessionOutput.None`
  - `sealed class ControlSession(ISimControl control, PairedDeviceStore devices, PairingCodes codes, string connectorVersion, Func<DateTime>? utcNow = null)` (o relógio padrão é monotônico: só mede as janelas de taxa) com `Phase`, `ReadyForPush` (pareada e com o pacote de boas-vindas já enviado), `MarkWelcomeSent()`, `DeviceId`, `DeviceName`, `LastCommand` (resumo para o Diagnóstico), `Handle(string json) : SessionOutput`, `StateMessage() : string`, `ControlsMessage() : string`, e as constantes `MaxCommandsPerSecond = 20`, `MaxRefusalsPer10Seconds = 100`, `CloseProtocolUnsupported = 4002`, `CloseRateLimited = 4008`
  - Helper de teste `FakeSimControl : ISimControl` com `Submitted`, `FailWith`, `RaiseChanged()`

A sessão não sabe nada de socket: recebe o texto de uma mensagem e devolve o que enviar e se deve fechar. É isso que a torna testável sem rede. A validação pelo catálogo e pela lista de controles da aeronave acontece **aqui**, antes de o comando chegar ao `ISimControl`.

- [ ] **Passo 1: O simulador falso dos testes**

`tests/TwoG.Connector.Core.Tests/FakeSimControl.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

/// <summary>Simulador falso para sessão e servidor: registra comandos e deixa disparar mudanças.</summary>
internal sealed class FakeSimControl : ISimControl
{
    public string? SimulatorName { get; set; } = "Microsoft Flight Simulator 2024";

    public IReadOnlyCollection<string> AvailableControls { get; set; } = RadioCatalog.All;

    public RadioState? State { get; set; } = new(
        new FrequencyPair(121_900_000, 118_500_000), null, null, null,
        null, new TransponderState(2000, 4), 101_325);

    /// <summary>Quando definido, todo Submit falha com este código.</summary>
    public string? FailWith { get; set; }

    public List<ControlCommand> Submitted { get; } = [];

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command)
    {
        lock (Submitted)
            Submitted.Add(command);
        return FailWith is null ? ControlResult.Success(command.Id) : ControlResult.Fail(command.Id, FailWith);
    }

    public void RaiseChanged() => Changed?.Invoke();
}
```

- [ ] **Passo 2: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ControlSessionTests.cs`:

```csharp
using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

public class ControlSessionTests : IDisposable
{
    private const string Hello = """{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad de teste"}}""";

    private readonly TestTempDir _tmp = new();
    private readonly FakeSimControl _sim = new();
    private readonly PairingCodes _codes;
    private readonly PairedDeviceStore _devices;
    private DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    public ControlSessionTests()
    {
        _codes = new PairingCodes(() => _now);
        _devices = new PairedDeviceStore(_tmp.Sub("paired.json"), () => _now);
    }

    public void Dispose() => _tmp.Dispose();

    private ControlSession NewSession() => new(_sim, _devices, _codes, "1.5.0", () => _now);

    private static string[] Types(SessionOutput output) =>
        output.Messages.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("type").GetString()!).ToArray();

    private static JsonElement Json(string message) => JsonDocument.Parse(message).RootElement.Clone();

    /// <summary>Sessão já pareada, pronta para comandos.</summary>
    private ControlSession PairedSession(ControlSession? session = null)
    {
        session ??= NewSession();
        session.Handle(Hello);
        session.Handle($$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        Assert.Equal(SessionPhase.Paired, session.Phase);
        return session;
    }

    private static string Set(string id, string control, long value) =>
        $$"""{"type":"set","id":"{{id}}","control":"{{control}}","value":{{value}}}""";

    [Fact]
    public void HelloWithoutTokenAsksForPairing()
    {
        var session = NewSession();

        Assert.Equal(["pairing_required"], Types(session.Handle(Hello)));
        Assert.Equal(SessionPhase.Unpaired, session.Phase);
    }

    [Fact]
    public void OtherProtocolIsRefusedAndClosedWith4002()
    {
        var output = NewSession().Handle("""{"type":"hello","protocol":2,"device":{"id":"a","name":"b"}}""");

        Assert.Equal(["error"], Types(output));
        Assert.Equal("protocol_unsupported", Json(output.Messages[0]).GetProperty("code").GetString());
        Assert.Equal(ControlSession.CloseProtocolUnsupported, output.CloseCode);
    }

    [Fact]
    public void AnythingBeforeHelloIsInvalid()
    {
        var output = NewSession().Handle(Set("a", "com1.active", 121_900_000));
        Assert.Equal("invalid_message", Json(output.Messages[0]).GetProperty("code").GetString());
    }

    [Fact]
    public void InvalidJsonIsInvalidMessage_AndTheSessionGoesOn()
    {
        var session = NewSession();
        Assert.Equal(["error"], Types(session.Handle("{")));
        Assert.Equal(["pairing_required"], Types(session.Handle(Hello)));
    }

    [Fact]
    public void UnknownTypeIsIgnored()
    {
        Assert.Empty(NewSession().Handle("""{"type":"future"}""").Messages);
    }

    [Fact]
    public void WrongCodeIsPairingInvalid()
    {
        var session = NewSession();
        session.Handle(Hello);
        var code = _codes.Generate();
        var wrong = code == "000000" ? "111111" : "000000";

        var output = session.Handle($$"""{"type":"pair","code":"{{wrong}}"}""");

        Assert.Equal("pairing_invalid", Json(output.Messages[0]).GetProperty("code").GetString());
        Assert.Equal(SessionPhase.Unpaired, session.Phase);
    }

    [Fact]
    public void PairingWithoutActiveCodeIsLocked()
    {
        var session = NewSession();
        session.Handle(Hello);

        var output = session.Handle("""{"type":"pair","code":"123456"}""");

        Assert.Equal("pairing_locked", Json(output.Messages[0]).GetProperty("code").GetString());
    }

    [Fact]
    public void RightCodePairsAndSendsWelcomeControlsState()
    {
        var session = NewSession();
        session.Handle(Hello);

        var output = session.Handle($$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");

        Assert.Equal(["paired", "welcome", "controls", "state"], Types(output));
        var token = Json(output.Messages[0]).GetProperty("token").GetString()!;
        Assert.Equal("ipad-1", _devices.Authenticate(token)!.Id);
        Assert.Equal("ipad-1", session.DeviceId);
        Assert.Equal("iPad de teste", session.DeviceName);
    }

    [Fact]
    public void HelloWithValidTokenSkipsPairing()
    {
        var token = _devices.Pair("ipad-1", "iPad de teste");
        var session = NewSession();

        var output = session.Handle($$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        Assert.Equal(["welcome", "controls", "state"], Types(output));
        Assert.Equal(SessionPhase.Paired, session.Phase);
        Assert.Equal("1.5.0", Json(output.Messages[0]).GetProperty("connector").GetString());
    }

    [Fact]
    public void HelloWithRevokedTokenAsksForPairing()
    {
        var token = _devices.Pair("ipad-1", "iPad");
        _devices.Remove("ipad-1");

        var output = NewSession().Handle($$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        Assert.Equal(["pairing_required"], Types(output));
    }

    [Fact]
    public void CommandBeforePairingIsNotPaired()
    {
        var session = NewSession();
        session.Handle(Hello);

        var result = Json(session.Handle(Set("a1", "com1.active", 121_900_000)).Messages[0]);

        Assert.Equal("not_paired", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void ValidCommandReachesTheSimulator()
    {
        var session = PairedSession();

        var result = Json(session.Handle(Set("a1", "com1.standby", 118_500_000)).Messages[0]);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(new ControlCommand("a1", "com1.standby", ControlKind.Set, 118_500_000), Assert.Single(_sim.Submitted));
        Assert.Contains("com1.standby", session.LastCommand);
    }

    [Fact]
    public void OutOfRangeNeverReachesTheSimulator()
    {
        var result = Json(PairedSession().Handle(Set("a1", "com1.standby", 118_020_000)).Messages[0]);

        Assert.Equal("out_of_range", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void ControlMissingFromThisAircraftIsUnsupported()
    {
        _sim.AvailableControls = ["com1.active", "com1.standby", "com1.swap"];

        var result = Json(PairedSession().Handle(Set("a1", "com2.active", 121_900_000)).Messages[0]);

        Assert.Equal("unsupported", result.GetProperty("error").GetString());
        Assert.Empty(_sim.Submitted);
    }

    [Fact]
    public void SimulatorRefusalIsPassedThrough()
    {
        _sim.FailWith = ControlErrors.SimNotConnected;

        var result = Json(PairedSession().Handle(Set("a1", "com1.active", 121_900_000)).Messages[0]);

        Assert.Equal("sim_not_connected", result.GetProperty("error").GetString());
    }

    [Fact]
    public void TwentyFirstCommandInOneSecondIsRateLimited()
    {
        var session = PairedSession();
        for (var i = 0; i < ControlSession.MaxCommandsPerSecond; i++)
            Assert.True(Json(session.Handle(Set($"c{i}", "com1.active", 121_900_000)).Messages[0]).GetProperty("ok").GetBoolean());

        var refused = Json(session.Handle(Set("extra", "com1.active", 121_900_000)).Messages[0]);
        Assert.Equal("rate_limited", refused.GetProperty("error").GetString());

        _now = _now.AddSeconds(1.1);
        Assert.True(Json(session.Handle(Set("later", "com1.active", 121_900_000)).Messages[0]).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void DefaultMonotonicClockAlsoLimitsTheRate()
    {
        // Sem relógio injetado: 21 comandos seguidos cabem folgados em um segundo.
        var session = PairedSession(new ControlSession(_sim, _devices, _codes, "1.5.0"));
        for (var i = 0; i < ControlSession.MaxCommandsPerSecond; i++)
            Assert.True(Json(session.Handle(Set($"c{i}", "com1.active", 121_900_000)).Messages[0]).GetProperty("ok").GetBoolean());

        var refused = Json(session.Handle(Set("extra", "com1.active", 121_900_000)).Messages[0]);
        Assert.Equal("rate_limited", refused.GetProperty("error").GetString());
    }

    [Fact]
    public void PushWaitsUntilTheWelcomeIsSent()
    {
        var session = NewSession();
        session.Handle(Hello);
        session.MarkWelcomeSent();
        Assert.False(session.ReadyForPush);   // antes de parear, marcar não vale

        session.Handle($$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        Assert.Equal(SessionPhase.Paired, session.Phase);
        Assert.False(session.ReadyForPush);   // paired/welcome ainda não foram para a rede

        session.MarkWelcomeSent();
        Assert.True(session.ReadyForPush);
    }

    [Fact]
    public void PushWaitsForTheWelcomeAlsoWithAToken()
    {
        var token = _devices.Pair("ipad-1", "iPad de teste");
        var session = NewSession();

        session.Handle($$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");
        Assert.False(session.ReadyForPush);

        session.MarkWelcomeSent();
        Assert.True(session.ReadyForPush);
    }

    [Fact]
    public void MoreThanOneHundredRefusalsInTenSecondsCloses4008()
    {
        var session = PairedSession();
        SessionOutput output = SessionOutput.None;
        var sent = 0;
        while (output.CloseCode is null && sent < 1000)
        {
            output = session.Handle(Set($"c{sent}", "com1.active", 121_900_000));
            sent++;
        }

        Assert.Equal(ControlSession.CloseRateLimited, output.CloseCode);
        Assert.Equal(ControlSession.MaxCommandsPerSecond + ControlSession.MaxRefusalsPer10Seconds + 1, sent);
    }

    [Fact]
    public void StateSequenceGrows()
    {
        var session = PairedSession();
        var first = Json(session.StateMessage()).GetProperty("seq").GetInt64();
        var second = Json(session.StateMessage()).GetProperty("seq").GetInt64();

        Assert.True(second > first);
    }

    [Fact]
    public void ControlsMessageFollowsTheAircraft()
    {
        var session = PairedSession();
        _sim.AvailableControls = ["xpdr.code"];

        var controls = Json(session.ControlsMessage()).GetProperty("controls").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(["xpdr.code"], controls);
    }
}
```

- [ ] **Passo 3: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `ControlSession` não existe.

- [ ] **Passo 4: Implementar**

`src/TwoG.Connector.Core/ControlSession.cs`:

```csharp
using System.Diagnostics;

namespace TwoG.Connector.Core;

public enum SessionPhase
{
    AwaitingHello,
    Unpaired,
    Paired,
}

/// <summary>O que enviar em resposta a uma mensagem, e se a conexão deve fechar.</summary>
public sealed record SessionOutput(IReadOnlyList<string> Messages, int? CloseCode = null, string? CloseReason = null)
{
    public static readonly SessionOutput None = new([]);
}

/// <summary>
/// Máquina de estados de UMA conexão (spec 01, "Conexão" e "Pareamento"): aguardando
/// hello → não pareada → pareada. Não conhece socket: recebe texto e devolve mensagens.
/// Todo comando é validado aqui (catálogo e controles da aeronave) antes de chegar ao
/// simulador. <see cref="Handle"/> roda só no laço de recepção da conexão;
/// <see cref="StateMessage"/> e <see cref="ControlsMessage"/> podem vir do laço de envio.
/// </summary>
public sealed class ControlSession
{
    public const int MaxCommandsPerSecond = 20;
    public const int MaxRefusalsPer10Seconds = 100;
    public const int CloseProtocolUnsupported = 4002;
    public const int CloseRateLimited = 4008;

    private readonly ISimControl _control;
    private readonly PairedDeviceStore _devices;
    private readonly PairingCodes _codes;
    private readonly string _connectorVersion;
    private static readonly long ClockOrigin = Stopwatch.GetTimestamp();

    private readonly Func<DateTime> _now;
    private readonly Queue<DateTime> _recentCommands = new();
    private readonly Queue<DateTime> _recentRefusals = new();
    private long _stateSeq;
    private volatile SessionPhase _phase = SessionPhase.AwaitingHello;
    private volatile bool _welcomeSent;
    private string? _helloDeviceId;
    private string? _helloDeviceName;

    /// <param name="utcNow">
    /// Relógio das janelas de taxa, só para os testes. O padrão é monotônico e não a hora do
    /// sistema: ela pode recuar (acerto do W32Time, volta do modo de espera) e, como a fila
    /// só é podada pela cabeça, travaria todo comando em rate_limited até alcançar o salto.
    /// </param>
    public ControlSession(ISimControl control, PairedDeviceStore devices, PairingCodes codes,
                          string connectorVersion, Func<DateTime>? utcNow = null)
    {
        _control = control;
        _devices = devices;
        _codes = codes;
        _connectorVersion = connectorVersion;
        _now = utcNow ?? MonotonicNow;
    }

    public SessionPhase Phase => _phase;

    /// <summary>
    /// Pareada E com o pacote de boas-vindas já enviado (<see cref="MarkWelcomeSent"/>).
    /// É o que libera o laço de envio: <see cref="Phase"/> vira Paired dentro do
    /// <see cref="Handle"/>, antes de paired/welcome irem para a rede, e um controls ou
    /// state mandado nessa janela chegaria ao app fora da ordem do spec 01.
    /// </summary>
    public bool ReadyForPush => _welcomeSent;

    /// <summary>Aparelho pareado desta conexão; null antes do pareamento.</summary>
    public string? DeviceId { get; private set; }

    public string? DeviceName { get; private set; }

    /// <summary>Resumo do último comando, para o Diagnóstico ("com1.standby = 118500000 → ok").</summary>
    public string? LastCommand { get; private set; }

    public SessionOutput Handle(string json)
    {
        if (!ControlProtocol.TryParse(json, out var message))
            return Send(ControlProtocol.Error(ControlErrors.InvalidMessage));

        return (_phase, message) switch
        {
            (_, UnknownMessage) => SessionOutput.None,
            (SessionPhase.AwaitingHello, HelloMessage hello) => OnHello(hello),
            (SessionPhase.Unpaired, PairMessage pair) => OnPair(pair),
            (SessionPhase.Unpaired, CommandMessage command) =>
                Send(ControlProtocol.Result(ControlResult.Fail(command.Command.Id, ControlErrors.NotPaired))),
            (SessionPhase.Paired, CommandMessage command) => OnCommand(command.Command),
            _ => Send(ControlProtocol.Error(ControlErrors.InvalidMessage)),
        };
    }

    public string StateMessage() =>
        ControlProtocol.State(Interlocked.Increment(ref _stateSeq), _control.SimulatorName, _control.State);

    public string ControlsMessage() => ControlProtocol.Controls(_control.AvailableControls);

    /// <summary>O servidor chama depois de enviar TODAS as mensagens do Handle que pareou.</summary>
    public void MarkWelcomeSent()
    {
        if (_phase == SessionPhase.Paired)
            _welcomeSent = true;
    }

    private SessionOutput OnHello(HelloMessage hello)
    {
        if (hello.Protocol != ControlProtocol.Version)
            return new SessionOutput(
                [ControlProtocol.Error(ControlErrors.ProtocolUnsupported)],
                CloseProtocolUnsupported, "protocolo não suportado");

        // O token identifica o aparelho; o id do hello só vale para um pareamento novo.
        if (hello.Token is not null && _devices.Authenticate(hello.Token) is { } device)
            return BecomePaired(device.Id, device.Name, prefix: null);

        _helloDeviceId = hello.DeviceId;
        _helloDeviceName = hello.DeviceName;
        _phase = SessionPhase.Unpaired;
        return Send(ControlProtocol.PairingRequired());
    }

    private SessionOutput OnPair(PairMessage pair)
    {
        switch (_codes.TryConsume(pair.Code))
        {
            case PairingAttempt.Accepted:
                var token = _devices.Pair(_helloDeviceId!, _helloDeviceName!);
                return BecomePaired(_helloDeviceId!, _helloDeviceName!, prefix: ControlProtocol.Paired(token));
            case PairingAttempt.Invalid:
                return Send(ControlProtocol.Error(ControlErrors.PairingInvalid));
            default:
                return Send(ControlProtocol.Error(ControlErrors.PairingLocked));
        }
    }

    private SessionOutput BecomePaired(string deviceId, string deviceName, string? prefix)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        _phase = SessionPhase.Paired;

        var messages = new List<string>(4);
        if (prefix is not null)
            messages.Add(prefix);
        messages.Add(ControlProtocol.Welcome(_connectorVersion, _control.SimulatorName));
        messages.Add(ControlsMessage());
        messages.Add(StateMessage());
        return new SessionOutput(messages);
    }

    private SessionOutput OnCommand(ControlCommand command)
    {
        var now = _now();
        Prune(_recentCommands, now - TimeSpan.FromSeconds(1));

        if (_recentCommands.Count >= MaxCommandsPerSecond)
        {
            Prune(_recentRefusals, now - TimeSpan.FromSeconds(10));
            _recentRefusals.Enqueue(now);
            var refused = ControlProtocol.Result(ControlResult.Fail(command.Id, ControlErrors.RateLimited));
            return _recentRefusals.Count > MaxRefusalsPer10Seconds
                ? new SessionOutput([refused], CloseRateLimited, "excesso de comandos")
                : Send(refused);
        }
        _recentCommands.Enqueue(now);

        var error = RadioCatalog.Validate(command.Control, command.Kind, command.Value);
        if (error is null && !_control.AvailableControls.Contains(command.Control))
            error = ControlErrors.Unsupported;

        var result = error is null ? _control.Submit(command) : ControlResult.Fail(command.Id, error);

        LastCommand = command.Kind == ControlKind.Set
            ? $"{command.Control} = {command.Value} → {result.Error ?? "ok"}"
            : $"{command.Control} → {result.Error ?? "ok"}";
        return Send(ControlProtocol.Result(result));
    }

    private static void Prune(Queue<DateTime> queue, DateTime olderThan)
    {
        while (queue.Count > 0 && queue.Peek() <= olderThan)
            queue.Dequeue();
    }

    /// <summary>Instante monotônico em forma de DateTime: só serve para medir intervalos.</summary>
    private static DateTime MonotonicNow() => DateTime.UnixEpoch + Stopwatch.GetElapsedTime(ClockOrigin);

    private static SessionOutput Send(string message) => new([message]);
}
```

- [ ] **Passo 5: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 6: Commit**

```bash
git add src/TwoG.Connector.Core/ControlSession.cs tests/TwoG.Connector.Core.Tests/FakeSimControl.cs tests/TwoG.Connector.Core.Tests/ControlSessionTests.cs
git commit -m "Sessão do canal de controle: hello, pareamento e comandos

Máquina de estados de uma conexão, sem socket, testável isoladamente.
Token válido pula o pareamento; código certo gera token e já manda
welcome, controls e state. Comando passa pelo catálogo e pela lista de
controles da aeronave antes de chegar ao simulador, com limite de 20 por
segundo e fechamento 4008 em excesso persistente.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 7: Servidor WebSocket

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ControlServer.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ControlServerTests.cs`

**Interfaces:**
- Consome: `ControlSession`, `SessionPhase`, `SessionOutput` (Tarefa 6); `ISimControl` (Tarefa 1); `ControlProtocol`, `ControlErrors`; `PairedDeviceStore` (evento `Removed`); `PairingCodes`.
- Produz:
  - `sealed record ControlServerOptions` com `HelloTimeout` (10 s), `PushInterval` (100 ms), `KeepAliveInterval` (15 s), `KeepAliveTimeout` (30 s)
  - `sealed record ConnectedDevice(string? DeviceId, string? DeviceName, bool Paired)`
  - `sealed class ControlServer(ISimControl control, PairedDeviceStore devices, PairingCodes codes, string connectorVersion, ControlServerOptions? options = null) : IDisposable` com `Path = "/control"`, `MaxConnections = 4`, `MaxMessageBytes = 4096`, `CloseRevoked = 4001`, `CloseBusy = 4003`, `Start(int port)`, `Stop()`, `Port`, `IsRunning`, `LastError`, `LastCommand`, `Connected : IReadOnlyList<ConnectedDevice>`, evento `ConnectionsChanged`

Decisões de implementação, todas vindas do 01:
- **O upgrade HTTP é feito à mão**, lendo os cabeçalhos byte a byte até a linha em branco (limite de 8 KB, prazo de 5 s). O cliente só manda quadros WebSocket depois do `101`, então ler além dos cabeçalhos é impossível. Falhas respondem `405` (não é GET), `404` (outro caminho) e `400` (sem os cabeçalhos de WebSocket).
- **A vaga de conexão é reservada com `Interlocked` antes da sessão.** Com a checagem e a inclusão separadas, duas conexões simultâneas passariam juntas pelo limite.
- **Ping e prazo de pong vêm do próprio `WebSocket`** (`KeepAliveInterval` + `KeepAliveTimeout`), somando os 45 s do 01.
- **O envio de estado é feito por um laço único** em `PushInterval`: quando o `ISimControl` avisa mudança, cada conexão pareada recebe `controls` (se a lista mudou) e `state`. Isso limita a 10 `state` por segundo e junta mudanças próximas. O filtro é `Session.ReadyForPush`, não `Phase == Paired`: a fase muda dentro do `Handle`, antes de `paired`/`welcome` irem para a rede, e o laço de recepção só chama `MarkWelcomeSent()` depois de enviar o pacote todo. Para não perder o que mudou nessa janela: `LastControls` guarda a lista que foi no pacote (não uma recalculada depois), e `_dirty` só é remarcado se o contador `_changeGeneration` andou desde o `Handle` — marcar sempre mandaria um `state` extra a cada pareamento, na frente do `result`/`controls`/`state` que os testes esperam.
- **Um envio de cada vez por conexão** (`SemaphoreSlim`), porque o `WebSocket` não aceita dois `SendAsync` simultâneos.
- **`LastCommand` do servidor só muda quando a mensagem foi um comando.** `ControlSession.LastCommand` guarda o último da sessão; copiar depois de qualquer `Handle` faria um `type` desconhecido, uma mensagem inválida ou um `rate_limited` do aparelho A trazer de volta o comando antigo de A por cima do mais novo de B. O laço guarda a referência antes do `Handle` e só copia se ela mudou (cada comando gera uma string nova na sessão).
- **`Stop()` fecha com 1000 antes de cancelar.** Cada conexão tem um `ReceiveAsync` pendente com o token do servidor, e cancelar uma leitura pendente aborta o `WebSocket`: cancelando primeiro, o quadro de fechamento nunca sairia. Os fechamentos rodam num `Task.Run` (fora do contexto da interface, que é quem chama o `Stop`) e o cancelamento vem quando o laço de recepção de cada conexão recebeu o `Close` do app e a soltou (`Connection.Finished`), ou em 2 s. Cancelar assim que o 1000 é escrito abortaria a leitura antes de a resposta chegar (numa Wi-Fi, um RTT), e o TCP seria solto com ela em trânsito. Nada bloqueia quem chamou. Os laços de accept e de envio saem na hora, porque o listener deixou de ser o atual: num `Stop` seguido de `Start` não há dois laços mandando `state`.
- **O accept só termina com o servidor parado.** Uma `SocketException` qualquer (no Windows, `ConnectionReset` de uma conexão desfeita na fila antes do accept) volta ao laço. Sair deixaria o servidor surdo com `IsRunning` verdadeiro, e o `ControlService.Apply` nunca o reiniciaria.
- **Fechamento por iniciativa do servidor espera a resposta do app** (`CloseAndDrainAsync`: 1009, 4002, 4003, 4008). Descarta o que chegar até o `Close` do app, por no máximo 2 s. Soltar o TCP com bytes não lidos manda RST, e no Windows o RST apaga do lado de lá o quadro que acabou de sair. No `Stop` e na remoção do aparelho o laço de recepção continua lendo e é ele quem recebe a resposta. Um quadro de dados que chegue com o fechamento já enviado (estado `CloseSent`) não passa pelo `Handle` — o `set` de um aparelho removido não pode chegar ao simulador — e o laço passa a descartar até o `Close` do app (`DrainAsync`, 2 s), em vez de sair com o quadro dele em trânsito.
- **`ConnectionsChanged` nunca derruba conexão.** A exceção de um assinante é ignorada (`RaiseConnectionsChanged`), e a inclusão fica dentro do `try/finally` que tira a conexão e devolve a vaga. Sem isso, um assinante com defeito deixaria conexões fantasmas e, depois de 4, `busy` para todos.

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ControlServerTests.cs`:

```csharp
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TwoG.Connector.Core.Tests;

/// <summary>
/// Integração de verdade: o servidor numa porta efêmera e um ClientWebSocket — o mesmo
/// caminho que o URLSessionWebSocketTask do iPad vai percorrer. O simulador é falso.
/// </summary>
public class ControlServerTests : IDisposable
{
    private const string Hello = """{"type":"hello","protocol":1,"app":"2G Pilot","device":{"id":"ipad-1","name":"iPad de teste"}}""";

    private readonly TestTempDir _tmp = new();
    private readonly FakeSimControl _sim = new();
    private readonly PairingCodes _codes = new();
    private readonly PairedDeviceStore _devices;
    private readonly ControlServer _server;

    public ControlServerTests()
    {
        _devices = new PairedDeviceStore(_tmp.Sub("paired.json"));
        _server = new ControlServer(_sim, _devices, _codes, "1.5.0", new ControlServerOptions
        {
            HelloTimeout = TimeSpan.FromMilliseconds(500),
            PushInterval = TimeSpan.FromMilliseconds(20),
        });
        _server.Start(0);
        Assert.True(_server.IsRunning, _server.LastError ?? "servidor não subiu");
    }

    public void Dispose()
    {
        _server.Dispose();
        _tmp.Dispose();
    }

    private static CancellationToken Timeout(int ms = 5000) => new CancellationTokenSource(ms).Token;

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}{ControlServer.Path}"), Timeout());
        return ws;
    }

    private static Task SendAsync(ClientWebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, Timeout());

    private static async Task<JsonElement> ExpectAsync(ClientWebSocket ws, string type)
    {
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(buffer, Timeout());
        Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
        var message = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
        Assert.Equal(type, message.GetProperty("type").GetString());
        return message;
    }

    /// <summary>Espera o fechamento e devolve o código; null se a conexão caiu sem quadro de fechamento.</summary>
    private static async Task<int?> ExpectCloseAsync(ClientWebSocket ws)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, Timeout());
                if (result.MessageType == WebSocketMessageType.Close)
                    return (int?)result.CloseStatus;
            }
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    private async Task<(ClientWebSocket Ws, string Token)> PairAsync()
    {
        var ws = await ConnectAsync();
        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");
        await SendAsync(ws, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        var token = (await ExpectAsync(ws, "paired")).GetProperty("token").GetString()!;
        await ExpectAsync(ws, "welcome");
        await ExpectAsync(ws, "controls");
        await ExpectAsync(ws, "state");
        return (ws, token);
    }

    [Fact]
    public async Task PairingFlowEndToEnd()
    {
        var ws = await ConnectAsync();
        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");

        await SendAsync(ws, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");

        await ExpectAsync(ws, "paired");
        Assert.Equal("1.5.0", (await ExpectAsync(ws, "welcome")).GetProperty("connector").GetString());
        Assert.Contains("com1.active", (await ExpectAsync(ws, "controls")).GetProperty("controls").EnumerateArray().Select(e => e.GetString()));
        var state = await ExpectAsync(ws, "state");
        Assert.Equal(121_900_000, state.GetProperty("radios").GetProperty("com1").GetProperty("active").GetInt64());
    }

    [Fact]
    public async Task ReconnectingWithTheTokenSkipsPairing()
    {
        var (first, token) = await PairAsync();
        first.Abort();

        var ws = await ConnectAsync();
        await SendAsync(ws, $$"""{"type":"hello","protocol":1,"device":{"id":"ipad-1","name":"iPad"},"token":"{{token}}"}""");

        await ExpectAsync(ws, "welcome");
        await ExpectAsync(ws, "controls");
        await ExpectAsync(ws, "state");
    }

    [Fact]
    public async Task SetReachesTheSimulatorAndGetsAResult()
    {
        var (ws, _) = await PairAsync();

        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");

        var result = await ExpectAsync(ws, "result");
        Assert.Equal("a1", result.GetProperty("id").GetString());
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("com1.standby", Assert.Single(_sim.Submitted).Control);
        Assert.Contains("com1.standby", _server.LastCommand);
    }

    [Fact]
    public async Task LastCommandOnlyChangesWhenTheMessageIsACommand()
    {
        var (a, _) = await PairAsync();

        // Segundo aparelho, com id próprio: o PairAsync usa sempre o hello do ipad-1.
        var b = await ConnectAsync();
        await SendAsync(b, """{"type":"hello","protocol":1,"app":"2G Pilot","device":{"id":"ipad-2","name":"iPad B"}}""");
        await ExpectAsync(b, "pairing_required");
        await SendAsync(b, $$"""{"type":"pair","code":"{{_codes.Generate()}}"}""");
        await ExpectAsync(b, "paired");
        await ExpectAsync(b, "welcome");
        await ExpectAsync(b, "controls");
        await ExpectAsync(b, "state");

        await SendAsync(a, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        await ExpectAsync(a, "result");
        await SendAsync(b, """{"type":"set","id":"b1","control":"com2.standby","value":121500000}""");
        await ExpectAsync(b, "result");
        Assert.Contains("com2.standby", _server.LastCommand);

        // O type desconhecido não tem resposta. A mensagem inválida logo atrás tem, e a
        // conexão trata na ordem: quando o error chega, o xyz já passou pelo Handle.
        await SendAsync(a, """{"type":"xyz"}""");
        await SendAsync(a, "{");
        await ExpectAsync(a, "error");

        Assert.Contains("com2.standby", _server.LastCommand);
    }

    [Fact]
    public async Task StateIsPushedWhenTheSimulatorChanges()
    {
        var (ws, _) = await PairAsync();

        _sim.State = _sim.State! with { Com1 = new FrequencyPair(121_900_000, 124_350_000) };
        _sim.RaiseChanged();

        var state = await ExpectAsync(ws, "state");
        Assert.Equal(124_350_000, state.GetProperty("radios").GetProperty("com1").GetProperty("standby").GetInt64());
        Assert.True(state.GetProperty("seq").GetInt64() > 1);
    }

    [Fact]
    public async Task ControlsArePushedWhenTheAircraftChanges()
    {
        var (ws, _) = await PairAsync();

        _sim.AvailableControls = ["com1.active", "com1.standby", "com1.swap"];
        _sim.RaiseChanged();

        var controls = await ExpectAsync(ws, "controls");
        Assert.Equal(3, controls.GetProperty("controls").GetArrayLength());
        await ExpectAsync(ws, "state");
    }

    [Fact]
    public async Task FifthConnectionGetsBusyAndCloses4003()
    {
        // As quatro mandam hello: sem ele, o prazo curto do teste as fecharia antes da quinta.
        var open = new List<ClientWebSocket>();
        for (var i = 0; i < ControlServer.MaxConnections; i++)
        {
            var ws = await ConnectAsync();
            await SendAsync(ws, Hello);
            await ExpectAsync(ws, "pairing_required");
            open.Add(ws);
        }

        var fifth = await ConnectAsync();

        Assert.Equal("busy", (await ExpectAsync(fifth, "error")).GetProperty("code").GetString());
        Assert.Equal(ControlServer.CloseBusy, await ExpectCloseAsync(fifth));
    }

    [Fact]
    public async Task RemovingTheDeviceClosesItsConnectionWith4001()
    {
        var (ws, _) = await PairAsync();

        _devices.Remove("ipad-1");

        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task CommandInFlightAfterRemovalNeverReachesTheSimulator()
    {
        var (ws, _) = await PairAsync();

        _devices.Remove("ipad-1");
        // Sai do app antes de ele ler o 4001: o servidor já fechou e não pode mais executá-lo.
        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        Assert.Equal(ControlServer.CloseRevoked, await ExpectCloseAsync(ws));

        // E continua esperando a resposta do app, em vez de soltar o TCP com o set.
        await Task.Delay(300);
        lock (_sim.Submitted)
            Assert.Empty(_sim.Submitted);
        Assert.Single(_server.Connected);

        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();
    }

    [Fact]
    public async Task StoppingWaitsForTheClientsCloseBeforeReleasingTheConnection()
    {
        var (ws, _) = await PairAsync();

        _server.Stop();
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(ws));

        // O 1000 já saiu, mas o app ainda não respondeu: a leitura não pode ter sido cancelada.
        await Task.Delay(300);
        Assert.Single(_server.Connected);

        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        await WaitForNoConnectionsAsync();
    }

    [Fact]
    public async Task StoppingGivesUpOnASilentClientAfterTwoSeconds()
    {
        var (ws, _) = await PairAsync();

        _server.Stop();
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(ws));

        await WaitForNoConnectionsAsync();   // o app nunca responde: o prazo de 2 s solta a conexão
    }

    private async Task WaitForNoConnectionsAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_server.Connected.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Empty(_server.Connected);
    }

    [Fact]
    public async Task SilentConnectionIsClosedAfterTheHelloTimeout()
    {
        var ws = await ConnectAsync();

        await ExpectCloseAsync(ws);   // não pode ficar pendurada: Timeout() é de 5 s e o prazo do hello é 500 ms

        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task OversizedMessageCloses1009()
    {
        var ws = await ConnectAsync();

        await SendAsync(ws, new string('x', ControlServer.MaxMessageBytes + 100));

        Assert.Equal((int)WebSocketCloseStatus.MessageTooBig, await ExpectCloseAsync(ws));
    }

    [Fact]
    public async Task OversizedMessageKeepsTheTcpOpenUntilTheClientsClose()
    {
        // Em TCP cru para ver o que o servidor faz com o socket. Largar o TCP logo após o
        // quadro 1009, com o resto da mensagem ainda não lido, manda RST no Windows, e o RST
        // pode apagar o quadro no app antes de ele ser lido.
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET /control HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));

        // Quadro de texto mascarado (máscara zero: o payload vai como está), 16 vezes o limite.
        var payload = ControlServer.MaxMessageBytes * 16;
        var frame = new List<byte> { 0x81, 0xFF };
        frame.AddRange(BitConverter.GetBytes((long)payload).Reverse());
        frame.AddRange(new byte[4]);
        frame.AddRange(Enumerable.Repeat((byte)'x', payload));
        await stream.WriteAsync(frame.ToArray(), Timeout());

        // Lê a resposta 101 e o quadro de fechamento inteiro.
        var received = new List<byte>();
        var buffer = new byte[1024];
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(received)) < 0 || received.Count < headerEnd + 2
               || received.Count < headerEnd + 2 + received[headerEnd + 1])
        {
            var read = await stream.ReadAsync(buffer, Timeout());
            Assert.NotEqual(0, read);
            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        Assert.Equal(0x88, received[headerEnd]);
        Assert.Equal((int)WebSocketCloseStatus.MessageTooBig, (received[headerEnd + 2] << 8) | received[headerEnd + 3]);

        // O servidor espera a resposta do app: nada de EOF por enquanto.
        var pending = stream.ReadAsync(buffer).AsTask();
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(300)));

        // Fechamento do cliente (mascarado, código 1000): agora sim o servidor solta o TCP.
        await stream.WriteAsync(new byte[] { 0x88, 0x82, 0, 0, 0, 0, 0x03, 0xE8 }, Timeout());
        Assert.Same(pending, await Task.WhenAny(pending, Task.Delay(5000)));
        Assert.Equal(0, await pending);
    }

    private static int IndexOfHeaderEnd(List<byte> bytes)
    {
        for (var i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                return i + 1;
        }
        return -1;
    }

    [Fact]
    public async Task InvalidJsonKeepsTheConnectionOpen()
    {
        var ws = await ConnectAsync();

        await SendAsync(ws, "{");
        Assert.Equal("invalid_message", (await ExpectAsync(ws, "error")).GetProperty("code").GetString());

        await SendAsync(ws, Hello);
        await ExpectAsync(ws, "pairing_required");
    }

    [Theory]
    [InlineData("GET /control HTTP/1.1\r\nHost: x\r\n\r\n", "400")]
    [InlineData("GET /outra HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\n\r\n", "404")]
    [InlineData("POST /control HTTP/1.1\r\nHost: x\r\n\r\n", "405")]
    public async Task NonWebSocketRequestsAreRejected(string request, string status)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, Timeout());

        Assert.StartsWith($"HTTP/1.1 {status}", Encoding.ASCII.GetString(buffer, 0, read));
    }

    [Fact]
    public async Task StoppingClosesOpenConnectionsWith1000()
    {
        var (paired, _) = await PairAsync();
        var waitingHello = await ConnectAsync();

        _server.Stop();

        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(paired));
        Assert.Equal((int)WebSocketCloseStatus.NormalClosure, await ExpectCloseAsync(waitingHello));
    }

    [Fact]
    public async Task ThrowingSubscriberDoesNotLeakTheConnection()
    {
        _server.ConnectionsChanged += () => throw new InvalidOperationException("assinante com defeito");

        var (ws, _) = await PairAsync();   // o aviso do pareamento também não pode derrubar a conexão
        await SendAsync(ws, """{"type":"set","id":"a1","control":"com1.standby","value":118500000}""");
        await ExpectAsync(ws, "result");

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_server.Connected.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Empty(_server.Connected);
    }

    [Fact]
    public async Task ConnectedListShowsThePairedDevice()
    {
        await PairAsync();

        var device = Assert.Single(_server.Connected);
        Assert.Equal("iPad de teste", device.DeviceName);
        Assert.True(device.Paired);
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `ControlServer` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/ControlServer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core;

public sealed record ControlServerOptions
{
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PushInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record ConnectedDevice(string? DeviceId, string? DeviceName, bool Paired);

/// <summary>
/// Servidor do canal de controle (spec 01): WebSocket sobre um TcpListener, com o
/// upgrade HTTP feito à mão. Pelo mesmo motivo do FlightPlanServer, nada de HttpListener:
/// escutar em todas as interfaces exigiria reserva de URL ou administrador.
/// Uma falha aqui nunca derruba o app nem afeta o XGPS: tudo roda em tarefas próprias e
/// só fala com o simulador pelo <see cref="ISimControl"/>.
/// </summary>
public sealed class ControlServer : IDisposable
{
    public const string Path = "/control";
    public const int MaxConnections = 4;
    public const int MaxMessageBytes = 4096;
    public const int CloseRevoked = 4001;
    public const int CloseBusy = 4003;

    private const int MaxRequestBytes = 8192;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly ISimControl _control;
    private readonly PairedDeviceStore _devices;
    private readonly PairingCodes _codes;
    private readonly string _connectorVersion;
    private readonly ControlServerOptions _options;
    private readonly ConcurrentDictionary<Connection, byte> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private int _slots;
    private volatile bool _dirty;
    private long _changeGeneration;   // conta os avisos do simulador; ver o pareamento no laço de recepção

    public ControlServer(ISimControl control, PairedDeviceStore devices, PairingCodes codes,
                         string connectorVersion, ControlServerOptions? options = null)
    {
        _control = control;
        _devices = devices;
        _codes = codes;
        _connectorVersion = connectorVersion;
        _options = options ?? new ControlServerOptions();
        _control.Changed += () =>
        {
            Interlocked.Increment(ref _changeGeneration);
            _dirty = true;
        };
        _devices.Removed += OnDeviceRemoved;
    }

    public int Port { get; private set; }

    public bool IsRunning => _listener is not null;

    /// <summary>Erro que impediu o servidor de subir (porta ocupada), ou null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Resumo do último comando recebido de qualquer aparelho, para o Diagnóstico.</summary>
    public string? LastCommand { get; private set; }

    public IReadOnlyList<ConnectedDevice> Connected =>
        _connections.Keys
            .Select(c => new ConnectedDevice(c.Session.DeviceId, c.Session.DeviceName, c.Session.Phase == SessionPhase.Paired))
            .ToArray();

    /// <summary>
    /// Uma conexão abriu, fechou ou pareou. Disparado de threads do pool, nunca da interface;
    /// uma exceção do assinante é ignorada e não afeta a conexão.
    /// </summary>
    public event Action? ConnectionsChanged;

    public void Start(int port)
    {
        Stop();
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            LastError = null;
            _ = AcceptLoopAsync(listener, _cancellation.Token);
            _ = PushLoopAsync(listener, _cancellation.Token);
        }
        catch (SocketException ex)
        {
            LastError = $"Porta {port} indisponível: {ex.Message}";
            _listener = null;
        }
    }

    public void Stop()
    {
        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Já parado.
        }
        _listener = null;

        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null)
            return;

        // Fecha com 1000 ANTES de cancelar: cada conexão tem uma leitura pendente com este
        // token, e cancelar uma leitura aborta o WebSocket — o quadro de fechamento não sairia,
        // nem a resposta do app seria lida (soltar o TCP com ela em trânsito manda RST).
        // Nada espera aqui (quem chama é a thread da interface): o Task.Run tira os fechamentos
        // do contexto dela, e o cancelamento vem quando o laço de recepção de cada uma recebeu
        // o Close do app e a soltou, ou em 2 s, o que vier antes (um app que não responde, ou
        // um envio travado, que segura o lock de envio até o cancelamento).
        var connections = _connections.Keys.ToArray();
        var closing = Task.Run(async () =>
        {
            await Task.WhenAll(connections.Select(
                c => c.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connector encerrando")));
            await Task.WhenAll(connections.Select(c => c.Finished));
        });
        _ = Task.WhenAny(closing, Task.Delay(CloseTimeout)).ContinueWith(_ =>
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    public void Dispose() => Stop();

    private void OnDeviceRemoved(string deviceId)
    {
        foreach (var connection in _connections.Keys.Where(c => c.Session.DeviceId == deviceId))
            _ = connection.CloseAsync((WebSocketCloseStatus)CloseRevoked, "aparelho removido");
    }

    private void RaiseConnectionsChanged()
    {
        try
        {
            ConnectionsChanged?.Invoke();
        }
        catch (Exception)
        {
            // Defeito do assinante não pode deixar conexão fantasma nem vaga presa.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.OperationAborted
                                             && !token.IsCancellationRequested
                                             && ReferenceEquals(_listener, listener))
            {
                // Conexão da fila desfeita antes do accept (no Windows, ConnectionReset): um
                // iPad que desistiu no meio do connect. Sair aqui deixaria o servidor surdo com
                // IsRunning verdadeiro, e nada o reiniciaria.
                continue;
            }
            catch (Exception)
            {
                return;   // servidor parado
            }
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            var reserved = false;
            try
            {
                var stream = client.GetStream();
                var key = await ReadUpgradeAsync(stream, token);
                if (key is null)
                    return;

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\n"
                    + "Connection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\n\r\n"), token);

                using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
                {
                    IsServer = true,
                    KeepAliveInterval = _options.KeepAliveInterval,
                    KeepAliveTimeout = _options.KeepAliveTimeout,
                });
                var connection = new Connection(socket, new ControlSession(_control, _devices, _codes, _connectorVersion));

                // Reserva a vaga ANTES de aceitar: checar e incluir separado deixaria duas
                // conexões simultâneas passarem juntas pelo limite.
                reserved = Interlocked.Increment(ref _slots) <= MaxConnections;
                if (!reserved)
                {
                    Interlocked.Decrement(ref _slots);
                    await connection.SendAsync(ControlProtocol.Error(ControlErrors.Busy), token);
                    await connection.CloseAndDrainAsync((WebSocketCloseStatus)CloseBusy, "conexões esgotadas");
                    return;
                }

                try
                {
                    _connections.TryAdd(connection, 0);
                    RaiseConnectionsChanged();
                    await ReceiveLoopAsync(connection, token);
                }
                finally
                {
                    _connections.TryRemove(connection, out _);
                    Interlocked.Decrement(ref _slots);
                    RaiseConnectionsChanged();
                    connection.MarkFinished();
                }
            }
            catch (Exception)
            {
                // Cliente sumiu no meio: nada a fazer além de liberar a vaga (finally acima).
            }
        }
    }

    /// <summary>
    /// Lê o pedido HTTP de upgrade e devolve o Sec-WebSocket-Key. Pedido inválido já recebe a
    /// resposta de erro aqui, e o retorno é null.
    /// </summary>
    private static async Task<string?> ReadUpgradeAsync(NetworkStream stream, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        var bytes = new List<byte>(512);
        var one = new byte[1];
        while (bytes.Count < MaxRequestBytes)
        {
            if (await stream.ReadAsync(one, deadline.Token) == 0)
                return null;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                break;
        }

        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var method = requestLine.Length > 0 ? requestLine[0] : "";
        var target = requestLine.Length > 1 ? requestLine[1].Split('?')[0] : "";

        string? error =
            !method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "405 Method Not Allowed"
            : !target.Equals(Path, StringComparison.Ordinal) ? "404 Not Found"
            : !headers.TryGetValue("Upgrade", out var upgrade) || !upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase)
              || !headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13"
              || !headers.ContainsKey("Sec-WebSocket-Key") ? "400 Bad Request"
            : null;

        if (error is not null)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {error}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
            return null;
        }
        return headers["Sec-WebSocket-Key"];
    }

    private async Task ReceiveLoopAsync(Connection connection, CancellationToken token)
    {
        var buffer = new byte[MaxMessageBytes];
        using var helloDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        helloDeadline.CancelAfter(_options.HelloTimeout);

        while (connection.Socket.State == WebSocketState.Open)
        {
            var receiveToken = connection.Session.Phase == SessionPhase.AwaitingHello ? helloDeadline.Token : token;
            var count = 0;
            ValueWebSocketReceiveResult received;
            try
            {
                do
                {
                    received = await connection.Socket.ReceiveAsync(buffer.AsMemory(count), receiveToken);
                    count += received.Count;
                    if (!received.EndOfMessage && count == buffer.Length)
                    {
                        await connection.CloseAndDrainAsync(WebSocketCloseStatus.MessageTooBig, "mensagem maior que 4 KB");
                        return;
                    }
                }
                while (!received.EndOfMessage);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Prazo do hello: cancelar a leitura já aborta o WebSocket.
                return;
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "");
                return;
            }

            if (connection.Socket.State != WebSocketState.Open)
            {
                // O servidor já mandou o fechamento (Stop, aparelho removido) e este quadro saiu
                // do app antes de ele o ver: não vira comando, senão o set de um aparelho removido
                // chegaria ao simulador. Descarta até o Close do app, por no máximo 2 s.
                await connection.DrainAsync();
                return;
            }

            if (received.MessageType != WebSocketMessageType.Text)
            {
                await connection.SendAsync(ControlProtocol.Error(ControlErrors.InvalidMessage), token);
                continue;
            }

            var wasPaired = connection.Session.Phase == SessionPhase.Paired;
            var generation = Interlocked.Read(ref _changeGeneration);
            var lastBefore = connection.Session.LastCommand;
            var output = connection.Session.Handle(Encoding.UTF8.GetString(buffer, 0, count));
            // Antes de enviar: quem recebe o result já pode ler o LastCommand.
            // Só copia se ESTA mensagem foi um comando: a sessão guarda o último dela, e um
            // type desconhecido, uma mensagem inválida ou um rate_limited deste aparelho
            // trariam de volta o comando antigo dele por cima do mais novo de outro aparelho.
            // Cada comando gera uma string nova na sessão, então a referência basta.
            if (connection.Session.LastCommand is { } last && !ReferenceEquals(last, lastBefore))
                LastCommand = last;

            foreach (var message in output.Messages)
                await connection.SendAsync(message, token);

            if (!wasPaired && connection.Session.Phase == SessionPhase.Paired)
            {
                // O app tem a lista que foi no pacote de boas-vindas, não a de agora: se a
                // aeronave mudou depois do Handle, o laço de envio vê a diferença e manda a nova.
                connection.LastControls = output.Messages.First(
                    m => m.StartsWith("""{"type":"controls",""", StringComparison.Ordinal));
                // Só agora o laço de envio fala com este aparelho: antes, controls e state
                // chegariam na frente de paired/welcome. Se o simulador avisou mudança desde
                // o Handle, o ciclo que a tratou pode ter pulado esta conexão: marca de novo.
                // Sem mudança nada é marcado, e nenhum state extra vai para a rede.
                connection.Session.MarkWelcomeSent();
                if (Interlocked.Read(ref _changeGeneration) != generation)
                    _dirty = true;
                RaiseConnectionsChanged();
            }

            if (output.CloseCode is int code)
            {
                await connection.CloseAndDrainAsync((WebSocketCloseStatus)code, output.CloseReason ?? "");
                return;
            }
        }
    }

    /// <summary>
    /// Laço único de envio: a cada <see cref="ControlServerOptions.PushInterval"/>, se o
    /// simulador avisou mudança, manda a lista de controles (se mudou) e o estado para cada
    /// aparelho pareado. Mudanças próximas viram um só state: no máximo 10 por segundo.
    /// </summary>
    private async Task PushLoopAsync(TcpListener listener, CancellationToken token)
    {
        using var timer = new PeriodicTimer(_options.PushInterval);
        try
        {
            // Sai assim que o servidor para, sem esperar o cancelamento (que o Stop adia até os
            // fechamentos): num Stop seguido de Start, dois laços mandariam state em dobro.
            while (await timer.WaitForNextTickAsync(token) && ReferenceEquals(_listener, listener))
            {
                if (!_dirty)
                    continue;
                _dirty = false;

                foreach (var connection in _connections.Keys.Where(c => c.Session.ReadyForPush))
                {
                    var controls = connection.Session.ControlsMessage();
                    if (controls != connection.LastControls)
                    {
                        connection.LastControls = controls;
                        await connection.SendAsync(controls, token);
                    }
                    await connection.SendAsync(connection.Session.StateMessage(), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Servidor parado.
        }
    }

    private sealed class Connection(WebSocket socket, ControlSession session)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WebSocket Socket { get; } = socket;

        public ControlSession Session { get; } = session;

        /// <summary>Última lista de controles enviada a este aparelho.</summary>
        public string? LastControls { get; set; }

        /// <summary>Completa quando o laço de recepção terminou e a conexão saiu da lista.</summary>
        public Task Finished => _finished.Task;

        public void MarkFinished() => _finished.TrySetResult();

        public async Task SendAsync(string json, CancellationToken token)
        {
            try
            {
                await _sendLock.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // A conexão caiu; o laço de recepção dela vai perceber e encerrar.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Manda o quadro de fechamento sem esperar a resposta. Serve quando o laço de recepção
        /// continua lendo (Stop, aparelho removido) e é ele quem recebe a resposta do app: um
        /// quadro de dados que chegue antes dela é descartado (<see cref="DrainAsync"/>).
        /// </summary>
        public async Task CloseAsync(WebSocketCloseStatus status, string reason)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(CloseTimeout);
                    await Socket.CloseOutputAsync(status, reason, timeout.Token);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Já fechada ou abortada.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Fecha por iniciativa do servidor quando ninguém mais vai ler a conexão (1009, 4002,
        /// 4003, 4008): manda o quadro e descarta o que chegar até o fechamento do app, por no
        /// máximo 2 s. Fechar o TCP com bytes não lidos (o resto da mensagem grande, um hello já
        /// enviado) manda RST, e o RST pode fazer o app perder o código logo antes dele.
        /// </summary>
        public async Task CloseAndDrainAsync(WebSocketCloseStatus status, string reason)
        {
            await CloseAsync(status, reason);
            await DrainAsync();
        }

        /// <summary>Com o fechamento já enviado, descarta o que chegar até o Close do app, por no máximo 2 s.</summary>
        public async Task DrainAsync()
        {
            var discard = new byte[1024];
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                while (Socket.State == WebSocketState.CloseSent)
                {
                    var received = await Socket.ReceiveAsync(discard.AsMemory(), timeout.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (Exception)
            {
                // Prazo esgotado ou conexão caída: não há mais o que esperar.
            }
        }
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS. Se `SilentConnectionIsClosedAfterTheHelloTimeout` demorar mais que 5 s, o cancelamento do `ReceiveAsync` não está abortando o WebSocket — confira que o token passado é o `helloDeadline.Token` enquanto a fase é `AwaitingHello`.

- [ ] **Passo 5: Rodar a suíte três vezes seguidas**

Run: `for i in 1 2 3; do dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj --nologo 2>&1 | tail -1; done`
Expected: as três passam. Testes de rede instáveis são piores que nenhum; se algum falhar de vez em quando, a causa vem antes do commit.

- [ ] **Passo 6: Commit**

```bash
git add src/TwoG.Connector.Core/ControlServer.cs tests/TwoG.Connector.Core.Tests/ControlServerTests.cs
git commit -m "Servidor WebSocket do canal de controle

TcpListener com upgrade HTTP feito à mão e WebSocket.CreateFromStream,
sem HttpListener nem admin. Limite de 4 conexões com vaga reservada de
forma atômica, mensagem de até 4 KB, prazo do hello, ping e pong pelo
próprio WebSocket, estado agregado a no máximo 10 por segundo e
fechamento 4001 quando o aparelho é removido. Testado de ponta a ponta
com um ClientWebSocket real.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 8: Anúncio `2GCTL` com o IP certo por destino

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ControlAnnouncement.cs`
- Modificar: `src/TwoG.Connector.Core/NetworkMath.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ControlAnnouncementTests.cs`
- Modificar: `src/TwoG.Connector/Services/IXgpsBroadcaster.cs`, `src/TwoG.Connector/Services/XgpsBroadcaster.cs`
- Criar: `src/TwoG.Connector/Services/ControlAnnouncer.cs`

**Interfaces:**
- Consome: `ControlServer.Path` (Tarefa 7), `ControlProtocol.Version` (Tarefa 3), `XgpsSentences.SanitizeDeviceName` (existente).
- Produz:
  - `NetworkMath.SourceAddressFor(IPAddress destination, IEnumerable<(IPAddress Address, IPAddress Mask)> interfaces) : IPAddress?`
  - `ControlAnnouncement.Prefix = "2GCTL"`, `Url(IPAddress host, int port) : string`, `Sentence(string deviceName, string url) : string`, `SentenceFor(IPAddress destination, string deviceName, int port, IReadOnlyList<(IPAddress Address, IPAddress Mask)> interfaces, Func<IPAddress, IPAddress?> routeSource) : string?`
  - `IXgpsBroadcaster.SendToEach(Func<IPEndPoint, string?> sentenceFor)` — sentença própria por destino; `null` pula o destino
  - `ControlAnnouncer(IXgpsBroadcaster broadcaster, Func<string> deviceName, Func<int> port)` com `Start()`, `Stop()`, `Dispose()`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ControlAnnouncementTests.cs`:

```csharp
using System.Net;

namespace TwoG.Connector.Core.Tests;

public class ControlAnnouncementTests
{
    private static readonly (IPAddress Address, IPAddress Mask)[] WifiAndEthernet =
    [
        (IPAddress.Parse("192.168.68.102"), IPAddress.Parse("255.255.255.0")),
        (IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.0.0.0")),
    ];

    private static IPAddress? NoRoute(IPAddress _) => null;

    [Theory]
    [InlineData("192.168.68.255", "192.168.68.102")]   // broadcast dirigido da Wi-Fi
    [InlineData("192.168.68.101", "192.168.68.102")]   // iPad na Wi-Fi, em unicast
    [InlineData("10.255.255.255", "10.0.0.5")]         // broadcast da Ethernet
    public void SourceIsTheInterfaceOfTheDestinationSubnet(string destination, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), NetworkMath.SourceAddressFor(IPAddress.Parse(destination), WifiAndEthernet));
    }

    [Fact]
    public void DestinationOutsideEverySubnetHasNoInterface()
    {
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.Parse("172.16.0.9"), WifiAndEthernet));
    }

    [Fact]
    public void ZeroMaskAndIpv6AreIgnored()
    {
        (IPAddress, IPAddress)[] odd = [(IPAddress.Parse("192.168.1.2"), IPAddress.Any), (IPAddress.IPv6Loopback, IPAddress.Parse("255.255.255.0"))];
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.Parse("192.168.1.9"), odd));
        Assert.Null(NetworkMath.SourceAddressFor(IPAddress.IPv6Loopback, WifiAndEthernet));
    }

    [Fact]
    public void SentenceFollowsTheSpec()
    {
        var url = ControlAnnouncement.Url(IPAddress.Parse("192.168.68.102"), 49004);

        Assert.Equal("ws://192.168.68.102:49004/control", url);
        Assert.Equal("2GCTL2G Connector,1,ws://192.168.68.102:49004/control", ControlAnnouncement.Sentence("2G Connector", url));
    }

    [Fact]
    public void CommaInTheDeviceNameCannotBreakTheSentence()
    {
        Assert.StartsWith("2GCTLMeu PC 1,1,", ControlAnnouncement.Sentence("Meu PC, 1", "ws://h:1/control"));
    }

    [Fact]
    public void SentenceForUsesTheInterfaceOfTheDestination()
    {
        var sentence = ControlAnnouncement.SentenceFor(IPAddress.Parse("10.255.255.255"), "2G Connector", 49004, WifiAndEthernet, NoRoute);
        Assert.Equal("2GCTL2G Connector,1,ws://10.0.0.5:49004/control", sentence);
    }

    [Fact]
    public void SentenceForFallsBackToTheRoute()
    {
        var sentence = ControlAnnouncement.SentenceFor(IPAddress.Parse("172.16.0.9"), "X", 49004, WifiAndEthernet,
            _ => IPAddress.Parse("172.16.0.1"));
        Assert.Equal("2GCTLX,1,ws://172.16.0.1:49004/control", sentence);
    }

    [Fact]
    public void WithoutInterfaceOrRouteThereIsNoSentence()
    {
        Assert.Null(ControlAnnouncement.SentenceFor(IPAddress.Parse("172.16.0.9"), "X", 49004, WifiAndEthernet, NoRoute));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `SourceAddressFor` e `ControlAnnouncement` não existem.

- [ ] **Passo 3: Implementar no Core**

Em `src/TwoG.Connector.Core/NetworkMath.cs`, dentro da classe, depois de `DirectedBroadcast`:

```csharp
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
```

`src/TwoG.Connector.Core/ControlAnnouncement.cs`:

```csharp
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
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Sentença por destino no broadcaster (app)**

Em `src/TwoG.Connector/Services/IXgpsBroadcaster.cs`, depois de `SendNow`:

```csharp
    /// <summary>
    /// Envia a cada destino atual uma sentença própria, montada por <paramref name="sentenceFor"/>.
    /// Destino para o qual a função devolve null é pulado. Usado pelo anúncio 2GCTL, cujo
    /// conteúdo depende da interface de saída.
    /// </summary>
    void SendToEach(Func<IPEndPoint, string?> sentenceFor);
```

(acrescentar `using System.Net;` no topo do arquivo.)

Em `src/TwoG.Connector/Services/XgpsBroadcaster.cs`, substituir o método `SendToAll` inteiro por estes dois — o envio a todos passa a ser o caso particular do envio por destino:

```csharp
    private void SendToAll(string sentence) => SendToEach(_ => sentence);

    public void SendToEach(Func<IPEndPoint, string?> sentenceFor)
    {
        var udp = _udp;
        if (udp is null)
            return;

        foreach (var endpoint in AllEndpoints())
        {
            var sentence = sentenceFor(endpoint);
            if (sentence is null)
                continue;

            var payload = Encoding.ASCII.GetBytes(sentence);
            try
            {
                udp.Send(payload, payload.Length, endpoint);
                Interlocked.Increment(ref _packetsSent);
                Interlocked.Exchange(ref _lastSendTicks, DateTime.UtcNow.Ticks);
            }
            catch (SocketException ex)
            {
                // Interface pode ter caído entre o refresh e o envio; próximo refresh corrige.
                // Registrado para o painel de diagnóstico: falha silenciosa aqui era
                // indistinguível de "enviado e a rede engoliu".
                Interlocked.Increment(ref _sendFailures);
                _lastSendError = $"{endpoint}: {ex.SocketErrorCode} ({ex.Message})";
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }
```

- [ ] **Passo 6: O anunciador (app)**

`src/TwoG.Connector/Services/ControlAnnouncer.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Emite o anúncio 2GCTL a cada 5 s pelos mesmos destinos do XGPS (broadcast dirigido de
/// cada interface e EFBs descobertos), com o host da URL certo para cada um.
/// Melhor esforço: nenhuma falha aqui interrompe o XGPS.
/// </summary>
internal sealed class ControlAnnouncer : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly IXgpsBroadcaster _broadcaster;
    private readonly Func<string> _deviceName;
    private readonly Func<int> _port;
    private Timer? _timer;

    public ControlAnnouncer(IXgpsBroadcaster broadcaster, Func<string> deviceName, Func<int> port)
    {
        _broadcaster = broadcaster;
        _deviceName = deviceName;
        _port = port;
    }

    public void Start() => _timer ??= new Timer(_ => Announce(), null, TimeSpan.Zero, Interval);

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private void Announce()
    {
        try
        {
            var interfaces = LocalInterfaces();
            var name = _deviceName();
            var port = _port();
            _broadcaster.SendToEach(endpoint =>
                ControlAnnouncement.SentenceFor(endpoint.Address, name, port, interfaces, RouteSource));
        }
        catch (Exception)
        {
            // Anúncio é melhor esforço; o próximo tique tenta de novo.
        }
    }

    private static IReadOnlyList<(IPAddress Address, IPAddress Mask)> LocalInterfaces()
    {
        var list = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    list.Add((unicast.Address, unicast.IPv4Mask));
            }
        }
        return list;
    }

    /// <summary>IP de saída que o Windows escolheria para o destino. Não envia nada.</summary>
    private static IPAddress? RouteSource(IPAddress destination)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(destination, 9);
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
```

- [ ] **Passo 7: Compilar**

```bash
dotnet build src/TwoG.Connector/TwoG.Connector.csproj
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
```

Expected: build sem avisos, testes passando. O anunciador só entra em funcionamento na Tarefa 10.

- [ ] **Passo 8: Commit**

```bash
git add src/TwoG.Connector.Core/ControlAnnouncement.cs src/TwoG.Connector.Core/NetworkMath.cs tests/TwoG.Connector.Core.Tests/ControlAnnouncementTests.cs src/TwoG.Connector/Services/IXgpsBroadcaster.cs src/TwoG.Connector/Services/XgpsBroadcaster.cs src/TwoG.Connector/Services/ControlAnnouncer.cs
git commit -m "Anúncio 2GCTL com o IP da interface de cada destino

O host da URL é o IP da interface cuja sub-rede contém o destino, com a
rota do sistema como reserva: num PC com Wi-Fi e Ethernet, cada interface
anuncia o próprio endereço. O broadcaster ganha o envio de uma sentença
por destino, e o envio a todos passa a ser o caso particular dele.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 9: Rádios pelo SimConnect

Código que só roda no Windows. Localmente o critério é compilar sem avisos; a prova é o roteiro V1–V10 do spec 02, na Tarefa 11. As chamadas SimConnect abaixo foram compiladas contra a DLL vendorizada ao escrever este plano — em especial, `GetLastSentPacketID` **devolve** o ID (`uint GetLastSentPacketID()`), não usa parâmetro `out`.

**Arquivos:**
- Criar: `src/TwoG.Connector/Services/SimConnectRadios.cs`
- Criar: `src/TwoG.Connector/Services/CompositeSimControl.cs`
- Modificar: `src/TwoG.Connector/Services/ISimSource.cs`, `SimConnectService.cs`, `XPlaneService.cs`, `CompositeSimSource.cs`

**Interfaces:**
- Consome: `ISimControl`, `RadioState`, `RadioGroup`, `RadioCatalog`, `ControlResult`, `ControlErrors` (Tarefa 1); `RadioConversions` (Tarefa 2).
- Produz:
  - `ISimSource.Control : ISimControl?` (capacidade opcional, como `FlightPlans`)
  - `SimConnectRadios : ISimControl, IDisposable` com `CommandSignal : AutoResetEvent`, e os métodos da thread do SimConnect `Register(SimConnect)`, `OnData(SIMCONNECT_RECV_SIMOBJECT_DATA) : bool`, `OnException(SIMCONNECT_RECV_EXCEPTION) : bool`, `OnAircraftLoaded(SimConnect)`, `Drain(SimConnect)`, `Reset()`
  - `CompositeSimControl : ISimControl` — estável durante toda a vida do app, repassando para a fonte ativa
  - `CompositeSimSource.Control` devolve sempre o mesmo `CompositeSimControl`

- [ ] **Passo 1: Capacidade no `ISimSource`**

Em `src/TwoG.Connector/Services/ISimSource.cs`, depois de `FlightPlans`:

```csharp
    /// <summary>
    /// Comando do simulador pelo 2G Pilot, quando a fonte souber fazer. Null quando não
    /// suporta (X-Plane, até o spec 03).
    /// </summary>
    ISimControl? Control { get; }
```

(o arquivo já tem `using TwoG.Connector.Core;`? Se não tiver, acrescentar.)

Em `src/TwoG.Connector/Services/XPlaneService.cs`, junto das outras propriedades públicas:

```csharp
    /// <summary>Rádios no X-Plane chegam com o spec 03.</summary>
    public ISimControl? Control => null;
```

- [ ] **Passo 2: `SimConnectRadios`**

`src/TwoG.Connector/Services/SimConnectRadios.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Rádios pelo SimConnect (spec 02). Comandos entram numa fila por <see cref="Submit"/>, de
/// qualquer thread, e são executados por <see cref="Drain"/> na thread do SimConnectService,
/// a única dona do objeto SimConnect. O estado vem de uma definição de dados POR GRUPO, com
/// o flag CHANGED: uma SimVar desconhecida (o modo do transponder no Prepar3D) derruba só o
/// grupo dela, e não os outros.
///
/// Todo método que recebe <see cref="SimConnect"/>, e os campos marcados "thread do
/// SimConnect", são tocados SÓ nessa thread.
/// </summary>
internal sealed class SimConnectRadios : ISimControl, IDisposable
{
    private const int MaxPending = 32;

    // IDs a partir de 100: definição, requisição e evento precisam ser únicos na conexão, e o
    // SimConnectService já usa de 0 a 9.
    private enum Definition { Com1 = 100, Com2, Nav1, Nav2, Adf1, Transponder, TransponderMode, Altimeter }

    private enum Request { Com1 = 100, Com2, Nav1, Nav2, Adf1, Transponder, TransponderMode, Altimeter }

    private enum Event
    {
        Com1Active = 100, Com1Standby, Com1Swap,
        Com2Active, Com2Standby, Com2Swap,
        Nav1Active, Nav1Standby, Nav1Swap,
        Nav2Active, Nav2Standby, Nav2Swap,
        Adf1Active, XpdrCode, AltimeterBaro,
    }

    /// <summary>SIMCONNECT_GROUP_PRIORITY_HIGHEST: com GROUPID_IS_PRIORITY, o evento vai direto.</summary>
    private enum Group { Highest = 1 }

    private static readonly (Event Event, string SimEvent, string Control)[] Events =
    [
        (Event.Com1Active, "COM_RADIO_SET_HZ", "com1.active"),
        (Event.Com1Standby, "COM_STBY_RADIO_SET_HZ", "com1.standby"),
        (Event.Com1Swap, "COM_STBY_RADIO_SWAP", "com1.swap"),
        (Event.Com2Active, "COM2_RADIO_SET_HZ", "com2.active"),
        (Event.Com2Standby, "COM2_STBY_RADIO_SET_HZ", "com2.standby"),
        (Event.Com2Swap, "COM2_RADIO_SWAP", "com2.swap"),
        (Event.Nav1Active, "NAV1_RADIO_SET_HZ", "nav1.active"),
        (Event.Nav1Standby, "NAV1_STBY_SET_HZ", "nav1.standby"),
        (Event.Nav1Swap, "NAV1_RADIO_SWAP", "nav1.swap"),
        (Event.Nav2Active, "NAV2_RADIO_SET_HZ", "nav2.active"),
        (Event.Nav2Standby, "NAV2_STBY_SET_HZ", "nav2.standby"),
        (Event.Nav2Swap, "NAV2_RADIO_SWAP", "nav2.swap"),
        (Event.Adf1Active, "ADF_COMPLETE_SET", "adf1.active"),
        (Event.XpdrCode, "XPNDR_SET", "xpdr.code"),
        (Event.AltimeterBaro, "KOHLSMAN_SET", "altimeter.baro"),
    ];

    private static readonly (RadioGroup Group, Definition Definition, Request Request)[] Groups =
    [
        (RadioGroup.Com1, Definition.Com1, Request.Com1),
        (RadioGroup.Com2, Definition.Com2, Request.Com2),
        (RadioGroup.Nav1, Definition.Nav1, Request.Nav1),
        (RadioGroup.Nav2, Definition.Nav2, Request.Nav2),
        (RadioGroup.Adf1, Definition.Adf1, Request.Adf1),
        (RadioGroup.Transponder, Definition.Transponder, Request.Transponder),
        (RadioGroup.TransponderMode, Definition.TransponderMode, Request.TransponderMode),
        (RadioGroup.Altimeter, Definition.Altimeter, Request.Altimeter),
    ];

    // A ordem dos campos TEM de ser a ordem dos AddToDataDefinition do grupo.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FrequencyPairData { public double Active; public double Standby; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AdfData { public double Active; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TransponderData { public int CodeBco16; public int Available; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TransponderModeData { public int Mode; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AltimeterData { public double Millibars; }

    private readonly Func<string?> _simulatorName;
    private readonly ConcurrentQueue<ControlCommand> _pending = new();

    // Thread do SimConnect.
    private readonly Dictionary<uint, RadioGroup> _packetToGroup = new();
    private readonly HashSet<RadioGroup> _failed = [];
    private readonly HashSet<RadioGroup> _available = [];
    private FrequencyPair? _com1, _com2, _nav1, _nav2;
    private long? _adf1;
    private int? _xpdrCode, _xpdrMode;
    private long? _baroPa;

    // Lidos de qualquer thread; trocados de uma vez (referências imutáveis).
    private volatile bool _connected;
    private volatile RadioState? _state;
    private volatile string[] _controls = [];

    public SimConnectRadios(Func<string?> simulatorName) => _simulatorName = simulatorName;

    /// <summary>Sinalizado quando há comando na fila: o SimConnectService acorda e chama <see cref="Drain"/>.</summary>
    public AutoResetEvent CommandSignal { get; } = new(false);

    public string? SimulatorName => _connected ? _simulatorName() : null;

    public IReadOnlyCollection<string> AvailableControls => _controls;

    public RadioState? State => _state;

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command)
    {
        if (!_connected)
            return ControlResult.Fail(command.Id, ControlErrors.SimNotConnected);
        if (_pending.Count >= MaxPending)
            return ControlResult.Fail(command.Id, ControlErrors.SimUnresponsive);

        _pending.Enqueue(command);
        CommandSignal.Set();
        return ControlResult.Success(command.Id);
    }

    public void Dispose() => CommandSignal.Dispose();

    // ── Thread do SimConnect ────────────────────────────────────────────

    /// <summary>Chamado no OnRecvOpen: mapeia eventos, define os grupos e pede os dados.</summary>
    public void Register(SimConnect sim)
    {
        ClearRadios();
        _packetToGroup.Clear();
        _failed.Clear();

        foreach (var (evt, simEvent, _) in Events)
            sim.MapClientEventToSimEvent(evt, simEvent);

        DefinePair(sim, RadioGroup.Com1, Definition.Com1, "COM", 1);
        DefinePair(sim, RadioGroup.Com2, Definition.Com2, "COM", 2);
        DefinePair(sim, RadioGroup.Nav1, Definition.Nav1, "NAV", 1);
        DefinePair(sim, RadioGroup.Nav2, Definition.Nav2, "NAV", 2);

        Define(sim, RadioGroup.Adf1, Definition.Adf1,
            ("ADF ACTIVE FREQUENCY:1", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ("ADF AVAILABLE:1", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<AdfData>(Definition.Adf1);

        Define(sim, RadioGroup.Transponder, Definition.Transponder,
            ("TRANSPONDER CODE:1", "BCO16", SIMCONNECT_DATATYPE.INT32),
            ("TRANSPONDER AVAILABLE:1", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<TransponderData>(Definition.Transponder);

        Define(sim, RadioGroup.TransponderMode, Definition.TransponderMode,
            ("TRANSPONDER STATE:1", "Enum", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<TransponderModeData>(Definition.TransponderMode);

        Define(sim, RadioGroup.Altimeter, Definition.Altimeter,
            ("KOHLSMAN SETTING MB:1", "millibars", SIMCONNECT_DATATYPE.FLOAT64));
        sim.RegisterDataDefineStruct<AltimeterData>(Definition.Altimeter);

        RequestAll(sim);
        _connected = true;
        Publish();
    }

    /// <summary>
    /// Troca de aeronave: um pedido novo zera a referência do CHANGED, e o simulador manda o
    /// retrato completo, com os AVAILABLE da aeronave nova.
    /// </summary>
    public void OnAircraftLoaded(SimConnect sim) => RequestAll(sim);

    /// <summary>Devolve true se o pacote era de um grupo de rádio.</summary>
    public bool OnData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwData is not { Length: > 0 })
            return false;

        switch ((Request)data.dwRequestID)
        {
            case Request.Com1 when data.dwData[0] is FrequencyPairData d:
                _com1 = Pair(RadioGroup.Com1, d);
                break;
            case Request.Com2 when data.dwData[0] is FrequencyPairData d:
                _com2 = Pair(RadioGroup.Com2, d);
                break;
            case Request.Nav1 when data.dwData[0] is FrequencyPairData d:
                _nav1 = Pair(RadioGroup.Nav1, d);
                break;
            case Request.Nav2 when data.dwData[0] is FrequencyPairData d:
                _nav2 = Pair(RadioGroup.Nav2, d);
                break;
            case Request.Adf1 when data.dwData[0] is AdfData d:
                _adf1 = SetAvailable(RadioGroup.Adf1, d.Available != 0) ? RadioConversions.RoundHz(d.Active) : null;
                break;
            case Request.Transponder when data.dwData[0] is TransponderData d:
                _xpdrCode = SetAvailable(RadioGroup.Transponder, d.Available != 0)
                    ? RadioConversions.Bco16ToCode(unchecked((uint)d.CodeBco16))
                    : null;
                break;
            case Request.TransponderMode when data.dwData[0] is TransponderModeData d:
                SetAvailable(RadioGroup.TransponderMode, true);
                _xpdrMode = d.Mode;
                break;
            case Request.Altimeter when data.dwData[0] is AltimeterData d:
                SetAvailable(RadioGroup.Altimeter, true);
                _baroPa = RadioConversions.MillibarsToPa(d.Millibars);
                break;
            default:
                return false;
        }
        Publish();
        return true;
    }

    /// <summary>Exceção de um pedido dos rádios: o grupo dele fica indisponível. True se era nosso.</summary>
    public bool OnException(SIMCONNECT_RECV_EXCEPTION data)
    {
        if (!_packetToGroup.TryGetValue(data.dwSendID, out var group))
            return false;
        _failed.Add(group);
        _available.Remove(group);
        if (group == RadioGroup.TransponderMode)
            _xpdrMode = null;
        Publish();
        return true;
    }

    /// <summary>Executa os comandos da fila. Chamado quando <see cref="CommandSignal"/> dispara.</summary>
    public void Drain(SimConnect sim)
    {
        while (_pending.TryDequeue(out var command))
        {
            try
            {
                Execute(sim, command);
            }
            catch (COMException)
            {
                // O simulador recusou; o estado continua mostrando o valor antigo, e o app percebe.
            }
        }
    }

    /// <summary>Conexão caiu (TearDown): nada de estado, nada de controles.</summary>
    public void Reset()
    {
        _connected = false;
        while (_pending.TryDequeue(out _))
        {
        }
        ClearRadios();
        _packetToGroup.Clear();
        _failed.Clear();

        var hadSomething = _state is not null || _controls.Length > 0;
        _state = null;
        _controls = [];
        if (hadSomething)
            Changed?.Invoke();
    }

    private static void Execute(SimConnect sim, ControlCommand command)
    {
        if (command.Control == "xpdr.mode")
        {
            sim.SetDataOnSimObject(Definition.TransponderMode, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, new TransponderModeData { Mode = (int)command.Value });
            return;
        }

        var entry = Array.Find(Events, e => e.Control == command.Control);
        if (entry.SimEvent is null)
            return;   // o catálogo já barrou; não há o que fazer

        var parameter = command.Control switch
        {
            "adf1.active" => RadioConversions.HzToBcd32(command.Value),
            "xpdr.code" => RadioConversions.CodeToBcd16(command.Value),
            "altimeter.baro" => RadioConversions.PaToMillibars16(command.Value),
            _ => (uint)command.Value,   // Hz direto; nas ações o valor é 0
        };
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, entry.Event, parameter,
            Group.Highest, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private void DefinePair(SimConnect sim, RadioGroup group, Definition definition, string radio, int index)
    {
        Define(sim, group, definition,
            ($"{radio} ACTIVE FREQUENCY:{index}", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ($"{radio} STANDBY FREQUENCY:{index}", "Hz", SIMCONNECT_DATATYPE.FLOAT64),
            ($"{radio} AVAILABLE:{index}", "Bool", SIMCONNECT_DATATYPE.INT32));
        sim.RegisterDataDefineStruct<FrequencyPairData>(definition);
    }

    /// <summary>Acrescenta as SimVars e guarda o ID de cada pacote, para atribuir exceções ao grupo.</summary>
    private void Define(SimConnect sim, RadioGroup group, Definition definition,
                        params (string SimVar, string Unit, SIMCONNECT_DATATYPE Type)[] fields)
    {
        foreach (var (simVar, unit, type) in fields)
        {
            sim.AddToDataDefinition(definition, simVar, unit, type, 0f, SimConnect.SIMCONNECT_UNUSED);
            _packetToGroup[sim.GetLastSentPacketID()] = group;
        }
    }

    private void RequestAll(SimConnect sim)
    {
        foreach (var (group, definition, request) in Groups)
        {
            if (_failed.Contains(group))
                continue;
            // VISUAL_FRAME, e não SIM_FRAME: o estado continua chegando com o simulador pausado (V6).
            sim.RequestDataOnSimObject(request, definition, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _packetToGroup[sim.GetLastSentPacketID()] = group;
        }
    }

    private FrequencyPair? Pair(RadioGroup group, FrequencyPairData d) =>
        SetAvailable(group, d.Available != 0)
            ? new FrequencyPair(RadioConversions.RoundHz(d.Active), RadioConversions.RoundHz(d.Standby))
            : null;

    private bool SetAvailable(RadioGroup group, bool available)
    {
        if (available)
            _available.Add(group);
        else
            _available.Remove(group);
        return available;
    }

    private void ClearRadios()
    {
        _available.Clear();
        _com1 = _com2 = _nav1 = _nav2 = null;
        _adf1 = null;
        _xpdrCode = _xpdrMode = null;
        _baroPa = null;
    }

    /// <summary>Monta o retrato e a lista de controles; avisa só se algo mudou.</summary>
    private void Publish()
    {
        var hasTransponder = _xpdrCode is not null;
        var modeAvailable = hasTransponder && _available.Contains(RadioGroup.TransponderMode);

        var state = new RadioState(_com1, _com2, _nav1, _nav2, _adf1,
            hasTransponder ? new TransponderState(_xpdrCode!.Value, modeAvailable ? _xpdrMode : null) : null,
            _baroPa);

        var groups = _available.Where(g => !_failed.Contains(g) && (g != RadioGroup.TransponderMode || hasTransponder));
        var controls = RadioCatalog.ControlsFor(groups).ToArray();

        var changed = !Equals(state, _state) || !controls.SequenceEqual(_controls);
        _state = state;
        _controls = controls;
        if (changed)
            Changed?.Invoke();
    }
}
```

- [ ] **Passo 3: Ligar no `SimConnectService`**

Em `src/TwoG.Connector/Services/SimConnectService.cs`:

1. No enum `EVENT_ID`, acrescentar `AircraftLoaded` ao fim:

```csharp
    private enum EVENT_ID { Sim, PauseEx1, Crashed, CrashReset, FlightPlanActivated, FlightPlanDeactivated, AircraftLoaded }
```

2. Campo e construtor, logo depois do campo `_nonFiniteSamples`:

```csharp
    private readonly SimConnectRadios _radios;

    public SimConnectService()
    {
        _radios = new SimConnectRadios(() => _simulatorName);
    }
```

3. Propriedade pública, junto de `LatestFix`:

```csharp
    public ISimControl? Control => _radios;
```

4. Em `Dispose()`, antes de `_simEvent.Dispose();`: `_radios.Dispose();`

5. Em `ThreadMain`, o array de espera ganha o sinal dos comandos, e o laço, o terceiro caso:

```csharp
        var waitConnected = new WaitHandle[] { _stopEvent, _simEvent, _radios.CommandSignal };
```

e, depois do bloco `if (signaled == 1) { ... }`:

```csharp
            if (signaled == 2)
            {
                try
                {
                    _radios.Drain(_sim);
                }
                catch (COMException)
                {
                    TearDown();
                }
            }
```

6. Em `TryConnect`, junto das outras assinaturas de eventos:

```csharp
            sim.OnRecvException += OnRecvException;
            sim.OnRecvEventFilename += OnRecvEventFilename;
```

7. No fim de `OnRecvOpen`, depois de `RequestFlightPlanPath(sender);`:

```csharp
        // Troca de aeronave refaz a lista de controles (spec 02, "Lista de controles por aeronave").
        sender.SubscribeToSystemEvent(EVENT_ID.AircraftLoaded, "AircraftLoaded");
        _radios.Register(sender);
```

8. Primeira linha de `OnRecvSimobjectData`:

```csharp
        if (_radios.OnData(data))
            return;
```

9. Métodos novos, junto dos outros callbacks:

```csharp
    private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data) => _radios.OnException(data);

    private void OnRecvEventFilename(SimConnect sender, SIMCONNECT_RECV_EVENT_FILENAME data)
    {
        if ((EVENT_ID)data.uEventID == EVENT_ID.AircraftLoaded)
            _radios.OnAircraftLoaded(sender);
    }
```

10. Em `TearDown()`, antes de `_state = SimConnectionState.Searching;`: `_radios.Reset();`

- [ ] **Passo 4: Controle estável no `CompositeSimSource`**

`src/TwoG.Connector/Services/CompositeSimControl.cs`:

```csharp
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// ISimControl que vive o app inteiro e repassa para a fonte ativa. O servidor do canal
/// guarda UMA referência, mas a fonte ativa muda (MSFS fecha, X-Plane abre): este objeto
/// absorve a troca e retransmite as mudanças de todas as fontes.
/// </summary>
internal sealed class CompositeSimControl : ISimControl
{
    private readonly Func<ISimSource?> _active;

    public CompositeSimControl(Func<ISimSource?> active, IEnumerable<ISimSource> sources)
    {
        _active = active;
        foreach (var source in sources)
        {
            if (source.Control is { } control)
                control.Changed += () => Changed?.Invoke();
        }
    }

    public string? SimulatorName => _active()?.Control?.SimulatorName;

    public IReadOnlyCollection<string> AvailableControls => _active()?.Control?.AvailableControls ?? [];

    public RadioState? State => _active()?.Control?.State;

    public event Action? Changed;

    public ControlResult Submit(ControlCommand command) =>
        _active()?.Control is { } control
            ? control.Submit(command)
            : ControlResult.Fail(command.Id, ControlErrors.SimNotConnected);
}
```

Em `src/TwoG.Connector/Services/CompositeSimSource.cs`:

- campo `private readonly CompositeSimControl _control;`
- no construtor, depois de `_sources = sources;`: `_control = new CompositeSimControl(() => Active, sources);`
  (se o construtor for de expressão, `=> _sources = sources;`, transformá-lo em bloco com as duas linhas)
- propriedade, junto de `FlightPlans`:

```csharp
    /// <summary>Sempre o mesmo objeto: o servidor do canal guarda esta referência.</summary>
    public ISimControl? Control => _control;
```

- [ ] **Passo 5: Compilar**

```bash
dotnet build TwoG.Connector.slnx
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
```

Expected: build sem avisos (inclusive de nulabilidade), testes passando.

- [ ] **Passo 6: Commit**

```bash
git add src/TwoG.Connector/Services/SimConnectRadios.cs src/TwoG.Connector/Services/CompositeSimControl.cs src/TwoG.Connector/Services/ISimSource.cs src/TwoG.Connector/Services/SimConnectService.cs src/TwoG.Connector/Services/XPlaneService.cs src/TwoG.Connector/Services/CompositeSimSource.cs
git commit -m "Rádios pelo SimConnect: fila na thread do simulador e estado por grupo

Comandos entram numa fila de até 32 e são executados só pela thread do
SimConnectService, acordada por um sinal a mais no WaitAny. O estado vem
de uma definição de dados por grupo com CHANGED e período VISUAL_FRAME:
uma SimVar desconhecida derruba só o grupo dela. IDs dos rádios começam
em 100 para não colidir com os da posição. A troca de aeronave refaz a
lista de controles.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 10: Ligação no app, configurações e interface

**Arquivos:**
- Criar: `src/TwoG.Connector/Services/ControlService.cs`
- Modificar: `src/TwoG.Connector/Configuration/AppSettings.cs`, `Configuration/SettingsService.cs`
- Modificar: `src/TwoG.Connector/App.xaml.cs`
- Modificar: `src/TwoG.Connector/ViewModels/MainViewModel.cs`
- Modificar: `src/TwoG.Connector/MainWindow.xaml`

**Interfaces:**
- Consome: `ControlServer`, `PairingCodes`, `PairedDeviceStore` (Core); `ControlAnnouncer`, `IXgpsBroadcaster` (Tarefa 8); `ISimSource.Control` (Tarefa 9); `UpdateService.CurrentVersion` (existente).
- Produz:
  - `AppSettings.AllowControl : bool` (padrão `true`), `AppSettings.ControlPort : int` (padrão `49004`)
  - `ControlService(ISimControl control, IXgpsBroadcaster broadcaster, AppSettings settings, string connectorVersion) : IDisposable` com `Devices`, `Codes`, `Server`, `Apply()`
  - No ViewModel: `ControlStatusText`, `ControlStatusBrush`, `ControlDetail`, `PairingCodeText`, `PairingCountdownText`, `HasPairingCode`, `CanStartPairing`, `PairedDevices`, `HasPairedDevices`, `AllowControlInput`, `ControlPortInput`, `DiagControl`, e os comandos `StartPairingCommand`, `CancelPairingCommand`, `RemoveDeviceCommand`
  - `sealed record PairedDeviceItem(string Id, string Name, string Detail)`

- [ ] **Passo 1: Configurações**

`Configuration/AppSettings.cs`, depois de `AutoUpdate`:

```csharp
    /// <summary>Aceitar comandos do 2G Pilot pelo canal de controle. Desligado, a porta nem abre.</summary>
    public bool AllowControl { get; set; } = true;

    /// <summary>Porta TCP do canal de controle (WebSocket).</summary>
    public int ControlPort { get; set; } = 49004;
```

`Configuration/SettingsService.cs`, em `Sanitize`, depois da linha da porta XGPS:

```csharp
        if (s.ControlPort is < 1 or > 65535) s.ControlPort = 49004;
```

- [ ] **Passo 2: `ControlService`**

`src/TwoG.Connector/Services/ControlService.cs`:

```csharp
using System.IO;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Junta as peças do canal de controle — servidor, código de pareamento, aparelhos e
/// anúncio — e as liga ou desliga conforme "Permitir controle pelo 2G Pilot".
/// Desligado: a porta 49004 não abre, o 2GCTL não é anunciado e as conexões são fechadas.
/// Os pareamentos continuam guardados.
/// </summary>
// Público como os demais serviços: o MainViewModel, público, o recebe no construtor
// (internal daria CS0051 — o mesmo caso do UpdateService na v1.4).
public sealed class ControlService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ControlAnnouncer _announcer;

    public ControlService(ISimControl control, IXgpsBroadcaster broadcaster, AppSettings settings, string connectorVersion)
    {
        _settings = settings;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductIdentity.DataFolderName);
        Devices = new PairedDeviceStore(Path.Combine(dir, "paired-devices.json"));
        Codes = new PairingCodes();
        Server = new ControlServer(control, Devices, Codes, connectorVersion);
        _announcer = new ControlAnnouncer(broadcaster, () => _settings.DeviceName, () => Server.Port);
    }

    public PairedDeviceStore Devices { get; }

    public PairingCodes Codes { get; }

    public ControlServer Server { get; }

    /// <summary>Aplica as Configurações atuais. Chamado na partida e ao clicar em Aplicar.</summary>
    public void Apply()
    {
        if (!_settings.AllowControl)
        {
            _announcer.Stop();
            Codes.Cancel();
            Server.Stop();
            return;
        }

        if (!Server.IsRunning || Server.Port != _settings.ControlPort)
            Server.Start(_settings.ControlPort);

        // Sem servidor (porta ocupada), anunciar seria convidar o app a uma porta fechada.
        if (Server.IsRunning)
            _announcer.Start();
        else
            _announcer.Stop();
    }

    public void Dispose()
    {
        _announcer.Dispose();
        Server.Dispose();
    }
}
```

- [ ] **Passo 3: Ligação no `App.xaml.cs`**

Campo novo, junto dos outros serviços: `private ControlService? _control;`

Logo depois de `_flightPlanServer.Start(settings.FlightPlanPort);`:

```csharp
        if (_sim.Control is { } simControl)
            _control = new ControlService(simControl, _broadcaster, settings, _updates.CurrentVersion.ToString());
```

A criação do ViewModel passa `_control` como último argumento:

```csharp
        var viewModel = new MainViewModel(_sim, _broadcaster, settingsService, settings,
                                          _flightPlanServer, _discovery, _updates, _control);
```

Depois de `_broadcaster.Start();` (o anúncio sai pelo broadcaster): `_control?.Apply();`

Em `OnExit`, antes de `_broadcaster?.Dispose();`: `_control?.Dispose();`

- [ ] **Passo 4: ViewModel**

Em `ViewModels/MainViewModel.cs`:

1. `using System.Collections.ObjectModel;` no topo.
2. Tipo novo, no fim do arquivo, fora da classe:

```csharp
/// <summary>Uma linha da lista de aparelhos pareados.</summary>
public sealed record PairedDeviceItem(string Id, string Name, string Detail);
```

3. Campo e parâmetro do construtor: `private readonly ControlService? _control;` e o parâmetro `ControlService? control = null` por último, com `_control = control;` junto das outras atribuições.

4. Propriedades, depois do bloco `// ── Atualização ──`:

```csharp
    // ── Controle pelo 2G Pilot ──────────────────────────────────────────
    [ObservableProperty] private string _controlStatusText = "";
    [ObservableProperty] private Brush _controlStatusBrush = Dim;
    [ObservableProperty] private string _controlDetail = "";
    [ObservableProperty] private string _pairingCodeText = "";
    [ObservableProperty] private string _pairingCountdownText = "";
    [ObservableProperty] private bool _hasPairingCode;
    [ObservableProperty] private bool _canStartPairing;
    [ObservableProperty] private bool _hasPairedDevices;
    [ObservableProperty] private string _diagControl = "—";

    public ObservableCollection<PairedDeviceItem> PairedDevices { get; } = [];

    private string _devicesSignature = "";
```

e, no bloco de configurações: `[ObservableProperty] private bool _allowControlInput;` e `[ObservableProperty] private string _controlPortInput = "";`

5. Em `LoadSettingsIntoInputs()`, depois de `AutoUpdateInput = _settings.AutoUpdate;`:

```csharp
        AllowControlInput = _settings.AllowControl;
        ControlPortInput = _settings.ControlPort.ToString(CultureInfo.InvariantCulture);
```

6. Em `Refresh()`, logo depois de `UpdateUpdaterStatus();`: `UpdateControlStatus();`

7. Em `UpdateDiagnostics()`, a lista de erros ganha o do servidor:

```csharp
            new[] { sendError, discoveryError, _updates?.LastError, _control?.Server.LastError }.Where(e => e is { Length: > 0 }));
```

8. Em `ApplySettings()`, logo depois do bloco que valida `XattHzInput`:

```csharp
        if (!int.TryParse(ControlPortInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var controlPort)
            || controlPort is < 1 or > 65535 || controlPort == _settings.FlightPlanPort)
        {
            ShowFeedback("Porta do controle inválida (1–65535, diferente da do plano de voo).", isError: true);
            return;
        }
```

depois de `_settings.AutoUpdate = AutoUpdateInput;`:

```csharp
        _settings.AllowControl = AllowControlInput;
        _settings.ControlPort = controlPort;
```

e depois de `_broadcaster.UpdateSettings(_settings);`: `_control?.Apply();`

9. Métodos novos, depois de `UpdateUpdaterStatus()`:

```csharp
    /// <summary>Card "Controle pelo 2G Pilot" e linha no Diagnóstico.</summary>
    private void UpdateControlStatus()
    {
        var control = _control;
        if (control is null)
        {
            ControlStatusText = "Indisponível neste simulador";
            ControlStatusBrush = Dim;
            CanStartPairing = HasPairingCode = false;
            return;
        }

        if (!_settings.AllowControl)
        {
            ControlStatusText = "Desligado nas Configurações";
            ControlStatusBrush = Dim;
            ControlDetail = "";
            DiagControl = "desligado";
        }
        else if (!control.Server.IsRunning)
        {
            ControlStatusText = "Canal de controle indisponível";
            ControlStatusBrush = Err;
            ControlDetail = control.Server.LastError ?? "";
            DiagControl = control.Server.LastError ?? "servidor parado";
        }
        else
        {
            var paired = control.Server.Connected.Where(c => c.Paired).ToArray();
            ControlStatusText = paired.Length switch
            {
                0 => "Aguardando aparelho",
                1 => $"1 aparelho conectado: {paired[0].DeviceName}",
                var n => $"{n} aparelhos conectados",
            };
            ControlStatusBrush = paired.Length > 0 ? Ok : Warn;
            ControlDetail = $"TCP {control.Server.Port} • anúncio 2GCTL na UDP {_settings.Port}";
            DiagControl = $"conexões {control.Server.Connected.Count}"
                          + (control.Server.LastCommand is { } last ? $"  •  último comando: {last}" : "");
        }

        var active = control.Codes.Active;
        HasPairingCode = active is not null;
        if (active is { } code)
        {
            PairingCodeText = $"{code.Code[..3]} {code.Code[3..]}";
            PairingCountdownText = $"expira em {Math.Max(0, (int)(code.ExpiresUtc - DateTime.UtcNow).TotalSeconds)} s";
        }
        CanStartPairing = _settings.AllowControl && control.Server.IsRunning && !HasPairingCode;

        RefreshPairedDevices(control);
    }

    /// <summary>Recria a lista só quando algo mudou: a cada 250 ms seria piscar a tela à toa.</summary>
    private void RefreshPairedDevices(ControlService control)
    {
        var connected = control.Server.Connected.Where(c => c.Paired).Select(c => c.DeviceId).ToHashSet();
        var items = control.Devices.Devices
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new PairedDeviceItem(d.Id, d.Name,
                connected.Contains(d.Id) ? "conectado agora" : $"último acesso {d.LastSeenUtc.ToLocalTime():dd/MM HH:mm}"))
            .ToArray();

        var signature = string.Join("|", items.Select(i => $"{i.Id}:{i.Name}:{i.Detail}"));
        if (signature == _devicesSignature)
            return;
        _devicesSignature = signature;

        PairedDevices.Clear();
        foreach (var item in items)
            PairedDevices.Add(item);
        HasPairedDevices = items.Length > 0;
    }

    [RelayCommand]
    private void StartPairing()
    {
        _control?.Codes.Generate();
        Refresh();
    }

    [RelayCommand]
    private void CancelPairing()
    {
        _control?.Codes.Cancel();
        Refresh();
    }

    [RelayCommand]
    private void RemoveDevice(string? deviceId)
    {
        if (_control is null || deviceId is null)
            return;

        var name = _control.Devices.Devices.FirstOrDefault(d => d.Id == deviceId)?.Name ?? "este aparelho";
        var answer = System.Windows.MessageBox.Show(
            $"Remover \"{name}\"? Ele deixa de controlar o simulador e precisa parear de novo.",
            ProductIdentity.Name, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        _control.Devices.Remove(deviceId);   // derruba a conexão dele com 4001
        _devicesSignature = "";
        Refresh();
    }
```

- [ ] **Passo 5: Interface (`MainWindow.xaml`)**

1. O Grid principal ganha uma linha: acrescentar mais um `<RowDefinition Height="Auto" />` às sete que existem.

2. Renumerar: o card `<!-- ══ Diagnóstico ══ -->` passa de `Grid.Row="4"` para `Grid.Row="5"`; `<!-- ══ Configurações ══ -->` de `5` para `6`; o rodapé de `Grid.Row="6"` para `Grid.Row="7"`.

3. Imediatamente antes de `<!-- ══ Diagnóstico ══ -->`, o card novo:

```xml
        <!-- ══ Controle pelo 2G Pilot ══ -->
        <Border Grid.Row="4" Style="{StaticResource Card}" Margin="0,0,0,10">
            <StackPanel>
                <TextBlock Text="CONTROLE PELO 2G PILOT" Style="{StaticResource SectionTitle}" Margin="0,0,0,8" />
                <StackPanel Orientation="Horizontal">
                    <Ellipse Width="10" Height="10" Fill="{Binding ControlStatusBrush}" VerticalAlignment="Center" />
                    <TextBlock Text="{Binding ControlStatusText}" FontSize="14.5" FontWeight="SemiBold"
                               Foreground="{StaticResource TextBrush}" Margin="9,0,0,0" VerticalAlignment="Center" />
                </StackPanel>
                <TextBlock Text="{Binding ControlDetail}" FontSize="11.5" TextWrapping="Wrap"
                           Foreground="{StaticResource DimBrush}" Margin="19,3,0,0" />

                <!-- Só enquanto existe um código de pareamento -->
                <Border Background="{StaticResource InputBrush}" CornerRadius="10" Padding="14,10" Margin="0,12,0,0"
                        Visibility="{Binding HasPairingCode, Converter={StaticResource BoolToVisibility}}">
                    <StackPanel>
                        <TextBlock Text="Digite este código no 2G Pilot" FontSize="11.5" Foreground="{StaticResource DimBrush}" />
                        <TextBlock Text="{Binding PairingCodeText}" FontSize="30" FontWeight="Bold" FontFamily="Consolas"
                                   Foreground="{StaticResource AccentBrush}" Margin="0,4,0,0" />
                        <Grid Margin="0,4,0,0">
                            <TextBlock Text="{Binding PairingCountdownText}" FontSize="11.5"
                                       Foreground="{StaticResource DimBrush}" VerticalAlignment="Center" />
                            <Button Content="Cancelar" Command="{Binding CancelPairingCommand}"
                                    HorizontalAlignment="Right" Padding="10,3" FontSize="11.5" />
                        </Grid>
                    </StackPanel>
                </Border>

                <Button Content="Parear aparelho" Command="{Binding StartPairingCommand}"
                        IsEnabled="{Binding CanStartPairing}" HorizontalAlignment="Left"
                        Margin="19,12,0,0" Padding="14,7" FontSize="12" />

                <ItemsControl ItemsSource="{Binding PairedDevices}" Margin="19,10,0,0"
                              Visibility="{Binding HasPairedDevices, Converter={StaticResource BoolToVisibility}}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,3">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel>
                                    <TextBlock Text="{Binding Name}" FontSize="12.5" Foreground="{StaticResource TextBrush}" />
                                    <TextBlock Text="{Binding Detail}" FontSize="11" Foreground="{StaticResource DimBrush}" />
                                </StackPanel>
                                <Button Grid.Column="1" Content="Remover" Padding="10,3" FontSize="11" VerticalAlignment="Center"
                                        Command="{Binding DataContext.RemoveDeviceCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                        CommandParameter="{Binding Id}" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>

```

4. Nas Configurações, logo depois do `CheckBox` "Atualizar automaticamente (sempre fora de voo)":

```xml
                    <CheckBox Content="Permitir controle pelo 2G Pilot"
                              IsChecked="{Binding AllowControlInput}" Margin="0,8,0,0" />
                    <TextBlock Text="Porta do controle (TCP)" Style="{StaticResource ValueLabel}" Margin="0,10,0,4" />
                    <TextBox Text="{Binding ControlPortInput, UpdateSourceTrigger=PropertyChanged}"
                             Width="120" HorizontalAlignment="Left" />
```

5. No Diagnóstico, imediatamente antes de `<TextBlock Text="ATUALIZAÇÃO" Style="{StaticResource ValueLabel}" />`:

```xml
                    <TextBlock Text="CONTROLE" Style="{StaticResource ValueLabel}" />
                    <TextBlock Text="{Binding DiagControl}" FontSize="11.5" TextWrapping="Wrap"
                               Foreground="{StaticResource DimBrush}" Margin="0,3,0,12" />
```

- [ ] **Passo 6: Compilar e conferir o layout**

```bash
dotnet build TwoG.Connector.slnx
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
grep -n '<Border Grid.Row=\|<TextBlock Grid.Row="7"' src/TwoG.Connector/MainWindow.xaml
```

Expected: build sem avisos; testes passando; os cards de topo em `Grid.Row` 1 a 6, cada um uma vez, e o rodapé no 7.

- [ ] **Passo 7: Commit**

```bash
git add src/TwoG.Connector/Services/ControlService.cs src/TwoG.Connector/Configuration/AppSettings.cs src/TwoG.Connector/Configuration/SettingsService.cs src/TwoG.Connector/App.xaml.cs src/TwoG.Connector/ViewModels/MainViewModel.cs src/TwoG.Connector/MainWindow.xaml
git commit -m "Card de controle pelo 2G Pilot: pareamento e aparelhos

O canal sobe com o app quando \"Permitir controle pelo 2G Pilot\" está
ligado (padrão), anunciando 2GCTL pelo broadcaster. O card mostra quem
está conectado, gera o código de pareamento com contagem regressiva e
lista os aparelhos pareados com botão para remover, que derruba a
conexão na hora. Porta do controle configurável; Diagnóstico mostra
conexões, último comando e erro do servidor.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 11: Documentação e verificação em Windows

Os passos 3 em diante dependem de uma máquina Windows com os simuladores e de um 2G Pilot com o lado do app implementado (o contrato no fim do 01). Nada aqui publica versão: PR, tag e release seguem o mesmo rito da v1.4.0 e exigem autorização explícita.

**Arquivos:**
- Modificar: `README.md`, `CLAUDE.md`, `docs/2g-connector-controles/02-radios-simconnect.md` (tabela de resultados)

- [ ] **Passo 1: README**

Seção nova, depois de "Sincronizar plano de voo":

```markdown
## Controle pelo 2G Pilot

O 2G Pilot pode sintonizar os rádios do simulador — COM1/COM2, NAV1/NAV2, ADF,
transponder e altímetro — e mostra sempre o que está de fato no cockpit. Funciona
com MSFS 2020, MSFS 2024 e Prepar3D.

**Parear um iPad, uma vez:**

1. No 2G Connector, clique em **Parear aparelho**. Aparece um código de 6 dígitos,
   válido por 2 minutos.
2. No 2G Pilot, escolha o Connector na lista e digite o código.

Depois disso o aparelho conecta sozinho. A lista de aparelhos pareados fica no card
**Controle pelo 2G Pilot**, com o botão **Remover**. Para desligar o controle de vez:
**Configurações → Permitir controle pelo 2G Pilot**.

Como funciona: o Connector anuncia o canal (`2GCTL`) na UDP 49002 e escuta em
WebSocket na TCP 49004. O protocolo e o contrato para o app estão em
[docs/2g-connector-controles](docs/2g-connector-controles/00-visao-geral.md).

Aeronaves de terceiros com rádios próprios (Fenix, PMDG e muitas do MSFS 2024) podem
ignorar os comandos padrão: o 2G Pilot avisa quando o rádio não mudou. O suporte a
elas vem com os perfis de aeronave (spec 04).
```

Na tabela de solução de problemas, duas linhas:

```markdown
| O 2G Pilot não encontra o Connector para controlar | Confira se "Permitir controle pelo 2G Pilot" está ligado e se o card mostra "Aguardando aparelho". Porta TCP 49004 bloqueada pelo firewall: libere o app para redes privadas |
| O comando chega mas o rádio não muda | A aeronave ignora os eventos padrão do SimConnect (comum em aeronaves de terceiros) — ver spec 04 |
```

- [ ] **Passo 2: CLAUDE.md**

Em "Estrutura":

```markdown
- Canal de controle (specs em `docs/2g-connector-controles/`): Core com `ControlProtocol`,
  `ControlSession`, `ControlServer` (WebSocket sobre TcpListener), `PairingCodes`,
  `PairedDeviceStore`, `RadioCatalog`; no app, `SimConnectRadios` (ISimControl pelo
  SimConnect) e `ControlService` (liga servidor, pareamento e anúncio 2GCTL)
```

Em "Regras importantes":

```markdown
- **Canal de controle — invariante:** o app manda só ID do catálogo e inteiro. Nenhuma
  mensagem carrega nome de variável, evento ou código; a tradução é do Connector.
- **SimConnect:** só a thread do `SimConnectService` toca o objeto `SimConnect`; comandos
  chegam pela fila do `SimConnectRadios`. IDs de definição, requisição e evento dos rádios
  começam em 100 — os da posição usam de 0 a 9 e não podem colidir.
- Tokens de pareamento: em disco só o SHA-256 (`paired-devices.json`). Nunca logar token.
```

- [ ] **Passo 3: Suíte completa, build e commit**

```bash
dotnet build TwoG.Connector.slnx
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
dotnet publish src/TwoG.Connector/TwoG.Connector.csproj -c Release -o /tmp/2gc-pub-ctl && ls /tmp/2gc-pub-ctl
git add README.md CLAUDE.md
git commit -m "Documenta o controle pelo 2G Pilot

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Expected: build sem avisos; todos os testes passando; publish com um único `2G-Connector.exe`.

- [ ] **Passo 4 (Windows, sem iPad): o canal por um cliente WebSocket qualquer**

Com o Connector rodando e um simulador aberto, dá para validar o canal inteiro antes de o app estar pronto, usando qualquer cliente WebSocket (por exemplo `websocat ws://127.0.0.1:49004/control`):

1. Mandar `{"type":"hello","protocol":1,"device":{"id":"teste","name":"Teste"}}` → recebe `pairing_required`.
2. Clicar em **Parear aparelho**, mandar `{"type":"pair","code":"<código>"}` → recebe `paired`, `welcome`, `controls`, `state`.
3. Mandar `{"type":"set","id":"1","control":"com1.standby","value":118500000}` → `result ok`, e um `state` com `118500000` no COM1 standby.
4. Remover o aparelho no Connector → a conexão fecha com 4001.

- [ ] **Passo 5 (Windows): roteiro V1–V10 do spec 02**

Executar cada item da tabela "Itens de verificação" do 02 no MSFS 2020, no MSFS 2024 e no Prepar3D, conferindo pelo `state`. Registrar os resultados numa tabela nova no fim do 02 (controle × simulador → ok / falhou / ação tomada). O que falhar vira ajuste (plano B do próprio item) antes da entrega.

- [ ] **Passo 6 (Windows + iPad): fluxo completo com o 2G Pilot**

Com o lado do app implementado: descoberta pelo `2GCTL`, pareamento, os 16 controles numa aeronave padrão, dois iPads ao mesmo tempo (o `state` chega aos dois), troca de aeronave (a lista de controles muda), fechar o simulador (o `state` vai para `simulator: null`) e remover um aparelho com ele conectado.
