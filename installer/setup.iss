; ══════════════════════════════════════════════════════════════════════════════
;  2G Connector — instalador (Inno Setup 6)
;
;  Decisões (ver docs/pesquisa no repositório):
;   • Instalação POR USUÁRIO (PrivilegesRequired=lowest): sem UAC, e as constantes
;     {localappdata}/{userappdata} resolvem para o usuário certo — essencial para
;     a detecção do MSFS (UserCfg.opt) e para o EXE.xml, que são por usuário.
;   • O app só ENVIA UDP (broadcast XGPS): não precisa de regra de firewall
;     (política padrão do Windows libera todo tráfego de saída).
;   • O registro no EXE.xml (iniciar junto com o simulador) é feito PELO APP,
;     que se auto-repara a cada execução; o instalador chama "-register" e o
;     desinstalador, "-unregister".
;   • Publicação self-contained: nenhum pré-requisito de runtime .NET.
;
;  Compilar:  iscc setup.iss /DAppVersion=1.0.0
;             (espera a saída do publish em ..\publish)
; ══════════════════════════════════════════════════════════════════════════════

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define MyAppName "2G Connector"
#define MyAppShortName "2G Connector"
#define MyAppPublisher "2G"
#define MyAppExeName "2G-Connector.exe"

; Nomes da v1.3.0, só para limpar o que ela deixou ([InstallDelete] e [Registry]).
#define LegacyAppName "2G GPS Cliente for MSFS"
#define LegacyShortName "2G GPS Cliente"
#define LegacyExeName "2G-GPS-Cliente.exe"

[Setup]
; Mesmo AppId da v1.3.0: é o que faz o instalador ATUALIZAR em vez de instalar ao lado.
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
OutputBaseFilename=2G-Connector-Setup
SetupIconFile=..\src\TwoG.Connector\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
; Nome herdado da v1.3.0 de propósito — ver ProductIdentity.SingleInstanceMutexName.
AppMutex=Local\TwoG.GpsClient.SingleInstance

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.DetectCaption=Detecção do Simulador
brazilianportuguese.DetectDescription=Procurando instalações do Microsoft Flight Simulator
brazilianportuguese.DetectSubCaption=O instalador verificou os locais padrão do MSFS 2020/2024 (Microsoft Store e Steam) e do Prepar3D:
brazilianportuguese.DetectFound=Simuladores encontrados neste computador:
brazilianportuguese.DetectNone=Nenhuma instalação do Microsoft Flight Simulator foi encontrada.%n%nVocê pode continuar mesmo assim — o aplicativo detecta o simulador automaticamente quando ele estiver instalado e aberto.
brazilianportuguese.AutostartTask=Iniciar automaticamente com o Windows
english.DetectCaption=Simulator Detection
english.DetectDescription=Looking for Microsoft Flight Simulator installations
english.DetectSubCaption=Setup checked the standard locations for MSFS 2020/2024 (Microsoft Store and Steam) and Prepar3D:
english.DetectFound=Simulators found on this computer:
english.DetectNone=No Microsoft Flight Simulator installation was found.%n%nYou can continue anyway — the app detects the simulator automatically once it is installed and running.
english.AutostartTask=Start automatically with Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Atualização sobre a v1.3.0: o exe e os atalhos com o nome antigo. Sem isto o
; atalho velho do Menu Iniciar continuaria lá, apontando para um exe que sumiu.
Type: files; Name: "{app}\{#LegacyExeName}"
Type: files; Name: "{autoprograms}\{#LegacyAppName}.lnk"
Type: files; Name: "{autodesktop}\{#LegacyShortName}.lnk"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppShortName}"; ValueData: """{app}\{#MyAppExeName}"" -minimized"; Tasks: autostart; Flags: uninsdeletevalue
; Valor da v1.3.0 na chave Run: removido sempre, marcada ou não a tarefa nova.
; O "desabilitado" do Gerenciador de Tarefas vai para o nome novo em MigrateStartupApproved.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#LegacyShortName}"; Flags: deletevalue

[Run]
; Registra no EXE.xml já na instalação, se o usuário quer iniciar com o simulador.
; Na atualização da v1.3.0 o [InstallDelete] apaga o exe antigo, e a entrada legada
; ficaria apontando para ele até o app novo ser aberto: com a última caixa desmarcada
; ou em modo silencioso, o simulador deixaria de lançar o conector.
Filename: "{app}\{#MyAppExeName}"; Parameters: "-register"; Flags: runhidden
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
  if FileExists(ExpandConstant('{userappdata}\Lockheed Martin\Prepar3D v6\Prepar3D.cfg')) then
    Result := Result + '   •  Prepar3D v6' + #13#10;
  if FileExists(ExpandConstant('{userappdata}\Lockheed Martin\Prepar3D v5\Prepar3D.cfg')) then
    Result := Result + '   •  Prepar3D v5' + #13#10;
  if FileExists(ExpandConstant('{userappdata}\Lockheed Martin\Prepar3D v4\Prepar3D.cfg')) then
    Result := Result + '   •  Prepar3D v4' + #13#10;

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

const
  StartupApprovedRun = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';

// O Gerenciador de Tarefas guarda "desabilitado" pelo NOME do valor da chave Run, e
// valor sem registro ali conta como habilitado. Como o nome mudou, a escolha feita
// para o nome antigo vai junto: sem isto, quem desabilitou lá voltaria a ver o
// conector abrindo com o Windows. O registro antigo sai sempre, como o valor da Run.
procedure MigrateStartupApproved();
var
  State: AnsiString;
begin
  if not RegQueryBinaryValue(HKEY_CURRENT_USER, StartupApprovedRun, '{#LegacyShortName}', State) then
    Exit;
  if WizardIsTaskSelected('autostart') and
     not RegValueExists(HKEY_CURRENT_USER, StartupApprovedRun, '{#MyAppShortName}') then
    RegWriteBinaryValue(HKEY_CURRENT_USER, StartupApprovedRun, '{#MyAppShortName}', State);
  RegDeleteValue(HKEY_CURRENT_USER, StartupApprovedRun, '{#LegacyShortName}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MigrateStartupApproved();
end;
