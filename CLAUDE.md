# 2G GPS Cliente for MSFS

Conector Windows (WPF, .NET 10, x64) que lê posição do Microsoft Flight Simulator
2020/2024 via SimConnect e transmite no protocolo XGPS (UDP broadcast, porta 49002)
para EFBs — 2G Pilot, ForeFlight, SkyDemon etc.

## Estrutura

- `src/TwoG.GpsClient/` — app WPF (namespace `TwoG.GpsClient`, exe `2G-GPS-Cliente.exe`)
  - `Services/SimConnectService.cs` — conexão/retry SimConnect, normaliza unidades para `GpsFix`
  - `Services/XgpsBroadcaster.cs` — sentenças XGPS/XATT via UDP (broadcast + unicast opcional)
  - `ViewModels/MainViewModel.cs` — UI orientada por polling (DispatcherTimer 250 ms)
  - `Services/SimConnectRuntime.cs` — extrai as DLLs do SimConnect (recursos embutidos)
    para `%LOCALAPPDATA%` e as carrega; chamar `Ensure()` ANTES de tocar tipos do SimConnect
- `installer/setup.iss` — instalador Inno Setup opcional (detecta MSFS, atalhos, desinstalador)
- `.github/workflows/build.yml` — build + instalador no windows-latest

## Regras importantes

- Sentenças XGPS/XATT usam SEMPRE `CultureInfo.InvariantCulture` (ponto decimal).
- `GpsFix` já é normalizado: metros MSL, m/s, pitch positivo = nariz p/ cima,
  roll positivo = asa direita — a conversão de sinais do MSFS acontece SÓ no SimConnectService.
- Build local em macOS/Linux exige `EnableWindowsTargeting` (já no csproj): `dotnet build` compila,
  mas só roda no Windows.
- Taxas padrão: 5 Hz para XGPS e XATT (decisão do produto; configurável na UI).
- **Entrega é UM único .exe** (`PublishSingleFile` no csproj, RID fixo `win-x64`).
  As DLLs do SimConnect NÃO podem entrar no bundle (mixed-mode C++/CLI não carrega
  da memória) — são recursos embutidos extraídos pelo `SimConnectRuntime`.
  Nunca adicionar arquivos soltos à saída de publish: o CI falha se houver mais de um.
- Neste volume (exFAT) o macOS cria arquivos `._*` que quebram o build; o
  `Directory.Build.targets` os remove dos globs, mas passe sempre o caminho do
  `.csproj` explicitamente nos comandos `dotnet` (a busca por pasta ainda os enxerga).
