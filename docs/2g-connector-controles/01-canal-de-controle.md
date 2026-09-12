# 01 — Canal de controle 2G Pilot ⇄ 2G Connector

Data: 2026-09-12
Status: design aprovado em conversa; aguardando revisão do documento escrito
Visão geral: [00-visao-geral.md](00-visao-geral.md) · Catálogo: [02-radios-simconnect.md](02-radios-simconnect.md)

## Objetivo

Um canal pelo qual o 2G Pilot envia comandos ao simulador e recebe de volta o estado
real do cockpit. O canal não sabe o que é um rádio: transporta controles identificados
por nome, com valores inteiros. O que cada controle significa, e como vira comando no
simulador, está no catálogo (02).

### Requisitos

1. O app encontra o Connector sozinho na rede local.
2. Só aparelhos pareados pelo piloto controlam o simulador.
3. O app sempre mostra o que o simulador reporta, não o que ele mandou.
4. Um comando repetido (reconexão, toque duplo) não muda o resultado.
5. Mais de um aparelho pode estar conectado ao mesmo tempo — dois iPads na mesma cabine.
6. Nenhuma falha do canal interfere na transmissão de posição (XGPS/XATT).
7. O protocolo aceita controles e mensagens novos sem quebrar apps antigos.

### Fora do escopo

- TLS (`wss://`). Ver "Segurança".
- Controle de fora da rede local.
- Comandos relativos (incrementar/decrementar). Ver "Semântica dos comandos".

---

## Visão da arquitetura

```
 2G Pilot (iPad)                         2G Connector (PC)
 ───────────────                         ─────────────────────────────────────────
 escuta UDP 49002 ◀── 2GCTL ─────────── ControlAnnouncer (a cada 5 s)
                                         │
 WebSocket ─────── ws://ip:49004/control ▶ ControlServer ──▶ ControlSession (por conexão)
   hello / pair / set / action               │                 │ pareamento, validação
   ◀── welcome / result / state / controls   │                 ▼
                                             │            ISimControl (capacidade da fonte)
                                             │                 │ fila
                                             │                 ▼
                                             │            thread do SimConnect ──▶ simulador
                                             ◀──── estado (CHANGED) ◀────────────────┘
```

### Peças

**No Core** — sem WPF, testáveis no macOS, como o `FlightPlanServer`:

| Unidade | Responsabilidade |
|---|---|
| `ControlProtocol` | Ler e gravar as mensagens JSON; rejeitar mensagens malformadas sem lançar |
| `ControlCommand`, `ControlResult`, `RadioState` | Tipos das mensagens e do estado |
| `PairingCodes` | Gerar, validar e expirar o código de 6 dígitos |
| `PairedDeviceStore` | Tokens pareados: gravar só o hash, conferir, remover |
| `ControlSession` | Máquina de estados de uma conexão: aguardando `hello` → não pareada → pareada |
| `ControlServer` | `TcpListener` na 49004, upgrade HTTP para WebSocket (`WebSocket.CreateFromStream`), limites de conexões e de taxa |
| `ControlAnnouncement` | Formatar a sentença `2GCTL` |

`HttpListener` não é usado pelo mesmo motivo do `FlightPlanServer`: escutar em todas as
interfaces com ele exige reserva de URL ou administrador, e o Connector é instalado por
usuário. O upgrade HTTP é feito à mão sobre o `TcpListener`, e o enquadramento, o ping e
o fechamento ficam com o `WebSocket` do .NET.

**No app WPF:**

| Unidade | Responsabilidade |
|---|---|
| `ISimControl` | Capacidade **opcional** de uma fonte de simulador, como o `IFlightPlanSource` |
| `SimConnectService` | Implementa `ISimControl` para MSFS e P3D (detalhes no 02) |
| `CompositeSimSource` | Repassa `ISimControl` da fonte ativa |
| `ControlAnnouncer` | Envia o `2GCTL` pelos mesmos destinos do XGPS |
| UI | Card "Controle pelo 2G Pilot", pareamento, lista de aparelhos, linha no Diagnóstico |

Esboço da capacidade, para orientar o plano de implementação:

```csharp
public interface ISimControl
{
    /// <summary>Controles que a aeronave atual oferece (ex.: "com1.standby").</summary>
    IReadOnlyCollection<string> AvailableControls { get; }

    /// <summary>Último estado reportado pelo simulador, ou null sem simulador.</summary>
    RadioState? State { get; }

    /// <summary>Estado ou lista de controles mudou.</summary>
    event Action? Changed;

    /// <summary>Valida e enfileira. Não bloqueia: a execução é na thread do simulador.</summary>
    ControlResult Submit(ControlCommand command);
}
```

---

## Descoberta: o anúncio `2GCTL`

A cada 5 s, enquanto o controle estiver ligado, o Connector envia pela UDP 49002 — a
porta que o app já escuta — uma sentença ASCII, sem terminador, no mesmo estilo do
`2GFPL`:

```
2GCTL<nome-do-dispositivo>,<versão-do-protocolo>,<url>
```

Exemplo: `2GCTL2G Connector,1,ws://192.168.68.102:49004/control`

- `nome-do-dispositivo` é o mesmo das sentenças XGPS (vírgulas já saneadas).
- `versão-do-protocolo` é o maior protocolo que o Connector fala (hoje `1`).
- O anúncio vai para **os mesmos destinos do XGPS**: broadcast dirigido de cada interface
  e os EFBs descobertos em unicast.
- **O host da URL é o IP da interface por onde aquele anúncio sai**, calculado por
  destino. Num PC com Wi-Fi e Ethernet, cada interface anuncia o próprio endereço. (É
  aqui que o `2GCTL` difere do `2GFPL`, que usa só a rota padrão — ver o 00.)
- O app deve preferir a URL do anúncio mais recente. Se o anúncio parar de chegar, o app
  pode continuar tentando a última URL conhecida.

## Conexão

1. O app abre um WebSocket em `ws://<ip>:49004/control`.
2. Em até **10 s** manda `hello`. Sem `hello` nesse prazo o Connector fecha a conexão.
3. O Connector responde `welcome` (token válido) ou `pairing_required`.
4. Pareado, o Connector manda em seguida `controls` e um `state` completo, e passa a
   empurrar `state` a cada mudança.

A porta padrão é 49004, configurável nas Configurações como a do plano de voo. A porta
anunciada no `2GCTL` é sempre a real.

### Limites

| Limite | Valor | Ao exceder |
|---|---|---|
| Conexões simultâneas | 4 (o mesmo teto da descoberta de EFBs) | A 5ª recebe `error` `busy` e é fechada com código 4003 |
| Tamanho de mensagem | 4 KB | Fechamento com código 1009 |
| Comandos por conexão | 20 por segundo | `result` com `rate_limited`; mais de 100 recusas em 10 s, fechamento 4008 |
| Tempo sem nenhum quadro | 45 s | Fechamento; o Connector manda ping a cada 15 s |

## Mensagens

Todas são quadros de texto WebSocket, UTF-8, uma mensagem JSON por quadro, com o campo
`type`. Nomes de campo em camelCase. **Mensagem de `type` desconhecido é ignorada** pelos
dois lados; campo desconhecido também. É isso que permite estender a versão 1.

### `hello` (app → Connector)

```json
{
  "type": "hello",
  "protocol": 1,
  "app": "2G Pilot",
  "appVersion": "3.2.0",
  "device": { "id": "6F1C2E9A-4B7D-4E2A-9C3B-1D5E8F0A2B4C", "name": "iPad do Galeno" },
  "token": "q3Zk…43 caracteres base64url…"
}
```

- `device.id` é estável por instalação do app (ex.: `identifierForVendor`).
- `device.name` é o nome que o piloto verá na lista de aparelhos do Connector (até 64 caracteres).
- `token` é omitido quando o app ainda não foi pareado com este Connector.
- `protocol` diferente de 1: o Connector responde `error` `protocol_unsupported` e fecha com 4002.

### `welcome` (Connector → app)

```json
{
  "type": "welcome",
  "protocol": 1,
  "connector": "1.5.0",
  "simulator": "Microsoft Flight Simulator 2024"
}
```

`simulator` é `null` sem simulador conectado. Logo depois vêm `controls` e `state`.

### `pairing_required` (Connector → app)

```json
{ "type": "pairing_required" }
```

Enviado quando não há token ou quando ele não confere (aparelho removido, Connector
reinstalado). A conexão continua aberta, esperando `pair`.

### `pair` (app → Connector) e `paired` (Connector → app)

```json
{ "type": "pair", "code": "482913" }
```

```json
{ "type": "paired", "token": "q3Zk…43 caracteres base64url…" }
```

O app guarda o token no Keychain, associado ao Connector. Depois de `paired`, a sessão
segue como se tivesse chegado com token: `welcome`, `controls`, `state`.

Código errado ou expirado: `error` `pairing_invalid`. Nenhum código ativo, ou tentativas
esgotadas: `error` `pairing_locked`. Em ambos a conexão continua aberta.

### `controls` (Connector → app)

```json
{ "type": "controls", "controls": ["com1.active", "com1.standby", "com1.swap", "xpdr.code"] }
```

A lista completa dos controles que a aeronave atual aceita. Enviada depois do `welcome` e
de novo sempre que muda — troca de aeronave, simulador que conecta ou desconecta. O app
só oferece na tela o que está nesta lista.

### `set` e `action` (app → Connector)

```json
{ "type": "set", "id": "a1", "control": "com1.standby", "value": 118500000 }
```

```json
{ "type": "action", "id": "a2", "control": "com1.swap" }
```

- `id` é escolhido pelo app (até 64 caracteres) e volta no `result`.
- `value` é sempre **inteiro**. Unidades e faixas estão no catálogo (02): frequências em
  Hz, pressão em Pa. Nada de decimal no fio — sem ambiguidade de vírgula, e o espaçamento
  de 8,33 kHz fica exato.

### `result` (Connector → app)

```json
{ "type": "result", "id": "a1", "ok": true }
```

```json
{ "type": "result", "id": "a1", "ok": false, "error": "out_of_range" }
```

**`ok: true` significa "entregue ao simulador", não "aplicado".** A confirmação de que o
cockpit mudou é o `state`.

Exceção: nos controles que a aeronave só aceita girando o seletor (mecanismo `knob`, ver
[04](04-perfis-e-modulo-msfs.md)), o `result` só chega depois da sequência de cliques, em
até 3 s, e já diz se a aeronave seguiu o comando: `ok: true` ou `not_applied`.

### `state` (Connector → app)

```json
{
  "type": "state",
  "seq": 1042,
  "simulator": "Microsoft Flight Simulator 2024",
  "radios": {
    "com1": { "active": 121900000, "standby": 118500000 },
    "com2": { "active": 122800000, "standby": 124350000 },
    "nav1": { "active": 110300000, "standby": 113900000 },
    "nav2": { "active": 116700000, "standby": 108000000 },
    "adf1": { "active": 350000 },
    "xpdr": { "code": 2000, "mode": 4 },
    "altimeter": { "baroPa": 101320 }
  }
}
```

- Sempre o **retrato completo**, nunca diferença: o app pode descartar tudo e redesenhar.
  É pequeno (cerca de 400 bytes) e elimina a classe inteira de bugs de estado parcial.
- `seq` cresce a cada `state` da conexão. O app ignora um `state` com `seq` menor que o último.
- Um rádio que a aeronave não tem simplesmente não aparece em `radios`.
- Sem simulador: `"simulator": null` e `"radios": {}`.
- Enviado ao parear ou conectar, e depois a cada mudança, **agregado a no máximo 10 por
  segundo**: mudanças dentro da janela viram um só `state` com o valor mais recente.

### `error` (Connector → app)

```json
{ "type": "error", "code": "pairing_invalid" }
```

Erros que não pertencem a um comando específico.

O app deve tratar um código de erro desconhecido como falha genérica do comando, sem
quebrar: é assim que etapas futuras acrescentam códigos sem mudar o protocolo.

### Códigos de erro

| Código | Onde | Significado |
|---|---|---|
| `out_of_range` | `result` | Valor fora da faixa ou do espaçamento do controle |
| `unsupported` | `result` | Controle desconhecido ou fora de `controls` agora |
| `sim_not_connected` | `result` | Nenhum simulador conectado |
| `not_paired` | `result` | Comando antes de parear |
| `rate_limited` | `result` | Mais de 20 comandos por segundo nesta conexão |
| `sim_unresponsive` | `result` | Simulador conectado, mas com 32 comandos ainda pendentes na fila |
| `not_applied` | `result` | A aeronave não seguiu o comando (sequência `knob` divergiu, estourou 60 cliques ou 3 s — ver 04) |
| `invalid_message` | `error` | JSON inválido ou campo obrigatório ausente |
| `protocol_unsupported` | `error` | `protocol` diferente de 1 |
| `pairing_invalid` | `error` | Código errado ou expirado |
| `pairing_locked` | `error` | Nenhum código ativo, ou tentativas esgotadas |
| `busy` | `error` | Já há 4 conexões |

### Códigos de fechamento

| Código | Motivo |
|---|---|
| 1000 | Normal (Connector encerrando, controle desligado nas Configurações) |
| 1009 | Mensagem maior que 4 KB |
| 4001 | Aparelho removido da lista de pareados, com a conexão aberta |
| 4002 | Protocolo não suportado |
| 4003 | Conexões esgotadas |
| 4008 | Excesso de comandos persistente |

Ao receber 4001, o app apaga o token guardado e volta ao pareamento.

## Semântica dos comandos

- **Valores absolutos e ações idempotentes em conjunto:** repetir `set com1.standby
  118500000` é inofensivo. A única ação da v1, `swap`, não é idempotente — o app deve
  enviá-la uma vez por toque, e nunca reenviar automaticamente um `swap` sem `result`.
- **Vários aparelhos, último comando vale.** Todos recebem o mesmo `state`; se dois
  iPads mudam o COM1 ao mesmo tempo, o `state` mostra aos dois o valor que ficou.
- **Validação antes da fila.** Faixa e espaçamento são conferidos no Connector, antes de
  chegar ao simulador; o que não passa volta como `out_of_range` imediatamente.
- **Aeronaves que ignoram eventos padrão.** Algumas aeronaves de terceiros não respondem
  aos eventos do SimConnect. O comando sai com `ok: true` e o `state` não muda. O app deve
  tratar "o `state` não mudou em 2 s" como sinal disso e informar o piloto, sem repetir o
  comando. A solução é o módulo WASM (spec 04).

## Pareamento

### Fluxo

1. No Connector, o piloto clica em **Parear aparelho**. Aparece um código de 6 dígitos em
   fonte grande, com contagem regressiva.
2. No app, o piloto escolhe o Connector (vindo do `2GCTL`) e digita o código.
3. O app manda `pair`; o Connector confere e responde `paired` com o token.
4. O aparelho aparece na lista do Connector com o nome do `hello`.

### Regras do código

- Gerado com gerador criptográfico, uniforme entre `000000` e `999999`.
- **Só existe quando o piloto pede**, e só um por vez: gerar outro invalida o anterior.
- Vale **2 minutos** e é de **uso único**.
- **5 tentativas erradas**, somando todas as conexões, invalidam o código. É preciso gerar outro.
- Comparação em tempo constante.

Com 6 dígitos, 5 tentativas e 2 minutos, a chance de acertar por força bruta é de 5 em
um milhão por código gerado — e o código só existe durante o pareamento que o próprio
piloto iniciou.

### Tokens

- 32 bytes aleatórios de gerador criptográfico, em base64url sem padding (43 caracteres).
- O Connector grava **só o hash SHA-256** em `%APPDATA%\2G Connector\paired-devices.json`:

  ```json
  {
    "devices": [
      {
        "id": "6F1C2E9A-4B7D-4E2A-9C3B-1D5E8F0A2B4C",
        "name": "iPad do Galeno",
        "tokenSha256": "9f2c…64 hex…",
        "pairedUtc": "2026-09-12T13:10:00Z",
        "lastSeenUtc": "2026-09-12T14:02:11Z"
      }
    ]
  }
  ```

- Parear de novo o mesmo `device.id` substitui a entrada e invalida o token antigo.
- O token não expira. Só sai quando o piloto remove o aparelho.
- Remover um aparelho com conexão aberta fecha essa conexão com 4001 na hora.
- Arquivo ilegível ou corrompido: tratado como lista vazia, e todos precisam parear de novo.
  Nunca derruba o Connector.

## Segurança

**O que o protocolo nunca transporta:** nome de variável, nome de evento ou código. O app
manda só um ID do catálogo e um inteiro; a tradução para o que o simulador entende é do
Connector (ver 02 e 04). É isso que impede um aparelho pareado de executar código dentro
do simulador.

**O que o pareamento garante:** sem o código mostrado na tela do Connector, nenhum
aparelho da rede consegue comandar o simulador. Uma página web maliciosa aberta num
navegador da rede consegue abrir o WebSocket, mas não tem como parear.

**Risco aceito na v1 — sem TLS:** o canal é `ws://`. Quem está na mesma rede e captura o
tráfego vê o token e os comandos. Para rádios de simulador numa rede doméstica, o custo é
baixo, e `wss://` com certificado autoassinado exige pinning no iOS — atrito grande para a
primeira entrega.

**Caminho de evolução:** o Connector gera um certificado próprio na primeira execução, e
o pareamento transporta a impressão digital dele, confirmada pelo código de 6 dígitos. O
app fixa essa impressão e passa a exigir `wss://`. Isso muda o contrato — protocolo 2 —
e fica para quando o catálogo incluir controles com consequência maior que um rádio.

**Chave geral:** a opção **Permitir controle pelo 2G Pilot** nas Configurações vem
ligada. Desligada, a porta 49004 não abre, o `2GCTL` não é anunciado e as conexões
abertas são fechadas com 1000. Os pareamentos continuam guardados.

## Interface no Connector

- **Card "Controle pelo 2G Pilot"**, na janela principal:
  - estado do canal: "Aguardando aparelho", "1 aparelho conectado: iPad do Galeno", ou "Desligado nas Configurações";
  - botão **Parear aparelho**, que abre o código com a contagem regressiva;
  - lista de aparelhos pareados, com último acesso, indicação de conectado agora e botão **Remover**.
- **Configurações:** "Permitir controle pelo 2G Pilot" e a porta.
- **Diagnóstico:** conexões ativas, último comando recebido (controle e resultado) e o
  último erro do servidor (porta ocupada, por exemplo).

## Erros e isolamento

- Uma falha no canal nunca afeta o XGPS/XATT. O servidor roda na própria thread e só
  conversa com o simulador pela fila do `ISimControl`.
- Porta 49004 ocupada: o erro aparece no card e no Diagnóstico, e o resto do Connector
  segue normal.
- Mensagem inválida: `error` `invalid_message`, e a conexão continua. Um app com defeito
  não derruba o canal dos outros.
- A fila de comandos tem limite: com o simulador travado, comandos acima de 32 pendentes
  voltam `sim_unresponsive`, em vez de acumular sem fim.

## Testes

**Automatizados, no Core (rodam no macOS e no CI):**

- `ControlProtocol`: cada mensagem ida e volta; JSON inválido, campos faltando e tipos
  errados viram erro sem exceção; `type` e campos desconhecidos são ignorados.
- `PairingCodes`: validade de 2 min (com relógio injetado), uso único, 5 tentativas,
  um código por vez, geração dentro da faixa.
- `PairedDeviceStore`: grava só o hash, confere, substitui por `device.id`, remove,
  arquivo corrompido vira lista vazia.
- `ControlAnnouncement`: formato do `2GCTL`, saneamento do nome.
- `ControlServer`, integração com um `ClientWebSocket` real numa porta efêmera: upgrade,
  `hello` sem token → `pairing_required`, `pair` → `paired` → `controls` → `state`,
  `set` com resultado, limite de 4 conexões, limite de taxa, fechamento 4001 ao remover
  aparelho, prazo do `hello`. O simulador é substituído por um `ISimControl` falso.

**No Windows, por roteiro:** o que depende do SimConnect (ver 02) e o fluxo completo com
o 2G Pilot num iPad real.

---

## Contrato para o time do 2G Pilot

O que o app precisa fazer, em ordem:

1. **Descobrir:** na UDP 49002, que já escuta, reconhecer sentenças que começam com
   `2GCTL` e guardar nome, versão e URL. Mais de um Connector na rede: listar pelo nome.
2. **Conectar:** abrir WebSocket na URL (`URLSessionWebSocketTask`) e mandar `hello` com
   `device.id` estável e o token do Keychain, se houver.
3. **Parear**, ao receber `pairing_required`: pedir ao piloto o código de 6 dígitos que o
   Connector mostra, mandar `pair`, guardar o token de `paired` no Keychain.
4. **Mostrar só o que existe:** oferecer na tela apenas os controles de `controls`.
5. **Comandar com valores absolutos:** frequências em Hz, pressão em Pa, sempre inteiros.
   A conversão inHg ↔ hPa e a formatação da tela são do app.
6. **Mostrar o `state`, não o comando:** o valor na tela é o do último `state`. Enquanto o
   comando não aparece no `state`, o app pode mostrar o valor pedido como "pendente".
7. **Detectar aeronave que ignora o comando:** `result ok` sem mudança no `state` em 2 s →
   avisar o piloto que esta aeronave não aceita o comando padrão.
8. **Reconectar:** em queda, tentar de novo com espera crescente (1, 2, 4, 8… até 30 s),
   usando a URL do anúncio mais recente.
9. **Tratar os fechamentos:** 4001 → apagar o token e voltar ao pareamento; 4002 →
   avisar que o Connector precisa ser atualizado; 4003 → avisar que há aparelhos demais.
10. **Nunca reenviar `swap` automaticamente.** Reenviar um `set` é seguro; um `swap` repetido desfaz o anterior.
