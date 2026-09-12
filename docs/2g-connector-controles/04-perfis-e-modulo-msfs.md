# 04 — Perfis de aeronave e módulo para o MSFS

Data: 2026-09-12
Status: design aprovado em conversa; aguardando revisão do documento escrito
Visão geral: [00-visao-geral.md](00-visao-geral.md) · Canal: [01-canal-de-controle.md](01-canal-de-controle.md) · Catálogo: [02-radios-simconnect.md](02-radios-simconnect.md)

## Objetivo

Fazer os rádios do 02 funcionarem em aeronaves que ignoram os eventos padrão do
SimConnect. Isso inclui a frota nativa do MSFS 2024 e as aeronaves complexas de
terceiros. **O canal (01) e o catálogo (02) não mudam**: o app continua mandando
`com1.standby = 118500000`. Muda só como o Connector transforma isso em algo que a
aeronave entende.

### Escopo

Quatro perfis de rádio, nesta ordem de entrega:

| Etapa | Perfis | Mecanismos que estreia |
|---|---|---|
| 4a | Frota padrão do MSFS 2024, FlyByWire A32NX | `lvar`, `bevent` |
| 4b | PMDG 737 | `pmdg` |
| 4c | Fenix A320 | `wasm_hevent`, `wasm_rpn`, `knob` e o módulo WASM |

A etapa 4a não instala nada no simulador. A 4c é a primeira que exige o módulo.

### Fora do escopo

- Piloto automático, luzes e demais controles gerais. O formato dos perfis os comporta,
  mas o catálogo deles é outro documento.
- Perfis escritos pelo usuário, numa pasta local. Um perfil manda comandos ao
  simulador; só perfis assinados são carregados (ver "Segurança").
- Perfis para o X-Plane. O formato prevê o campo `simulators`, mas os mecanismos do
  X-Plane (`dref`, `cmnd`) ficam para quando houver demanda (ver 03).

## O invariante de segurança

> **O app nunca envia nome de variável, nome de evento nem código.** Ele envia só um ID
> do catálogo e um inteiro. Quem traduz para L:var, B:event, H:event ou RPN é o
> Connector, **a partir de um perfil assinado ou embutido no exe**.

Todo o resto deste documento se apoia nisso. Sem essa regra, o canal de controle viraria
um jeito de executar código dentro do simulador a partir de qualquer aparelho pareado.

## Arquitetura

```
 canal (01) ──▶ ControlCommand ──▶ ProfileExecutor ──▶ mecanismo ──▶ simulador
                                        ▲                  │
                      AircraftMatcher ──┤                  ├─ kevent   (02, padrão)
                      (AircraftLoaded)  │                  ├─ lvar     (SimConnect)
                                        │                  ├─ bevent   (SimConnect InputEvents)
                      ProfileProvider ──┘                  ├─ pmdg     (SDK da PMDG)
                      (pacote assinado                     ├─ wasm_*   (módulo WASM)
                       ou embutido)                        └─ knob     (sequência em malha fechada)
```

| Unidade | Onde | Responsabilidade |
|---|---|---|
| `AircraftProfile`, `ProfileSchema` | Core | Ler e validar um perfil; controle com mecanismo ou transformação desconhecidos fica indisponível, sem invalidar o perfil |
| `ProfilePackage` | Core | Pacote `profiles.json`: assinatura, `revision`, `schema`, `minConnector` |
| `ProfileProvider` | Core | Escolher entre pacote baixado e embutido; baixar e verificar a cada 6 h |
| `AircraftMatcher` | Core | Achar o perfil pelo caminho da aeronave |
| `ValueTransforms` | Core | As transformações permitidas, fechadas em código |
| `KnobPlanner`, `KnobSequencer` | Core | Calcular cliques e executá-los conferindo o estado |
| `ProfileExecutor` | App | Aplicar o perfil ativo: montar `controls`, despachar comandos, montar `state` |
| Mecanismos `lvar`, `bevent`, `pmdg`, `wasm` | App | Falar SimConnect; tudo na thread do SimConnect, pela fila do 01 |

## Identificação da aeronave

Pelo caminho que o evento de sistema `AircraftLoaded` entrega (o arquivo `.air` ou
`.cfg` da aeronave). Não pelo `TITLE`, que muda de pintura para pintura. Cada perfil lista
trechos de caminho, comparados sem diferenciar maiúsculas:

```json
"match": ["fnx-aircraft-320"]
```

O primeiro perfil cujo `match` casar vale. Nenhum casou: comportamento do 02, só com
eventos K:. O caminho entregue pelo MSFS 2024 precisa ser conferido (M3).

## Formato do perfil

Os nomes de variáveis abaixo são **ilustrativos**. Os reais são levantados e validados no
simulador durante a criação de cada perfil (ver "Critério para publicar um perfil").

```json
{
  "id": "fbw-a32nx",
  "name": "FlyByWire A32NX",
  "simulators": ["msfs2020", "msfs2024"],
  "match": ["flybywire-aircraft-a320-neo"],
  "requires": [],
  "fallback": "none",
  "controls": {
    "com1.standby": {
      "write": { "mechanism": "lvar", "target": "L:EXEMPLO_RMP1_STBY", "transform": "hz_to_khz" },
      "read":  { "source": "lvar", "target": "L:EXEMPLO_RMP1_STBY", "transform": "khz_to_hz" }
    },
    "com1.swap": {
      "write": { "mechanism": "bevent", "target": "EXEMPLO_RMP1_SWAP", "value": 1 }
    },
    "altimeter.baro": {
      "write": { "mechanism": "kevent", "target": "KOHLSMAN_SET", "transform": "pa_to_mb16" },
      "read":  { "source": "avar", "target": "KOHLSMAN SETTING MB:1", "unit": "millibars", "transform": "mb_to_pa" }
    }
  }
}
```

### Campos

| Campo | Significado |
|---|---|
| `id`, `name` | Identificação; o `name` aparece na UI do Connector |
| `simulators` | `msfs2020`, `msfs2024`, `p3d`; perfil de outro simulador é ignorado |
| `match` | Trechos do caminho da aeronave (ver acima) |
| `requires` | Pré-requisitos: `wasm_module` (módulo instalado e compatível) e/ou `pmdg_sdk` (transmissão de dados da PMDG ligada) |
| `fallback` | `none`: controles fora do perfil somem de `controls`. `default`: controles fora do perfil usam o 02 |
| `controls` | Um objeto por controle do catálogo, com `write` e, quando houver, `read` |

`fallback: none` é o padrão para airliners de terceiros: um controle que o perfil não
cobre não deve ser oferecido ao piloto, porque o evento padrão não teria efeito.
`fallback: default` serve para a frota do MSFS 2024, em que o perfil só corrige o que os
eventos K: não alcançam.

### Mecanismos de escrita

| `mechanism` | Campos | O que faz |
|---|---|---|
| `kevent` | `target` (nome do evento), `transform` | `TransmitClientEvent`, como no 02 |
| `lvar` | `target` (`L:NOME`), `transform` | Escreve a L:var pelo SimConnect (`AddToDataDefinition` + `SetDataOnSimObject`) |
| `bevent` | `target` (nome do InputEvent), `transform` ou `value` | `SetInputEvent`, com o hash obtido de `EnumerateInputEvents` ao carregar a aeronave |
| `pmdg` | `target` (ID do evento no SDK da PMDG), `value` | `TransmitClientEvent` com o ID de evento de terceiros definido no SDK |
| `wasm_hevent` | `target` (nome do H:event) | Pede ao módulo para acionar o H:event |
| `wasm_rpn` | `code` (modelo RPN com `{value}`), `transform` | Pede ao módulo para executar o RPN, com `{value}` substituído pelo inteiro já transformado |
| `knob` | `outer`, `inner` (cada um uma escrita de outro mecanismo, com `inc` e `dec`), `outerStep`, `innerStep` | Sequência de cliques em malha fechada (seção própria abaixo) |

No `wasm_rpn`, a substituição de `{value}` só aceita o número já validado e
transformado. Não há interpolação de texto vindo do app, e o modelo RPN só vem de perfil
assinado.

### Fontes de estado

| `source` | Campos | O que lê |
|---|---|---|
| `avar` | `target` (SimVar), `unit`, `transform` | A SimVar padrão, como no 02 |
| `lvar` | `target`, `transform` | L:var pelo SimConnect, com `CHANGED` |
| `pmdg` | `field` (campo da estrutura de dados da PMDG), `transform` | Client data area da PMDG |
| `wasm_rpn` | `code`, `transform`, `hz` | O módulo avalia o RPN periodicamente e devolve o número |

### Transformações

Um conjunto **fechado, implementado em código**, sem expressão livre no perfil: um perfil
não pode introduzir cálculo que o Connector não conheça.

`identity`, `hz_to_khz`, `khz_to_hz`, `hz_to_mhz_x100` / `mhz_x100_to_hz` (unidades de 10
kHz), `hz_to_bcd16` / `bcd16_to_hz`, `code_to_bcd16` / `bco16_to_code`, `pa_to_mb16`,
`mb_to_pa`, `pa_to_inhg_x100` / `inhg_x100_to_pa`, e `enum` (com uma tabela `map` no próprio
controle, para modos de transponder).

Transformação desconhecida: o controle fica indisponível e o motivo vai para o
Diagnóstico.

## Os mecanismos em detalhe

### `lvar` — L:vars pelo SimConnect

A documentação do SimConnect diz que o `AddToDataDefinition` aceita L:vars, sempre como
`FLOAT64`, para leitura e escrita. As L:vars são globais: se outro cliente SimConnect
escrever a mesma variável, vale a última escrita. **Precisa confirmar** que a DLL do
SimConnect que o Connector vendoriza (SDK 0.24.3) já tem esse suporte (M1). Se não tiver, o
`lvar` passa pelo módulo WASM (`(L:NOME)` e `v (>L:NOME)` em RPN), e perfis `lvar` passam a
exigir `wasm_module`.

### `bevent` — InputEvents

Ao carregar a aeronave, o Connector chama `EnumerateInputEvents` e guarda a tabela nome →
hash. O comando vira `SetInputEvent(hash, valor)`. É o caminho dos rádios da frota nativa
do MSFS 2024, que respondem a B:events e ignoram parte dos eventos K:. Disponibilidade
na DLL vendorizada e no MSFS 2020: M2.

### `pmdg` — SDK da PMDG

O PMDG 737 publica o estado do cockpit numa client data area do SimConnect e aceita
eventos de controle com IDs próprios, a partir da faixa de eventos de terceiros, conforme
o cabeçalho do SDK distribuído com a aeronave. Dois pré-requisitos:

- **O piloto liga a transmissão de dados** no arquivo de opções da aeronave. O Connector
  detecta a ausência de dados e, em vez de oferecer controles que não funcionariam,
  mostra na UI o que precisa ser ligado (P1).
- **Termos de uso do SDK** conferidos antes de publicar o perfil (L1).

### `wasm_*` — o módulo

Detalhado na seção "Módulo WASM". H:events e RPN só existem dentro do simulador; nenhum
cliente externo os alcança sem um módulo.

### `knob` — sequência em malha fechada

Muitas aeronaves complexas não aceitam "COM1 = 118.500". Só aceitam girar o seletor, como
o RMP do Fenix e boa parte dos rádios de airliner. O canal continua absoluto; o
incremento fica escondido no Connector:

1. **Ler** o valor atual pela fonte de estado do perfil.
2. **Planejar** (`KnobPlanner`): diferença em passos do seletor externo (`outerStep`,
   normalmente 1 MHz) e do interno (`innerStep`, 25 ou 5 kHz), escolhendo o sentido mais
   curto quando o seletor dá a volta.
3. **Executar** (`KnobSequencer`): mandar os cliques a no máximo **20 por segundo**,
   conferindo o estado a cada **5 cliques**.
4. **Parar** se o estado divergir do esperado, se passar de **60 cliques** ou de **3 s**. O
   `result` volta `not_applied`.
5. **Concluir** quando o estado bate com o valor pedido: `result ok`.

Nos controles `knob`, o `result` só chega depois da sequência, e não na entrega (ver a
nota no 01). Dois comandos `knob` para o mesmo controle não correm juntos: o segundo
espera o primeiro.

## Módulo WASM

### Pacote

Um pacote na pasta Community do MSFS:

```
2g-connector-module/
  manifest.json
  layout.json
  modules/2GConnector.wasm
```

Compilado com o SDK do MSFS 2020, que também roda no MSFS 2024. Não altera nenhuma
aeronave; só fica disponível para o Connector.

### Comunicação

O módulo é um cliente SimConnect de dentro do simulador e conversa com o Connector por
três **client data areas**:

| Área | Sentido | Estrutura |
|---|---|---|
| `2GConnector.Command` | Connector → módulo | `uint32 seq` + texto ASCII de até 252 bytes |
| `2GConnector.Response` | módulo → Connector | `uint32 seq` + `int32 status` + `double value` + texto de até 240 bytes |
| `2GConnector.Watch` | módulo → Connector | até 32 pares `uint32 id` + `double value`, das leituras registradas com `watch` |

As duas primeiras têm 256 bytes cada.

Comandos:

| Comando | Resposta |
|---|---|
| `ping` | `status 0`, versão do módulo no texto |
| `hevent <nome>` | `status 0` se acionado |
| `rpn <código>` | `status 0` e o resultado numérico em `value` |
| `watch <id> <hz> <código>` | `status 0`; a partir daí o valor aparece em `2GConnector.Watch` |
| `unwatch <id>` | `status 0` |

Um comando por vez, casado pelo `seq`. Sem resposta em 500 ms, o Connector considera o
comando perdido e o `result` volta `sim_unresponsive`. Para as leituras periódicas de
estado por RPN, o Connector registra os códigos uma vez (`watch <id> <hz> <código>`) e o
módulo publica os valores na área `2GConnector.Watch`, só quando mudam. Assim não se
paga um vaivém por leitura.

### Detecção e compatibilidade

Ao conectar ao simulador, o Connector manda `ping`:

- sem resposta → módulo ausente; perfis com `requires: ["wasm_module"]` não valem, e a UI
  explica o que instalar;
- versão incompatível → idem, e a UI diz que o módulo precisa ser atualizado;
- versão compatível → os mecanismos `wasm_*` ficam disponíveis.

A compatibilidade é por versão maior do protocolo do módulo, anunciada no `ping`.

### Instalação e atualização

- **No instalador:** uma tarefa opcional, "Instalar módulo 2G Connector no MSFS". Ela
  acha a pasta Community do MSFS 2020 e do 2024 (Store e Steam) pelo `InstalledPackagesPath`
  do `UserCfg.opt`, nos mesmos caminhos que o instalador já usa para detectar os
  simuladores.
- **Atualização junto com o Connector:** o exe traz o módulo embutido. Na partida, se o
  módulo estiver instalado e a versão for diferente da embutida, o Connector o atualiza —
  **só com o simulador fechado**, porque o MSFS mantém o `.wasm` carregado aberto. Assim o
  auto-update da v1.4 também atualiza o módulo, sem mecanismo novo.
- **No desinstalador:** o módulo é removido das pastas Community.
- **Sem instalador (exe avulso):** o botão "Instalar módulo" no card de controle faz o
  mesmo que a tarefa do instalador.

### Compilação

O módulo é C++ compilado para WebAssembly com o toolchain do SDK do MSFS. Antes de qualquer
código, é preciso confirmar se o SDK pode ser instalado no runner do GitHub Actions (B1).
Se não puder, o `.wasm` é compilado na máquina do mantenedor e versionado no repositório,
com o hash registrado; o CI então só confere que o arquivo embutido bate com o registrado.

## Distribuição dos perfis

### Onde ficam

Num **repositório separado**, `galenoferreira/2g-connector-profiles`, publicado pelos
releases dele. O motivo: um release de perfis no repositório do app viraria o `latest`,
e os links permanentes do site e o feed do updater passariam a apontar para ele. No
repositório próprio, o `releases/latest/download/profiles.json` é independente do app.

### O pacote

Um arquivo `profiles.json` com todos os perfis, e ao lado o `profiles.json.sig`:

```json
{
  "schema": 1,
  "revision": 17,
  "minConnector": "1.6.0",
  "profiles": [ { "id": "fbw-a32nx", "…": "…" } ]
}
```

- **Assinado com a mesma chave ECDSA P-256 do updater**, pela mesma ferramenta de
  release (que ganha um modo genérico de assinar arquivo). O repositório de perfis
  recebe o mesmo secret. A confiança está na assinatura, não na hospedagem.
- **`revision` só cresce.** O Connector recusa revisão igual ou menor que a que já tem —
  a mesma defesa contra rollback do updater.
- **`schema`** é a versão do formato. Schema maior que o conhecido: pacote ignorado,
  embutido vale.
- **`minConnector`**: pacote que exige Connector mais novo é ignorado, e a UI sugere
  atualizar.
- Limite de 1 MB.

### Ciclo de vida

- **Verificação:** na partida e a cada 6 h, como o updater.
- **Reserva offline:** o exe traz uma revisão embutida. Vale a de maior `revision` entre
  a baixada e verificada e a embutida.
- **Troca:** um pacote novo vale a partir da próxima aeronave carregada. O perfil ativo
  não muda no meio de um voo.
- **Local:** `%LOCALAPPDATA%\2G Connector\profiles\`, com o pacote e a assinatura.

### Critério para publicar um perfil

Nenhum perfil entra no pacote sem uma checklist preenchida no próprio simulador: cada
controle do perfil comandado pelo app, e o `state` confirmando o valor. A checklist fica no
PR do repositório de perfis, com a versão da aeronave testada. Quando uma atualização da
aeronave quebra o perfil, a correção sai como revisão nova do pacote, sem release do
Connector.

## Interface no Connector

- **No card "Controle pelo 2G Pilot":** a aeronave atual e o perfil ativo ("Fenix A320 —
  perfis r17"), o estado do módulo (instalado, ausente ou desatualizado, com botão
  "Instalar módulo") e os controles que o perfil não cobre.
- **Configurações:** "Usar perfis de aeronave", ligado por padrão. Desligado, tudo volta ao
  comportamento do 02, só com eventos K:.
- **Diagnóstico:** revisão do pacote de perfis, última verificação, controles
  indisponíveis e por quê (mecanismo desconhecido, pré-requisito faltando), último erro do
  módulo.

## Mudança no canal (01)

Uma só, prevista pela regra do 01 de tratar códigos novos de forma genérica:

- **Novo código de erro `not_applied`** no `result`: a aeronave não seguiu o comando (o
  `knob` divergiu, estourou o limite de cliques ou o tempo).
- **Nos controles `knob`, o `result` chega depois da sequência** (até 3 s), e não na
  entrega.

O 01 já traz as duas regras.

## Segurança

- O invariante do topo: o app manda só ID e inteiro.
- Perfis só vêm de pacote assinado ou do exe. Não há pasta de perfis do usuário.
- Transformações e mecanismos são fechados em código; o perfil só escolhe entre eles.
- O RPN do `wasm_rpn` só vem de perfil assinado, e `{value}` recebe só um inteiro validado.
- O módulo só aceita comandos pela client data area, ou seja, de clientes SimConnect da
  mesma máquina — a mesma fronteira de confiança do próprio SimConnect.
- A chave de assinatura é a mesma do updater; as regras de guarda dela valem igual.

## Itens de verificação

| # | O que verificar | Esperado | Se falhar |
|---|---|---|---|
| M1 | L:var por `AddToDataDefinition` com a DLL vendorizada (SDK 0.24.3), no MSFS 2020 e 2024 | Leitura e escrita de uma L:var do FlyByWire funcionam | `lvar` passa pelo módulo WASM; perfis `lvar` passam a exigir `wasm_module` |
| M2 | `EnumerateInputEvents` e `SetInputEvent` com a DLL vendorizada, no MSFS 2020 e 2024 | Tabela de InputEvents da aeronave e troca de COM1 por B:event | Atualizar a DLL do SimConnect vendorizada para um SDK mais novo |
| M3 | Caminho entregue pelo `AircraftLoaded` no MSFS 2024 | Caminho que identifica o pacote da aeronave | Usar outra fonte de identificação (ex.: `ATC MODEL` + `TITLE`) |
| M4 | Módulo WASM compilado com o SDK de 2020 carregando no MSFS 2024 | `ping` responde nos dois | Dois builds, um por simulador |
| M5 | `knob` no RMP do Fenix: de 118.000 para 136.975 | Sequência conclui em menos de 3 s e o `state` confirma | Ajustar ritmo, limite ou tamanho do lote |
| P1 | Detecção da transmissão de dados da PMDG desligada | UI mostra o que ligar; nenhum controle `pmdg` oferecido | — |
| L1 | Termos de uso do SDK da PMDG | Permite uso em ferramenta gratuita de terceiros | Sem perfil PMDG até haver permissão |
| B1 | Instalação do SDK do MSFS no runner do GitHub Actions | Módulo compila no CI | Compilar na máquina do mantenedor e versionar o `.wasm` com hash |

## Testes automatizados (Core)

- **Perfil:** leitura e validação; mecanismo, transformação ou campo desconhecido tornam
  indisponível só o controle afetado; `fallback` nos dois modos.
- **`AircraftMatcher`:** casamento por trecho sem diferenciar maiúsculas, primeiro perfil
  vence, nenhum casamento cai no 02.
- **Transformações:** cada uma nos dois sentidos, com valores de borda.
- **`KnobPlanner`:** menor caminho, volta do seletor, passos de 25 e de 5 kHz, limite de 60
  cliques.
- **`KnobSequencer`**, contra uma aeronave falsa: conclusão, divergência no meio,
  estouro de tempo, aeronave que ignora cliques, dois comandos seguidos no mesmo controle.
- **Pacote:** assinatura válida e inválida, `revision` menor recusada, `schema` e
  `minConnector` desconhecidos, escolha entre baixado e embutido.
- **Módulo, do lado do Connector:** montagem e leitura das estruturas das client data
  areas, casamento por `seq`, tempo esgotado.

O módulo em si e cada perfil são verificados no simulador, pelos itens acima e pela
checklist de publicação.
