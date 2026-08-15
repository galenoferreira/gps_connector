# Estudo: suporte a X-Plane e Prepar3D

Avaliação técnica para estender o 2G GPS Cliente além do MSFS.
Data: 2026-08-15 · Base: código na v1.0.1

---

## Sumário executivo

| Simulador | Viabilidade | Esforço | Reaproveitamento | Recomendação |
|---|---|---|---|---|
| **Prepar3D v4/v5/v6** | Alta | **Baixo** (~1–2 dias) | Usa SimConnect, a mesma API do MSFS | **Fazer primeiro** |
| **X-Plane 11/12** | Alta | Médio (~3–4 dias) | Nada — protocolo UDP próprio | Fazer depois |
| FSX / P3D v1–v3 | Baixa | — | SimConnect só 32 bits | **Descartar** |

**A arquitetura atual já está pronta para isso.** A interface
[`ISimSource`](../src/TwoG.GpsClient/Services/ISimSource.cs) isola a origem dos dados:
o broadcaster XGPS, o ViewModel e toda a UI dependem só dela. Adicionar um simulador
é escrever uma nova implementação — **nenhuma linha do caminho de transmissão muda**.

```
                       ┌── SimConnectService (MSFS)     ── existe
ISimSource ────────────┼── Prepar3DService              ── novo, quase idêntico
   │                   └── XPlaneService                ── novo, UDP puro
   ▼
XgpsBroadcaster → UDP 49002 → EFB          (inalterado)
```

**Ponto de atenção comercial:** o X-Plane **já transmite XGPS/XATT nativamente** na
porta 49002. Ver [a seção sobre isso](#o-x-plane-já-faz-isso-sozinho) antes de
priorizar — o valor do nosso conector ali é de experiência e marca, não de
viabilidade técnica.

---

## Prepar3D

### Por que é barato

O P3D descende do FSX e usa **SimConnect** — a mesma API que já implementamos. As
SimVars que consumimos são idênticas: `PLANE LATITUDE`, `PLANE LONGITUDE`,
`PLANE ALTITUDE`, `GPS GROUND TRUE TRACK`, `GPS GROUND SPEED`,
`PLANE HEADING DEGREES TRUE`, `PLANE PITCH DEGREES`, `PLANE BANK DEGREES`,
`SIM ON GROUND`. As convenções de sinal invertidas (arfagem positiva = nariz para
baixo, rolagem positiva = esquerda) também são herdadas do FSX, então
[a negação que já fazemos](../src/TwoG.GpsClient/Services/SimConnectService.cs)
continua correta.

Na prática, `SimConnectService.cs` é reaproveitável quase integralmente.

### O obstáculo: qual DLL

| | MSFS | Prepar3D v5/v6 |
|---|---|---|
| Wrapper gerenciado | `Microsoft.FlightSimulator.SimConnect.dll` | `LockheedMartin.Prepar3D.SimConnect.dll` |
| Namespace | `Microsoft.FlightSimulator.SimConnect` | `LockheedMartin.Prepar3D.SimConnect` |
| DLL nativa | `SimConnect.dll` | `SimConnect.dll` (mesmo nome!) |
| Named pipe | `\\.\pipe\Microsoft Flight Simulator\SimConnect` | `\\.\pipe\Lockheed Martin Prepar3D v5\SimConnect` |
| Arquitetura | x64 | x64 (v4+) |

Os dois wrappers **gerenciados** coexistem no mesmo processo sem conflito, porque
os namespaces diferem. O problema é a **DLL nativa**: ambas se chamam
`SimConnect.dll`, e o Windows resolve por nome de módulo — a primeira carregada
vence. Isso impede atender aos dois simuladores simultaneamente no mesmo processo.

Não é um problema real para nós: **ninguém roda MSFS e P3D ao mesmo tempo**. A
decisão de qual carregar é tomada uma vez, na inicialização, com base em qual
simulador está instalado/rodando.

### Incerteza a validar antes de codificar

Há um relato de desenvolvedores no fórum oficial do MSFS de que *"o SimConnect do
MSFS conecta ao Prepar3D, ainda que sem as funcionalidades adicionais"* — o que
tornaria o suporte quase gratuito. **Mas isso conflita com os named pipes serem
diferentes**, e não encontrei confirmação da Lockheed Martin.

Teste decisivo, ~30 minutos numa máquina com P3D: rodar o binário atual com o P3D
aberto e ver se o `SimConnect_Open` tem sucesso. O resultado determina o caminho:

| Resultado | Implicação |
|---|---|
| Conecta | Suporte a P3D é praticamente **grátis** — só ajustar o nome exibido |
| Não conecta | Adicionar o wrapper da LM e escolher o par de DLLs em runtime (~1–2 dias) |

Se for preciso o wrapper da LM, o desenho fica: extrair para pastas separadas
(`runtime/<versão>/msfs/` e `runtime/<versão>/p3d/`) e carregar só o par
correspondente ao simulador detectado — o
[`SimConnectRuntime`](../src/TwoG.GpsClient/Services/SimConnectRuntime.cs) já faz
exatamente esse tipo de extração e pré-carga.

### Pendência jurídica

Redistribuir `LockheedMartin.Prepar3D.SimConnect.dll` dentro do nosso `.exe`
exige checar a licença do SDK do P3D — que é mais restritiva que a da Microsoft.
**Alternativa segura:** carregar a DLL da instalação local do P3D do usuário (o
caminho é conhecido e o SDK costuma estar presente), em vez de embutir. Isso evita
a questão por completo, ao custo de uma dependência externa.

### Detecção

- Processo: `Prepar3D.exe`
- Registro: `HKEY_LOCAL_MACHINE\SOFTWARE\Lockheed Martin\Prepar3D v5` (e `v6`),
  valor `SetupPath`
- Configuração do usuário: `%APPDATA%\Lockheed Martin\Prepar3D v5\`

O `EXE.xml` para autostart existe no P3D no mesmo formato do MSFS — a classe
[`ExeXmlAutoStart`](../src/TwoG.GpsClient/Services/ExeXmlAutoStart.cs) funciona lá
com apenas novos caminhos em `MsfsInstallations`.

---

## X-Plane

### O X-Plane já faz isso sozinho

**Este é o achado mais importante do estudo.** O X-Plane 11 e 12 transmitem XGPS e
XATT nativamente na porta 49002, sem nenhum software adicional: basta o usuário
abrir *Settings → Network → "iPhone, iPad and External Apps"* e marcar o broadcast
para apps de mapa. A ForeFlight documenta esse caminho oficialmente.

Ou seja, para X-Plane **não existe o problema que o nosso conector resolve no MSFS**.
O valor passa a ser outro:

| A favor de implementar | Contra |
|---|---|
| **Marca**: o X-Plane se anuncia como `XGPS1`/`XATT1` — a ForeFlight mostra um dispositivo chamado **"1"**. Com o nosso conector, aparece **"2G GPS"** | O nativo tem **precisão melhor** (ver abaixo) |
| **Zero configuração**: descobrimos o X-Plane sozinhos; o nativo exige o usuário achar a opção | Mais um caminho de código para manter e suportar |
| **Uma experiência só**: mesmo app, mesma tela, mesmo suporte para os três simuladores | O nativo não quebra nunca — é a própria Laminar que mantém |
| **Unicast para outra sub-rede**, que o nativo não oferece | |
| Base para enviar dados proprietários ao 2G Pilot EFB no futuro | |

**Recomendação:** implementar, mas documentar o caminho nativo no README como
alternativa — é honesto e reduz tickets de suporte.

### Como ler os dados: protocolo RREF

O X-Plane expõe um protocolo UDP que não exige plugin nem DLL — **é o caminho mais
limpo dos três simuladores**.

**Descoberta automática (beacon BECN)**, que dá a mesma UX de auto-conexão do MSFS:

| Item | Valor |
|---|---|
| Grupo multicast | `239.255.1.1` |
| Porta | `49707` |
| Conteúdo | versão do X-Plane, papel da máquina, porta de escuta, hostname |

**Assinatura de datarefs (RREF)** — enviar para a porta anunciada no beacon (padrão `49000`):

```
struct: "<5sii400s"   → 413 bytes
  "RREF\0"            (5 bytes)
  frequência em Hz    (int32)   — 0 cancela a assinatura
  índice              (int32)   — nosso identificador, volta na resposta
  caminho do dataref  (400 bytes, terminado em \0)
```

**Resposta** — cabeçalho `RREF,` seguido de pares de 8 bytes:

```
struct: "<if"  por valor
  índice (int32) + valor (float32)
```

Uma resposta cabe em 1472 bytes (MTU Ethernet), ou seja ~183 valores por pacote —
folgado para os 8 que precisamos.

### Datarefs necessários

Confirmados no `DataRefs.txt` oficial do X-Plane, **com as unidades já no formato
que o XGPS exige** — nenhuma conversão necessária:

| Dataref | Tipo | Unidade | Campo XGPS/XATT |
|---|---|---|---|
| `sim/flightmodel/position/latitude` | double | graus | latitude |
| `sim/flightmodel/position/longitude` | double | graus | longitude |
| `sim/flightmodel/position/elevation` | double | **metros** MSL | altitude |
| `sim/flightmodel/position/groundspeed` | float | **m/s** | velocidade de solo |
| `sim/flightmodel/position/hpath` | float | graus | curso sobre o solo (verdadeiro) |
| `sim/flightmodel/position/psi` | float | graus | proa verdadeira |
| `sim/flightmodel/position/theta` | float | graus | arfagem |
| `sim/flightmodel/position/phi` | float | graus | rolagem |

**Sinais: não há inversão a fazer.** Diferente do MSFS, o X-Plane usa a mesma
convenção do XATT (arfagem positiva = nariz para cima, rolagem positiva = asa
direita para baixo) — o que é esperado, já que o formato XATT foi definido pelo
próprio X-Plane a partir desses datarefs. Vale confirmar em voo, mas é um ajuste
de minutos se estiver invertido.

### Limitação real: precisão de posição

O RREF devolve **float32** mesmo para datarefs declarados como `double`. Com 24 bits
de mantissa, a precisão absoluta em uma longitude de ~122° é de aproximadamente
`122 × 1,19e-7 ≈ 1,5e-5` grau — cerca de **1,5 metro**.

Na prática isso é aceitável: é a mesma ordem de grandeza do erro de um GPS real
(3–5 m) e invisível num mapa móvel de EFB. Mas é um **piso** do caminho UDP: para
precisão sub-métrica seria preciso um plugin XPLM em C++ (que lê os doubles
diretamente), o que multiplicaria o custo do projeto. **Não recomendo** — e é
justamente aqui que o broadcast nativo do X-Plane leva vantagem, pois ele formata
a sentença internamente sem passar por float32.

### Detecção de estado de voo

O X-Plane não tem o equivalente aos eventos `Sim`/`Pause_EX1` do SimConnect. Para
replicar a "pausa inteligente" existente:

- `sim/time/paused` (int) — pausa explícita
- Watchdog de fluxo: sem pacotes RREF por N segundos ⇒ tratar como sem voo

O `XgpsBroadcaster` já tem o watchdog de frescor (`FixFreshness`, 3 s), então o
comportamento correto sai quase de graça.

---

## Impacto no produto

### Interface

Hoje a UI diz "Procurando simulador…" e depois mostra o nome do MSFS. Com três
simuladores, a mudança mínima é:

- Detecção automática por ordem de prioridade (o que estiver rodando vence)
- O card SIMULADOR passa a exibir qual foi encontrado ("X-Plane 12", "Prepar3D v5")
- Em Configurações, um seletor opcional *Automático / MSFS / P3D / X-Plane* para
  quem tem mais de um instalado e quer forçar

Nenhuma mudança no card de transmissão, no de posição ou nas configurações de rede.

### Tamanho do executável

| Cenário | Impacto |
|---|---|
| X-Plane | **Zero** — UDP puro, sem binários novos |
| P3D com DLL da instalação local | **Zero** |
| P3D com DLLs embutidas | +~1,5 MB |

### Testes

O `TwoG.GpsClient.Core` continua sendo a camada testável e multiplataforma. O
parser RREF e o mapeamento de datarefs para `GpsFix` **devem morar lá** — são
lógica pura, testável no macOS sem simulador, exatamente como as sentenças XGPS
hoje. É o maior ganho de qualidade disponível neste projeto.

---

## Riscos

| Risco | Severidade | Mitigação |
|---|---|---|
| Cliente SimConnect do MSFS não conectar ao P3D | Média | Teste de 30 min decide o caminho antes de qualquer código |
| Licença do SDK do P3D proibir redistribuição | Média | Carregar a DLL da instalação local do usuário |
| Colisão de `SimConnect.dll` nativa | Baixa | Um simulador por sessão; escolha na inicialização |
| Precisão float no X-Plane | Baixa | ~1,5 m, dentro do erro de GPS real; documentar |
| Sem acesso a P3D/X-Plane para testar | **Alta** | É o gargalo real do projeto — ver abaixo |

**O gargalo não é código, é ambiente de teste.** Nenhuma dessas integrações pode
ser validada sem os simuladores instalados numa máquina Windows. O X-Plane tem
demo gratuita (15 minutos por voo, suficiente); o P3D exige licença paga (a versão
Academic é a mais barata).

---

## Plano sugerido

**Fase 1 — Prepar3D (1–2 dias).** Começar pelo teste de conexão. Se o cliente
atual conectar, o suporte sai quase pronto; senão, adicionar o segundo wrapper.
Melhor relação custo/benefício e valida a arquitetura multi-simulador com pouco
código novo.

**Fase 2 — X-Plane (3–4 dias).** Parser RREF + descoberta BECN no `Core` com
testes, `XPlaneService` implementando `ISimSource`, e o seletor na UI.

**Fase 3 — Polimento (1 dia).** README com a matriz de simuladores, o caminho
nativo do X-Plane documentado, e `EXE.xml` do P3D no autostart.

Total: **5–7 dias de desenvolvimento**, mais o tempo de validação com os
simuladores reais.

---

## Fontes

- [ForeFlight — Flight Simulator GPS Integration (UDP)](https://support.foreflight.com/hc/en-us/articles/204115005-Flight-Simulator-GPS-Integration-UDP-Protocol)
- [ForeFlight — Conectar ao X-Plane](https://support.foreflight.com/hc/en-us/articles/204115525-How-can-ForeFlight-be-connected-to-the-X-Plane-flight-simulator)
- [X-Plane — DataRefs.txt oficial](https://github.com/X-Plane/XPlane2Blender/blob/master/io_xplane2blender/resources/DataRefs.txt)
- [XPlaneUDP — implementação de referência do RREF e do beacon BECN](https://github.com/charlylima/XPlaneUDP/blob/master/XPlaneUdp.py)
- [Prepar3D SDK](https://www.prepar3d.com/sdk/)
- [Fórum MSFS — cliente SimConnect para MSFS e P3D](https://forums.flightsimulator.com/t/simconnect-client-to-be-used-with-both-msfs-and-p3d/400213)
