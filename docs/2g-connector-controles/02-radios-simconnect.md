# 02 — Rádios pelo SimConnect (MSFS 2020/2024 e Prepar3D)

Data: 2026-09-12
Status: design aprovado em conversa; aguardando revisão do documento escrito
Visão geral: [00-visao-geral.md](00-visao-geral.md) · Canal: [01-canal-de-controle.md](01-canal-de-controle.md)

## Objetivo

Definir os controles de rádio da primeira entrega — nomes, valores, faixas e validação —
e como cada um vira comando no simulador e volta como estado, pelo SimConnect. O mesmo
código atende MSFS 2020, MSFS 2024 e Prepar3D; onde eles divergem, está marcado.

Este documento não depende de módulo instalado no simulador. O que só funciona com
módulo está em "Limitação conhecida".

## Catálogo

Todos os valores são inteiros, como exige o canal.

| Controle | Tipo | Unidade | Faixa válida | Regra de espaçamento |
|---|---|---|---|---|
| `com1.active`, `com1.standby` | set | Hz | 118.000.000 – 136.990.000 | kHz com resto 0, 5, 10 ou 15 na divisão por 25 |
| `com1.swap` | action | — | — | — |
| `com2.active`, `com2.standby` | set | Hz | idem COM1 | idem COM1 |
| `com2.swap` | action | — | — | — |
| `nav1.active`, `nav1.standby` | set | Hz | 108.000.000 – 117.950.000 | múltiplo de 50.000 |
| `nav1.swap` | action | — | — | — |
| `nav2.active`, `nav2.standby` | set | Hz | idem NAV1 | idem NAV1 |
| `nav2.swap` | action | — | — | — |
| `adf1.active` | set | Hz | 190.000 – 1.799.500 | múltiplo de 500 |
| `xpdr.code` | set | código | 0 – 7777 | cada dígito decimal de 0 a 7 |
| `xpdr.mode` | set | enumeração | 0, 1, 3, 4 | — |
| `altimeter.baro` | set | Pa | 94.800 – 105.000 | — |

### Detalhes de cada regra

**COM — 25 kHz e 8,33 kHz.** O valor é o **canal como aparece no rádio**: o canal
118.005 é `118005000`. A regra "resto 0, 5, 10 ou 15" aceita exatamente os canais que
existem: `.000 .005 .010 .015 .025 .030 .035 .040 .050 …`. `.020`, `.045`, `.070` e
`.095` não existem na grade de 8,33 kHz e são recusados. Se o rádio da aeronave estiver
em 25 kHz, o simulador arredonda um canal de 8,33, e o `state` mostra o que ficou.

**ADF.** Passo de 500 Hz cobre os rádios que sintonizam meio quilohertz. Standby e troca
do ADF ficam fora da primeira entrega: o Prepar3D não modela ADF standby, e o uso de ADF
é pequeno.

**Transponder — código.** O valor é o código escrito como número decimal: 7700 é
`7700`, 1200 é `1200`. Cada dígito precisa estar entre 0 e 7 (é octal na prática):
`7800` é recusado.

**Transponder — modo.** Segue a enumeração do MSFS: `0` desligado, `1` standby, `3`
ligado (modo A), `4` ALT (modo C). O `2` (teste) existe no MSFS e fica de fora de
propósito: não há motivo para o app mandá-lo. **Só no MSFS** — o Prepar3D não modela o
modo do transponder, e nele o controle não aparece em `controls`.

**Altímetro.** Pa inteiro: 1013,25 hPa é `101325`; 29,92 inHg é `101321`. A faixa
corresponde a 948–1050 hPa, ou 28,00–31,00 inHg. O Connector só fala Pa; a unidade que o
piloto vê é escolha do app.

## Mapeamento para o SimConnect

### Comandos

Os eventos são mapeados uma vez, no `OnRecvOpen` (`MapClientEventToSimEvent`), e
enviados com `TransmitClientEvent` ao objeto do usuário
(`SIMCONNECT_OBJECT_ID_USER`), sempre da thread do SimConnect.

| Controle | Evento | Parâmetro |
|---|---|---|
| `com1.active` | `COM_RADIO_SET_HZ` | Hz |
| `com1.standby` | `COM_STBY_RADIO_SET_HZ` | Hz |
| `com1.swap` | `COM_STBY_RADIO_SWAP` | — |
| `com2.active` | `COM2_RADIO_SET_HZ` | Hz |
| `com2.standby` | `COM2_STBY_RADIO_SET_HZ` | Hz |
| `com2.swap` | `COM2_RADIO_SWAP` | — |
| `nav1.active` | `NAV1_RADIO_SET_HZ` | Hz |
| `nav1.standby` | `NAV1_STBY_SET_HZ` | Hz |
| `nav1.swap` | `NAV1_RADIO_SWAP` | — |
| `nav2.active` | `NAV2_RADIO_SET_HZ` | Hz |
| `nav2.standby` | `NAV2_STBY_SET_HZ` | Hz |
| `nav2.swap` | `NAV2_RADIO_SWAP` | — |
| `adf1.active` | `ADF_COMPLETE_SET` | BCD32 do ADF: `350000` Hz → `0x03500000` (ver V4) |
| `xpdr.code` | `XPNDR_SET` | BCD16: `7700` → `0x7700` |
| `xpdr.mode` | escrita na SimVar `TRANSPONDER STATE:1` (`SetDataOnSimObject`) | enumeração |
| `altimeter.baro` | `KOHLSMAN_SET` | milibares × 16: `101325` Pa → `16212` |

Conversões, todas no Core e testadas:

- **Hz → parâmetro:** direto; o maior valor (136.990.000) cabe no `uint` do evento.
- **Código → BCD16:** cada dígito decimal vira um nibble: `7700` → `0x7700`, `1200` → `0x1200`.
- **Hz → BCD32 do ADF:** unidade Frequency ADF BCD32 do FSX/P3D/MSFS: quatro dígitos de
  kHz, o décimo e três nibbles zero, ou seja, BCD de Hz × 10: `350000` → `0x03500000`,
  `1799500` → `0x17995000` (1.234,5 kHz é `0x12345000`, exemplo de Pete Dowson no
  FSDeveloper). A leitura volta em Hz por `ADF ACTIVE FREQUENCY:1`, sem BCD.
- **Pa → milibares × 16:** `round(Pa × 16 / 100)`.

### Estado

O estado vem de definições de dados próprias, pedidas com
`SIMCONNECT_DATA_REQUEST_FLAG.CHANGED`: o simulador só manda quando algum valor da
definição muda. Período `VISUAL_FRAME`, para o estado continuar chegando com o simulador
pausado — o piloto sintoniza rádio na pausa (ver V6).

**Uma definição por grupo de rádio**, e não uma só para tudo. Pedir uma SimVar que o
simulador não conhece gera exceção no SimConnect; com grupos separados, a falha de um
(o modo do transponder no P3D, por exemplo) só marca aquele grupo como indisponível, sem
derrubar os outros.

| Grupo | SimVars | Unidade pedida | Conversão para o `state` |
|---|---|---|---|
| COM1 | `COM AVAILABLE:1`, `COM ACTIVE FREQUENCY:1`, `COM STANDBY FREQUENCY:1` | Bool, Hz, Hz | Hz arredondado para inteiro |
| COM2 | idem, índice 2 | idem | idem |
| NAV1, NAV2 | `NAV AVAILABLE:n`, `NAV ACTIVE FREQUENCY:n`, `NAV STANDBY FREQUENCY:n` | Bool, Hz, Hz | idem |
| ADF1 | `ADF AVAILABLE:1`, `ADF ACTIVE FREQUENCY:1` | Bool, Hz | idem |
| Transponder | `TRANSPONDER AVAILABLE:1`, `TRANSPONDER CODE:1` | Bool, BCO16 | BCO16 → código decimal (`0x7700` → `7700`) |
| Modo do transponder | `TRANSPONDER STATE:1` | Enum | direto |
| Altímetro | `KOHLSMAN SETTING MB:1` | milibares | `round(mb × 100)` Pa |

### Lista de controles por aeronave

`controls` é montada a partir dos grupos:

- grupo com `AVAILABLE` verdadeiro → entram os controles dele (`com2.active`,
  `com2.standby`, `com2.swap` para o COM2);
- grupo que falhou na definição, ou com `AVAILABLE` falso → nenhum controle dele;
- altímetro → sempre presente quando o grupo responde (toda aeronave tem altímetro).

A lista é refeita:

- ao conectar ao simulador;
- no evento de sistema `AircraftLoaded`, quando o piloto troca de aeronave;
- sempre que um `AVAILABLE` muda.

Cada mudança gera uma mensagem `controls` para os aparelhos conectados (ver 01).

### Diferenças entre simuladores

| Tema | MSFS 2020 / 2024 | Prepar3D v4+ |
|---|---|---|
| Eventos `*_SET_HZ` | Existem | A confirmar (V1); plano B abaixo |
| Canais de 8,33 kHz | Suportados | Só se V1 confirmar os `*_SET_HZ` |
| Modo do transponder | `TRANSPONDER STATE:1` | Não modelado: `xpdr.mode` fora de `controls` |
| Nome no `state` | Vem do `SimulatorIdentity`, como hoje | Idem |

**Plano B do Prepar3D, se V1 falhar:** usar a família BCD16 herdada do FSX
(`COM_RADIO_SET`, `COM_STBY_RADIO_SET`, `NAV1_RADIO_SET`…), que só representa frequência
com 4 dígitos e grade de 25 kHz (COM) e 50 kHz (NAV). Nesse caso, no P3D, um canal de 8,33
kHz volta `unsupported` — não `out_of_range`, porque o valor é válido, só não é
representável ali.

## Itens de verificação (roteiro no Windows)

Cada item é conferido pelo `state` que chega ao app, não pela tela do simulador.

| # | O que verificar | Esperado | Se falhar |
|---|---|---|---|
| V1 | `COM_STBY_RADIO_SET_HZ` com `118500000` no **P3D** | `state` mostra 118500000 | Plano B do Prepar3D, acima |
| V2 | Canal de 8,33 (`118005000`) no MSFS 2020 e 2024, rádio em modo 8,33 | `state` mostra 118005000 | Se o `state` vier em frequência real (118000000), converter canal ↔ frequência no Core e documentar no 01 |
| V3 | Mesmo canal com o rádio em modo 25 kHz | Simulador arredonda; `state` mostra o valor arredondado; app exibe o que veio | Nenhuma ação: comportamento esperado |
| V4 | `ADF_COMPLETE_SET` com 350 kHz e 1.799,5 kHz no MSFS 2024 e no P3D | Enviando `0x03500000` e `0x17995000` (BCD de Hz × 10), `state` mostra 350000 e 1799500 | Testar o formato BCD alternativo (BCD direto dos dígitos de Hz, `0x00350000`); no MSFS, alternativa de escrever `ADF ACTIVE FREQUENCY:1` |
| V5 | `KOHLSMAN_SET` com `16212` no MSFS 2024 | Altímetro 1 em 1013 hPa; `state` com ≈101325 | Se ajustar outro altímetro ou nenhum, usar `TransmitClientEvent_EX1` com o índice do altímetro |
| V6 | Mudar COM1 com o simulador pausado | `state` chega na pausa | Se não chegar, pedir o estado também com período `SECOND` |
| V7 | Escrever `TRANSPONDER STATE:1` = 4 no MSFS 2020 e 2024 | `state` com `mode: 4` | Procurar o evento equivalente no SDK; se não houver, `xpdr.mode` sai da primeira entrega |
| V8 | Trocar de aeronave com o app conectado (C172 → aeronave sem COM2) | Mensagem `controls` sem `com2.*` | Reforçar a detecção com a SimVar `TITLE` |
| V9 | `XPNDR_SET` com `7700` e `1200` | `state` com `code: 7700` e `1200` | Conferir a conversão BCD16 |
| V10 | Os 16 controles numa aeronave padrão (C172 G1000) no MSFS 2020, MSFS 2024 e P3D | Cada `state` confirma o comando | Registrar por controle e por simulador |

Os resultados viram uma tabela neste documento, e o que falhar vira ajuste antes da
entrega.

## Limitação conhecida

Aeronaves de terceiros com sistemas próprios — Fenix A320, a linha PMDG, e os rádios de
muitas aeronaves do MSFS 2024 — não respondem aos eventos `K:` do SimConnect. Nelas o
comando sai com `result ok` e o `state` não muda. O canal já prevê isso (o app avisa o
piloto quando o `state` não confirma o comando em 2 s — ver 01). A solução é o módulo
WASM (spec 04), que fala a língua de cada aeronave por L:vars, H:events e B:events.

Na tabela da V10 vale registrar também uma ou duas aeronaves de terceiros populares, só
para documentar o comportamento; resolver fica para o 04.

## Testes automatizados (Core)

- Validação de cada controle: limites inclusivos, um passo abaixo e um acima; toda a
  grade de 8,33 kHz de 118.000 a 118.100; dígito 8 ou 9 no transponder; modos 2 e 5
  recusados.
- Conversões: Hz → parâmetro, código ↔ BCD16/BCO16, Pa ↔ milibares × 16, com os
  exemplos deste documento como casos de teste.
- Montagem da lista `controls` a partir de grupos disponíveis, indisponíveis e com falha.

A tradução para chamadas SimConnect (mapeamento de eventos, definições de dados, fila na
thread do SimConnect) depende do Windows e é coberta pelo roteiro acima.
