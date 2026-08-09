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

Baixe o instalador (`2G-GPS-Cliente-Setup-x.y.z.exe`) na página de releases e execute.

- Não requer administrador (instala por usuário, sem UAC).
- Detecta as instalações do MSFS 2020/2024 (Store e Steam) e mostra o resultado.
- Não precisa de regra de firewall: o app apenas **envia** UDP, liberado por padrão
  no Windows.
- Também há um `.zip` portátil em cada release (basta extrair e executar).

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
installer/setup.iss        Instalador Inno Setup
```

```bash
dotnet test                                        # roda em qualquer SO
dotnet build src/TwoG.GpsClient -c Release         # compila até em macOS/Linux (EnableWindowsTargeting)
```

O executável só roda no Windows (SimConnect é x64/Windows). O pipeline de CI
(`.github/workflows/build.yml`) publica self-contained, compila o instalador e anexa
os artefatos; tags `v*` geram release automaticamente.

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
