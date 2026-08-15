<p align="center">
  <img src="assets/icon256.png" width="96" alt="2G GPS Cliente" />
</p>

<h1 align="center">2G GPS Cliente for MSFS</h1>

<p align="center">
  Conector Windows que transmite a posição do <b>Microsoft Flight Simulator 2020/2024</b>
  para o seu EFB, usando o protocolo <b>XGPS</b> (broadcast UDP, porta 49002).
</p>

<p align="center">
  <a href="https://github.com/galenoferreira/gps_connector/releases/latest"><img src="https://img.shields.io/github/v/release/galenoferreira/gps_connector?label=vers%C3%A3o" alt="Versão" /></a>
  <img src="https://img.shields.io/badge/Windows-x64-0078D4" alt="Windows x64" />
  <img src="https://img.shields.io/badge/MSFS-2020%20%7C%202024-1B7F8E" alt="MSFS 2020 e 2024" />
</p>

---

## EFBs suportados

| EFB | Configuração necessária |
|---|---|
| **2G Pilot EFB** | Nenhuma — basta estar na mesma rede Wi-Fi do PC |
| **ForeFlight** | Nenhuma — o dispositivo aparece sozinho em **More → Devices** |

Voando, o EFB passa a usar a posição do simulador no lugar do GPS do aparelho.
No ForeFlight, a barra de instrumentos mostra a precisão identificada pelo nome do
dispositivo (ex.: *Accuracy (2G GPS)*), e a atitude enviada alimenta o horizonte
do Synthetic Vision.

> Outros EFBs que leem o protocolo XGPS na porta 49002 tendem a funcionar, mas
> não são testados nem oficialmente suportados.

## Simuladores suportados

| Simulador | Como conecta | Situação |
|---|---|---|
| **MSFS 2024** (Store e Steam) | SimConnect | ✅ Suportado |
| **MSFS 2020** (Store e Steam) | SimConnect | ✅ Suportado |
| **Prepar3D v4 / v5 / v6** | SimConnect | 🧪 **Experimental** — não validado com o simulador real |
| **X-Plane 11 / 12** | UDP (beacon + datarefs) | 🧪 **Experimental** — não validado com o simulador real |
| FSX, Prepar3D v1–v3 | — | ❌ Fora de escopo (SimConnect só 32 bits) |

Não há nada a escolher: o app procura os três ao mesmo tempo e usa o que
responder — inclusive um X-Plane rodando em **outra máquina da rede**, que ele
descobre sozinho pelo beacon multicast.

Se você testar o Prepar3D ou o X-Plane,
[conte como foi](https://github.com/galenoferreira/gps_connector/issues) — a tela
mostra qual simulador foi detectado e se a conexão foi aceita. Detalhes técnicos e
decisões no [estudo multi-simulador](docs/estudo-multi-simulador.md).

<details>
<summary>X-Plane: alternativa sem instalar nada</summary>

O X-Plane já sabe transmitir XGPS/XATT sozinho: *Settings → Network → "iPhone,
iPad and External Apps"*, marcando o broadcast para apps de mapa. Funciona sem
este conector.

O que você ganha usando o 2G GPS Cliente: o dispositivo aparece com o nome
**"2G GPS"** no EFB em vez de **"1"** (o X-Plane se anuncia como `XGPS1`), não
precisa achar a opção nos ajustes, e você pode enviar para um IP específico em
outra sub-rede. O que você perde: o caminho nativo tem precisão um pouco melhor
(~1,5 m contra o float32 do protocolo de datarefs), diferença irrelevante para um
mapa móvel.

</details>

## Como funciona

```
MSFS · Prepar3D ──SimConnect──┐
                              ├─▶ 2G GPS Cliente ──UDP 49002 (XGPS/XATT)──▶ EFB
X-Plane ──────────UDP RREF────┘
```

- **Conexão automática**: o app procura todos os simuladores ao mesmo tempo e usa
  o que responder — pode abrir o simulador antes ou depois, tanto faz. Um único
  binário atende MSFS 2020/2024, Prepar3D e X-Plane.
- **Transmissão**: posição (`XGPS`) e atitude (`XATT`) por broadcast dirigido em
  todas as interfaces de rede ativas, mais unicast opcional para IPs específicos.
  Padrão de 5 Hz, ajustável na interface.
- **Pausa inteligente**: com o simulador pausado ou no menu, a transmissão para e
  retoma sozinha quando o voo volta — o EFB não fica com a aeronave congelada.
- **Iniciar junto com o MSFS**: registra-se no `EXE.xml` do simulador com merge
  seguro e backup, sem tocar nas entradas de outros add-ons. Se um update do MSFS
  apagar o registro, ele se refaz na execução seguinte.
- **Bandeja do sistema**: fechar a janela mantém a transmissão ativa em segundo
  plano; para encerrar de fato, use **Sair** no menu da bandeja.

## Download

Links **permanentes** — sempre entregam a versão mais recente, sem precisar de
atualização a cada release:

| Link | Para quem |
|---|---|
| [**2G-GPS-Cliente.exe**](https://github.com/galenoferreira/gps_connector/releases/latest/download/2G-GPS-Cliente.exe) | **Recomendado** — executável único, sem instalação |
| [2G-GPS-Cliente.zip](https://github.com/galenoferreira/gps_connector/releases/latest/download/2G-GPS-Cliente.zip) | Mesmo executável, compactado |
| [2G-GPS-Cliente-Setup.exe](https://github.com/galenoferreira/gps_connector/releases/latest/download/2G-GPS-Cliente-Setup.exe) | Instalador com atalho no Menu Iniciar e desinstalador |
| [Página de releases](https://github.com/galenoferreira/gps_connector/releases/latest) | Notas da versão e checksums SHA-256 |

## Instalação

**Não há instalação.** Baixe o `.exe` e execute — um arquivo só, sem pasta de
dependências e sem instalador.

- **Zero pré-requisitos**: o runtime .NET e o Visual C++ Redistributable viajam
  dentro do executável.
- Não requer administrador nem regra de firewall (o app apenas **envia** UDP,
  liberado por padrão no Windows).
- Rode de onde quiser: Desktop, Downloads, pendrive.
- Detecta o MSFS sozinho, em tempo de execução. Se você mover o `.exe` de lugar,
  o "Iniciar junto com o MSFS" se reajusta na próxima abertura.

<details>
<summary>O que o app grava fora do .exe</summary>

| Caminho | Conteúdo |
|---|---|
| `%APPDATA%\2G GPS Cliente\settings.json` | Suas configurações |
| `%LOCALAPPDATA%\2G GPS Cliente\runtime\<versão>\` | DLLs do SimConnect e do runtime C++, extraídas na 1ª execução |
| `%LOCALAPPDATA%\2G GPS Cliente\erro.log` | Só se ocorrer um erro inesperado |
| `EXE.xml` do MSFS | Só se "Iniciar junto com o MSFS" estiver marcado |

A primeira execução é um pouco mais lenta, porque o executável se descompacta;
as seguintes são normais.

</details>

> **Aviso SmartScreen**: builds sem assinatura digital exibem *"O Windows protegeu
> seu computador"* — clique em **Mais informações → Executar assim mesmo**. É o
> comportamento padrão do Windows para executáveis novos não assinados.

## Solução de problemas

| Sintoma | Causa provável |
|---|---|
| EFB não recebe posição | Tablet em outra rede/sub-rede Wi-Fi, ou o roteador/AP está com "isolamento de clientes" (AP/client isolation) ligado |
| Tablet em sub-rede diferente | Informe o IP do tablet em **Configurações → IPs adicionais** (unicast) |
| Posição congela no EFB | Simulador pausado ou no menu — normal; retoma sozinho no voo |
| Recebe em um EFB mas não em outro | Porta 49002 ocupada por outro conector — feche outras pontes de GPS |
| "Falha ao inicializar o SimConnect" | Consulte `%LOCALAPPDATA%\2G GPS Cliente\erro.log` e [abra uma issue](https://github.com/galenoferreira/gps_connector/issues) com a mensagem |

## Desenvolvimento

```
src/TwoG.GpsClient/        App WPF (.NET 10, x64) — UI, SimConnect, broadcaster
src/TwoG.GpsClient.Core/   Lógica pura do protocolo (multiplataforma, testável)
tests/                     Testes de unidade do protocolo
libs/                      DLLs do SimConnect (MSFS SDK) e do runtime VC++ x64
installer/setup.iss        Instalador Inno Setup (opcional)
```

```bash
dotnet test tests/TwoG.GpsClient.Core.Tests/TwoG.GpsClient.Core.Tests.csproj
```

```bash
dotnet publish src/TwoG.GpsClient/TwoG.GpsClient.csproj -c Release -o publish
```

Ambos rodam em qualquer sistema operacional — o csproj traz `EnableWindowsTargeting`
e RID fixo `win-x64`, então o publish gera o `.exe` até a partir de macOS/Linux.
O binário, porém, só **executa** no Windows: o SimConnect é x64/Windows.

### Publicar uma nova versão

```bash
git tag v1.1.0 && git push origin v1.1.0
```

Só isso. O CI roda os testes, publica o executável único, **valida que a saída tem
exatamente 1 arquivo**, compila o instalador, gera os checksums e cria o Release
com notas automáticas. A versão da tag vira a versão do binário.

Os arquivos do Release **não levam versão no nome** — é o que mantém os links de
download permanentes válidos. Tags com hífen (`v1.1.0-beta.1`) entram como
pré-release e não assumem o `latest`, preservando esses links.

### Como o .exe único funciona

O publish usa `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` +
`EnableCompressionInSingleFile`, definidos no csproj. As DLLs do SimConnect ficam
**fora** do bundle: o wrapper gerenciado é *mixed-mode* C++/CLI, e a
[documentação da Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
avisa que componentes managed C++ não são adequados a single-file — assemblies do
bundle são carregados da memória, o que não funciona para mixed-mode.

Em vez disso, elas viajam como **recursos embutidos** e o
[`SimConnectRuntime`](src/TwoG.GpsClient/Services/SimConnectRuntime.cs) as extrai
para `%LOCALAPPDATA%` na primeira execução, carregando-as de arquivos reais em
disco. Junto vão `MSVCP140.dll`, `VCRUNTIME140.dll` e `VCRUNTIME140_1.dll`, que a
`SimConnect.dll` importa: sem elas, uma máquina que nunca instalou o Visual C++
Redistributable falha com `erro 126` (`ERROR_MOD_NOT_FOUND`).

### Protocolo XGPS (referência)

```
XGPS<nome>,<lon>,<lat>,<alt m MSL>,<curso verdadeiro °>,<vel. solo m/s>
XATT<nome>,<proa verdadeira °>,<pitch °>,<roll °>,,,,,,,,,
```

Uma sentença por datagrama UDP, ASCII, separador decimal ponto, sem terminador de
linha. Pitch positivo = nariz para cima; roll positivo = asa direita — convenção
do X-Plane, e os sinais do MSFS são invertidos pelo conector. Os 9 campos vazios
finais do `XATT` completam os 13 campos que alguns EFBs esperam; o ForeFlight
ignora os extras.

> A especificação da ForeFlight recomenda posição a 1 Hz e atitude a 4–10 Hz.
> O padrão deste produto é 5 Hz para ambas, ajustável na interface.

---

© 2026 2G. Todos os direitos reservados.
