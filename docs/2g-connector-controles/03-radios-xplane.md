# 03 — Rádios no X-Plane 11/12

Data: 2026-09-12
Status: design aprovado em conversa; aguardando revisão do documento escrito
Visão geral: [00-visao-geral.md](00-visao-geral.md) · Canal: [01-canal-de-controle.md](01-canal-de-controle.md) · Catálogo: [02-radios-simconnect.md](02-radios-simconnect.md)

## Objetivo

Levar ao X-Plane os mesmos 16 controles de rádio do 02, **sem mudar nada no app nem no
canal**. O catálogo — nomes, unidades, faixas e validação — é o do 02; este documento só
define como cada controle vira comando e estado no X-Plane.

### Pré-requisito

A leitura de posição do X-Plane (`XPlaneService`) ainda não foi validada num X-Plane
real. Esta etapa só começa depois disso: não faz sentido controlar rádios por um
caminho cuja base não foi provada.

### Fora do escopo

- Aeronaves de terceiros com rádios próprios (Zibo 737, ToLiss A319/A320/A321, entre
  outras), que usam datarefs e commands próprios. Elas podem ganhar perfis no formato
  do 04 depois; o formato já prevê o simulador de cada perfil.
- A Web API do X-Plane 12 (REST/WebSocket na porta 8086). Ela só escuta no próprio PC
  por padrão e não existe no X-Plane 11. O UDP funciona nos dois, inclusive com o X-Plane
  em outra máquina, como já acontece com a posição.

## Como o X-Plane recebe comandos

Pela mesma porta UDP de comandos que o `XPlaneService` já usa para assinar datarefs
(a anunciada no beacon `BECN`, 49000 por padrão). Dois pacotes, ambos parte do protocolo
UDP nativo do X-Plane:

**`DREF` — escrever um valor.** 509 bytes:

| Bytes | Conteúdo |
|---|---|
| 0–4 | `"DREF"` + byte nulo |
| 5–8 | valor, `float32` little-endian |
| 9–508 | caminho do dataref, ASCII, terminado e preenchido com nulos até 500 bytes |

**`CMND` — executar um command.** `"CMND"` + byte nulo, seguido do caminho do command
em ASCII, terminado em nulo.

Os dois são montados no Core (`XPlaneProtocol`), como o `RREF` que já existe, e têm
teste byte a byte.

## Mapeamento

| Controle | Escrita | Estado lido de | Conversão |
|---|---|---|---|
| `com1.active` | `DREF` `sim/cockpit2/radios/actuators/com1_frequency_hz_833` | o mesmo dataref | Hz ↔ kHz (ver XV1) |
| `com1.standby` | `DREF` `sim/cockpit2/radios/actuators/com1_standby_frequency_hz_833` | o mesmo | idem |
| `com1.swap` | `CMND` `sim/radios/com1_standy_flip` | — | — |
| `com2.*` | idem, com `com2_` | idem | idem |
| `nav1.active` | `DREF` `sim/cockpit2/radios/actuators/nav1_frequency_hz` | o mesmo | Hz ↔ unidades de 10 kHz (11030 = 110,30 MHz) |
| `nav1.standby` | `DREF` `sim/cockpit2/radios/actuators/nav1_standby_frequency_hz` | o mesmo | idem |
| `nav1.swap` | `CMND` `sim/radios/nav1_standy_flip` | — | — |
| `nav2.*` | idem, com `nav2_` | idem | idem |
| `adf1.active` | `DREF` `sim/cockpit2/radios/actuators/adf1_frequency_hz` | o mesmo | Hz ↔ kHz |
| `xpdr.code` | `DREF` `sim/cockpit2/radios/actuators/transponder_code` | o mesmo | direto (7700 é 7700) |
| `xpdr.mode` | `DREF` `sim/cockpit2/radios/actuators/transponder_mode` | o mesmo | enumeração do X-Plane ↔ a do 02 (ver XV4) |
| `altimeter.baro` | `DREF` `sim/cockpit2/gauges/actuators/barometer_setting_in_hg_pilot` | o mesmo | Pa ↔ inHg (1 inHg = 3386,389 Pa) |

O `standy` nos nomes dos commands é grafia do próprio X-Plane, não erro deste documento.

### Diferenças em relação ao SimConnect

- **ADF em meio quilohertz.** O dataref do ADF é inteiro em kHz. Um valor válido no 02,
  como 1.799.500 Hz, não é representável aqui e volta `unsupported` — não `out_of_range`,
  porque o valor em si é válido.
- **Disponibilidade por rádio.** O X-Plane não tem um equivalente ao `COM AVAILABLE` do
  SimConnect. Os 16 controles aparecem em `controls` para qualquer aeronave. Numa
  aeronave sem COM2, o comando é aceito e não tem efeito, e o app percebe pelo `state`
  (regra dos 2 s do 01). É uma limitação declarada.
- **Troca de aeronave.** Sem evento equivalente ao `AircraftLoaded`, a lista de controles
  não muda com a aeronave — coerente com o item anterior.

## Estado

O estado vem pelo `RREF`, o mesmo mecanismo da posição:

- **Assinatura separada da posição**, em faixa de índices própria (a partir de 100). A
  ordem do array `XPlaneFixAssembler.Datarefs` é contrato de fio da posição e não é
  tocada; os índices dos rádios vivem num segundo array, também imutável depois de
  publicado.
- **5 Hz.** Rádio muda por ação do piloto; a posição continua em 10 Hz.
- **O X-Plane manda os valores em ritmo fixo, não quando mudam.** O Connector compara
  cada lote com o último retrato e só emite `state` quando algo mudou — o mesmo
  comportamento que o SimConnect entrega com o flag `CHANGED`.
- O `RREF` transporta `float32`. Todos os valores dos rádios cabem exatamente num float
  (o maior, 136.990 kHz, fica muito abaixo de 2²⁴).
- As assinaturas dos rádios são canceladas junto com as da posição quando o X-Plane some
  (frequência 0, como o `XPlaneService` já faz).

## Itens de verificação (roteiro no X-Plane 11 e 12)

| # | O que verificar | Esperado | Se falhar |
|---|---|---|---|
| XV1 | Unidade de `com1_frequency_hz_833` | Canal em kHz: 121.900 MHz é `121900` | Ajustar a conversão no Core |
| XV2 | Canal de 8,33 kHz (118.005) no dataref `_833` | `state` volta com 118005000 | Se o X-Plane não aceitar canal de 8,33, esses valores viram `unsupported` no X-Plane |
| XV3 | `DREF` com `float` num dataref inteiro | O X-Plane converte e aplica | Procurar o dataref equivalente em float |
| XV4 | Enumeração de `transponder_mode` | Levantar a tabela do X-Plane e mapear para 0, 1, 3, 4 do 02 | Se não houver equivalente para um valor, ele fica `unsupported` no X-Plane |
| XV5 | Os quatro commands `_standy_flip` | Trocam ativa e standby | Procurar o command atual no X-Plane 12 (DataRefTool) |
| XV6 | Altímetro: 29,92 inHg e 1013 hPa | `state` com ≈101321 e ≈101300 Pa | Conferir a conversão |
| XV7 | Os 16 controles numa aeronave padrão (Cessna 172) no X-Plane 11 e 12 | Cada `state` confirma o comando | Registrar por controle e por versão |
| XV8 | X-Plane noutra máquina da rede | Comandos e estado funcionam como no mesmo PC | Conferir a porta de comandos do beacon |

Os resultados viram uma tabela neste documento antes da entrega.

## Testes automatizados (Core)

- `DREF` e `CMND` byte a byte: tamanho de 509 bytes, float little-endian, preenchimento
  com nulos, caminho no limite de 499 caracteres.
- Conversões de cada controle nos dois sentidos, com os exemplos deste documento.
- Detector de mudança: lote igual não gera `state`, lote com um valor diferente gera.
- Índices dos rádios não colidem com os da posição.

A troca de pacotes com o X-Plane de verdade é coberta pelo roteiro acima.
