# 2G Connector

Conector Windows (WPF, .NET 10, x64) que lê posição de simuladores via SimConnect
(MSFS 2020/2024 e Prepar3D v4+) e transmite no protocolo XGPS (UDP broadcast, porta
49002) para EFBs. EFBs oficialmente suportados: **2G Pilot EFB e ForeFlight**.

## Estrutura

- `src/TwoG.Connector/` — app WPF (namespace `TwoG.Connector`, exe `2G-Connector.exe`)
  - `Services/SimConnectService.cs` — conexão/retry SimConnect, normaliza unidades para `GpsFix`
  - `Services/XgpsBroadcaster.cs` — sentenças XGPS/XATT via UDP (broadcast + unicast opcional)
  - `ViewModels/MainViewModel.cs` — UI orientada por polling (DispatcherTimer 250 ms)
  - `Services/SimConnectRuntime.cs` — extrai as DLLs do SimConnect (recursos embutidos)
    para `%LOCALAPPDATA%` e as carrega; chamar `Ensure()` ANTES de tocar tipos do SimConnect
  - `Services/UpdateService.cs` / `UpdateInstaller.cs` — auto-update (gatilhos, setup silencioso, troca do exe)
- `src/TwoG.Connector.Core/` — lógica pura testável: identidade do produto, EXE.xml, manifesto/assinatura/política do updater, parsers
- `tools/TwoG.Connector.ReleaseSigner/` — gera e assina o `update.json` no CI
- `installer/setup.iss` — instalador Inno Setup opcional (detecta MSFS, atalhos, desinstalador,
  limpeza da v1.3.0, `-register` ao terminar, reabertura após o setup silencioso do updater)
- `.github/workflows/build.yml` — build + instalador no windows-latest

## Regras importantes

- Sentenças XGPS/XATT usam SEMPRE `CultureInfo.InvariantCulture` (ponto decimal).
- `GpsFix` já é normalizado: metros MSL, m/s, pitch positivo = nariz p/ cima,
  roll positivo = asa direita — a conversão de sinais do MSFS acontece SÓ no SimConnectService.
- Build local em macOS/Linux exige `EnableWindowsTargeting` (já no csproj): `dotnet build` compila,
  mas só roda no Windows.
- Taxas padrão: 2 Hz para XGPS e XATT (decisão do produto; configurável na UI).
- Prepar3D usa a MESMA API SimConnect, mesmas SimVars e mesmas convenções de sinal
  invertidas do FSX — não há caminho de código separado, só o nome exibido muda
  (`SimulatorIdentity.Describe`). Suporte ainda NÃO validado num P3D real.
- X-Plane é UDP puro (`XPlaneService` + `XPlaneProtocol`/`XPlaneFixAssembler` no Core):
  beacon multicast BECN 239.255.1.1:49707 para descoberta, RREF na porta anunciada.
  **Sem inversão de sinais** — theta/phi já são a convenção do XATT, ao contrário do
  MSFS. A ordem do array `XPlaneFixAssembler.Datarefs` É o contrato de fio (o índice
  vai no pacote e volta na resposta): nunca reordenar. Também NÃO validado num X-Plane real.
- Descoberta de EFB (`EfbDiscoveryService` no Core): Bonjour `_2gpilot._udp.local`
  por query mDNS one-shot de porta efêmera (bit QU — NUNCA bindar a 5353, o Windows
  já tem responder lá) e anúncio JSON na UDP 63093, cujo IP de ORIGEM é o do app.
  Os dois alimentam `DiscoveredEfbRegistry` (TTL 30 s, teto 4). O unicast SOMA ao
  broadcast, nunca substitui: se a descoberta cair em voo o piloto perde posição.
- Todas as fontes rodam juntas via `CompositeSimSource`; quem responder primeiro vence.
- Plano de voo (botão SYNC PV): `IFlightPlanSource` é capacidade OPCIONAL de uma fonte.
  MSFS/P3D leem o `.PLN` (caminho via `RequestSystemState("FlightPlan")`, com fallback
  em `PlnFileLocator` porque o MSFS 2024 devolve `".PLN"` — bug confirmado pela Asobo);
  X-Plane lê o `.fms` mais recente, só local e só se o piloto salvou. Entrega ao EFB:
  anúncio `2GFPL` na UDP 49002 + `FlightPlanServer` (TcpListener, HTTP mínimo) na 49003.
  HttpListener NÃO serve aqui: escutar em todas as interfaces exigiria admin.
- `FlightPlanServer` mora no `Core` de propósito — sem dependência de WPF, roda e é
  testado por HTTP real no macOS.
- Simuladores novos entram implementando `ISimSource`; o broadcaster e a UI não mudam.
  Lógica pura (parsers, identificação) vai no `Core`, que é testável sem simulador.
- **Entrega é UM único .exe** (`PublishSingleFile` no csproj, RID fixo `win-x64`).
  As DLLs do SimConnect NÃO podem entrar no bundle (mixed-mode C++/CLI não carrega
  da memória) — são recursos embutidos extraídos pelo `SimConnectRuntime`, junto
  com o runtime VC++ x64 em `libs/vcruntime/` (a SimConnect.dll nativa importa
  MSVCP140/VCRUNTIME140/VCRUNTIME140_1; sem elas dá erro 126 em máquina sem MSFS).
  Nunca adicionar arquivos soltos à saída de publish: o CI falha se houver mais de um.
- Neste volume (exFAT) o macOS cria arquivos `._*` que quebram o build; o
  `Directory.Build.targets` os remove dos globs, mas passe sempre o caminho do
  `.csproj` explicitamente nos comandos `dotnet` (a busca por pasta ainda os enxerga).
- **Nomes herdados da v1.3.0 que NUNCA mudam:** mutex `Local\TwoG.GpsClient.SingleInstance`,
  evento `Local\TwoG.GpsClient.ShowWindow` (em `ProductIdentity`, com teste que os fixa) e o
  `AppId` do instalador (em `setup.iss`, que também usa o mutex no `AppMutex`). Mudar faz a
  v1.3.0 e a atual transmitirem em dobro, e o instalador instalar ao lado em vez de atualizar.
- **Updater:** só instala nos gatilhos Startup, Exit e Manual (`UpdatePolicy`), nunca por
  timer. Só builds de tag (`UpdateChannel=stable`) se atualizam. Toda atualização passa por
  assinatura ECDSA P-256 → versão maior → SHA256, nessa ordem.
- **Troca do exe avulso:** quem troca o arquivo é o exe NOVO, rodando da pasta de updates com
  `--replace <exe> <pid> <1|0> [argumentos...]` (`UpdateInstaller.ReplaceArg`). O formato é
  contrato entre versões: a versão instalada chama a NOVA com ele; nunca mudar.
- **Chave de assinatura:** a privada vive só no secret `UPDATE_SIGNING_KEY` e com o
  mantenedor (arquivo local + backup). Nunca ler, copiar ou imprimir. Testes geram pares
  descartáveis. Rotação = publicar antes uma versão cuja pasta `UpdateKeys/` tenha a chave
  antiga e a nova.
