; Set-up program for Password Generator (Inno Setup 6).
; Installs for the current user only, so it never asks for administrator rights
; and the app can update itself later.
;
; Built by the pipeline with:
;   ISCC /DMyAppVersion=1.0.0 /DMyDisplayVersion=1.0.0-beta001 /DMyPackageName=PasswordGenerator_1.0.0-beta001
;        /DSourceDir=<folder with PasswordGenerator.exe> /DOutputDir=<folder> PasswordGenerator.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyDisplayVersion
  #define MyDisplayVersion MyAppVersion
#endif
#ifndef MyPackageName
  #define MyPackageName "PasswordGenerator_" + MyDisplayVersion
#endif
#ifndef SourceDir
  #define SourceDir "..\..\out\portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\out\installer"
#endif

[Setup]
AppId={{34BF2B5F-7FF2-4E0C-A0DF-1D2D87C01C74}
AppName=Password Generator
AppVersion={#MyDisplayVersion}
AppVerName=Password Generator {#MyDisplayVersion}
AppPublisher=Krishna Bhunia
AppPublisherURL=https://github.com/krishnabhunia/password_generator
AppSupportURL=https://github.com/krishnabhunia/password_generator/issues
AppUpdatesURL=https://github.com/krishnabhunia/password_generator/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoProductTextVersion={#MyDisplayVersion}
DefaultDirName={localappdata}\Programs\Password Generator
DefaultGroupName=Password Generator
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#MyPackageName}
SetupIconFile=..\..\common\src\PasswordGenerator.App\Assets\app.ico
UninstallDisplayIcon={app}\PasswordGenerator.exe
UninstallDisplayName=Password Generator
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\PasswordGenerator.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Password Generator"; Filename: "{app}\PasswordGenerator.exe"
Name: "{autodesktop}\Password Generator"; Filename: "{app}\PasswordGenerator.exe"; Tasks: desktopicon

[Run]
; After a normal install: the usual "start now" tick box.
Filename: "{app}\PasswordGenerator.exe"; Description: "{cm:LaunchProgram,Password Generator}"; Flags: nowait postinstall skipifsilent
; After an in-app update (silent, started with /relaunch=1): open the app again.
Filename: "{app}\PasswordGenerator.exe"; Flags: nowait; Check: WantsRelaunch

[Code]
function WantsRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;
