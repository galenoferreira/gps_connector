<p align="center">
  <img src="assets/icon256.png" width="96" alt="2G GPS Cliente" />
</p>

<h1 align="center">2G GPS Cliente for MSFS</h1>

<p align="center">
  Conector Windows que transmite a posição do <b>Microsoft Flight Simulator 2020/2024</b>
  para qualquer EFB na rede — <b>2G Pilot</b>, ForeFlight, Garmin Pilot, SkyDemon e outros —
  usando o protocolo <b>XGPS</b> (broadcast UDP, porta 49002).
</p>

---

## Como funciona

```
MSFS 2020/2024 ──SimConnect──▶ 2G GPS Cliente ──UDP 49002 (XGPS/XATT)──▶ EFB (tablet/celular)
```

- **Conexão automática**: o app tenta se conectar ao simulador a cada 3 segundos; basta
  abrir o MSFS em qualquer ordem. Suporta MSFS 2020 e MSFS 2024 (Microsoft Store e Steam)
  com um único binário.
- **Transmissão XGPS**: posição (`XGPS`) e atitude (`XATT`) enviadas por broadcast dirigido
  em todas as interfaces de rede ativas + unicast opcional para IPs específicos.
  Padrão: 5 Hz (configurável na interface).
- **Iniciar junto com o MSFS**: o app se registra no `EXE.xml` do simulador
  (mecanismo padrão usado por FSUIPC7 etc.), com merge seguro e backup — nunca
  sobrescreve entradas de outros add-ons. Auto-repara o registro a cada execução.
- **Bandeja do sistema**: fechar a janela mantém a transmissão ativa na bandeja.

No EFB não há nada para configurar: com o tablet na mesma rede Wi-Fi do PC, o
dispositivo "2G GPS" aparece automaticamente (no ForeFlight: **More → Devices**).

## Instalação

**Não há instalação.** Baixe `2G-GPS-Cliente-x.y.z.exe` na página de releases e
execute — um único arquivo, sem pasta de dependências, sem instalador e sem
precisar do runtime .NET.

- Rode de onde quiser: Desktop, Downloads, pendrive.
- Não requer administrador nem regra de firewall (o app apenas **envia** UDP,
  liberado por padrão no Windows).
- Detecta MSFS 2020/2024 (Store e Steam) sozinho, em tempo de execução.
- Se você mover o .exe de lugar, o "Iniciar junto com o MSFS" se reajusta no
  próximo início.

<details>
<summary>O que o app grava fora do .exe</summary>

| Caminho | Conteúdo |
|---|---|
| `%APPDATA%\2G GPS Cliente\settings.json` | Suas configurações |
| `%LOCALAPPDATA%\2G GPS Cliente\runtime\<versão>\` | As duas DLLs do SimConnect, extraídas na 1ª execução |
| `EXE.xml` do MSFS | Só se "Iniciar junto com o MSFS" estiver marcado |

A primeira execução é um pouco mais lenta (o .exe se descompacta); as seguintes
são normais.

</details>

> Também é publicado um instalador (`2G-GPS-Cliente-Setup-x.y.z.exe`) para quem
> prefere atalho no Menu Iniciar e entrada em "Adicionar ou remover programas".
> Ele instala exatamente o mesmo arquivo único.

> **Aviso SmartScreen**: builds não assinados digitalmente exibem o alerta
> "O Windows protegeu seu computador" — clique em *Mais informações → Executar
> assim mesmo*. É o comportamento padrão para executáveis novos sem assinatura.

## Solução de problemas

| Sintoma | Causa provável |
|---|---|
| EFB não recebe posição | Tablet em outra rede/sub-rede Wi-Fi; ou o roteador/AP tem "isolamento de clientes" (AP/client isolation) ativado |
| Recebe em um EFB mas não em outro | Porta 49002 em uso por outro conector (feche outros bridges GPS) |
| Posição congela no EFB | Simulador pausado ou no menu — a transmissão pausa automaticamente e retoma no voo |
| Tablet em sub-rede diferente | Adicione o IP do tablet em **Configurações → IPs adicionais** (unicast) |

## Desenvolvimento

```
src/TwoG.GpsClient/        App WPF (.NET 10, x64) — UI, SimConnect, broadcaster
src/TwoG.GpsClient.Core/   Lógica pura do protocolo (multiplataforma, testável)
tests/                     Testes de unidade do protocolo
libs/                      DLLs oficiais do SimConnect (MSFS SDK 0.24.3.0)
installer/setup.iss        Instalador Inno Setup (opcional)
```

```bash
dotnet test tests/TwoG.GpsClient.Core.Tests/TwoG.GpsClient.Core.Tests.csproj
```

```bash
dotnet publish src/TwoG.GpsClient/TwoG.GpsClient.csproj -c Release -o publish
```

Ambos rodam em qualquer SO (o csproj tem `EnableWindowsTargeting` e RID fixo
`win-x64`); o publish gera o `.exe` único mesmo a partir do macOS/Linux, mas o
binário só **executa** no Windows (SimConnect é x64/Windows). O CI
(`.github/workflows/build.yml`) publica, **valida que a saída é 1 arquivo só**,
compila o instalador e anexa os artefatos; tags `v*` geram release automaticamente.

### Como o .exe único funciona

O publish usa `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` +
`EnableCompressionInSingleFile` (definidos no csproj). As duas DLLs do SimConnect
ficam **fora** do bundle: o wrapper gerenciado é *mixed-mode* C++/CLI, e a
[doc da Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
avisa que componentes managed C++ não são adequados a single-file (assemblies do
bundle são carregados da memória, o que não funciona para mixed-mode). Em vez
disso elas viajam como **recursos embutidos** e o
[`SimConnectRuntime`](src/TwoG.GpsClient/Services/SimConnectRuntime.cs) as extrai
para `%LOCALAPPDATA%` na primeira execução, pré-carrega a nativa via `LoadLibraryEx`
e resolve a gerenciada por `AssemblyLoadContext.Default.Resolving` — carregamento
a partir de arquivos reais em disco, que é o cenário suportado.

### Protocolo XGPS (referência)

```
XGPS<nome>,<lon>,<lat>,<alt m MSL>,<curso verdadeiro °>,<vel. solo m/s>
XATT<nome>,<proa verdadeira °>,<pitch °>,<roll °>,,,,,,,,,
```

Uma sentença por datagrama UDP, ASCII, decimal com ponto, sem terminador.
Pitch positivo = nariz para cima; roll positivo = asa direita (convenção X-Plane —
os sinais do MSFS são invertidos pelo conector). Os 9 campos vazios do `XATT`
atendem ao Garmin Pilot (exige 13 campos); ForeFlight ignora os extras.

> **Nota**: a spec da ForeFlight recomenda posição a 1 Hz e atitude a 4–10 Hz.
> O padrão do produto é 5 Hz para ambos; ajuste na UI se necessário.

---

© 2026 2G. Todos os direitos reservados.
