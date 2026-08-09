# 2G GPS Cliente for MSFS

Conector Windows (WPF, .NET 10, x64) que lê posição do Microsoft Flight Simulator
2020/2024 via SimConnect e transmite no protocolo XGPS (UDP broadcast, porta 49002)
para EFBs — 2G Pilot, ForeFlight, SkyDemon etc.

## Estrutura

- `src/TwoG.GpsClient/` — app WPF (namespace `TwoG.GpsClient`, exe `2G-GPS-Cliente.exe`)
  - `Services/SimConnectService.cs` — conexão/retry SimConnect, normaliza unidades para `GpsFix`
  - `Services/XgpsBroadcaster.cs` — sentenças XGPS/XATT via UDP (broadcast + unicast opcional)
  - `ViewModels/MainViewModel.cs` — UI orientada por polling (DispatcherTimer 250 ms)
- `installer/setup.iss` — instalador Inno Setup (detecta MSFS, opção de autostart via EXE.xml)
- `.github/workflows/build.yml` — build + instalador no windows-latest

## Regras importantes

- Sentenças XGPS/XATT usam SEMPRE `CultureInfo.InvariantCulture` (ponto decimal).
- `GpsFix` já é normalizado: metros MSL, m/s, pitch positivo = nariz p/ cima,
  roll positivo = asa direita — a conversão de sinais do MSFS acontece SÓ no SimConnectService.
- Build local em macOS/Linux exige `EnableWindowsTargeting` (já no csproj): `dotnet build` compila,
  mas só roda no Windows.
- Taxas padrão: 5 Hz para XGPS e XATT (decisão do produto; configurável na UI).
