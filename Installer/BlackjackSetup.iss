; Inno Setup script for the Windows build of Blackjack.
;
; It wraps the self-contained publish folder into one BlackjackSetup.exe,
; because "download a zip, unblock it, find the right .exe among two hundred
; DLLs" is not something you can reasonably ask a player to do.
;
; PublishDir is passed in by the build (/DPublishDir=...), so this script does
; not care where the publish output landed. It is deliberately not called
; SourceDir, which is the name of a built-in [Setup] directive.
;
; Two deliberate choices:
;
;   PrivilegesRequired=lowest installs under the user's own AppData instead of
;   Program Files. That means no UAC prompt, which matters because this build
;   is not code-signed - an unsigned installer asking for admin is exactly the
;   shape of thing people are told not to run.
;
;   The app is x64 and self-contained: it carries its own .NET runtime and its
;   own Windows App SDK, so a player needs nothing installed beforehand. That
;   is most of the download size and it is worth it.

#define AppName "Blackjack"
#define AppVersion "1.0"
#define AppExeName "BlackjackApp.Maui.exe"

[Setup]
AppId={{AE18EC62-21FD-462E-9067-32DDA4689081}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=BlackjackSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Play now"; Flags: nowait postinstall skipifsilent
