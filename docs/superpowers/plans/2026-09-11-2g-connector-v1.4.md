# 2G Connector v1.4.0 — Plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans para executar este plano tarefa a tarefa. Os passos usam checkbox (`- [ ]`).

**Objetivo:** renomear o produto para 2G Connector com migração transparente da v1.3.0 e acrescentar auto-update assinado que só instala fora de voo.

**Arquitetura:** toda lógica pura (nomes do produto, transformação do `EXE.xml`, migração de configuração, versões, manifesto, assinatura, política, verificação e download) vai para o `TwoG.Connector.Core`, testável em qualquer SO. O projeto WPF fica com o que depende do Windows: I/O do `EXE.xml`, execução do instalador, troca do exe e UI. Uma ferramenta de console (`tools/TwoG.Connector.ReleaseSigner`) gera e assina o manifesto no CI usando o mesmo código do app.

**Stack:** .NET 10 (WPF `net10.0-windows` x64, Core `net10.0`), xUnit, `System.Security.Cryptography.ECDsa` (P-256, nativo), Inno Setup 6, GitHub Actions (windows-latest).

**Spec:** [docs/superpowers/specs/2026-09-11-2g-connector-v1.4-design.md](../specs/2026-09-11-2g-connector-v1.4-design.md)

## Restrições globais

- Nome do produto: `2G Connector`. Nome legado: `2G GPS Cliente`.
- Arquivos: `2G-Connector.exe`, `2G-Connector.zip`, `2G-Connector-Setup.exe`. Legados: `2G-GPS-Cliente.exe`, `2G-GPS-Cliente.zip`, `2G-GPS-Cliente-Setup.exe`.
- **Nunca mudar:** mutex `Local\TwoG.GpsClient.SingleInstance`, evento `Local\TwoG.GpsClient.ShowWindow`, `AppId={{B23B9502-7C57-45EF-9075-D53016835238}`, e os contratos de fio (`XGPS`/`XATT`, `2GFPL`, `_2gpilot._udp`, `{"App":"2G Pilot",...}`, portas 49002, 49003, 63093).
- A saída de `dotnet publish` do app tem **exatamente um arquivo**. Nada de arquivos soltos.
- Neste volume (exFAT) o macOS cria `._*`: passe **sempre** o caminho do `.csproj` nos comandos `dotnet`.
- Sentenças XGPS/XATT continuam com `CultureInfo.InvariantCulture` (não mexer).
- Assinatura: ECDSA P-256 sobre SHA-256, DER (`DSASignatureFormat.Rfc3279DerSequence`), em Base64 no `update.json.sig`.
- **Nunca ler, copiar ou imprimir a chave privada** (`~/2g-update-keys/update-signing.pem`, secret `UPDATE_SIGNING_KEY`). Testes geram pares descartáveis.
- O updater só instala nos gatilhos `Startup`, `Exit` e `Manual`; nunca por timer. Máximo de 2 tentativas por versão.
- Comentários e mensagens de commit em português, no estilo do repositório. Todo commit termina com:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`
- Versão alvo: `v1.4.0`. Linha de base: 149 testes passando no `PROD` (`daff59e`).

## Mapa de arquivos

Caminhos já com o nome novo (depois da Tarefa 1).

**Core — `src/TwoG.Connector.Core/`**

| Arquivo | Responsabilidade |
|---|---|
| `ProductIdentity.cs` | nomes do produto, legados e herdados do SO, num lugar só |
| `SettingsMigration.cs` | copiar `settings.json` da pasta da v1.3.0 |
| `ExeXmlDocument.cs` | transformação pura do `EXE.xml` (registrar, desregistrar, migrar legado) |
| `AppVersion.cs` | SemVer 2.0: parse e precedência |
| `UpdateManifest.cs` | `UpdateFile` e `UpdateManifest`: ler, validar e serializar o `update.json` |
| `UpdateSignature.cs` | assinar (ferramenta) e verificar (app) ECDSA |
| `UpdateKeys.cs` + `UpdateKeys/2026-09-11.pubkey` | chaves públicas aceitas, embutidas |
| `UpdatePolicy.cs` | gatilhos e decisão de instalar |
| `PendingUpdate.cs` | atualização baixada: registro e persistência |
| `InstallKindDetector.cs` | instalador × exe avulso × pasta sem escrita |
| `UpdateChecker.cs` | `UpdateFeed`, buscar, verificar, baixar, conferir, deixar pendente |

**WPF — `src/TwoG.Connector/`**

| Arquivo | Mudança |
|---|---|
| `Services/UpdateInstaller.cs` | novo: executa instalador ou troca o exe, limpa `.old` |
| `Services/UpdateService.cs` | novo: timer, gatilhos, estado para a UI |
| `Services/ExeXmlAutoStart.cs` | fica só com I/O, usa `ExeXmlDocument` |
| `App.xaml.cs` | nomes, mutex com `--updated`, gatilhos Startup/Exit/Manual |
| `Configuration/*`, `MainWindow.*`, `ViewModels/MainViewModel.cs`, `Services/SimConnect*.cs` | nomes e UI do updater |
| `TwoG.Connector.csproj` | `AssemblyName`, `Product`, `UpdateChannel` |

**Ferramenta — `tools/TwoG.Connector.ReleaseSigner/`**: `Program.cs` + `.csproj`.

**Distribuição:** `installer/setup.iss`, `.github/workflows/build.yml`.

**Testes — `tests/TwoG.Connector.Core.Tests/`:** `TestTempDir.cs` (helper), e um arquivo `*Tests.cs` por unidade do Core.

---

### Tarefa 1: Renomear `TwoG.GpsClient` para `TwoG.Connector` (mecânico)

Nada além de nomes muda nesta tarefa. O nome do exe (`AssemblyName = 2G-GPS-Cliente`) **não** muda aqui.

**Arquivos:** pastas e projetos de `src/`, `tests/`, a solução, `.github/workflows/build.yml`, `installer/setup.iss`, `README.md`, `CLAUDE.md`, `docs/estudo-multi-simulador.md`.

- [ ] **Passo 1: Mover pastas e arquivos de projeto**

```bash
git mv src/TwoG.GpsClient src/TwoG.Connector
git mv src/TwoG.GpsClient.Core src/TwoG.Connector.Core
git mv tests/TwoG.GpsClient.Core.Tests tests/TwoG.Connector.Core.Tests
git mv src/TwoG.Connector/TwoG.GpsClient.csproj src/TwoG.Connector/TwoG.Connector.csproj
git mv src/TwoG.Connector.Core/TwoG.GpsClient.Core.csproj src/TwoG.Connector.Core/TwoG.Connector.Core.csproj
git mv tests/TwoG.Connector.Core.Tests/TwoG.GpsClient.Core.Tests.csproj tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
git mv TwoG.GpsClient.slnx TwoG.Connector.slnx
rm -rf src/TwoG.Connector/obj src/TwoG.Connector/bin src/TwoG.Connector.Core/obj src/TwoG.Connector.Core/bin tests/TwoG.Connector.Core.Tests/obj tests/TwoG.Connector.Core.Tests/bin
```

- [ ] **Passo 2: Trocar o identificador no conteúdo, preservando mutex e evento**

A lookahead negativa mantém `TwoG.GpsClient.SingleInstance` e `TwoG.GpsClient.ShowWindow`, que são contrato com a v1.3.0. `docs/superpowers/` fica de fora porque descreve o nome antigo de propósito.

```bash
git grep -lz "TwoG\.GpsClient" -- ':!docs/superpowers' | xargs -0 perl -pi -e 's/TwoG\.GpsClient(?!\.SingleInstance|\.ShowWindow)/TwoG.Connector/g'
```

- [ ] **Passo 3: Conferir as sobras**

Run: `git grep -n "GpsClient" -- ':!docs/superpowers'`
Expected: exatamente 3 linhas — `MutexName` e `ShowEventName` em `src/TwoG.Connector/App.xaml.cs` e `AppMutex=` em `installer/setup.iss`.

Run: `git grep -n "Native\." -- src/TwoG.Connector`
Expected: os `LogicalName` do `.csproj` e as constantes do `SimConnectRuntime.cs` todos com `TwoG.Connector.Native.` — se um lado tiver o nome antigo, o SimConnect não carrega.

- [ ] **Passo 4: Build, testes e publish**

```bash
dotnet build src/TwoG.Connector/TwoG.Connector.csproj
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
dotnet publish src/TwoG.Connector/TwoG.Connector.csproj -c Release -o /tmp/2gc-pub-t1 && ls /tmp/2gc-pub-t1
```

Expected: build sem erros; 149 testes passando; publish com um único arquivo, `2G-GPS-Cliente.exe`.

- [ ] **Passo 5: Commit**

```bash
git add -A
git commit -m "Renomeia TwoG.GpsClient para TwoG.Connector (mecânico)

Pastas, projetos, namespaces, solução e caminhos no CI e no instalador.
Nenhuma mudança de comportamento. O mutex e o evento de instância única
mantêm o nome antigo de propósito: são o que faz a v1.3.0 e as versões
seguintes se enxergarem.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarefa 2: Identidade do produto e migração da configuração

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ProductIdentity.cs`
- Criar: `src/TwoG.Connector.Core/SettingsMigration.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/TestTempDir.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ProductIdentityTests.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/SettingsMigrationTests.cs`
- Modificar: `src/TwoG.Connector/App.xaml.cs`, `Configuration/SettingsService.cs`, `Configuration/AppSettings.cs`, `Services/SimConnectService.cs`, `Services/SimConnectRuntime.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`

**Interfaces:**
- Produz: `ProductIdentity` (constantes `Name`, `LegacyName`, `ExeFileName`, `SetupFileName`, `DataFolderName`, `LegacyDataFolderName`, `DefaultDeviceName`, `GitHubRepository`, `SingleInstanceMutexName`, `ShowWindowEventName`); `SettingsMigration.FileName`, `SettingsMigration.CopyLegacyIfMissing(string currentDir, string legacyDir) : bool`; helper de teste `TestTempDir` (`Path`, `Sub(string)`).

- [ ] **Passo 1: Helper de diretório temporário para os testes**

`tests/TwoG.Connector.Core.Tests/TestTempDir.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

/// <summary>Pasta temporária exclusiva por teste, apagada no Dispose.</summary>
internal sealed class TestTempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "2gconn-" + Guid.NewGuid().ToString("N"));

    public TestTempDir() => Directory.CreateDirectory(Path);

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Arquivo preso no Windows: sobra na pasta temporária, não quebra o teste.
        }
    }
}
```

- [ ] **Passo 2: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ProductIdentityTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class ProductIdentityTests
{
    /// <summary>
    /// Mudar estes nomes faz a v1.3.0 e a versão atual subirem juntas e
    /// transmitirem em dobro. Se este teste falhar, releia o spec antes de "corrigir".
    /// </summary>
    [Fact]
    public void LegacyWindowsObjectNamesArePinned()
    {
        Assert.Equal(@"Local\TwoG.GpsClient.SingleInstance", ProductIdentity.SingleInstanceMutexName);
        Assert.Equal(@"Local\TwoG.GpsClient.ShowWindow", ProductIdentity.ShowWindowEventName);
    }

    [Fact]
    public void LegacyNamesMatchWhatV130WroteToDisk()
    {
        Assert.Equal("2G GPS Cliente", ProductIdentity.LegacyName);
        Assert.Equal("2G GPS Cliente", ProductIdentity.LegacyDataFolderName);
    }

    [Fact]
    public void DefaultDeviceNameSurvivesSanitization()
    {
        Assert.Equal(ProductIdentity.DefaultDeviceName,
            XgpsSentences.SanitizeDeviceName(ProductIdentity.DefaultDeviceName));
    }
}
```

`tests/TwoG.Connector.Core.Tests/SettingsMigrationTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void CopiesLegacySettingsWhenOnlyTheyExist()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("2G GPS Cliente");
        var current = tmp.Sub("2G Connector");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"DeviceName":"2G GPS"}""");

        Assert.True(SettingsMigration.CopyLegacyIfMissing(current, legacy));
        Assert.Equal("""{"DeviceName":"2G GPS"}""", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void LeavesLegacyFileIntactForRollback()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("old");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        SettingsMigration.CopyLegacyIfMissing(tmp.Sub("new"), legacy);

        Assert.True(File.Exists(Path.Combine(legacy, "settings.json")));
    }

    [Fact]
    public void NeverOverwritesExistingSettings()
    {
        using var tmp = new TestTempDir();
        var legacy = tmp.Sub("old");
        var current = tmp.Sub("new");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"DeviceName":"antigo"}""");
        File.WriteAllText(Path.Combine(current, "settings.json"), """{"DeviceName":"novo"}""");

        Assert.False(SettingsMigration.CopyLegacyIfMissing(current, legacy));
        Assert.Equal("""{"DeviceName":"novo"}""", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void DoesNothingOnFreshInstall()
    {
        using var tmp = new TestTempDir();
        var current = tmp.Sub("new");

        Assert.False(SettingsMigration.CopyLegacyIfMissing(current, tmp.Sub("old")));
        Assert.False(Directory.Exists(current));
    }
}
```

- [ ] **Passo 3: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `ProductIdentity` e `SettingsMigration` não existem.

- [ ] **Passo 4: Implementar**

`src/TwoG.Connector.Core/ProductIdentity.cs`:

```csharp
namespace TwoG.Connector.Core;

/// <summary>
/// Nomes do produto num lugar só. Separa o que é marca (pode mudar) do que é
/// contrato com o sistema operacional ou com a v1.3.0 (não pode).
/// </summary>
public static class ProductIdentity
{
    public const string Name = "2G Connector";

    /// <summary>Nome até a v1.3.0. Usado só para migrar o que ela deixou no sistema.</summary>
    public const string LegacyName = "2G GPS Cliente";

    public const string ExeFileName = "2G-Connector.exe";
    public const string SetupFileName = "2G-Connector-Setup.exe";

    /// <summary>Pasta em %APPDATA% e %LOCALAPPDATA%.</summary>
    public const string DataFolderName = Name;
    public const string LegacyDataFolderName = LegacyName;

    /// <summary>Nome nas sentenças XGPS para quem não tem configuração gravada.</summary>
    public const string DefaultDeviceName = "2G Connector";

    public const string GitHubRepository = "galenoferreira/gps_connector";

    // ── Herdados da v1.3.0: NUNCA mudar ────────────────────────────────
    // São a forma de a v1.3.0 e as versões seguintes se enxergarem. Com os
    // mesmos nomes, se as duas forem lançadas (entrada antiga no EXE.xml, atalho
    // velho), só uma sobe; com nomes diferentes, as duas transmitiriam em dobro.
    public const string SingleInstanceMutexName = @"Local\TwoG.GpsClient.SingleInstance";
    public const string ShowWindowEventName = @"Local\TwoG.GpsClient.ShowWindow";
}
```

`src/TwoG.Connector.Core/SettingsMigration.cs`:

```csharp
namespace TwoG.Connector.Core;

/// <summary>
/// Leva a configuração da v1.3.0 para a pasta nova, uma vez. A antiga fica
/// intacta para que voltar à v1.3.0 continue funcionando.
/// </summary>
public static class SettingsMigration
{
    public const string FileName = "settings.json";

    /// <summary>
    /// Copia <c>settings.json</c> de <paramref name="legacyDir"/> para
    /// <paramref name="currentDir"/> quando só existe o antigo. Devolve true se copiou.
    /// </summary>
    public static bool CopyLegacyIfMissing(string currentDir, string legacyDir)
    {
        var current = Path.Combine(currentDir, FileName);
        var legacy = Path.Combine(legacyDir, FileName);
        if (File.Exists(current) || !File.Exists(legacy))
            return false;

        try
        {
            Directory.CreateDirectory(currentDir);
            File.Copy(legacy, current, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            // Outro processo criou o arquivo no meio do caminho: vale o que está lá.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Passo 5: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS, 7 testes novos.

- [ ] **Passo 6: Usar a identidade no app**

`Configuration/SettingsService.cs` — construtor e fallback do nome:

```csharp
    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, Core.ProductIdentity.DataFolderName);
        // Primeira execução depois da v1.3.0: traz a configuração antiga.
        Core.SettingsMigration.CopyLegacyIfMissing(dir, Path.Combine(appData, Core.ProductIdentity.LegacyDataFolderName));
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, Core.SettingsMigration.FileName);
    }
```

e em `Sanitize`: `if (s.DeviceName.Length == 0) s.DeviceName = Core.ProductIdentity.DefaultDeviceName;`

`Configuration/AppSettings.cs`:

```csharp
/// <summary>Configurações persistidas em %APPDATA%\2G Connector\settings.json.</summary>
```

```csharp
    public string DeviceName { get; set; } = Core.ProductIdentity.DefaultDeviceName;
```

`App.xaml.cs` — constantes (o `using TwoG.Connector.Core;` já existe):

```csharp
    private const string MutexName = ProductIdentity.SingleInstanceMutexName;
    private const string ShowEventName = ProductIdentity.ShowWindowEventName;
```

e em `ReportFatal`, trocar as duas ocorrências de `"2G GPS Cliente"` por `ProductIdentity.DataFolderName` (pasta do log) e `ProductIdentity.Name` (título do `MessageBox`).

`Services/SimConnectService.cs`: `private const string ClientName = ProductIdentity.Name;`

`Services/SimConnectRuntime.cs`: acrescentar `using TwoG.Connector.Core;`; trocar `"2G GPS Cliente", "runtime", version` por `ProductIdentity.DataFolderName, "runtime", version`; no comentário da classe, `%LOCALAPPDATA%\2G GPS Cliente\runtime\` vira `%LOCALAPPDATA%\2G Connector\runtime\`.

`MainWindow.xaml`: `Title="2G Connector"`; `ToolTipText="2G Connector"`; no cabeçalho, `Text="2G GPS Cliente"` vira `Text="2G Connector"` e `Text="for Microsoft Flight Simulator"` vira `Text="MSFS • Prepar3D • X-Plane"`.

`MainWindow.xaml.cs`: acrescentar `using TwoG.Connector.Core;` e trocar `TrayIcon.ShowBalloonTip("2G GPS Cliente",` por `TrayIcon.ShowBalloonTip(ProductIdentity.Name,`.

- [ ] **Passo 7: Conferir sobras e compilar**

Run: `git grep -n "2G GPS Cliente" -- src`
Expected: só `ProductIdentity.cs` (2), `Services/ExeXmlAutoStart.cs` (`AddonName`, Tarefa 3), `EfbAnnouncement.cs` (comentário — trocar para "2G Connector" agora) e `TwoG.Connector.csproj` (`Product`, Tarefa 4).

```bash
dotnet build src/TwoG.Connector/TwoG.Connector.csproj
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
```

Expected: build limpo, 156 testes passando.

- [ ] **Passo 8: Commit**

```bash
git add -A
git commit -m "Identidade 2G Connector e migração da configuração da v1.3.0

ProductIdentity concentra os nomes do produto e separa o que é marca do
que é contrato com o Windows e com a v1.3.0. A configuração é copiada da
pasta antiga na primeira execução; a original fica intacta para permitir
voltar de versão. Quem tinha nome de dispositivo gravado continua com ele.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 3: `EXE.xml` — transformação no Core e migração da entrada legada

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/ExeXmlDocument.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/ExeXmlDocumentTests.cs`
- Modificar: `src/TwoG.Connector/Services/ExeXmlAutoStart.cs`

**Interfaces:**
- Consome: `ProductIdentity.Name`, `ProductIdentity.LegacyName`.
- Produz: `ExeXmlDocument.AddonName`, `ExeXmlDocument.LegacyAddonNames`, `ExeXmlDocument.LaunchCommandLine`, `enum ExeXmlDocument.Outcome { Changed, Unchanged, Malformed }`, `ExeXmlDocument.CreateEmpty() : XDocument`, `ExeXmlDocument.Register(XDocument doc, string exePath) : Outcome`, `ExeXmlDocument.Unregister(XDocument doc) : Outcome`.

Melhoria embutida: `Register` devolve `Unchanged` quando a entrada já está certa, e o `ExeXmlAutoStart` deixa de regravar o `EXE.xml` (e criar backup) a cada partida do app.

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/ExeXmlDocumentTests.cs`:

```csharp
using System.Xml.Linq;

namespace TwoG.Connector.Core.Tests;

public class ExeXmlDocumentTests
{
    private const string NewExe = @"C:\Users\p\AppData\Local\Programs\2G Connector\2G-Connector.exe";

    /// <summary>EXE.xml real de quem tem FSUIPC e a v1.3.0 instalados.</summary>
    private const string WithLegacyAndFsuipc = """
        <?xml version="1.0" encoding="utf-8"?>
        <SimBase.Document Type="Launch" version="1,0">
          <Descr>Launch</Descr>
          <Filename>EXE.xml</Filename>
          <Disabled>False</Disabled>
          <Launch.Addon>
            <Name>FSUIPC7</Name>
            <Disabled>False</Disabled>
            <Path>C:\FSUIPC7\FSUIPC7.exe</Path>
          </Launch.Addon>
          <Launch.Addon>
            <Name>2G GPS Cliente</Name>
            <Disabled>False</Disabled>
            <Path>C:\Users\p\Downloads\2G-GPS-Cliente.exe</Path>
            <CommandLine>-minimized</CommandLine>
          </Launch.Addon>
        </SimBase.Document>
        """;

    private static List<string?> Names(XDocument doc) =>
        doc.Root!.Elements("Launch.Addon").Select(a => (string?)a.Element("Name")).ToList();

    private static XElement Ours(XDocument doc) =>
        doc.Root!.Elements("Launch.Addon").Single(a => (string?)a.Element("Name") == "2G Connector");

    [Fact]
    public void Register_OnEmptyDocument_AddsOurEntry()
    {
        var doc = ExeXmlDocument.CreateEmpty();

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        var ours = Ours(doc);
        Assert.Equal(NewExe, (string?)ours.Element("Path"));
        Assert.Equal("False", (string?)ours.Element("Disabled"));
        Assert.Equal("-minimized", (string?)ours.Element("CommandLine"));
    }

    [Fact]
    public void Register_ReplacesLegacyEntry_AndKeepsOtherAddons()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc);

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(["FSUIPC7", "2G Connector"], Names(doc));
        Assert.Equal(@"C:\FSUIPC7\FSUIPC7.exe",
            (string?)doc.Root!.Elements("Launch.Addon").First().Element("Path"));
    }

    [Fact]
    public void Register_LegacyNameMatchIsCaseInsensitive()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc.Replace("2G GPS Cliente", "2g gps cliente"));

        ExeXmlDocument.Register(doc, NewExe);

        Assert.DoesNotContain("2g gps cliente", Names(doc));
    }

    [Fact]
    public void Register_WhenAlreadyCurrent_IsUnchanged()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, NewExe);

        Assert.Equal(ExeXmlDocument.Outcome.Unchanged, ExeXmlDocument.Register(doc, NewExe));
    }

    [Fact]
    public void Register_WhenExeMoved_UpdatesPathInPlace()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, @"C:\antigo\2G-Connector.exe");

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));

        Assert.Equal(NewExe, (string?)Ours(doc).Element("Path"));
        Assert.Single(Names(doc));
    }

    [Fact]
    public void Register_CollapsesDuplicateEntriesOfOurs()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        doc.Root!.Add(
            new XElement("Launch.Addon", new XElement("Name", "2G Connector"), new XElement("Path", "a")),
            new XElement("Launch.Addon", new XElement("Name", "2G Connector"), new XElement("Path", "b")));

        ExeXmlDocument.Register(doc, NewExe);

        Assert.Equal(NewExe, (string?)Ours(doc).Element("Path"));
    }

    [Fact]
    public void Register_ReenablesADisabledEntry()
    {
        var doc = ExeXmlDocument.CreateEmpty();
        ExeXmlDocument.Register(doc, NewExe);
        Ours(doc).Element("Disabled")!.Value = "True";

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Register(doc, NewExe));
        Assert.Equal("False", (string?)Ours(doc).Element("Disabled"));
    }

    [Fact]
    public void Register_OnUnexpectedRoot_IsMalformed_AndDoesNotTouchIt()
    {
        var doc = XDocument.Parse("<Outra.Coisa><Launch.Addon><Name>X</Name></Launch.Addon></Outra.Coisa>");
        var before = doc.ToString();

        Assert.Equal(ExeXmlDocument.Outcome.Malformed, ExeXmlDocument.Register(doc, NewExe));
        Assert.Equal(before, doc.ToString());
    }

    [Fact]
    public void Unregister_RemovesOursAndLegacy_KeepsOthers()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc);
        ExeXmlDocument.Register(doc, NewExe);
        doc.Root!.Add(new XElement("Launch.Addon", new XElement("Name", "2G GPS Cliente")));

        Assert.Equal(ExeXmlDocument.Outcome.Changed, ExeXmlDocument.Unregister(doc));

        Assert.Equal(["FSUIPC7"], Names(doc));
    }

    [Fact]
    public void Unregister_WithNothingOfOurs_IsUnchanged()
    {
        var doc = XDocument.Parse(WithLegacyAndFsuipc.Replace("2G GPS Cliente", "Outro Addon"));

        Assert.Equal(ExeXmlDocument.Outcome.Unchanged, ExeXmlDocument.Unregister(doc));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `ExeXmlDocument` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/ExeXmlDocument.cs`:

```csharp
using System.Xml.Linq;

namespace TwoG.Connector.Core;

/// <summary>
/// Transformação pura do EXE.xml do MSFS (lista de add-ons lançados com o
/// simulador). Separada do I/O para ser testável: um EXE.xml quebrado impede o
/// MSFS de abrir, e a migração da entrada da v1.3.0 mexe justamente nele.
///
/// Só toca nós <c>Launch.Addon</c> com o nosso nome ou com o nome legado;
/// entradas de outros add-ons nunca são alteradas.
/// </summary>
public static class ExeXmlDocument
{
    public const string AddonName = ProductIdentity.Name;

    /// <summary>Nomes com que versões anteriores se registraram.</summary>
    public static readonly IReadOnlyList<string> LegacyAddonNames = [ProductIdentity.LegacyName];

    public const string LaunchCommandLine = "-minimized";

    public enum Outcome
    {
        /// <summary>O documento foi alterado e precisa ser gravado.</summary>
        Changed,

        /// <summary>Já estava como deveria: não grave (evita backup e escrita à toa).</summary>
        Unchanged,

        /// <summary>Estrutura inesperada: não grave de jeito nenhum.</summary>
        Malformed,
    }

    /// <summary>Documento novo, para quando o EXE.xml ainda não existe.</summary>
    public static XDocument CreateEmpty() =>
        new(new XElement("SimBase.Document",
            new XAttribute("Type", "Launch"),
            new XAttribute("version", "1,0"),
            new XElement("Descr", "Launch"),
            new XElement("Filename", "EXE.xml"),
            new XElement("Disabled", "False")));

    /// <summary>
    /// Garante uma única entrada nossa, habilitada, apontando para
    /// <paramref name="exePath"/>, e remove as entradas legadas.
    /// </summary>
    public static Outcome Register(XDocument doc, string exePath)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SimBase.Document")
            return Outcome.Malformed;

        var changed = RemoveAddons(root, LegacyAddonNames);

        var ours = FindAddons(root, [AddonName]).ToList();
        // Duplicatas nossas (edição manual, versões antigas) viram uma só.
        foreach (var extra in ours.Skip(1))
        {
            extra.Remove();
            changed = true;
        }

        var current = ours.FirstOrDefault();
        if (current is not null && IsUpToDate(current, exePath))
            return changed ? Outcome.Changed : Outcome.Unchanged;

        if (current is null)
        {
            current = new XElement("Launch.Addon");
            root.Add(current);
        }

        // Reescreve apenas o NOSSO nó, na ordem convencional (FSUIPC).
        current.RemoveAll();
        current.Add(
            new XElement("Name", AddonName),
            new XElement("Disabled", "False"),
            new XElement("Path", exePath),
            new XElement("CommandLine", LaunchCommandLine));
        return Outcome.Changed;
    }

    /// <summary>Remove a nossa entrada e as legadas (usado pelo desinstalador).</summary>
    public static Outcome Unregister(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SimBase.Document")
            return Outcome.Malformed;

        return RemoveAddons(root, [AddonName, .. LegacyAddonNames])
            ? Outcome.Changed
            : Outcome.Unchanged;
    }

    private static bool IsUpToDate(XElement addon, string exePath) =>
        string.Equals((string?)addon.Element("Path"), exePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals((string?)addon.Element("Disabled"), "False", StringComparison.OrdinalIgnoreCase)
        && (string?)addon.Element("CommandLine") == LaunchCommandLine;

    private static IEnumerable<XElement> FindAddons(XElement root, IReadOnlyCollection<string> names) =>
        root.Elements("Launch.Addon").Where(a =>
            names.Contains((string?)a.Element("Name") ?? "", StringComparer.OrdinalIgnoreCase));

    private static bool RemoveAddons(XElement root, IReadOnlyCollection<string> names)
    {
        var matches = FindAddons(root, names).ToList();
        foreach (var addon in matches)
            addon.Remove();
        return matches.Count > 0;
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS, 10 testes novos (166 no total).

- [ ] **Passo 5: `ExeXmlAutoStart` fica só com o I/O**

Em `src/TwoG.Connector/Services/ExeXmlAutoStart.cs`:

1. Acrescentar `using TwoG.Connector.Core;`.
2. Remover as constantes `AddonName` e `LaunchCommandLine` e o método `FindOurAddon`.
3. No comentário da classe, trocar "mexe apenas no nó com o nosso Name" por "mexe apenas no nó com o nosso Name e no da v1.3.0, que é migrado (ver ExeXmlDocument)".
4. Substituir `Register` e `Unregister` inteiros por:

```csharp
    private SyncResult Register(SimInstall install)
    {
        var exePath = ExePath;
        if (exePath is null)
            return new SyncResult(install.DisplayName, false, "caminho do executável indisponível");

        XDocument doc;
        if (File.Exists(install.ExeXmlPath))
        {
            var loaded = TryLoad(install.ExeXmlPath);
            if (loaded is null)
                return new SyncResult(install.DisplayName, false,
                    "EXE.xml existente está malformado — não modificado por segurança");
            doc = loaded;
        }
        else
        {
            doc = ExeXmlDocument.CreateEmpty();
        }

        switch (ExeXmlDocument.Register(doc, exePath))
        {
            case ExeXmlDocument.Outcome.Malformed:
                return new SyncResult(install.DisplayName, false,
                    "EXE.xml existente tem estrutura inesperada — não modificado por segurança");
            case ExeXmlDocument.Outcome.Unchanged:
                return new SyncResult(install.DisplayName, true, "já registrado");
            default:
                Save(doc, install.ExeXmlPath);
                return new SyncResult(install.DisplayName, true, "registrado");
        }
    }

    private SyncResult Unregister(SimInstall install)
    {
        if (!File.Exists(install.ExeXmlPath))
            return new SyncResult(install.DisplayName, true, "sem registro");

        var doc = TryLoad(install.ExeXmlPath);
        if (doc is null)
            return new SyncResult(install.DisplayName, false,
                "EXE.xml malformado — não modificado por segurança");

        switch (ExeXmlDocument.Unregister(doc))
        {
            case ExeXmlDocument.Outcome.Malformed:
                return new SyncResult(install.DisplayName, false,
                    "EXE.xml tem estrutura inesperada — não modificado por segurança");
            case ExeXmlDocument.Outcome.Unchanged:
                return new SyncResult(install.DisplayName, true, "sem registro");
            default:
                Save(doc, install.ExeXmlPath);
                return new SyncResult(install.DisplayName, true, "registro removido");
        }
    }
```

`TryLoad`, `Save` (backup, UTF-8 sem BOM, escrita atômica) e o registro do `CodePagesEncodingProvider` ficam como estão.

- [ ] **Passo 6: Compilar**

Run: `dotnet build src/TwoG.Connector/TwoG.Connector.csproj`
Expected: build limpo. `git grep -n "AddonName" -- src/TwoG.Connector` não encontra nada.

- [ ] **Passo 7: Commit**

```bash
git add -A
git commit -m "Move a transformação do EXE.xml para o Core e migra a entrada da v1.3.0

ExeXmlDocument registra a entrada \"2G Connector\" e remove a legada
\"2G GPS Cliente\", preservando os outros add-ons. O desregistro remove os
dois nomes. Um EXE.xml quebrado impede o MSFS de abrir, e essa lógica não
tinha teste nenhum; agora tem dez.

De quebra, o autorreparo deixa de regravar o arquivo e criar backup a
cada partida quando a entrada já está certa.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 4: Distribuição com os nomes novos e limpeza da v1.3.0 no instalador

Troca o nome do exe, do instalador e dos arquivos do release, e ensina o instalador a limpar o que a v1.3.0 deixou. Esta tarefa não tem teste automatizado local: o `iscc` só existe no CI. A validação vem no build do PR (Tarefa 12).

**Arquivos:**
- Modificar: `src/TwoG.Connector/TwoG.Connector.csproj`
- Modificar: `.github/workflows/build.yml`
- Modificar: `installer/setup.iss`

- [ ] **Passo 1: Nome do exe e metadados**

Em `src/TwoG.Connector/TwoG.Connector.csproj`:

```xml
    <AssemblyName>2G-Connector</AssemblyName>
```

```xml
    <Product>2G Connector</Product>
```

```xml
    <Description>Ponte entre simuladores de voo (MSFS, Prepar3D, X-Plane) e EFBs — transmite posição via XGPS para 2G Pilot, ForeFlight e outros.</Description>
```

- [ ] **Passo 2: Conferir o publish**

Run: `dotnet publish src/TwoG.Connector/TwoG.Connector.csproj -c Release -o /tmp/2gc-pub-t4 && ls /tmp/2gc-pub-t4`
Expected: um único arquivo, `2G-Connector.exe`.

- [ ] **Passo 3: Nomes no CI e cópias com o nome antigo**

Em `.github/workflows/build.yml`, o bloco `env:` passa a ser:

```yaml
env:
  # Nomes SEM versão: são o alvo dos links permanentes
  # https://github.com/<owner>/<repo>/releases/latest/download/<nome>
  # que sempre apontam para o release mais recente. Se a versão entrar no nome
  # do arquivo, esses links quebram a cada release.
  EXE_NAME: 2G-Connector.exe
  ZIP_NAME: 2G-Connector.zip
  SETUP_NAME: 2G-Connector-Setup.exe
  # Nomes da v1.3.0 — ver o passo "Stage release assets".
  LEGACY_EXE_NAME: 2G-GPS-Cliente.exe
  LEGACY_ZIP_NAME: 2G-GPS-Cliente.zip
  LEGACY_SETUP_NAME: 2G-GPS-Cliente-Setup.exe
```

No passo `Stage release assets`, logo depois do `if (-not (Test-Path "dist\$env:SETUP_NAME")) { ... }` e antes do comentário `# Checksums`, acrescentar:

```powershell
          # Cópias com o nome da v1.3.0, para link salvo e página em cache não darem
          # 404. Só na linha 1.4.x: somem sozinhas a partir da v1.5.0.
          if ($env:VERSION -like '1.4.*') {
            Copy-Item "dist\$env:EXE_NAME"   "dist\$env:LEGACY_EXE_NAME"
            Copy-Item "dist\$env:ZIP_NAME"   "dist\$env:LEGACY_ZIP_NAME"
            Copy-Item "dist\$env:SETUP_NAME" "dist\$env:LEGACY_SETUP_NAME"
          }
```

No `actions/upload-artifact`, `name: 2G-GPS-Cliente-${{ env.VERSION }}` vira `name: 2G-Connector-${{ env.VERSION }}`.

No `body:` do `Publish release`, a tabela passa a ser:

```markdown
            | Arquivo | Para quem |
            |---|---|
            | [`2G-Connector.exe`](https://github.com/${{ github.repository }}/releases/latest/download/2G-Connector.exe) | **Recomendado** — executável único, sem instalação |
            | [`2G-Connector.zip`](https://github.com/${{ github.repository }}/releases/latest/download/2G-Connector.zip) | Mesmo executável, compactado |
            | [`2G-Connector-Setup.exe`](https://github.com/${{ github.repository }}/releases/latest/download/2G-Connector-Setup.exe) | Instalador com atalho no Menu Iniciar e desinstalador |

            Requer Windows 10/11 x64 e MSFS 2020/2024, Prepar3D v4+ ou X-Plane 11/12. Não precisa do runtime .NET.
            A partir desta versão o app se atualiza sozinho, sempre fora de voo.
```

(o parágrafo sobre o aviso "O Windows protegeu seu computador" permanece.)

- [ ] **Passo 4: Nomes no instalador**

Em `installer/setup.iss`, o cabeçalho de comentário passa a dizer `2G Connector — instalador (Inno Setup 6)`, e os `#define` ficam:

```
#define MyAppName "2G Connector"
#define MyAppShortName "2G Connector"
#define MyAppPublisher "2G"
#define MyAppExeName "2G-Connector.exe"

; Nomes da v1.3.0, só para limpar o que ela deixou ([InstallDelete] e [Registry]).
#define LegacyAppName "2G GPS Cliente for MSFS"
#define LegacyShortName "2G GPS Cliente"
#define LegacyExeName "2G-GPS-Cliente.exe"
```

Na seção `[Setup]`:

```
; Mesmo AppId da v1.3.0: é o que faz o instalador ATUALIZAR em vez de instalar ao lado.
AppId={{B23B9502-7C57-45EF-9075-D53016835238}
```

```
OutputBaseFilename=2G-Connector-Setup
```

```
; Nome herdado da v1.3.0 de propósito — ver ProductIdentity.SingleInstanceMutexName.
AppMutex=Local\TwoG.GpsClient.SingleInstance
```

- [ ] **Passo 5: Limpeza da v1.3.0 na atualização**

Logo depois da seção `[Files]`:

```
[InstallDelete]
; Atualização sobre a v1.3.0: o exe e os atalhos com o nome antigo. Sem isto o
; atalho velho do Menu Iniciar continuaria lá, apontando para um exe que sumiu.
Type: files; Name: "{app}\{#LegacyExeName}"
Type: files; Name: "{autoprograms}\{#LegacyAppName}.lnk"
Type: files; Name: "{autodesktop}\{#LegacyShortName}.lnk"
```

Na seção `[Registry]`, depois da linha existente:

```
; Valor da v1.3.0 na chave Run: removido sempre, marcada ou não a tarefa nova.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#LegacyShortName}"; Flags: deletevalue
```

O `[UninstallRun]` com `-unregister` não muda: desde a Tarefa 3 ele remove as duas entradas do `EXE.xml`.

- [ ] **Passo 6: Commit**

```bash
git add -A
git commit -m "Distribui como 2G Connector e limpa o que a v1.3.0 deixou

Exe, zip e instalador com os nomes novos. Na linha 1.4.x o release
publica também cópias com os nomes antigos, para link salvo não dar 404.
Na atualização o instalador apaga o exe e os atalhos antigos e o valor
legado da chave Run. AppId e AppMutex continuam os mesmos de propósito.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 5: `AppVersion` — versões SemVer

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/AppVersion.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/AppVersionTests.cs`

**Interfaces:**
- Produz: `sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>` com `Major`, `Minor`, `Patch`, `PreRelease : IReadOnlyList<string>`, `IsPreRelease`, `static bool TryParse(string?, out AppVersion?)`, `static AppVersion Parse(string)`, `CompareTo`, `ToString()` (sem `v` e sem `+build`), operadores `>` e `<`.

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/AppVersionTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("v1.4.0", "1.4.0")]
    [InlineData("1.4.0+a1b2c3d", "1.4.0")]          // InformationalVersion do SDK
    [InlineData("1.4.1-rc.1", "1.4.1-rc.1")]
    [InlineData("1.0.0-ci.42", "1.0.0-ci.42")]      // builds de branch do CI
    [InlineData(" 2.0.0 ", "2.0.0")]
    public void ParsesAndNormalizes(string text, string expected)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(expected, v!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.4")]
    [InlineData("1.4.0.0")]
    [InlineData("01.4.0")]
    [InlineData("1.4.0-")]
    [InlineData("1.4.0-rc..1")]
    [InlineData("1.4.0-01")]
    [InlineData("1.4.0-rc_1")]
    [InlineData("abc")]
    [InlineData("-1.4.0")]
    public void RejectsInvalid(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    /// <summary>Ordem do exemplo da especificação SemVer 2.0, mais casos nossos.</summary>
    [Fact]
    public void FollowsSemVerPrecedence()
    {
        string[] ascending =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0",
            "1.4.0", "1.4.1-rc.1", "1.4.1", "1.10.0", "2.0.0",
        ];

        for (var i = 0; i < ascending.Length - 1; i++)
        {
            var lower = AppVersion.Parse(ascending[i]);
            var higher = AppVersion.Parse(ascending[i + 1]);
            Assert.True(lower < higher, $"{lower} deveria ser menor que {higher}");
            Assert.True(higher > lower, $"{higher} deveria ser maior que {lower}");
        }
    }

    [Fact]
    public void BuildMetadataDoesNotAffectEquality()
    {
        var a = AppVersion.Parse("1.4.0+aaa");
        var b = AppVersion.Parse("1.4.0+bbb");
        Assert.Equal(a, b);
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ParseThrowsOnInvalid()
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse("não é versão"));
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `AppVersion` não existe.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/AppVersion.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TwoG.Connector.Core;

/// <summary>
/// Versão SemVer 2.0 (maior.menor.correção[-pré-release][+build]), com a regra de
/// precedência da especificação. O "+build" é ignorado: o SDK do .NET acrescenta o
/// hash do commit à InformationalVersion, e ele não diz nada sobre ordem.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Identificadores de pré-release ("rc", "1"); vazio numa versão final.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    private AppVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versão inválida: {text}");

    public static bool TryParse(string? text, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;

        var parts = core.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
            return false;

        string[] preRelease = [];
        if (dash >= 0)
        {
            preRelease = s[(dash + 1)..].Split('.');
            if (!preRelease.All(IsValidIdentifier))
                return false;
        }

        version = new AppVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>Número sem sinal e sem zero à esquerda ("0" vale, "01" não).</summary>
    private static bool TryParseNumber(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && (s.Length == 1 || s[0] != '0');

    private static bool IsValidIdentifier(string id)
    {
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return false;
        // Identificador numérico com zero à esquerda é inválido na SemVer.
        return !(id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit));
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;

        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A versão final é maior que qualquer pré-release dela.
        if (!IsPreRelease)
            return other.IsPreRelease ? 1 : 0;
        if (!other.IsPreRelease)
            return -1;

        for (var i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aIsNumber = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
        var bIsNumber = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
        if (aIsNumber && bIsNumber) return an.CompareTo(bn);
        if (aIsNumber) return -1;   // numérico tem precedência menor que alfanumérico
        if (bIsNumber) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as AppVersion);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}"
            : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS (5 métodos de teste novos, 20 casos).

- [ ] **Passo 5: Commit**

```bash
git add -A
git commit -m "AppVersion: versões SemVer com a precedência da especificação

Base da regra do updater de só aceitar versão maior que a instalada.
Ignora o +build que o SDK acrescenta à InformationalVersion e rejeita
zeros à esquerda, como a SemVer exige.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 6: Manifesto, assinatura e chaves embutidas

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/UpdateManifest.cs`
- Criar: `src/TwoG.Connector.Core/UpdateSignature.cs`
- Criar: `src/TwoG.Connector.Core/UpdateKeys.cs`
- Criar: `src/TwoG.Connector.Core/UpdateKeys/2026-09-11.pubkey`
- Modificar: `src/TwoG.Connector.Core/TwoG.Connector.Core.csproj`
- Criar: `tests/TwoG.Connector.Core.Tests/UpdateManifestTests.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/UpdateSignatureTests.cs`

**Interfaces:**
- Consome: `AppVersion`.
- Produz:
  - `sealed record UpdateFile(string Sha256, long Size)` com `static UpdateFile FromFile(string path)`
  - `sealed record UpdateManifest(AppVersion Version, IReadOnlyDictionary<string, UpdateFile> Files)` com `static bool TryParse(byte[] json, out UpdateManifest? manifest, out string? error)` e `static byte[] Serialize(AppVersion version, IReadOnlyDictionary<string, UpdateFile> files)`
  - `UpdateSignature.Verify(byte[] data, string signatureBase64, IReadOnlyList<string> acceptedPublicKeysPem) : bool` e `UpdateSignature.Sign(byte[] data, string privateKeyPem) : string`
  - `UpdateKeys.Accepted : IReadOnlyList<string>`

A extensão `.pubkey` é de propósito: o `.gitignore` bloqueia `*.pem`, e a chave pública precisa ser versionada.

- [ ] **Passo 1: Arquivo da chave pública**

`src/TwoG.Connector.Core/UpdateKeys/2026-09-11.pubkey` (copiar exatamente; é a do spec):

```
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEkU39e98bOMDbzZsTK1VvFMqqFum/
HvGSzgEtgfETqNCJSdUisfjCt7BVr8WgX51D+sGc7Tz9iNdW6NOAE3Rm0Q==
-----END PUBLIC KEY-----
```

Conferir: `git check-ignore src/TwoG.Connector.Core/UpdateKeys/2026-09-11.pubkey` não imprime nada (arquivo não ignorado).

Em `src/TwoG.Connector.Core/TwoG.Connector.Core.csproj`, antes de `</Project>`:

```xml
  <!-- Chaves públicas aceitas para assinar atualizações (ver UpdateKeys.cs). -->
  <ItemGroup>
    <EmbeddedResource Include="UpdateKeys\*.pubkey"
                      LogicalName="TwoG.Connector.Core.UpdateKeys.%(Filename)%(Extension)" />
  </ItemGroup>
```

- [ ] **Passo 2: Testes que falham**

`tests/TwoG.Connector.Core.Tests/UpdateManifestTests.cs`:

```csharp
using System.Text;

namespace TwoG.Connector.Core.Tests;

public class UpdateManifestTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static bool Parse(string json, out UpdateManifest? manifest, out string? error) =>
        UpdateManifest.TryParse(Encoding.UTF8.GetBytes(json), out manifest, out error);

    [Fact]
    public void ParsesTheCiFormat()
    {
        var ok = Parse($$"""{"version":"1.5.0","files":{"2G-Connector.exe":{"sha256":"{{Hash}}","size":75517030}}}""",
            out var m, out var error);

        Assert.True(ok, error);
        Assert.Equal("1.5.0", m!.Version.ToString());
        Assert.Equal(new UpdateFile(Hash, 75517030), m.Files["2G-Connector.exe"]);
    }

    [Fact]
    public void FileLookupIgnoresCase_AndHashIsNormalizedToLowercase()
    {
        Parse($$"""{"version":"1.5.0","files":{"2G-Connector.exe":{"sha256":"{{Hash.ToUpperInvariant()}}","size":1}}}""",
            out var m, out _);

        Assert.Equal(Hash, m!.Files["2g-connector.EXE"].Sha256);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("""{"files":{}}""")]
    [InlineData("""{"version":"1.5","files":{}}""")]
    [InlineData("""{"version":"1.5.0"}""")]
    [InlineData("""{"version":"1.5.0","files":{}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"abc","size":1}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","size":0}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":{"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","size":"1"}}}""")]
    [InlineData("""{"version":"1.5.0","files":{"a.exe":"x"}}""")]
    public void RejectsMalformedManifests_WithoutThrowing(string json)
    {
        Assert.False(Parse(json, out var m, out var error));
        Assert.Null(m);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SerializeRoundTrips()
    {
        var files = new Dictionary<string, UpdateFile>
        {
            ["2G-Connector.exe"] = new(Hash, 10),
            ["2G-Connector-Setup.exe"] = new(Hash, 20),
        };

        var bytes = UpdateManifest.Serialize(AppVersion.Parse("1.5.0-rc.1"), files);

        Assert.True(UpdateManifest.TryParse(bytes, out var m, out var error), error);
        Assert.Equal("1.5.0-rc.1", m!.Version.ToString());
        Assert.Equal(20, m.Files["2G-Connector-Setup.exe"].Size);
    }

    [Fact]
    public void FromFileHashesAndMeasures()
    {
        using var tmp = new TestTempDir();
        var path = tmp.Sub("a.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

        var file = UpdateFile.FromFile(path);

        // SHA-256("abc"), vetor de teste do FIPS 180-2.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", file.Sha256);
        Assert.Equal(3, file.Size);
    }
}
```

`tests/TwoG.Connector.Core.Tests/UpdateSignatureTests.cs`:

```csharp
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
```

- [ ] **Passo 3: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `UpdateManifest`, `UpdateFile`, `UpdateSignature`, `UpdateKeys` não existem.

- [ ] **Passo 4: Implementar**

`src/TwoG.Connector.Core/UpdateManifest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>Um arquivo do release, como declarado no manifesto.</summary>
public sealed record UpdateFile(string Sha256, long Size)
{
    public static UpdateFile FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return new UpdateFile(Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }
}

/// <summary>
/// Manifesto publicado em cada release (update.json). Só deve ser interpretado
/// DEPOIS de a assinatura ter sido verificada — ver <see cref="UpdateSignature"/>.
/// </summary>
public sealed record UpdateManifest(AppVersion Version, IReadOnlyDictionary<string, UpdateFile> Files)
{
    public static bool TryParse(byte[] json, [NotNullWhen(true)] out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "manifesto não é um objeto JSON";
                return false;
            }

            if (!root.TryGetProperty("version", out var versionEl)
                || versionEl.ValueKind != JsonValueKind.String
                || !AppVersion.TryParse(versionEl.GetString(), out var version))
            {
                error = "campo \"version\" ausente ou inválido";
                return false;
            }

            if (!root.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Object)
            {
                error = "campo \"files\" ausente";
                return false;
            }

            var files = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in filesEl.EnumerateObject())
            {
                if (!TryReadFile(entry.Value, out var file))
                {
                    error = $"entrada inválida para \"{entry.Name}\"";
                    return false;
                }
                files[entry.Name] = file;
            }

            if (files.Count == 0)
            {
                error = "manifesto sem arquivos";
                return false;
            }

            manifest = new UpdateManifest(version, files);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON inválido: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadFile(JsonElement el, [NotNullWhen(true)] out UpdateFile? file)
    {
        file = null;
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty("sha256", out var hashEl) || hashEl.ValueKind != JsonValueKind.String
            || !el.TryGetProperty("size", out var sizeEl) || sizeEl.ValueKind != JsonValueKind.Number
            || !sizeEl.TryGetInt64(out var size) || size <= 0)
            return false;

        var hash = hashEl.GetString();
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            return false;

        file = new UpdateFile(hash.ToLowerInvariant(), size);
        return true;
    }

    /// <summary>Gera o update.json (usado pela ferramenta de release).</summary>
    public static byte[] Serialize(AppVersion version, IReadOnlyDictionary<string, UpdateFile> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("version", version.ToString());
            writer.WriteStartObject("files");
            foreach (var (name, file) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(name);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteNumber("size", file.Size);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
```

`src/TwoG.Connector.Core/UpdateSignature.cs`:

```csharp
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
```

`src/TwoG.Connector.Core/UpdateKeys.cs`:

```csharp
namespace TwoG.Connector.Core;

/// <summary>
/// Chaves públicas aceitas para assinar atualizações, vindas dos arquivos
/// UpdateKeys/*.pubkey embutidos no assembly. É uma lista, e não uma chave só,
/// para permitir rotação: publica-se uma versão que aceite a antiga e a nova antes
/// de trocar o secret do CI.
/// </summary>
public static class UpdateKeys
{
    private const string ResourcePrefix = "TwoG.Connector.Core.UpdateKeys.";

    public static IReadOnlyList<string> Accepted { get; } = Load();

    private static string[] Load()
    {
        var assembly = typeof(UpdateKeys).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".pubkey", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            })
            .ToArray();
    }
}
```

- [ ] **Passo 5: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS. Se `ShipsExactlyTheMaintainerKey` falhar com lista vazia, o `EmbeddedResource` não entrou — conferir o `LogicalName` no `.csproj`.

- [ ] **Passo 6: Commit**

```bash
git add -A
git commit -m "Manifesto de atualização, assinatura ECDSA e chave pública embutida

UpdateManifest lê, valida e serializa o update.json; UpdateSignature
verifica ECDSA P-256 contra a lista de chaves aceitas e nunca lança com
entrada ruim. A chave pública do mantenedor entra como recurso embutido,
numa lista, para permitir rotação sem quebrar quem já está instalado.

Testes usam pares de chaves descartáveis gerados no próprio teste.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 7: Política de instalação, atualização pendente e tipo de instalação

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/UpdatePolicy.cs`
- Criar: `src/TwoG.Connector.Core/PendingUpdate.cs`
- Criar: `src/TwoG.Connector.Core/InstallKindDetector.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/UpdatePolicyTests.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/PendingUpdateStoreTests.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/PendingUpdateTests.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/InstallKindDetectorTests.cs`

**Interfaces:**
- Consome: `ProductIdentity.ExeFileName`, `ProductIdentity.SetupFileName`.
- Produz:
  - `enum UpdateTrigger { Startup, Exit, Manual, Periodic }`, `enum UpdateDecision { Apply, ConfirmFirst, Skip }`, `UpdatePolicy.MaxAttempts = 2`, `UpdatePolicy.Decide(UpdateTrigger trigger, bool inFlight, int attemptsSoFar) : UpdateDecision`
  - `sealed record PendingUpdate(string Version, string AssetName, string FilePath, string Sha256, int Attempts)` com `IsFor(InstallKind) : bool` (o arquivo baixado é o `AssetFor` deste tipo, ignorando caixa; `ReadOnly` nunca), `AttemptsExhausted : bool` (`Attempts >= UpdatePolicy.MaxAttempts`), `CanBeInstalledBy(InstallKind, AppVersion) : bool` (`IsFor`, tentativas sobrando e versão maior que a instalada: regra única do `TryApply` e da faixa da interface) e `IsStaleFor(AppVersion) : bool` (versão igual ou anterior à instalada, ou ilegível)
  - `PendingUpdateStore.StateFileName`, `Load(string updatesDir) : PendingUpdate?`, `Save(string updatesDir, PendingUpdate)`, `Clear(string updatesDir)`, `FileIsIntact(PendingUpdate) : bool`, `RecordAttempt(string updatesDir, PendingUpdate) : PendingUpdate`
  - `enum InstallKind { Installer, Portable, ReadOnly }`, `InstallKindDetector.Detect(IEnumerable<string> fileNamesInExeDir, bool exeDirWritable) : InstallKind`, `InstallKindDetector.AssetFor(InstallKind) : string?`

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/UpdatePolicyTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class UpdatePolicyTests
{
    [Theory]
    [InlineData(UpdateTrigger.Startup, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Startup, true, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Exit, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Exit, true, UpdateDecision.Apply)]        // o piloto mandou encerrar
    [InlineData(UpdateTrigger.Manual, false, UpdateDecision.Apply)]
    [InlineData(UpdateTrigger.Manual, true, UpdateDecision.ConfirmFirst)]
    [InlineData(UpdateTrigger.Periodic, false, UpdateDecision.Skip)]    // nunca por timer
    [InlineData(UpdateTrigger.Periodic, true, UpdateDecision.Skip)]
    public void DecidesByTriggerAndFlightState(UpdateTrigger trigger, bool inFlight, UpdateDecision expected)
    {
        Assert.Equal(expected, UpdatePolicy.Decide(trigger, inFlight, attemptsSoFar: 0));
    }

    [Theory]
    [InlineData(UpdateTrigger.Startup)]
    [InlineData(UpdateTrigger.Exit)]
    [InlineData(UpdateTrigger.Manual)]
    public void GivesUpAfterMaxAttempts_ToAvoidALoopOnEveryStart(UpdateTrigger trigger)
    {
        Assert.Equal(UpdateDecision.Apply, UpdatePolicy.Decide(trigger, false, UpdatePolicy.MaxAttempts - 1));
        Assert.Equal(UpdateDecision.Skip, UpdatePolicy.Decide(trigger, false, UpdatePolicy.MaxAttempts));
    }
}
```

`tests/TwoG.Connector.Core.Tests/PendingUpdateStoreTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core.Tests;

public class PendingUpdateStoreTests
{
    private static PendingUpdate WriteDownload(TestTempDir tmp, string content = "binário novo")
    {
        var dir = tmp.Sub("1.5.0");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "2G-Connector.exe");
        File.WriteAllText(path, content, Encoding.UTF8);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        return new PendingUpdate("1.5.0", "2G-Connector.exe", path, hash, Attempts: 0);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);

        PendingUpdateStore.Save(tmp.Path, pending);

        Assert.Equal(pending, PendingUpdateStore.Load(tmp.Path));
    }

    [Fact]
    public void LoadWithoutStateIsNull()
    {
        using var tmp = new TestTempDir();
        Assert.Null(PendingUpdateStore.Load(tmp.Path));
        Assert.Null(PendingUpdateStore.Load(tmp.Sub("não-existe")));
    }

    [Fact]
    public void LoadWithCorruptStateIsNull_NotAnException()
    {
        using var tmp = new TestTempDir();
        File.WriteAllText(Path.Combine(tmp.Path, PendingUpdateStore.StateFileName), "{ quebrado");
        Assert.Null(PendingUpdateStore.Load(tmp.Path));
    }

    [Fact]
    public void RecordAttemptPersistsTheCount()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);
        PendingUpdateStore.Save(tmp.Path, pending);

        PendingUpdateStore.RecordAttempt(tmp.Path, pending);

        Assert.Equal(1, PendingUpdateStore.Load(tmp.Path)!.Attempts);
    }

    [Fact]
    public void FileIsIntactDetectsLocalChanges()
    {
        using var tmp = new TestTempDir();
        var pending = WriteDownload(tmp);
        Assert.True(PendingUpdateStore.FileIsIntact(pending));

        File.AppendAllText(pending.FilePath, "!");
        Assert.False(PendingUpdateStore.FileIsIntact(pending));

        File.Delete(pending.FilePath);
        Assert.False(PendingUpdateStore.FileIsIntact(pending));
    }

    [Fact]
    public void ClearRemovesStateAndDownloads()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        Directory.CreateDirectory(updates);
        PendingUpdateStore.Save(updates, new PendingUpdate("1.5.0", "a", "b", "c", 0));

        PendingUpdateStore.Clear(updates);

        Assert.Null(PendingUpdateStore.Load(updates));
        Assert.False(Directory.Exists(updates));
    }
}
```

`tests/TwoG.Connector.Core.Tests/InstallKindDetectorTests.cs`:

```csharp
namespace TwoG.Connector.Core.Tests;

public class InstallKindDetectorTests
{
    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("unins001.exe")]
    [InlineData("UNINS000.EXE")]
    public void InnoUninstallerMeansInstaller(string uninstaller)
    {
        Assert.Equal(InstallKind.Installer,
            InstallKindDetector.Detect(["2G-Connector.exe", uninstaller, "unins000.dat"], exeDirWritable: true));
    }

    [Theory]
    [InlineData("uninstall.exe")]
    [InlineData("unins00.exe")]
    [InlineData("uninsABC.exe")]
    public void OtherUninstallersDoNotCount(string file)
    {
        Assert.Equal(InstallKind.Portable, InstallKindDetector.Detect(["2G-Connector.exe", file], true));
    }

    [Fact]
    public void LoneExeInWritableFolderIsPortable()
    {
        Assert.Equal(InstallKind.Portable, InstallKindDetector.Detect(["2G-Connector.exe"], true));
    }

    [Fact]
    public void LoneExeInReadOnlyFolderCannotSelfUpdate()
    {
        Assert.Equal(InstallKind.ReadOnly, InstallKindDetector.Detect(["2G-Connector.exe"], false));
    }

    [Fact]
    public void FullPathsWork()
    {
        Assert.Equal(InstallKind.Installer,
            InstallKindDetector.Detect([@"C:\Programs\2G Connector\unins000.exe"], true));
    }

    [Fact]
    public void EachKindDownloadsTheRightAsset()
    {
        Assert.Equal("2G-Connector-Setup.exe", InstallKindDetector.AssetFor(InstallKind.Installer));
        Assert.Equal("2G-Connector.exe", InstallKindDetector.AssetFor(InstallKind.Portable));
        Assert.Null(InstallKindDetector.AssetFor(InstallKind.ReadOnly));
    }
}
```

`FullPathsWork` usa separador `\`; em macOS `Path.GetFileName` não o reconhece — por isso o detector compara pelo último `\` ou `/`, como mostra a implementação.

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — tipos inexistentes.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/UpdatePolicy.cs`:

```csharp
namespace TwoG.Connector.Core;

public enum UpdateTrigger
{
    /// <summary>Ao abrir, antes de o simulador conectar.</summary>
    Startup,

    /// <summary>O piloto mandou encerrar o conector.</summary>
    Exit,

    /// <summary>Botão "Atualizar agora".</summary>
    Manual,

    /// <summary>Timer de verificação. Nunca instala.</summary>
    Periodic,
}

public enum UpdateDecision
{
    Apply,

    /// <summary>Em voo: só com confirmação explícita do piloto.</summary>
    ConfirmFirst,

    Skip,
}

/// <summary>
/// Quando uma atualização já baixada pode ser instalada. Reiniciar o conector no
/// meio de um voo é o piloto perdendo posição no tablet; por isso só há três
/// momentos, e nenhum deles acontece por conta própria durante a sessão.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>Tentativas por versão antes de desistir dela (evita ciclo a cada partida).</summary>
    public const int MaxAttempts = 2;

    public static UpdateDecision Decide(UpdateTrigger trigger, bool inFlight, int attemptsSoFar)
    {
        if (attemptsSoFar >= MaxAttempts)
            return UpdateDecision.Skip;

        return trigger switch
        {
            UpdateTrigger.Startup => UpdateDecision.Apply,
            UpdateTrigger.Exit => UpdateDecision.Apply,
            UpdateTrigger.Manual => inFlight ? UpdateDecision.ConfirmFirst : UpdateDecision.Apply,
            _ => UpdateDecision.Skip,
        };
    }
}
```

`src/TwoG.Connector.Core/PendingUpdate.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace TwoG.Connector.Core;

/// <summary>Atualização baixada e verificada, aguardando um momento seguro para instalar.</summary>
public sealed record PendingUpdate(string Version, string AssetName, string FilePath, string Sha256, int Attempts)
{
    /// <summary>
    /// Se o arquivo baixado serve para uma cópia deste tipo. A pasta updates é uma só
    /// por usuário, e a cópia instalada e uma avulsa enxergam a pendência uma da
    /// outra: sem esta conferência a avulsa se trocaria pelo setup, e a instalada
    /// rodaria o exe avulso como se fosse o setup. Pasta sem escrita não instala nada.
    /// Confira antes de <see cref="PendingUpdateStore.RecordAttempt"/>, para a pendência
    /// de outra cópia não gastar as tentativas dela.
    /// </summary>
    public bool IsFor(InstallKind kind) =>
        InstallKindDetector.AssetFor(kind) is { } asset
        && string.Equals(AssetName, asset, StringComparison.OrdinalIgnoreCase);

    /// <summary>As tentativas desta versão acabaram: ela não será mais instalada sozinha.</summary>
    public bool AttemptsExhausted => Attempts >= UpdatePolicy.MaxAttempts;

    /// <summary>
    /// Se esta cópia vai de fato instalar a pendência. Fonte única para o TryApply e
    /// para a faixa da interface: a faixa não pode oferecer o que o TryApply recusa.
    /// </summary>
    public bool CanBeInstalledBy(InstallKind kind, AppVersion current) =>
        IsFor(kind) && !AttemptsExhausted && !IsStaleFor(current);

    /// <summary>
    /// Versão igual ou anterior à instalada (ou ilegível): sobra de uma atualização que
    /// já deu certo. Deve ser apagada, nunca oferecida.
    /// </summary>
    public bool IsStaleFor(AppVersion current) =>
        !AppVersion.TryParse(Version, out var version) || version.CompareTo(current) <= 0;
}

/// <summary>Persiste a atualização pendente em <c>updates/pending.json</c>.</summary>
public static class PendingUpdateStore
{
    public const string StateFileName = "pending.json";

    public static PendingUpdate? Load(string updatesDir)
    {
        var path = Path.Combine(updatesDir, StateFileName);
        try
        {
            if (!File.Exists(path))
                return null;
            var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(path));
            return pending is not null && AppVersion.TryParse(pending.Version, out _) ? pending : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string updatesDir, PendingUpdate pending)
    {
        Directory.CreateDirectory(updatesDir);
        var path = Path.Combine(updatesDir, StateFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(pending));
        File.Move(tmp, path, overwrite: true);
    }

    public static PendingUpdate RecordAttempt(string updatesDir, PendingUpdate pending)
    {
        var next = pending with { Attempts = pending.Attempts + 1 };
        Save(updatesDir, next);
        return next;
    }

    /// <summary>Apaga estado e downloads. Melhor esforço.</summary>
    public static void Clear(string updatesDir)
    {
        try
        {
            if (Directory.Exists(updatesDir))
                Directory.Delete(updatesDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Um arquivo preso não pode impedir o app de subir.
        }
    }

    /// <summary>
    /// Confere o arquivo em disco de novo antes de instalar: pega download truncado,
    /// disco com defeito ou troca local desde a verificação.
    /// </summary>
    public static bool FileIsIntact(PendingUpdate pending)
    {
        try
        {
            if (!File.Exists(pending.FilePath))
                return false;
            using var stream = File.OpenRead(pending.FilePath);
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == pending.Sha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

`src/TwoG.Connector.Core/InstallKindDetector.cs`:

```csharp
namespace TwoG.Connector.Core;

public enum InstallKind
{
    /// <summary>Instalado pelo Inno Setup: atualiza rodando o instalador novo.</summary>
    Installer,

    /// <summary>Exe avulso numa pasta com escrita: atualiza trocando o arquivo.</summary>
    Portable,

    /// <summary>Exe avulso sem permissão de escrita: só avisa.</summary>
    ReadOnly,
}

public static class InstallKindDetector
{
    /// <summary>
    /// Instalação pelo Inno tem um desinstalador unins???.exe ao lado do exe; sem ele
    /// é o exe avulso, que só dá para trocar se a pasta aceitar escrita.
    /// </summary>
    public static InstallKind Detect(IEnumerable<string> fileNamesInExeDir, bool exeDirWritable)
    {
        if (fileNamesInExeDir.Any(IsInnoUninstaller))
            return InstallKind.Installer;
        return exeDirWritable ? InstallKind.Portable : InstallKind.ReadOnly;
    }

    public static string? AssetFor(InstallKind kind) => kind switch
    {
        InstallKind.Installer => ProductIdentity.SetupFileName,
        InstallKind.Portable => ProductIdentity.ExeFileName,
        _ => null,
    };

    private static bool IsInnoUninstaller(string path)
    {
        // Aceita "\" e "/" em qualquer SO: o nome pode vir de um caminho do Windows.
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Length == "unins000.exe".Length
               && name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
               && name.AsSpan(5, 3).ToArray().All(char.IsAsciiDigit);
    }
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS.

- [ ] **Passo 5: Commit**

```bash
git add -A
git commit -m "Política de instalação, atualização pendente e tipo de instalação

UpdatePolicy só permite instalar ao abrir, ao sair ou por clique, pede
confirmação em voo e desiste da versão depois de duas tentativas, para
não entrar em ciclo a cada partida. A atualização baixada fica registrada
com hash e é conferida de novo antes de instalar.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 8: `UpdateChecker` — buscar, verificar, baixar, conferir

**Arquivos:**
- Criar: `src/TwoG.Connector.Core/UpdateChecker.cs`
- Criar: `tests/TwoG.Connector.Core.Tests/UpdateCheckerTests.cs`

**Interfaces:**
- Consome: `AppVersion`, `UpdateManifest`, `UpdateFile`, `UpdateSignature`, `PendingUpdate`, `PendingUpdateStore`.
- Produz:
  - `sealed record UpdateFeed(Uri ManifestUrl, Func<AppVersion, string, Uri> AssetUrl)` com `SignatureUrl`, `static GitHubLatest(string repository)`, `static Beside(Uri manifestUrl)`
  - `enum UpdateCheckStatus { UpToDate, Available, Downloaded, AlreadyPending, Failed }`
  - `sealed record UpdateCheckResult(UpdateCheckStatus Status, AppVersion? Latest = null, string? Error = null)`
  - `sealed class UpdateChecker(HttpClient http, UpdateFeed feed, string updatesDir, IReadOnlyList<string> acceptedKeys)` com `Task<UpdateCheckResult> CheckAsync(AppVersion current, string? assetName, CancellationToken ct = default)` — `assetName` nulo = só verificar (pasta sem escrita)

**Desvio consciente do spec:** o spec fala em testar "contra um servidor HTTP local, como os testes do `FlightPlanServer`". Aqui os testes usam um `HttpMessageHandler` falso. O `FlightPlanServer` precisava de servidor real porque o código testado *é* o servidor; no `UpdateChecker` o código testado é o cliente, e o transporte é o `HttpClient` do .NET. O handler falso cobre o mesmo caminho sem abrir porta no runner do CI e permite contar requisições (provar que o binário **não** foi baixado).

- [ ] **Passo 1: Testes que falham**

`tests/TwoG.Connector.Core.Tests/UpdateCheckerTests.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TwoG.Connector.Core.Tests;

public class UpdateCheckerTests
{
    private const string Asset = "2G-Connector.exe";
    private static readonly Uri ManifestUrl = new("https://feed.test/update.json");
    private static readonly AppVersion Installed = AppVersion.Parse("1.4.0");

    /// <summary>Feed HTTP em memória: responde só o que foi cadastrado e registra cada pedido.</summary>
    private sealed class FakeFeed : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = new();
        public List<string> Requested { get; } = [];

        public void Put(string url, byte[] body) => _files[url] = body;
        public void Put(string url, string body) => Put(url, Encoding.UTF8.GetBytes(body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            return Task.FromResult(_files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>Um release publicado: chave própria, manifesto assinado e o binário.</summary>
    private sealed class Release : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string PublicPem => _key.ExportSubjectPublicKeyInfoPem();
        public string PrivatePem => _key.ExportECPrivateKeyPem();
        public void Dispose() => _key.Dispose();

        public byte[] Manifest(string version, byte[] content, string asset = Asset) =>
            UpdateManifest.Serialize(AppVersion.Parse(version),
                new Dictionary<string, UpdateFile>
                {
                    [asset] = new(Convert.ToHexStringLower(SHA256.HashData(content)), content.Length),
                });

        /// <summary>Publica manifesto, assinatura e binário no feed.</summary>
        public void PublishTo(FakeFeed feed, string version, byte[] content)
        {
            var manifest = Manifest(version, content);
            feed.Put(ManifestUrl.AbsoluteUri, manifest);
            feed.Put(ManifestUrl.AbsoluteUri + ".sig", UpdateSignature.Sign(manifest, PrivatePem));
            feed.Put(AssetUrl(version), content);
        }
    }

    // Cada teste usa uma subpasta "updates": o checker apaga a pasta inteira ao
    // descartar um download, e ela não pode ser a própria pasta temporária.
    private static string AssetUrl(string version, string asset = Asset) => $"https://feed.test/v{version}/{asset}";

    private static UpdateChecker Checker(FakeFeed feed, string updatesDir, string publicPem) =>
        new(new HttpClient(feed),
            new UpdateFeed(ManifestUrl, (v, a) => new Uri(AssetUrl(v.ToString(), a))),
            updatesDir,
            [publicPem]);

    private static readonly byte[] NewBinary = Encoding.UTF8.GetBytes("MZ... binário da 1.5.0");

    [Fact]
    public async Task NewerSignedVersion_IsDownloaded_AndBecomesPending()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Downloaded, result.Status);
        Assert.Equal("1.5.0", result.Latest!.ToString());
        var pending = PendingUpdateStore.Load(updates)!;
        Assert.Equal("1.5.0", pending.Version);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal(NewBinary, File.ReadAllBytes(pending.FilePath));
    }

    [Fact]
    public async Task DownloadsFromTheVersionedUrl_NotFromLatest()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Contains(AssetUrl("1.5.0"), feed.Requested);
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("1.3.0")]
    [InlineData("1.4.0-rc.9")]
    public async Task SameOrOlderVersion_IsUpToDate_AndDownloadsNothing(string published)
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, published, NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.DoesNotContain(AssetUrl(published), feed.Requested);
        Assert.Null(PendingUpdateStore.Load(updates));
    }

    [Fact]
    public async Task TamperedManifest_IsRejected_BeforeAnyDownload()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var tampered = Encoding.UTF8.GetString(release.Manifest("1.5.0", NewBinary)).Replace("1.5.0", "1.5.1");
        feed.Put(ManifestUrl.AbsoluteUri, tampered);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("assinatura", result.Error);
        Assert.DoesNotContain(feed.Requested, u => u.Contains("/v1.5"));
    }

    [Fact]
    public async Task SignatureFromAnUnknownKey_IsRejected()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var attacker = new Release();
        using var ours = new Release();
        var feed = new FakeFeed();
        attacker.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, ours.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(PendingUpdateStore.Load(updates));
    }

    [Fact]
    public async Task BinaryWithWrongHash_IsDiscarded()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var swapped = (byte[])NewBinary.Clone();
        swapped[0] ^= 0xFF;                        // mesmo tamanho, conteúdo diferente
        feed.Put(AssetUrl("1.5.0"), swapped);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("SHA256", result.Error);
        Assert.Null(PendingUpdateStore.Load(updates));
        Assert.False(Directory.Exists(updates));   // Clear apagou o download e a pasta
    }

    [Fact]
    public async Task OversizedBinary_IsCutOff_AndDiscarded()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        feed.Put(AssetUrl("1.5.0"), [.. NewBinary, .. new byte[1024 * 1024]]);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("tamanho", result.Error);
        Assert.False(Directory.Exists(updates));   // Clear apagou o download e a pasta
    }

    [Fact]
    public async Task SecondCheck_WithIntactPending_DoesNotDownloadAgain()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);
        var checker = Checker(feed, updates, release.PublicPem);

        await checker.CheckAsync(Installed, Asset);
        var second = await checker.CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.AlreadyPending, second.Status);
        Assert.Single(feed.Requested, u => u == AssetUrl("1.5.0"));
    }

    [Fact]
    public async Task NewerReleaseReplacesAnOlderPending()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        var checker = Checker(feed, updates, release.PublicPem);
        release.PublishTo(feed, "1.5.0", NewBinary);
        await checker.CheckAsync(Installed, Asset);

        release.PublishTo(feed, "1.5.1", Encoding.UTF8.GetBytes("MZ... 1.5.1"));
        var result = await checker.CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Downloaded, result.Status);
        Assert.Equal("1.5.1", PendingUpdateStore.Load(updates)!.Version);
        Assert.False(Directory.Exists(Path.Combine(updates, "1.5.0")));
    }

    [Fact]
    public async Task CheckOnly_ReportsAvailable_WithoutDownloading()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, assetName: null);

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.DoesNotContain(AssetUrl("1.5.0"), feed.Requested);
    }

    [Fact]
    public async Task ReleaseWithoutOurAsset_Fails()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();
        var feed = new FakeFeed();
        release.PublishTo(feed, "1.5.0", NewBinary);

        var result = await Checker(feed, updates, release.PublicPem).CheckAsync(Installed, "2G-Connector-Setup.exe");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("2G-Connector-Setup.exe", result.Error);
    }

    [Fact]
    public async Task UnreachableFeed_FailsWithoutThrowing()
    {
        using var tmp = new TestTempDir();
        var updates = tmp.Sub("updates");
        using var release = new Release();

        var result = await Checker(new FakeFeed(), updates, release.PublicPem).CheckAsync(Installed, Asset);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void GitHubFeedUsesPermanentLinkForManifest_AndVersionedUrlForBinaries()
    {
        var feed = UpdateFeed.GitHubLatest("galenoferreira/gps_connector");

        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json",
            feed.ManifestUrl.AbsoluteUri);
        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json.sig",
            feed.SignatureUrl.AbsoluteUri);
        Assert.Equal("https://github.com/galenoferreira/gps_connector/releases/download/v1.5.0/2G-Connector.exe",
            feed.AssetUrl(AppVersion.Parse("1.5.0"), "2G-Connector.exe").AbsoluteUri);
    }

    [Fact]
    public void TestFeedKeepsBinariesBesideTheManifest()
    {
        var feed = UpdateFeed.Beside(new Uri("https://github.com/o/r/releases/download/v1.4.0-rc.2/update.json"));

        Assert.Equal("https://github.com/o/r/releases/download/v1.4.0-rc.2/2G-Connector.exe",
            feed.AssetUrl(AppVersion.Parse("1.4.0-rc.2"), "2G-Connector.exe").AbsoluteUri);
    }
}
```

- [ ] **Passo 2: Rodar e ver falhar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: FAIL de compilação — `UpdateChecker`, `UpdateFeed` etc. não existem.

- [ ] **Passo 3: Implementar**

`src/TwoG.Connector.Core/UpdateChecker.cs`:

```csharp
using System.Security.Cryptography;

namespace TwoG.Connector.Core;

/// <summary>Onde buscar o manifesto e como montar a URL de cada arquivo.</summary>
public sealed record UpdateFeed(Uri ManifestUrl, Func<AppVersion, string, Uri> AssetUrl)
{
    public Uri SignatureUrl => new(ManifestUrl.AbsoluteUri + ".sig");

    /// <summary>
    /// Feed oficial: o manifesto pelo link permanente do "latest" (sem API do GitHub,
    /// sem limite de requisições) e os arquivos pela URL COM versão — senão um
    /// release publicado durante o download misturaria o manifesto de uma versão
    /// com o binário de outra.
    /// </summary>
    public static UpdateFeed GitHubLatest(string repository) => new(
        new Uri($"https://github.com/{repository}/releases/latest/download/update.json"),
        (version, asset) => new Uri($"https://github.com/{repository}/releases/download/v{version}/{asset}"));

    /// <summary>
    /// Feed de teste (TWOG_UPDATE_FEED): manifesto numa pasta fixa, arquivos ao lado.
    /// A assinatura continua obrigatória, então isto não enfraquece nada.
    /// </summary>
    public static UpdateFeed Beside(Uri manifestUrl) => new(manifestUrl, (_, asset) => new Uri(manifestUrl, asset));
}

public enum UpdateCheckStatus
{
    UpToDate,

    /// <summary>Há versão nova, mas não foi baixada (instalação sem permissão de escrita).</summary>
    Available,

    Downloaded,
    AlreadyPending,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, AppVersion? Latest = null, string? Error = null);

/// <summary>
/// Busca o manifesto, verifica a assinatura e a versão, baixa o arquivo e confere
/// tamanho e hash — nesta ordem. Só o que passou por tudo vira
/// <see cref="PendingUpdate"/>. Não instala nada: quando instalar é decisão de
/// <see cref="UpdatePolicy"/>. Nunca lança por falha de rede ou de conteúdo.
/// </summary>
public sealed class UpdateChecker(HttpClient http, UpdateFeed feed, string updatesDir, IReadOnlyList<string> acceptedKeys)
{
    private const int CopyBufferSize = 81920;

    public async Task<UpdateCheckResult> CheckAsync(AppVersion current, string? assetName, CancellationToken ct = default)
    {
        byte[] manifestBytes;
        string signature;
        try
        {
            manifestBytes = await http.GetByteArrayAsync(feed.ManifestUrl, ct);
            signature = await http.GetStringAsync(feed.SignatureUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            return Failed($"sem acesso ao feed de atualização: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("tempo esgotado ao buscar o manifesto");
        }

        if (!UpdateSignature.Verify(manifestBytes, signature, acceptedKeys))
            return Failed("assinatura inválida — atualização descartada");

        if (!UpdateManifest.TryParse(manifestBytes, out var manifest, out var error))
            return Failed($"manifesto inválido: {error}");

        var latest = manifest.Version;
        if (latest.CompareTo(current) <= 0)
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest);

        if (assetName is null)
            return new UpdateCheckResult(UpdateCheckStatus.Available, latest);

        if (!manifest.Files.TryGetValue(assetName, out var expected))
            return Failed($"o release {latest} não traz {assetName}", latest);

        var pending = PendingUpdateStore.Load(updatesDir);
        if (pending is not null
            && pending.Version == latest.ToString()
            && string.Equals(pending.AssetName, assetName, StringComparison.OrdinalIgnoreCase)
            && pending.Sha256 == expected.Sha256
            && PendingUpdateStore.FileIsIntact(pending))
            return new UpdateCheckResult(UpdateCheckStatus.AlreadyPending, latest);

        // Versão nova substitui qualquer pendência anterior.
        PendingUpdateStore.Clear(updatesDir);
        var dir = Path.Combine(updatesDir, latest.ToString());
        var finalPath = Path.Combine(dir, assetName);
        var partialPath = finalPath + ".partial";

        try
        {
            Directory.CreateDirectory(dir);
            long written;
            using (var response = await http.GetAsync(feed.AssetUrl(latest, assetName), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(partialPath);
                written = await CopyBoundedAsync(source, target, expected.Size, ct);
            }

            if (written != expected.Size)
            {
                PendingUpdateStore.Clear(updatesDir);
                return Failed($"tamanho do download não confere com o manifesto ({expected.Size} bytes esperados)", latest);
            }

            string actual;
            await using (var stream = File.OpenRead(partialPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));

            if (actual != expected.Sha256)
            {
                PendingUpdateStore.Clear(updatesDir);
                return Failed("SHA256 do download não confere com o manifesto — descartado", latest);
            }

            File.Move(partialPath, finalPath, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed($"falha no download: {ex.Message}", latest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed($"falha ao gravar o download: {ex.Message}", latest);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            PendingUpdateStore.Clear(updatesDir);
            return Failed("tempo esgotado no download", latest);
        }

        PendingUpdateStore.Save(updatesDir,
            new PendingUpdate(latest.ToString(), assetName, finalPath, expected.Sha256, Attempts: 0));
        return new UpdateCheckResult(UpdateCheckStatus.Downloaded, latest);
    }

    /// <summary>
    /// Copia até <paramref name="maxBytes"/> + 1: se o servidor mandar mais do que o
    /// manifesto declara, para ali em vez de encher o disco. Devolve o total lido.
    /// </summary>
    private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return total;
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }

    private static UpdateCheckResult Failed(string error, AppVersion? latest = null) =>
        new(UpdateCheckStatus.Failed, latest, error);
}
```

- [ ] **Passo 4: Rodar e ver passar**

Run: `dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj`
Expected: PASS (15 métodos novos).

- [ ] **Passo 5: Commit**

```bash
git add -A
git commit -m "UpdateChecker: manifesto assinado, versão maior, download conferido

Verifica a assinatura antes de interpretar o manifesto, recusa versão
igual ou menor (barra servir de novo um manifesto antigo legítimo), baixa
pela URL com versão e confere tamanho e SHA256. O download para assim que
passa do tamanho declarado, em vez de encher o disco. Só o que passou por
tudo vira atualização pendente; nada é instalado aqui.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 9: Instalação no Windows e gatilhos no ciclo de vida do app

Liga o Core ao app: o serviço que verifica em segundo plano, o instalador que troca o exe ou roda o setup, e os três gatilhos. Sem UI ainda. Esta tarefa depende do Windows; localmente só dá para compilar. A prova vem na verificação da Tarefa 12.

**Arquivos:**
- Criar: `src/TwoG.Connector/Services/UpdateInstaller.cs`
- Criar: `src/TwoG.Connector/Services/UpdateService.cs`
- Modificar: `src/TwoG.Connector/App.xaml.cs`
- Modificar: `src/TwoG.Connector/MainWindow.xaml.cs`
- Modificar: `src/TwoG.Connector/Configuration/AppSettings.cs`
- Modificar: `src/TwoG.Connector/TwoG.Connector.csproj`
- Modificar: `installer/setup.iss`

**Interfaces:**
- Consome: tudo do Core das Tarefas 5 a 8; `ProductIdentity`.
- Produz:
  - `AppSettings.AutoUpdate : bool` (padrão `true`)
  - `UpdateInstaller.UpdatedArg = "--updated"`, `UpdateInstaller.Launch(PendingUpdate, InstallKind, string exePath, IReadOnlyList<string> relaunchArgs, bool relaunch, Action releaseMutex)`, `UpdateInstaller.CleanupAfterUpdate(string? exePath)`
  - `UpdateService` com `static Create(AppSettings)`, `IsReleaseBuild`, `IsEnabled`, `Kind`, `CurrentVersion`, `Pending`, `LatestSeen`, `LastCheckUtc`, `LastError`, `Start()`, `CheckNowAsync()`, `TryApply(UpdateTrigger, bool inFlight, IReadOnlyList<string> relaunchArgs, bool relaunch, Action releaseMutex, Func<bool>? confirmInFlight = null) : bool`, `Dispose()`
  - `App.ReleaseSingleInstance()`, `App.BeginUserExit()`, `App.ApplyUpdateNow(bool inFlight, Func<bool> confirmInFlight) : bool`, `App.Updates`

- [ ] **Passo 1: Configuração e canal do build**

`Configuration/AppSettings.cs`, depois de `CloseToTray`:

```csharp
    /// <summary>Verificar, baixar e instalar atualizações sozinho — sempre fora de voo.</summary>
    public bool AutoUpdate { get; set; } = true;
```

`TwoG.Connector.csproj`, depois do `ItemGroup` de `PackageReference`:

```xml
  <!--
    Canal do updater, injetado pelo CI só em builds de tag (-p:UpdateChannel=stable).
    Sem ele o updater fica desligado: um build local ou de branch não pode se
    "atualizar" para o último release na primeira oportunidade.
  -->
  <ItemGroup Condition="'$(UpdateChannel)' != ''">
    <AssemblyMetadata Include="UpdateChannel" Value="$(UpdateChannel)" />
  </ItemGroup>
```

- [ ] **Passo 2: `UpdateInstaller`**

`src/TwoG.Connector/Services/UpdateInstaller.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Executa a instalação de uma atualização já verificada. Quem chama encerra o app
/// logo em seguida.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>
    /// Passado ao exe novo no modo avulso: espere o mutex em vez de sair como
    /// segunda instância — a versão anterior está terminando de sair.
    /// </summary>
    public const string UpdatedArg = "--updated";

    private const string OldSuffix = ".old";

    public static void Launch(PendingUpdate pending, InstallKind kind, string exePath,
                              IReadOnlyList<string> relaunchArgs, bool relaunch, Action releaseMutex)
    {
        switch (kind)
        {
            case InstallKind.Installer:
                LaunchInstaller(pending.FilePath, relaunchArgs, relaunch, releaseMutex);
                break;
            case InstallKind.Portable:
                SwapExecutable(pending.FilePath, exePath, relaunchArgs, relaunch, releaseMutex);
                break;
            default:
                throw new IOException("pasta do executável sem permissão de escrita");
        }
    }

    /// <summary>
    /// Setup silencioso. O mutex é liberado logo depois de o processo nascer: o
    /// AppMutex do Inno, em modo silencioso, CANCELA a instalação se nos achar vivos,
    /// e o setup leva centenas de ms para chegar a essa verificação.
    /// </summary>
    private static void LaunchInstaller(string setupPath, IReadOnlyList<string> relaunchArgs,
                                        bool relaunch, Action releaseMutex)
    {
        var start = new ProcessStartInfo(setupPath) { UseShellExecute = false };
        foreach (var arg in new[]
                 {
                     "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                     // Se ainda estivermos saindo quando ele for copiar o exe, o
                     // Restart Manager nos fecha. Sem reabrir por conta própria: quem
                     // reabre é a entrada [Run] do setup.iss, com os nossos argumentos.
                     "/CLOSEAPPLICATIONS", "/FORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
                 })
            start.ArgumentList.Add(arg);

        start.ArgumentList.Add(relaunch
            ? $"/relaunchargs={string.Join(' ', relaunchArgs)}"
            : "/norelaunch=1");

        Process.Start(start);
        releaseMutex();
    }

    /// <summary>
    /// Exe avulso: o Windows deixa renomear um exe em execução, só não sobrescrever.
    /// O caminho não muda, então o EXE.xml continua certo.
    /// </summary>
    private static void SwapExecutable(string newExePath, string exePath, IReadOnlyList<string> relaunchArgs,
                                       bool relaunch, Action releaseMutex)
    {
        var oldPath = exePath + OldSuffix;
        TryDelete(oldPath);                     // resto de uma troca anterior
        File.Move(exePath, oldPath);
        try
        {
            // Cópia, não move: a pasta de updates pode estar em outro volume.
            File.Copy(newExePath, exePath, overwrite: false);
        }
        catch
        {
            File.Move(oldPath, exePath);        // desfaz: o app continua como estava
            throw;
        }

        if (relaunch)
        {
            var start = new ProcessStartInfo(exePath) { UseShellExecute = false };
            foreach (var arg in relaunchArgs)
                start.ArgumentList.Add(arg);
            start.ArgumentList.Add(UpdatedArg);
            Process.Start(start);
        }
        releaseMutex();
    }

    /// <summary>Apaga o .old deixado pela troca do exe avulso. Melhor esforço.</summary>
    public static void CleanupAfterUpdate(string? exePath)
    {
        if (exePath is null)
            return;
        for (var i = 0; i < 5; i++)
        {
            if (TryDelete(exePath + OldSuffix))
                return;
            Thread.Sleep(200);                  // a versão anterior pode estar terminando de sair
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Passo 3: `UpdateService`**

`src/TwoG.Connector/Services/UpdateService.cs`:

```csharp
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Orquestra o auto-update: verifica 60 s depois de abrir e a cada 6 h, baixa em
/// segundo plano e instala só nos momentos que <see cref="UpdatePolicy"/> permite.
/// Nenhuma falha daqui pode derrubar o app.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly string _exePath;
    private readonly string _updatesDir;
    private readonly HttpClient _http;
    private readonly UpdateChecker _checker;
    private Timer? _timer;
    private int _checking;

    private UpdateService(AppSettings settings, AppVersion current, bool isReleaseBuild,
                          string exePath, string updatesDir, InstallKind kind, UpdateFeed feed)
    {
        _settings = settings;
        CurrentVersion = current;
        IsReleaseBuild = isReleaseBuild;
        _exePath = exePath;
        _updatesDir = updatesDir;
        Kind = kind;

        // Buffer pequeno: vale para manifesto e assinatura (bufferizados); o binário
        // vem por stream, com limite de tamanho no próprio UpdateChecker.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2), MaxResponseContentBufferSize = 64 * 1024 };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"2G-Connector/{current}");
        _checker = new UpdateChecker(_http, feed, updatesDir, UpdateKeys.Accepted);
        Pending = PendingUpdateStore.Load(updatesDir);
        // Sobra da atualização que acabou de dar certo: some já na partida, antes de
        // qualquer gatilho — com o auto-update desligado o TryApply nem chegaria a olhar.
        if (Pending is not null && Pending.IsStaleFor(current))
        {
            PendingUpdateStore.Clear(updatesDir);
            Pending = null;
        }
    }

    public static UpdateService Create(AppSettings settings)
    {
        var exePath = Environment.ProcessPath ?? "";
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var kind = InstallKindDetector.Detect(ListFiles(exeDir), IsWritable(exeDir));
        var updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.DataFolderName, "updates");
        return new UpdateService(settings, ReadCurrentVersion(), ReadIsReleaseBuild(), exePath, updatesDir, kind, ResolveFeed());
    }

    public AppVersion CurrentVersion { get; }

    /// <summary>Só builds de tag têm canal; os demais nunca se atualizam.</summary>
    public bool IsReleaseBuild { get; }

    public bool IsEnabled => IsReleaseBuild && _settings.AutoUpdate;

    public InstallKind Kind { get; }

    public PendingUpdate? Pending { get; private set; }

    public AppVersion? LatestSeen { get; private set; }

    public DateTime? LastCheckUtc { get; private set; }

    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsReleaseBuild)
            _timer = new Timer(_ => _ = CheckNowAsync(), null, FirstCheckDelay, CheckInterval);
    }

    public async Task CheckNowAsync()
    {
        if (!IsEnabled || Interlocked.Exchange(ref _checking, 1) == 1)
            return;
        try
        {
            using var budget = new CancellationTokenSource(DownloadBudget);
            var result = await _checker.CheckAsync(CurrentVersion, InstallKindDetector.AssetFor(Kind), budget.Token);
            LastCheckUtc = DateTime.UtcNow;
            LatestSeen = result.Latest ?? LatestSeen;
            LastError = result.Status == UpdateCheckStatus.Failed ? result.Error : null;
            Pending = PendingUpdateStore.Load(_updatesDir);
        }
        catch (Exception ex)
        {
            LastError = $"falha na verificação: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>
    /// Instala a atualização pendente, se a política permitir. Devolve true quando a
    /// instalação foi iniciada — quem chama deve encerrar o app em seguida.
    /// </summary>
    public bool TryApply(UpdateTrigger trigger, bool inFlight, IReadOnlyList<string> relaunchArgs,
                         bool relaunch, Action releaseMutex, Func<bool>? confirmInFlight = null)
    {
        if (!IsReleaseBuild)
            return false;
        // Desligado nas Configurações: nada automático; o botão continua valendo.
        if (trigger != UpdateTrigger.Manual && !_settings.AutoUpdate)
            return false;

        var pending = PendingUpdateStore.Load(_updatesDir);
        Pending = pending;
        // Mesma regra que a faixa da interface usa: o que não passa aqui não é oferecido lá.
        if (pending is null || !pending.CanBeInstalledBy(Kind, CurrentVersion))
            return false;

        switch (UpdatePolicy.Decide(trigger, inFlight, pending.Attempts))
        {
            case UpdateDecision.Skip:
                return false;
            case UpdateDecision.ConfirmFirst when confirmInFlight?.Invoke() != true:
                return false;
        }

        if (!PendingUpdateStore.FileIsIntact(pending))
        {
            PendingUpdateStore.Clear(_updatesDir);
            Pending = null;
            LastError = "arquivo da atualização alterado ou incompleto — será baixado de novo";
            return false;
        }

        try
        {
            // Dentro do try: se a tentativa não puder ser gravada, não instala (sem o
            // contador, uma instalação que falha se repetiria a cada partida) e não
            // lança — no gatilho Startup isto roda antes de a janela existir.
            Pending = PendingUpdateStore.RecordAttempt(_updatesDir, pending);
            UpdateInstaller.Launch(pending, Kind, _exePath, relaunchArgs, relaunch, releaseMutex);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            LastError = $"falha ao instalar a v{pending.Version}: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }

    private static UpdateFeed ResolveFeed()
    {
        // Só HTTPS: a assinatura já protege o conteúdo, mas não há motivo para aceitar menos.
        var overrideUrl = Environment.GetEnvironmentVariable("TWOG_UPDATE_FEED");
        return Uri.TryCreate(overrideUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? UpdateFeed.Beside(url)
            : UpdateFeed.GitHubLatest(ProductIdentity.GitHubRepository);
    }

    private static AppVersion ReadCurrentVersion()
    {
        var info = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return AppVersion.TryParse(info, out var version) ? version : AppVersion.Parse("0.0.0");
    }

    private static bool ReadIsReleaseBuild() =>
        typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "UpdateChannel" && a.Value == "stable");

    private static string[] ListFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".2g-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
```

- [ ] **Passo 4: Gatilhos no `App.xaml.cs`**

Novos campos, junto dos existentes:

```csharp
    private UpdateService? _updates;
    private bool _userExit;
    private bool _updateLaunched;

    internal UpdateService? Updates => _updates;
```

Trocar o bloco da instância única (do `_singleInstanceMutex = new Mutex(...)` até o `if (!isFirstInstance) { ... }`) por:

```csharp
        var updatedArg = e.Args.Any(a =>
            string.Equals(a, UpdateInstaller.UpdatedArg, StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance && updatedArg)
        {
            // Relançado pelo updater: a versão anterior está saindo agora. Espera o
            // mutex em vez de sair como segunda instância.
            isFirstInstance = WaitForMutex(_singleInstanceMutex, TimeSpan.FromSeconds(10));
        }

        if (!isFirstInstance)
        {
            // (corpo existente, sem mudança: sinaliza a instância aberta e sai)
        }
```

Logo depois de `var settings = settingsService.Load();` e **antes** de `SimConnectRuntime.Ensure();`:

```csharp
        UpdateInstaller.CleanupAfterUpdate(Environment.ProcessPath);
        _updates = UpdateService.Create(settings);

        // Momento seguro nº 1: antes de o simulador conectar não há voo a interromper.
        if (_updates.TryApply(UpdateTrigger.Startup, inFlight: false, RelaunchArgs(e.Args),
                              relaunch: true, ReleaseSingleInstance))
        {
            _updateLaunched = true;
            Shutdown();
            return;
        }
```

Depois de `_broadcaster.Start();`: `_updates.Start();`

Métodos novos na classe:

```csharp
    private static bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;    // a anterior saiu sem liberar: agora é nosso
        }
    }

    /// <summary>Argumentos para reabrir o app depois de atualizar, sem o marcador do updater.</summary>
    private static string[] RelaunchArgs(IEnumerable<string> args) =>
        args.Where(a => !string.Equals(a, UpdateInstaller.UpdatedArg, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>Solta o mutex de instância única. Idempotente.</summary>
    internal void ReleaseSingleInstance()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Não éramos donos (ex.: segunda instância saindo).
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    /// <summary>Encerramento pedido pelo piloto (menu Sair ou fechar sem bandeja).</summary>
    internal void BeginUserExit() => _userExit = true;

    /// <summary>Botão "Atualizar agora". Devolve false se não iniciou a instalação.</summary>
    internal bool ApplyUpdateNow(bool inFlight, Func<bool> confirmInFlight)
    {
        if (_updates is null)
            return false;
        var args = RelaunchArgs(Environment.GetCommandLineArgs().Skip(1));
        if (!_updates.TryApply(UpdateTrigger.Manual, inFlight, args, relaunch: true,
                               ReleaseSingleInstance, confirmInFlight))
            return false;

        _updateLaunched = true;
        Shutdown();
        return true;
    }
```

`OnExit` inteiro:

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        _flightPlanServer?.Dispose();
        _broadcaster?.Dispose();
        _discovery?.Dispose();
        _sim?.Dispose();

        // Momento seguro nº 2: o piloto mandou encerrar. Instala sem reabrir. Logoff
        // e desligamento do Windows não passam por BeginUserExit, e não instalam.
        if (_userExit && !_updateLaunched)
            _updates?.TryApply(UpdateTrigger.Exit, inFlight: false, [], relaunch: false, ReleaseSingleInstance);

        _updates?.Dispose();
        ReleaseSingleInstance();
        base.OnExit(e);
    }
```

- [ ] **Passo 5: Marcar a saída do piloto no `MainWindow.xaml.cs`**

Em `ExitApplication()`, primeira linha: `((App)Application.Current).BeginUserExit();`

Em `OnClosing`, no caminho que encerra de verdade (o `if (!_exiting)` depois de `base.OnClosing(e);`), antes de `TrayIcon.Dispose();`: `((App)Application.Current).BeginUserExit();`

- [ ] **Passo 6: Reabrir depois da instalação silenciosa (`installer/setup.iss`)**

Na seção `[Run]`, depois da linha existente:

```
; Reabre o conector depois de uma atualização silenciosa feita pelo updater, com
; os argumentos que ele tinha (-minimized quando lançado pelo MSFS). O updater
; passa /norelaunch=1 quando o piloto mandou encerrar.
Filename: "{app}\{#MyAppExeName}"; Parameters: "{param:relaunchargs|}"; Flags: nowait skipifnotsilent; Check: ShouldRelaunch
```

Na seção `[Code]`, antes de `procedure InitializeWizard();`:

```pascal
function ShouldRelaunch(): Boolean;
begin
  Result := ExpandConstant('{param:norelaunch|0}') <> '1';
end;
```

- [ ] **Passo 7: Compilar**

```bash
dotnet build src/TwoG.Connector/TwoG.Connector.csproj
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
```

Expected: build limpo, todos os testes passando.

- [ ] **Passo 8: Commit**

```bash
git add -A
git commit -m "Auto-update no app: verificação em segundo plano e três gatilhos

Verifica 60 s depois de abrir e a cada 6 h, só em builds de release.
Instala ao abrir (antes de conectar ao simulador), ao sair pelo menu ou
fechando a janela, e pelo botão — nunca por timer. Logoff do Windows não
instala. No exe avulso renomeia o exe em execução e o novo espera o mutex;
no instalado roda o setup silencioso, que reabre o app com os mesmos
argumentos.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 10: Interface do updater

**Arquivos:**
- Modificar: `src/TwoG.Connector/ViewModels/MainViewModel.cs`
- Modificar: `src/TwoG.Connector/MainWindow.xaml`
- Modificar: `src/TwoG.Connector/App.xaml.cs` (passar o serviço ao ViewModel)

**Interfaces:**
- Consome: `UpdateService` (`IsReleaseBuild`, `Kind`, `CurrentVersion`, `Pending`, `LatestSeen`, `LastCheckUtc`, `LastError`), `App.ApplyUpdateNow`.
- Produz, no ViewModel: `UpdateBannerText`, `HasUpdateBanner`, `CanApplyUpdateNow`, `AutoUpdateInput`, `DiagUpdate`, `ApplyUpdateNowCommand`.

- [ ] **Passo 1: ViewModel recebe o serviço**

Construtor — acrescentar o parâmetro opcional por último e guardar no campo:

```csharp
    private readonly UpdateService? _updates;
```

```csharp
    public MainViewModel(ISimSource sim, IXgpsBroadcaster broadcaster,
                         SettingsService settingsService, AppSettings settings,
                         FlightPlanServer flightPlanServer,
                         IEfbDiscovery? discovery = null,
                         UpdateService? updates = null)
    {
        _sim = sim;
        _broadcaster = broadcaster;
        _settingsService = settingsService;
        _settings = settings;
        _flightPlanServer = flightPlanServer;
        _discovery = discovery;
        _updates = updates;
        // (resto sem mudança)
```

Em `App.xaml.cs`, a criação do ViewModel passa `_updates` como último argumento:

```csharp
        var viewModel = new MainViewModel(_sim, _broadcaster, settingsService, settings,
                                          _flightPlanServer, _discovery, _updates);
```

- [ ] **Passo 2: Propriedades, configuração e atualização por tique**

Depois do bloco `// ── Apps descobertos ──`:

```csharp
    // ── Atualização ─────────────────────────────────────────────────────
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private bool _hasUpdateBanner;
    [ObservableProperty] private bool _canApplyUpdateNow;
    [ObservableProperty] private string _diagUpdate = "—";
```

No bloco de configurações: `[ObservableProperty] private bool _autoUpdateInput;`

Em `LoadSettingsIntoInputs()`: `AutoUpdateInput = _settings.AutoUpdate;`

Em `ApplySettings()`, junto das demais atribuições: `_settings.AutoUpdate = AutoUpdateInput;`

Em `Refresh()`, logo depois da chamada `UpdateDiagnostics();`: `UpdateUpdaterStatus();`

Em `UpdateDiagnostics()`, a lista de erros ganha o do updater:

```csharp
        DiagLastError = string.Join("  •  ",
            new[] { sendError, discoveryError, _updates?.LastError }.Where(e => e is { Length: > 0 }));
```

Métodos novos:

```csharp
    /// <summary>
    /// Faixa abaixo do cabeçalho e linha no Diagnóstico. Leituras baratas: o serviço
    /// guarda o estado em memória, sem I/O por tique.
    /// </summary>
    private void UpdateUpdaterStatus()
    {
        var updates = _updates;
        if (updates is null || !updates.IsReleaseBuild)
        {
            HasUpdateBanner = false;
            CanApplyUpdateNow = false;
            DiagUpdate = "desligado neste build (só builds de release se atualizam)";
            return;
        }

        var pending = updates.Pending;
        var latest = updates.LatestSeen;
        // Mesma regra do TryApply: só oferece o que esta cópia vai de fato instalar.
        if (pending is not null && pending.CanBeInstalledBy(updates.Kind, updates.CurrentVersion))
        {
            HasUpdateBanner = true;
            CanApplyUpdateNow = true;
            UpdateBannerText = $"v{pending.Version} pronta — instala ao reiniciar";
        }
        else if (pending is not null && pending.IsFor(updates.Kind) && pending.AttemptsExhausted
                 && !pending.IsStaleFor(updates.CurrentVersion))
        {
            // Derivado do estado em disco, não do LastError: a próxima verificação
            // zeraria a mensagem, e o spec pede que a falha continue visível.
            HasUpdateBanner = true;
            CanApplyUpdateNow = false;
            UpdateBannerText = $"A instalação da v{pending.Version} falhou {pending.Attempts} vezes — baixe manualmente";
        }
        else if (updates.Kind == InstallKind.ReadOnly && latest is not null && latest > updates.CurrentVersion)
        {
            HasUpdateBanner = true;
            CanApplyUpdateNow = false;
            UpdateBannerText = $"v{latest} disponível — baixe manualmente (pasta do app sem permissão de escrita)";
        }
        else
        {
            HasUpdateBanner = false;
            CanApplyUpdateNow = false;
        }

        if (!_settings.AutoUpdate)
        {
            DiagUpdate = $"v{updates.CurrentVersion}  •  atualização automática desligada";
            return;
        }

        var check = updates.LastCheckUtc is { } at
            ? $"verificado há {FormatElapsed(DateTime.UtcNow - at)}"
            : "ainda não verificado";
        var seen = latest is not null ? $"  •  mais recente: v{latest}" : "";
        DiagUpdate = $"v{updates.CurrentVersion}  •  {check}{seen}";
    }

    private static string FormatElapsed(TimeSpan span) =>
        span.TotalMinutes < 1 ? "menos de 1 min"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : $"{(int)span.TotalHours} h";

    [RelayCommand]
    private void ApplyUpdateNow()
    {
        if (System.Windows.Application.Current is not App app)
            return;

        var inFlight = _sim.State == SimConnectionState.Receiving;
        var pilotDeclined = false;
        var started = app.ApplyUpdateNow(inFlight, confirmInFlight: () =>
        {
            var confirmed = System.Windows.MessageBox.Show(
                "Você está em voo. O tablet fica sem posição por alguns segundos enquanto o "
                + $"{ProductIdentity.Name} se atualiza.\n\nAtualizar agora?",
                ProductIdentity.Name,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
            pilotDeclined = !confirmed;
            return confirmed;
        });

        // Caixa de mensagem, não texto na faixa: o próximo tique de 250 ms sobrescreveria
        // a faixa. Se quem recusou foi o piloto (Não na confirmação de voo), não há falha
        // a avisar — e o LastError estaria vazio.
        if (!started && !pilotDeclined)
            System.Windows.MessageBox.Show(
                _updates?.LastError ?? "Não foi possível instalar a atualização agora.",
                ProductIdentity.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
    }
```

(`TwoG.Connector.Core` e `TwoG.Connector.Services` já estão nos `using`; `App` resolve pelo namespace pai `TwoG.Connector`.)

- [ ] **Passo 3: Faixa de atualização no cabeçalho (`MainWindow.xaml`)**

Trocar o cabeçalho inteiro — do comentário `<!-- ══ Cabeçalho ══ -->` até o `</Grid>` que o fecha, antes de `<!-- ══ Simulador ══ -->` — por:

```xml
        <!-- ══ Cabeçalho ══ -->
        <StackPanel Grid.Row="0" Margin="2,0,2,16">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Width="42" Height="42" CornerRadius="11" VerticalAlignment="Center">
                    <Border.Background>
                        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                            <GradientStop Color="#33D1C4" Offset="0" />
                            <GradientStop Color="#1B7F8E" Offset="1" />
                        </LinearGradientBrush>
                    </Border.Background>
                    <TextBlock Text="2G" FontSize="17" FontWeight="Bold" Foreground="#07272B"
                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>
                <StackPanel Grid.Column="1" Margin="13,0,0,0" VerticalAlignment="Center">
                    <TextBlock Text="2G Connector" FontSize="19" FontWeight="Bold"
                               Foreground="{StaticResource TextBrush}" />
                    <TextBlock Text="MSFS • Prepar3D • X-Plane" FontSize="12"
                               Foreground="{StaticResource DimBrush}" Margin="1,1,0,0" />
                </StackPanel>
            </Grid>

            <!-- Só aparece com atualização pendente (ou disponível sem permissão de escrita). -->
            <Border Background="{StaticResource CardBrush}" BorderBrush="{StaticResource AccentDarkBrush}"
                    BorderThickness="1" CornerRadius="10" Padding="12,8" Margin="0,12,0,0"
                    Visibility="{Binding HasUpdateBanner, Converter={StaticResource BoolToVisibility}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding UpdateBannerText}" FontSize="12" TextWrapping="Wrap"
                               Foreground="{StaticResource TextBrush}" VerticalAlignment="Center" />
                    <Button Grid.Column="1" Content="Atualizar agora" Command="{Binding ApplyUpdateNowCommand}"
                            Visibility="{Binding CanApplyUpdateNow, Converter={StaticResource BoolToVisibility}}"
                            Margin="10,0,0,0" Padding="10,4" FontSize="11.5" />
                </Grid>
            </Border>
        </StackPanel>
```

- [ ] **Passo 4: Checkbox nas Configurações e linha no Diagnóstico**

Nas Configurações, logo depois do `StackPanel` horizontal com "Iniciar minimizado" e "Fechar para a bandeja":

```xml
                    <CheckBox Content="Atualizar automaticamente (sempre fora de voo)"
                              IsChecked="{Binding AutoUpdateInput}" Margin="0,8,0,0" />
```

No Diagnóstico, imediatamente antes de `<TextBlock Text="APPS DESCOBERTOS" Style="{StaticResource ValueLabel}" />`:

```xml
                    <TextBlock Text="ATUALIZAÇÃO" Style="{StaticResource ValueLabel}" />
                    <TextBlock Text="{Binding DiagUpdate}" FontSize="11.5" TextWrapping="Wrap"
                               Foreground="{StaticResource DimBrush}" Margin="0,3,0,12" />
```

- [ ] **Passo 5: Compilar e conferir o XAML**

Run: `dotnet build src/TwoG.Connector/TwoG.Connector.csproj`
Expected: build limpo (o BAML só compila com o XAML válido).

Run: `grep -n 'Grid.Row=' src/TwoG.Connector/MainWindow.xaml | grep -v 'Grid.Row="[0-4]" Grid.Column'`
Expected: as seções de topo continuam em `Grid.Row` 0 a 6, na mesma ordem de antes — a faixa entrou dentro do cabeçalho, sem linha nova no Grid.

- [ ] **Passo 6: Commit**

```bash
git add -A
git commit -m "Interface do updater: faixa de atualização pronta e linha no Diagnóstico

A faixa aparece só com atualização pendente e traz o botão Atualizar
agora, que pede confirmação quando há voo ativo. Na pasta sem permissão
de escrita ela vira aviso para baixar manualmente. Configurações ganham
o checkbox Atualizar automaticamente; o Diagnóstico mostra versão,
última verificação e o erro do updater junto dos demais.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 11: Assinatura do manifesto no CI

A assinatura é feita por uma ferramenta de console que usa o **mesmo** `UpdateManifest` e `UpdateSignature` do app, e confere a assinatura contra `UpdateKeys.Accepted` antes de publicar. Por que não PowerShell: `ECDsa.ImportFromPem` só aceita `ReadOnlySpan<char>`, que o PowerShell não consegue passar.

**Arquivos:**
- Criar: `tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj`
- Criar: `tools/TwoG.Connector.ReleaseSigner/Program.cs`
- Modificar: `TwoG.Connector.slnx`
- Modificar: `.github/workflows/build.yml`

**Interfaces:**
- Consome: `AppVersion`, `UpdateFile.FromFile`, `UpdateManifest.Serialize`, `UpdateSignature.Sign`/`Verify`, `UpdateKeys.Accepted`.
- Produz: `dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -- <dist> <versão> <arquivo>...` → grava `dist/update.json` e `dist/update.json.sig`; sai com código 0 só se a assinatura conferir.

- [ ] **Passo 1: Projeto da ferramenta**

`tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TwoG.Connector.Core\TwoG.Connector.Core.csproj" />
  </ItemGroup>

</Project>
```

`tools/TwoG.Connector.ReleaseSigner/Program.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using TwoG.Connector.Core;

// Gera e assina o update.json de um release.
//
//   dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -- <pasta dist> <versão> <arquivo>...
//
// A chave privada vem de UPDATE_SIGNING_KEY (conteúdo PEM, no CI) ou de
// UPDATE_SIGNING_KEY_PATH (arquivo, uso local). Ela nunca é impressa — nem em
// mensagem de erro, que mostra só o tipo da exceção.

if (args.Length < 3)
{
    Console.Error.WriteLine("uso: <pasta dist> <versão> <arquivo> [arquivo...]");
    return 2;
}

var dist = args[0];
if (!AppVersion.TryParse(args[1], out var version))
{
    Console.Error.WriteLine($"versão inválida: {args[1]}");
    return 2;
}

var keyPem = Environment.GetEnvironmentVariable("UPDATE_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(keyPem))
{
    var keyPath = Environment.GetEnvironmentVariable("UPDATE_SIGNING_KEY_PATH");
    if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
        keyPem = File.ReadAllText(keyPath);
}
if (string.IsNullOrWhiteSpace(keyPem))
{
    Console.Error.WriteLine("chave de assinatura ausente: defina UPDATE_SIGNING_KEY ou UPDATE_SIGNING_KEY_PATH");
    return 1;
}

var files = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);
foreach (var name in args.Skip(2))
{
    var path = Path.Combine(dist, name);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"arquivo não encontrado: {path}");
        return 1;
    }
    files[name] = UpdateFile.FromFile(path);
}

var manifest = UpdateManifest.Serialize(version, files);

string signature;
try
{
    signature = UpdateSignature.Sign(manifest, keyPem);
}
catch (Exception ex) when (ex is ArgumentException or CryptographicException)
{
    Console.Error.WriteLine($"chave de assinatura ilegível ({ex.GetType().Name})");
    return 1;
}

// Confere com as chaves embutidas no app ANTES de publicar: um secret que não
// bate com elas geraria um release que nenhum app instalado aceita.
if (!UpdateSignature.Verify(manifest, signature, UpdateKeys.Accepted))
{
    Console.Error.WriteLine("a assinatura não confere com nenhuma chave pública embutida no app");
    return 1;
}

File.WriteAllBytes(Path.Combine(dist, "update.json"), manifest);
File.WriteAllText(Path.Combine(dist, "update.json.sig"), signature);
Console.WriteLine(Encoding.UTF8.GetString(manifest));
return 0;
```

Em `TwoG.Connector.slnx`, antes de `</Solution>`:

```xml
  <Folder Name="/tools/">
    <Project Path="tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj" />
  </Folder>
```

- [ ] **Passo 2: Provar que a ferramenta recusa chave errada**

Um par descartável no scratchpad — nunca a chave real:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out /tmp/2gc-throwaway.pem
mkdir -p /tmp/2gc-dist && printf 'MZ teste' > /tmp/2gc-dist/2G-Connector.exe
UPDATE_SIGNING_KEY_PATH=/tmp/2gc-throwaway.pem dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -- /tmp/2gc-dist 1.4.0 2G-Connector.exe; echo "exit=$?"
rm -f /tmp/2gc-throwaway.pem
```

Expected: `a assinatura não confere com nenhuma chave pública embutida no app` e `exit=1`. Nenhum `update.json` em `/tmp/2gc-dist`.

Run: `dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -- /tmp/2gc-dist 1.4.0 2G-Connector.exe; echo "exit=$?"` (sem variável de chave)
Expected: `chave de assinatura ausente: ...` e `exit=1`.

- [ ] **Passo 3: Canal de release no publish**

Em `.github/workflows/build.yml`, o passo `Publish (single-file)` ganha uma linha:

```yaml
      - name: Publish (single-file)
        run: >
          dotnet publish src/TwoG.Connector/TwoG.Connector.csproj
          -c Release
          -p:PublishReadyToRun=true
          -p:Version=${{ env.VERSION }}
          -p:UpdateChannel=${{ startsWith(github.ref, 'refs/tags/v') && 'stable' || '' }}
          -o publish
```

- [ ] **Passo 4: Assinar o manifesto e mover os checksums para o fim**

No passo `Stage release assets`, **remover** o bloco dos checksums (do comentário `# Checksums para quem quiser conferir o download` até `Get-Content dist\SHA256SUMS.txt`). Depois desse passo e antes do `actions/upload-artifact`, acrescentar:

```yaml
      # Manifesto do auto-update, assinado. Só em tag: é o que os apps instalados
      # vão aceitar. Sem o secret o release FALHA — nunca se publica manifesto sem
      # assinatura. A ferramenta confere a assinatura contra a chave embutida no app.
      - name: Sign update manifest
        if: startsWith(github.ref, 'refs/tags/v')
        shell: pwsh
        env:
          UPDATE_SIGNING_KEY: ${{ secrets.UPDATE_SIGNING_KEY }}
        run: |
          if (-not $env:UPDATE_SIGNING_KEY) {
            throw "Secret UPDATE_SIGNING_KEY ausente: release sem manifesto assinado não sai."
          }
          dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -c Release -- dist $env:VERSION $env:EXE_NAME $env:SETUP_NAME
          if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar o manifesto." }

      - name: Checksums
        shell: pwsh
        run: |
          # Checksums para quem quiser conferir o download
          Get-FileHash "dist\*" -Algorithm SHA256 |
            ForEach-Object { "{0}  {1}" -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) } |
            Set-Content -Path dist\SHA256SUMS.txt -Encoding ascii
          Get-ChildItem dist | Format-Table Name, Length
          Get-Content dist\SHA256SUMS.txt
```

O `Publish release` já publica `dist/*`, então `update.json` e `update.json.sig` entram no release sem mudança ali.

- [ ] **Passo 5: Commit**

```bash
git add -A
git commit -m "CI assina o manifesto de atualização com a ferramenta de release

ReleaseSigner gera o update.json com o mesmo código que o app usa para
lê-lo, assina com a chave do secret e confere a assinatura contra a chave
pública embutida antes de publicar. Release de tag falha sem o secret.
Só builds de tag recebem o canal stable; os demais não se atualizam.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarefa 12: Documentação, verificação em Windows e release

Os passos 4 em diante publicam coisas e **exigem autorização explícita do mantenedor, passo a passo**: PR, tags, escrita no bucket do site. Um agente executando o plano para antes de cada um e pergunta.

**Arquivos:**
- Modificar: `README.md`, `CLAUDE.md`

- [ ] **Passo 1: README**

1. Título, `alt` do ícone e toda menção ao produto: "2G GPS Cliente for MSFS" / "2G GPS Cliente" → "2G Connector".
2. Tabela de download: os três links passam a `2G-Connector.exe`, `2G-Connector.zip`, `2G-Connector-Setup.exe` (mesmo formato `releases/latest/download/<nome>`).
3. Tabela de arquivos locais:

```markdown
| `%APPDATA%\2G Connector\settings.json` | Suas configurações (copiadas da pasta `2G GPS Cliente` na primeira execução, se existir) |
| `%LOCALAPPDATA%\2G Connector\runtime\<versão>\` | DLLs do SimConnect e do runtime C++, extraídas na 1ª execução |
| `%LOCALAPPDATA%\2G Connector\updates\` | Atualização baixada e verificada, aguardando instalação |
| `%LOCALAPPDATA%\2G Connector\erro.log` | Só se ocorrer um erro inesperado |
```

4. Na linha de solução de problemas do SimConnect, o caminho do `erro.log` passa a `%LOCALAPPDATA%\2G Connector\erro.log`.
5. Seção nova, depois das instruções de instalação:

```markdown
## Atualização automática

A partir da v1.4.0 o 2G Connector se atualiza sozinho — e **nunca durante um voo**.

- Verifica um minuto depois de abrir e a cada 6 horas, e baixa em segundo plano.
- Instala só em três momentos: ao abrir, antes de conectar ao simulador; ao sair
  pelo menu da bandeja; ou quando você clica em **Atualizar agora** (em voo, ele
  pergunta antes).
- Todo download é verificado antes de instalar: o manifesto do release é assinado
  digitalmente, a versão precisa ser mais nova que a instalada e o arquivo precisa
  bater com o hash declarado. Qualquer falha descarta o download e aparece no
  painel **Diagnóstico**.
- Para desligar: **Configurações → Atualizar automaticamente**.

Quem está na v1.3.0 ou anterior atualiza manualmente uma última vez. A
configuração e o registro no MSFS são migrados sozinhos.
```

6. Seção nova para mantenedores, perto de "Desenvolvimento":

```markdown
### Publicar uma versão

Um push de tag `v*` gera o release. O CI assina o `update.json` com o secret
`UPDATE_SIGNING_KEY` e **falha** se ele não existir ou não bater com a chave
pública em `src/TwoG.Connector.Core/UpdateKeys/`. Para testar o updater sem mexer
no `latest`, publique um pré-release (`v1.4.1-rc.1`) e aponte um app instalado
para ele com a variável de ambiente
`TWOG_UPDATE_FEED=https://github.com/galenoferreira/gps_connector/releases/download/v1.4.1-rc.1/update.json`.
```

- [ ] **Passo 2: CLAUDE.md**

1. Primeira linha: `# 2G Connector`.
2. Na lista "Estrutura", acrescentar:

```markdown
  - `Services/UpdateService.cs` / `UpdateInstaller.cs` — auto-update (gatilhos, setup silencioso, troca do exe)
- `src/TwoG.Connector.Core/` — lógica pura testável: identidade do produto, EXE.xml, manifesto/assinatura/política do updater, parsers
- `tools/TwoG.Connector.ReleaseSigner/` — gera e assina o `update.json` no CI
```

3. Em "Regras importantes", acrescentar:

```markdown
- **Nomes herdados da v1.3.0 que NUNCA mudam:** mutex `Local\TwoG.GpsClient.SingleInstance`,
  evento `Local\TwoG.GpsClient.ShowWindow` e o `AppId` do instalador. Estão em
  `ProductIdentity` e têm teste que os fixa. Mudar faz a v1.3.0 e a atual transmitirem em dobro.
- **Updater:** só instala nos gatilhos Startup, Exit e Manual (`UpdatePolicy`), nunca por
  timer. Só builds de tag (`UpdateChannel=stable`) se atualizam. Toda atualização passa por
  assinatura ECDSA P-256 → versão maior → SHA256, nessa ordem.
- **Chave de assinatura:** a privada vive só no secret `UPDATE_SIGNING_KEY` e no backup do
  mantenedor. Nunca ler, copiar ou imprimir. Testes geram pares descartáveis. Rotação = publicar
  antes uma versão cuja pasta `UpdateKeys/` tenha a chave antiga e a nova.
```

- [ ] **Passo 3: Suíte completa e commit**

```bash
dotnet build TwoG.Connector.slnx
dotnet test tests/TwoG.Connector.Core.Tests/TwoG.Connector.Core.Tests.csproj
dotnet publish src/TwoG.Connector/TwoG.Connector.csproj -c Release -o /tmp/2gc-pub-final && ls /tmp/2gc-pub-final
```

Expected: build limpo; todos os testes passando (149 da linha de base + os novos); publish com um único arquivo, `2G-Connector.exe`.

```bash
git add -A
git commit -m "Documenta o 2G Connector, a migração e o auto-update

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Passo 4 (autorização): PR para o PROD e CI verde**

Com aprovação do mantenedor: `git push -u origin claude/2g-connector` e abrir PR contra `PROD`. O CI precisa passar — é a primeira validação do `setup.iss` (a `iscc` só existe lá) e dos testes no Windows. O passo `Sign update manifest` **não** roda no PR (só em tag).

- [ ] **Passo 5 (autorização): pré-release e verificação em Windows**

Com aprovação, depois do merge no `PROD`:

1. Tag `v1.4.0-rc.1` no `PROD`. Pré-release: não assume o `latest`, os links do site não mudam. Confere no release que `update.json` e `update.json.sig` foram publicados.
2. Numa máquina Windows **com a v1.3.0 instalada pelo instalador**, rodar o `2G-Connector-Setup.exe` do rc.1 e conferir:
   - configuração preservada (nome de dispositivo, IPs unicast);
   - `EXE.xml` do MSFS só com a entrada `2G Connector`, apontando para o exe novo — já ao fim do instalador, mesmo com a caixa "Executar 2G Connector" desmarcada (é o `-register` do `[Run]`);
   - atalhos antigos sumiram do Menu Iniciar e da área de trabalho;
   - valor `2G GPS Cliente` ausente em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
   - com a tarefa "iniciar com o Windows" marcada na v1.3.0 e o app desabilitado no Gerenciador de Tarefas (aba Aplicativos de inicialização): o `2G Connector` aparece lá desabilitado, e o valor `2G GPS Cliente` sumiu de `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`;
   - abrir o exe antigo à mão (se ainda existir em outra pasta) não cria segunda instância.
3. Em outra pasta, baixar o `2G-Connector.exe` avulso do rc.1 e executar: o `EXE.xml` passa a apontar para ele.
4. Instalação limpa numa máquina sem nada: nomes, pastas, dispositivo "2G Connector".
5. Tag `v1.4.0-rc.2`. Nas duas instalações do rc.1 (via instalador e avulsa), definir
   `TWOG_UPDATE_FEED=https://github.com/galenoferreira/gps_connector/releases/download/v1.4.0-rc.2/update.json`
   e reabrir o app. Conferir, em cada uma:
   - a faixa "v1.4.0-rc.2 pronta" aparece em até ~1 min;
   - **Atualizar agora** instala e reabre já na rc.2 (rodapé);
   - repetir forçando pelo gatilho de saída (Sair na bandeja): instala e **não** reabre;
   - repetir pelo gatilho de partida: fechar, abrir de novo — instala antes de conectar ao simulador.
6. Adulterar um byte do `update.json` baixado num feed de teste (copiar os arquivos do rc.2 para um servidor HTTPS próprio, editar, apontar `TWOG_UPDATE_FEED`): o Diagnóstico deve mostrar "assinatura inválida — atualização descartada" e nada é instalado.

A etapa 5 é um refinamento do spec: lá o teste era depois da v1.4.0, com uma v1.4.1-rc.1. Fazer com rc.1 → rc.2 valida o updater **antes** de a v1.4.0 chegar aos usuários.

- [ ] **Passo 6 (autorização): site**

Com aprovação, atualizar `gps-connector.html` no bucket `2gpilot-com-site` (região `us-east-1`): links para os nomes novos e "2G GPS Cliente" → "2G Connector". O bucket tem versionamento, então a versão anterior da página fica recuperável. Conferir depois que os três links respondem 200.

- [ ] **Passo 7 (autorização): release v1.4.0**

Com aprovação, tag `v1.4.0` no `PROD`. Conferir:
- é o `latest`, não pré-release;
- tem os três arquivos novos, as três cópias com nome antigo, `update.json`, `update.json.sig` e `SHA256SUMS.txt`;
- `https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json` responde com a versão `1.4.0`;
- os links antigos (`.../latest/download/2G-GPS-Cliente.exe`) ainda respondem 200.
