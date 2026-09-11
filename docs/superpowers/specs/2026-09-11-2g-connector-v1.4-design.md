# 2G Connector v1.4.0 — rebrand e auto-update

Data: 2026-09-11
Status: aprovado em conversa; aguardando revisão do documento escrito

## Contexto

O "2G GPS Cliente for MSFS" vai deixar de ser só uma fonte de GPS. O próximo passo
é a ponte bidirecional: o 2G Pilot passa a controlar o simulador (rádios, controles
gerais), com um módulo a ser desenvolvido para o simulador. O nome atual deixa de
descrever o produto, e o código — `TwoG.GpsClient` — deixa de descrever o código.

Este documento cobre a v1.4.0, que entrega duas coisas:

1. **Rebrand para 2G Connector**, com migração transparente de quem já tem a v1.3.0.
2. **Auto-update**, assinado, que só instala fora de voo.

Os dois vão juntos porque mexem no mesmo lugar: os nomes dos arquivos publicados.

### Fora de escopo

Cada item abaixo terá spec próprio:

- **Canal de controle 2G Pilot → Connector** (protocolo de comandos, contrato com o
  time do EFB).
- **Atuação no simulador** (SimConnect para rádios padrão, módulo WASM para L:vars e
  H:events de aeronaves complexas, comandos UDP do X-Plane).
- **Authenticode.** Resolveria também o aviso do SmartScreen, mas tem custo anual,
  validação da empresa e exige chave em hardware. Pode ser somado depois à
  assinatura do manifesto.

Também fora: atualização por delta, escolha de canal pela UI, detecção de conexão
tarifada. Nenhum tem demanda hoje.

---

## Parte 1 — Rebrand

### O que muda

| Onde | Antes | Depois |
|---|---|---|
| Janela, bandeja, instalador, "Adicionar ou remover programas" | 2G GPS Cliente for MSFS | 2G Connector |
| Subtítulo da janela | for Microsoft Flight Simulator | MSFS • Prepar3D • X-Plane |
| Executável | `2G-GPS-Cliente.exe` | `2G-Connector.exe` |
| Configuração | `%APPDATA%\2G GPS Cliente\` | `%APPDATA%\2G Connector\` |
| Cache do SimConnect e `erro.log` | `%LOCALAPPDATA%\2G GPS Cliente\` | `%LOCALAPPDATA%\2G Connector\` |
| Entrada `Launch.Addon` no `EXE.xml` | `2G GPS Cliente` | `2G Connector` |
| Nome do cliente SimConnect | 2G GPS Cliente | 2G Connector |
| Valor na chave `Run` do registro | `2G GPS Cliente` | `2G Connector` |
| Dispositivo XGPS padrão (só instalação nova) | 2G GPS | 2G Connector |
| Namespaces, projetos e pastas | `TwoG.GpsClient` | `TwoG.Connector` |

O subtítulo antigo já estava errado: o app suporta Prepar3D e X-Plane desde a Fase 2.

O repositório `gps_connector` **não** é renomeado. O GitHub redirecionaria as URLs
antigas, mas os links do site e do README passariam a depender desse redirecionamento
para sempre.

### O que fica, de propósito e para sempre

Cada um destes leva um comentário no código explicando por que o nome antigo ficou.

- **Mutex `Local\TwoG.GpsClient.SingleInstance` e evento `Local\TwoG.GpsClient.ShowWindow`.**
  São o único jeito de a v1.3.0 e a v1.4.0 se enxergarem. Com eles preservados, se
  as duas forem lançadas, só uma sobe. Se mudassem, as duas transmitiriam em dobro.
- **`AppId` do instalador** (`{B23B9502-7C57-45EF-9075-D53016835238}`). É o que faz o
  Inno Setup atualizar a instalação existente em vez de instalar outra ao lado.
- **Pasta de instalação de quem atualiza.** O Inno reusa a pasta anterior por padrão
  (`UsePreviousAppDir`). Quem atualiza fica em `...\2G GPS Cliente\`; instalações
  novas vão para `...\2G Connector\`.
- **Contratos de fio com o EFB**, que não têm relação com o nome do produto: prefixos
  `XGPS`/`XATT`, anúncio `2GFPL`, serviço Bonjour `_2gpilot._udp`, anúncio
  `{"App":"2G Pilot",...}` na UDP 63093, portas 49002, 49003 e 63093.

### Migração na primeira execução do exe novo

**Configuração.** Se `%APPDATA%\2G Connector\settings.json` não existe e
`%APPDATA%\2G GPS Cliente\settings.json` existe, copia. A pasta antiga fica intacta,
para que voltar à v1.3.0 funcione. O nome de dispositivo gravado é respeitado: quem
tinha "2G GPS" continua com "2G GPS". O padrão "2G Connector" vale só para quem não
tem configuração nenhuma.

**`EXE.xml`.** Ao registrar, remove também qualquer `Launch.Addon` cujo `Name` seja
`2G GPS Cliente`. Ao desregistrar (desinstalador, via `-unregister`), remove os dois
nomes. As regras de segurança atuais continuam valendo: parser XML de verdade,
backup antes de gravar, arquivo malformado é pulado, UTF-8 sem BOM, escrita atômica.

**Cache do SimConnect.** Reextrai na pasta nova. A antiga fica órfã — poucos MB, não
compensa código para apagar arquivos do usuário.

### Migração no instalador

Na atualização sobre a v1.3.0, o instalador:

- apaga `{app}\2G-GPS-Cliente.exe` (`[InstallDelete]`);
- apaga os atalhos `2G GPS Cliente for MSFS` no Menu Iniciar e `2G GPS Cliente` na
  área de trabalho;
- remove o valor `2G GPS Cliente` de `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  O valor novo só é criado se a tarefa "iniciar com o Windows" estiver marcada, como hoje.

### O pingue-pongue que o mutex preservado torna inofensivo

Se o usuário abrir o exe da v1.3.0 depois de migrar, o autorreparo que já existe
nela recoloca a entrada `2G GPS Cliente` no `EXE.xml`. O MSFS passa a lançar os dois
exes — mas eles disputam o mesmo mutex, e só um sobe. Na próxima execução da v1.4.0,
a entrada legada sai de novo. O resultado é um ciclo sem efeito visível, em vez de
transmissão em dobro.

### Melhoria direcionada: `EXE.xml` no Core

A transformação do documento XML sai de `ExeXmlAutoStart` (projeto WPF, que só roda
no Windows) para uma classe pura no Core: recebe o `XDocument`, o nome atual, os
nomes legados e o caminho do exe, e devolve o documento transformado. O
`ExeXmlAutoStart` fica só com o I/O (localizar instalações, ler, gravar, backup).

Motivo: a migração da entrada legada é a parte que mais precisa de teste, e um
`EXE.xml` quebrado impede o MSFS de abrir. Hoje essa lógica não tem teste nenhum.

---

## Parte 2 — Distribuição

### Versão

**v1.4.0.** A 2.0 fica reservada para quando o controle do simulador chegar.

### Arquivos publicados no release

| Arquivo | Observação |
|---|---|
| `2G-Connector.exe` | sem versão no nome, alvo dos links permanentes |
| `2G-Connector.zip` | idem |
| `2G-Connector-Setup.exe` | idem |
| `2G-GPS-Cliente.exe`, `.zip`, `-Setup.exe` | cópias com o nome antigo, só nos releases v1.4.x |
| `SHA256SUMS.txt` | como hoje, para conferência humana |
| `update.json` | **novo** — manifesto do updater (Parte 3) |
| `update.json.sig` | **novo** — assinatura do manifesto |

As cópias com nome antigo existem para que link salvo, favorito ou página em cache
continuem baixando algo em vez de 404. Saem a partir da v1.5.0.

O corpo do release gerado pelo CI passa a listar os nomes novos.

### Site

A página `gps-connector.html` do bucket `2gpilot-com-site` passa a apontar para os
nomes novos, e o texto dela troca "GPS Cliente" por "2G Connector". A URL da página
não muda, porque há links externos para ela. A escrita no bucket de produção é feita
com confirmação explícita no momento de subir.

### Código

Commit **exclusivamente mecânico**, antes de qualquer mudança de comportamento:

- `src/TwoG.GpsClient` → `src/TwoG.Connector`
- `src/TwoG.GpsClient.Core` → `src/TwoG.Connector.Core`
- `tests/TwoG.GpsClient.Core.Tests` → `tests/TwoG.Connector.Core.Tests`
- `TwoG.GpsClient.slnx` → `TwoG.Connector.slnx`
- namespaces, `RootNamespace`, referências de projeto, caminhos no
  `.github/workflows/build.yml` e no `installer/setup.iss`
- `LogicalName` dos recursos embutidos (`TwoG.GpsClient.Native.*` →
  `TwoG.Connector.Native.*`) **junto** com as constantes que o `SimConnectRuntime` usa
  para encontrá-los. Se um mudar sem o outro, o SimConnect não carrega — e o build
  passa, porque o nome do recurso é só uma string.

O critério de aceite desse commit é: build limpo, todos os testes passando, e a saída
de publish com exatamente um arquivo. Nada além de nomes pode mudar nele, para que a
revisão das mudanças reais não fique soterrada no rename.

`CLAUDE.md`, `README.md` e `docs/` são atualizados no fim, com o comportamento novo.

---

## Parte 3 — Auto-update

### Premissas

- O primeiro updater existe a partir da v1.4.0. **Quem está na v1.3.0 atualiza
  manualmente uma última vez.**
- O app sobe minimizado junto com o MSFS e fica na bandeja a sessão inteira.
  Reiniciar o conector no meio de um voo é o piloto perdendo posição no tablet.
  **O updater nunca instala por conta própria durante a sessão.**
- O instalador é por usuário (`PrivilegesRequired=lowest`), então atualizar nunca
  pede UAC.

### O manifesto

O CI gera, a cada release de tag, um `update.json` (valores ilustrativos):

```json
{
  "version": "1.5.0",
  "files": {
    "2G-Connector.exe":       { "sha256": "…", "size": 75517030 },
    "2G-Connector-Setup.exe": { "sha256": "…", "size": 70169918 }
  }
}
```

e o assina. `update.json.sig` contém a assinatura **ECDSA P-256 sobre SHA-256** dos
bytes exatos do `update.json`, em DER, codificada em Base64.

Por que ECDSA e não Ed25519: o .NET 10 não oferece Ed25519 avulso (só dentro de
assinaturas híbridas pós-quânticas). ECDSA P-256 tem a mesma propriedade que importa
— chave privada só no CI, chave pública embutida no app — e é nativo via CNG no
Windows. Sem dependência nova no exe único e sem criptografia escrita à mão.

### Chaves

- A chave privada vive no secret `UPDATE_SIGNING_KEY` do GitHub, em PEM.
- A chave pública vai numa constante no Core, em formato SubjectPublicKeyInfo.
- A constante é uma **lista** de chaves aceitas, hoje com uma. Isso permite trocar a
  chave no futuro publicando uma versão que aceite a antiga e a nova.
- **O par é gerado pelo mantenedor**, na máquina dele, e a chave privada nunca passa
  por esta conversa nem por nenhum repositório:

  ```bash
  openssl ecparam -name prime256v1 -genkey -noout -out update-signing.pem
  openssl ec -in update-signing.pem -pubout -out update-signing.pub.pem
  gh secret set UPDATE_SIGNING_KEY < update-signing.pem
  ```

  Guardar um backup offline de `update-signing.pem`. Perder a chave obriga todo
  usuário a atualizar manualmente uma vez, para receber uma versão com chave nova.

- **Par gerado em 2026-09-11**, secret `UPDATE_SIGNING_KEY` configurado no mesmo dia.
  Chave pública a embutir no Core:

  ```
  -----BEGIN PUBLIC KEY-----
  MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEkU39e98bOMDbzZsTK1VvFMqqFum/
  HvGSzgEtgfETqNCJSdUisfjCt7BVr8WgX51D+sGc7Tz9iNdW6NOAE3Rm0Q==
  -----END PUBLIC KEY-----
  ```

- No CI, **um release de tag falha se o secret não estiver configurado**. Nunca se
  publica manifesto sem assinatura.

### Onde o app busca

`https://github.com/galenoferreira/gps_connector/releases/latest/download/update.json`
e o `.sig` ao lado. É o mesmo link permanente que o site usa: não consome a API do
GitHub, não tem limite de requisições, não precisa de token. O `latest` do GitHub já
exclui pré-releases.

A URL do feed pode ser trocada pela variável de ambiente `TWOG_UPDATE_FEED`, para
testar contra um pré-release. Isso não enfraquece nada: a confiança está na
assinatura, não na URL.

### Verificação, nesta ordem

1. A assinatura do `update.json` confere com alguma das chaves públicas embutidas.
2. A versão do manifesto é **maior** que a instalada.
3. O arquivo baixado tem o tamanho e o SHA256 declarados no manifesto.

Qualquer falha descarta o download e aparece no Diagnóstico.

A regra 2 barra um ataque específico: sem ela, quem controlasse o feed poderia servir
um manifesto antigo, legitimamente assinado, de uma versão com defeito conhecido.

Depois de validar o manifesto, o binário é baixado pela URL **com versão**
(`releases/download/v1.5.0/<arquivo>`), nunca pelo `latest`. Senão, um release
publicado durante o download misturaria o manifesto de uma versão com o binário de
outra.

### Builds que não se atualizam

O updater só roda em builds de release. O CI injeta `-p:UpdateChannel=stable` apenas
quando o build é disparado por tag `v*` (o mesmo job atende tags e branches, então a
condição fica no passo de publish); build local (versão 1.0.0 do csproj) e builds de branch
(`1.0.0-ci.N`) não têm canal e ficam com o updater desligado. Sem isso, um build de
desenvolvimento se "atualizaria" para o último release na primeira oportunidade.

### Quando verifica e baixa

- 60 s depois de abrir, e depois a cada 6 h.
- Download em segundo plano para `%LOCALAPPDATA%\2G Connector\updates\`, já
  verificado. Uma atualização baixada e verificada fica **pendente**.

### Quando instala — só nestes três momentos

1. **Ao abrir, antes de conectar ao simulador.** O MSFS lança o conector ao iniciar,
   antes de qualquer voo. Se há atualização pendente, ela é aplicada antes do
   `SimConnectService` subir. Custa alguns segundos de partida.
2. **Ao sair pela bandeja.** O piloto escolheu encerrar o conector, então a
   atualização é instalada **sem reabrir** o app no fim.
3. **No botão "Atualizar agora".** Se houver voo ativo (`Receiving`), o app confirma
   antes: "Você está em voo. O tablet fica sem posição por alguns segundos."

Nunca por timer, nunca por ociosidade.

Para não entrar em ciclo, cada versão tem no máximo duas tentativas de instalação.
Depois disso fica marcada como falha, deixa de ser tentada e aparece no Diagnóstico.

### Como instala

O tipo de instalação é detectado pela presença de um desinstalador do Inno
(`unins???.exe`) na pasta do exe.

**Instalação via instalador.** Baixa o `2G-Connector-Setup.exe` e executa com
`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /relaunchargs="<argumentos originais>"`.
O Inno já fecha o app pelo `AppMutex`. O `setup.iss` ganha uma entrada `[Run]` com
`skipifnotsilent` que reabre o conector com `{param:relaunchargs|}` — assim quem foi
lançado com `-minimized` pelo MSFS volta minimizado. No gatilho de saída o app passa
`/norelaunch`, e a entrada `[Run]` é condicionada à ausência desse parâmetro.

**Exe avulso.** O Windows permite renomear um exe em execução, só não sobrescrevê-lo.
O app:

1. renomeia o próprio exe para `2G-Connector.exe.old`;
2. move o novo para o nome original;
3. executa o novo com os argumentos originais mais `--updated` — exceto no gatilho
   de saída, em que não executa nada;
4. encerra.

O exe novo, ao receber `--updated`, **espera** o mutex ser liberado (até 10 s) em vez
de sair como segunda instância, e apaga o `.old` ao subir. O caminho do exe não muda,
então o `EXE.xml` continua certo.

**Pasta sem permissão de escrita.** Não tenta. Mostra "atualização disponível —
baixe manualmente", com o link.

Arquivos baixados por `HttpClient` não recebem a marca "da internet" (Mark of the
Web), então o SmartScreen não interrompe a instalação silenciosa.

### Interface

- Linha discreta abaixo do cabeçalho, só quando há atualização pendente:
  "v1.5.0 pronta — instala ao reiniciar", com o botão "Atualizar agora".
- Checkbox "Atualizar automaticamente" em Configurações, ligado por padrão
  (`AppSettings.AutoUpdate`). Desligado, o app não verifica nem baixa.
- No Diagnóstico: última verificação, versão mais recente vista, e o último erro
  ("assinatura inválida — atualização descartada", "sem permissão de escrita",
  "falha na instalação, 2ª tentativa").

### Onde cada parte mora

| Unidade | Projeto | Responsabilidade |
|---|---|---|
| `UpdateManifest` | Core | ler e validar o JSON |
| `UpdateSignature` | Core | verificar ECDSA contra a lista de chaves |
| `AppVersion` | Core | comparar versões SemVer, incluindo pré-release |
| `UpdatePolicy` | Core | decidir se pode instalar agora (gatilho + estado do simulador + tentativas) |
| `UpdateChecker` | Core | buscar manifesto, verificar, baixar, conferir hash, deixar pendente |
| `UpdateInstaller` | WPF | executar o instalador ou trocar o exe, e reabrir |
| `UpdateService` | WPF | orquestrar timer, gatilhos e UI |

`UpdateChecker` fica no Core pelo mesmo motivo do `FlightPlanServer`: sem WPF, roda
e é testado fora do Windows.

---

## Testes

Automatizados, no Core:

- **Rebrand:** transformação do `EXE.xml` — entrada nova criada, entrada legada
  removida, outros add-ons preservados, desregistro removendo os dois nomes; decisão
  de migração da configuração (copia só quando a nova não existe e a antiga existe).
- **Manifesto:** JSON válido, campos faltando, SHA256 malformado.
- **Versões:** ordem SemVer, pré-release menor que a versão final correspondente,
  igualdade não conta como atualização.
- **Assinatura:** par de chaves gerado no teste; assinatura válida aceita, manifesto
  alterado em um byte rejeitado, assinatura de outra chave rejeitada, Base64 inválido
  rejeitado.
- **Política:** cada gatilho contra cada estado do simulador; limite de duas
  tentativas.
- **Checker:** integração contra um servidor HTTP local, como os testes do
  `FlightPlanServer` — download completo, hash errado descartado, versão igual ou
  menor ignorada.

Sem teste automatizado, porque dependem do Windows: execução do instalador, troca do
exe, espera pelo mutex, migração do instalador (atalhos, chave `Run`).

## Verificação antes do release

Em Windows, com a v1.3.0 instalada:

1. **Atualização via instalador:** configuração preservada, `EXE.xml` só com a entrada
   nova, atalhos antigos removidos, chave `Run` migrada, uma instância só.
2. **Atualização via exe avulso:** baixar o `2G-Connector.exe` em outra pasta e
   executar — o `EXE.xml` passa a apontar para o novo caminho, sem entrada legada.
3. **Instalação nova:** nomes, pastas e dispositivo XGPS "2G Connector".
4. **Updater:** com `TWOG_UPDATE_FEED` apontando para um pré-release `v1.4.1-rc.1`,
   verificar os três gatilhos nos dois tipos de instalação, e um manifesto adulterado
   sendo rejeitado.

## Riscos

- **Antivírus.** Um exe sem assinatura Authenticode que se substitui pode disparar
  heurística. Mitigação possível depois: Authenticode.
- **Chave perdida ou vazada.** Perdida: todos atualizam manualmente uma vez. Vazada:
  publicar versão com a chave nova na lista e a antiga removida; até lá, quem tem a
  chave pode empurrar binários. O backup offline e o acesso restrito ao secret são a
  defesa.
- **O `EXE.xml` do MSFS.** A migração mexe num arquivo que, quebrado, impede o
  simulador de abrir. Todas as salvaguardas atuais continuam, e a lógica ganha testes
  que hoje não tem.
