# Controle do simulador pelo 2G Pilot — visão geral

Data: 2026-09-12
Status: design aprovado em conversa; documentos aguardando revisão

## O que é

Até a v1.4.0 o 2G Connector só envia dados ao 2G Pilot: posição e atitude (XGPS/XATT),
o anúncio do plano de voo (`2GFPL`) e o próprio plano, por HTTP. Este conjunto de
documentos especifica o caminho contrário: o piloto opera o simulador pelo app — a
começar pelos rádios — e o app mostra o estado real do cockpit.

## Decisões tomadas

| Tema | Decisão | Por quê |
|---|---|---|
| Primeira entrega | **Rádios**: COM1/COM2, NAV1/NAV2, ADF, transponder e altímetro | Caso de uso de EFB mais comum; funciona sem módulo no simulador; valida o canal inteiro |
| Simuladores da primeira entrega | **MSFS 2020/2024 e Prepar3D**, pelo SimConnect | Mesmo caminho de código para os três. O X-Plane entra depois, com o mesmo contrato |
| Transporte | **WebSocket com mensagens JSON**, porta TCP 49004 | Bidirecional: o estado chega ao app assim que muda, sem polling. Nativo no iOS e no .NET |
| Quem pode controlar | **Pareamento com código de 6 dígitos**, token persistente por aparelho | O Connector passa a aceitar comandos da rede; sem pareamento, qualquer aparelho na LAN mexeria nos rádios |
| Semântica dos comandos | **Valores absolutos** ("COM1 standby = 118.500") e ações ("trocar COM1"). Sem incremento | Um comando absoluto repetido chega ao mesmo resultado; um incremento repetido erra o valor |
| Verdade sobre o cockpit | **O estado que o simulador reporta**, não o comando enviado | Aeronaves de terceiros podem ignorar um comando; o app não pode mostrar o que não aconteceu |
| Descoberta do Connector | **Anúncio `2GCTL` na UDP 49002**, no formato do `2GFPL` | O app já escuta essa porta e já interpreta o `2GFPL` |
| Onde o comando executa | **Fila atendida pela thread do SimConnect** que já existe | A conexão SimConnect continua tendo um dono só |
| Aeronaves complexas | **Híbrido**: L:vars e B:events pelo próprio SimConnect; módulo WASM próprio só para H:events e RPN | O que o SimConnect já alcança não exige instalar nada no simulador |
| Perfis de aeronave | **Pacote JSON assinado**, num repositório separado, atualizado sem release do Connector | Atualização de aeronave quebra perfil; a correção não pode esperar versão nova do app |
| O que o app envia | **Só ID do catálogo e inteiro**, nunca variável, evento ou código | Nenhum aparelho pareado executa código dentro do simulador |

## As peças e os documentos

| Documento | Conteúdo | Etapa |
|---|---|---|
| [01-canal-de-controle.md](01-canal-de-controle.md) | Protocolo, pareamento, servidor, UI no Connector e o contrato para o time do app | Primeira entrega |
| [02-radios-simconnect.md](02-radios-simconnect.md) | Catálogo de rádios, faixas válidas e o mapeamento para eventos e SimVars do SimConnect | Primeira entrega |
| [03-radios-xplane.md](03-radios-xplane.md) | Mesmo catálogo via `DREF`/`CMND` por UDP. Nenhuma mudança no app | Etapa 2 |
| [04-perfis-e-modulo-msfs.md](04-perfis-e-modulo-msfs.md) | Perfis por aeronave (frota do MSFS 2024, FlyByWire A32NX, PMDG 737, Fenix A320), cinco mecanismos de acesso e o módulo WASM | Etapa 3 (4a, 4b, 4c) |

O canal (01) é independente de simulador e de catálogo: o 02, o 03 e o 04 só acrescentam
controles à lista que o canal transporta. O app não precisa mudar quando uma etapa
nova entra — só passa a ver mais itens em `controls`.

## Ordem de entrega

1. Canal e rádios no MSFS/P3D (01 + 02). Versão candidata do Connector: v1.5.0.
2. Rádios no X-Plane (03), depois que a leitura de posição do X-Plane for validada num X-Plane real.
3. Perfis de aeronave (04), em três partes:
   - 4a: frota do MSFS 2024 e FlyByWire A32NX, só com o SimConnect (sem módulo);
   - 4b: PMDG 737, pelo SDK da PMDG;
   - 4c: Fenix A320, com o módulo WASM e a sequência de cliques (`knob`).

   O mesmo formato de perfil é o caminho para piloto automático e controles gerais depois.

## Fora do escopo de todas as etapas acima

- Piloto automático, luzes, trem, flaps, combustível e demais controles gerais. Entram
  como catálogos novos sobre o mesmo canal, depois do módulo (04).
- Controle pela internet ou de fora da rede local.
- TLS no canal (`wss://`). O risco aceito e o caminho de evolução estão no 01.

## Problema conhecido, fora do escopo

O anúncio `2GFPL` usa o IP da interface da rota padrão do Windows. Num PC com mais de
uma placa de rede, ele pode anunciar um endereço que o iPad não alcança. O `2GCTL`
resolve isso para si (o host da URL é o IP da interface por onde cada anúncio sai); o
`2GFPL` merece a mesma correção num trabalho separado.
