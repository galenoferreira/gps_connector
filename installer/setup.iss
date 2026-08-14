; ══════════════════════════════════════════════════════════════════════════════
;  2G GPS Cliente for MSFS — instalador (Inno Setup 6)
;
;  Decisões (ver docs/pesquisa no repositório):
;   • Instalação POR USUÁRIO (PrivilegesRequired=lowest): sem UAC, e as constantes
;     {localappdata}/{userappdata} resolvem para o usuário certo — essencial para
;     a detecção do MSFS (UserCfg.opt) e para o EXE.xml, que são por usuário.
;   • O app só ENVIA UDP (broadcast XGPS): não precisa de regra de firewall
;     (política padrão do Windows libera todo tráfego de saída).
;   • O registro no EXE.xml (iniciar junto com o simulador) é feito PELO APP,
;     que se auto-repara a cada execução; o desinstalador chama "-unregister".
;   • Publicação self-contained: nenhum pré-requisito de runtime .NET.
;
;  Compilar:  iscc setup.iss /DAppVersion=1.0.0
;             (espera a saída do publish em ..\publish)
; ══════════════════════════════════════════════════════════════════════════════

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define MyAppName "2G GPS Cliente for MSFS"
#define MyAppShortName "2G GPS Cliente"
#define MyAppPublisher "2G"
#define MyAppExeName "2G-GPS-Cliente.exe"

[Setup]
AppId={{B23B9502-7C57-45EF-9075-D53016835238}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppShortName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\dist
; Nome SEM versão: é o alvo do link permanente /releases/latest/download/.
; A versão vai no AppVersion (visível em "Adicionar ou remover programas").
OutputBaseFilename=2G-GPS-Cliente-Setup
SetupIconFile=..\src\TwoG.GpsClient\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
AppMutex=Local\TwoG.GpsClient.SingleInstance

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.DetectCaption=Detecção do Simulador
brazilianportuguese.DetectDescription=Procurando instalações do Microsoft Flight Simulator
brazilianportuguese.DetectSubCaption=O instalador verificou os locais padrão do MSFS 2020 e MSFS 2024 (Microsoft Store e Steam):
brazilianportuguese.DetectFound=Simuladores encontrados neste computador:
brazilianportuguese.DetectNone=Nenhuma instalação do Microsoft Flight Simulator foi encontrada.%n%nVocê pode continuar mesmo assim — o aplicativo detecta o simulador automaticamente quando ele estiver instalado e aberto.
brazilianportuguese.AutostartTask=Iniciar automaticamente com o Windows
english.DetectCaption=Simulator Detection
english.DetectDescription=Looking for Microsoft Flight Simulator installations
english.DetectSubCaption=Setup checked the standard locations for MSFS 2020 and MSFS 2024 (Microsoft Store and Steam):
english.DetectFound=Simulators found on this computer:
english.DetectNone=No Microsoft Flight Simulator installation was found.%n%nYou can continue anyway — the app detects the simulator automatically once it is installed and running.
english.AutostartTask=Start automatically with Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppShortName}"; ValueData: """{app}\{#MyAppExeName}"" -minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppShortName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove nossas entradas Launch.Addon dos EXE.xml de todas as edições do MSFS.
Filename: "{app}\{#MyAppExeName}"; Parameters: "-unregister"; RunOnceId: "UnregisterExeXml"; Flags: skipifdoesntexist

[Code]
function DetectSims(): String;
begin
  Result := '';
  if FileExists(ExpandConstant('{localappdata}\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt')) then
    Result := Result + '   •  MSFS 2024 (Microsoft Store)' + #13#10;
  if FileExists(ExpandConstant('{userappdata}\Microsoft Flight Simulator 2024\UserCfg.opt')) then
    Result := Result + '   •  MSFS 2024 (Steam)' + #13#10;
  if FileExists(ExpandConstant('{localappdata}\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\UserCfg.opt')) then
    Result := Result + '   •  MSFS 2020 (Microsoft Store)' + #13#10;
  if FileExists(ExpandConstant('{userappdata}\Microsoft Flight Simulator\UserCfg.opt')) then
    Result := Result + '   •  MSFS 2020 (Steam)' + #13#10;

  if Result <> '' then
    Result := ExpandConstant('{cm:DetectFound}') + #13#10 + #13#10 + Result
  else
    Result := ExpandConstant('{cm:DetectNone}');
end;

procedure InitializeWizard();
begin
  CreateOutputMsgMemoPage(wpWelcome,
    ExpandConstant('{cm:DetectCaption}'),
    ExpandConstant('{cm:DetectDescription}'),
    ExpandConstant('{cm:DetectSubCaption}'),
    DetectSims());
end;
