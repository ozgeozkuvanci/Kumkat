; Kumkat kurulum programı (Inno Setup 6)
; Derlemek için: build-installer.cmd  (önce self-contained yayın alır, sonra bu betiği derler)

#define AppName "Kumkat"
#define AppVersion "0.1.0"
#ifndef PublishDir
  #define PublishDir "..\publish-sc"
#endif

[Setup]
AppId={{8C3F2A51-6D2E-4B7A-9E41-3B0F7C2D9A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppComments=Tek hareketle yerel dosya dönüştürücü
; Yönetici izni istemez: kullanıcının kendi klasörüne kurulur
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=Kumkat-Kurulum-{#AppVersion}
SetupIconFile=..\src\Kumkat\Assets\kumkat.ico
UninstallDisplayIcon={app}\Kumkat.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "darija"; Description: "Kumkat'ı Fas Arapçasıyla kullan  ·  استعمل كومكات بالدارجة"; GroupDescription: "Dil / اللغة:"; Flags: unchecked
Name: "startup"; Description: "Windows açılınca Kumkat'ı başlat"; GroupDescription: "Kumkat:"
Name: "sendto"; Description: "Sağ tık › Gönder menüsüne ekle"; GroupDescription: "Kumkat:"
Name: "ctxmenu"; Description: "Klasik sağ tık menüsüne de ekle"; GroupDescription: "Kumkat:"; Flags: unchecked
Name: "ffmpeg"; Description: "FFmpeg'i kur — ses, video, PSD, WebP, AVIF için (internet gerekir)"; GroupDescription: "Yardımcı araçlar (winget ile):"
Name: "libreoffice"; Description: "LibreOffice'i kur — Word, Excel, PowerPoint için (~350 MB, yönetici onayı ister)"; GroupDescription: "Yardımcı araçlar (winget ile):"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\Kumkat.exe"; Comment: "Dosya dönüştürücü"

[Run]
Filename: "{app}\Kumkat.exe"; Parameters: "--setup{code:SetupArgs}"; StatusMsg: "Windows entegrasyonu ayarlanıyor..."; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements || (echo. & echo FFmpeg kurulamadi. Kumkat tepsi menusu ^> Araclar bolumunden tekrar deneyebilirsiniz. & pause)"; StatusMsg: "FFmpeg kuruluyor (birkaç dakika sürebilir)..."; Tasks: ffmpeg; Flags: waituntilterminated
Filename: "{cmd}"; Parameters: "/c winget install --id TheDocumentFoundation.LibreOffice -e --accept-source-agreements --accept-package-agreements || (echo. & echo LibreOffice kurulamadi. Kumkat tepsi menusu ^> Araclar bolumunden tekrar deneyebilirsiniz. & pause)"; StatusMsg: "LibreOffice kuruluyor (birkaç dakika sürebilir)..."; Tasks: libreoffice; Flags: waituntilterminated
Filename: "{app}\Kumkat.exe"; Description: "Kumkat'ı şimdi başlat"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im Kumkat.exe"; Flags: runhidden waituntilterminated; RunOnceId: "KumkatKill"
Filename: "{app}\Kumkat.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "KumkatCleanup"

[Code]
function SetupArgs(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('startup') then Result := Result + ' startup';
  if WizardIsTaskSelected('sendto') then Result := Result + ' sendto';
  if WizardIsTaskSelected('ctxmenu') then Result := Result + ' ctxmenu';
  if WizardIsTaskSelected('darija') then Result := Result + ' lang=ar' else Result := Result + ' lang=tr';
end;

// Windows arayüzü Arapça ya da bölge Fas ise Darija seçeneğini önceden işaretle
function GetUserGeoID(GeoClass: Integer): Integer;
  external 'GetUserGeoID@kernel32.dll stdcall';

var
  LangTaskInit: Boolean;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and (not LangTaskInit) then
  begin
    LangTaskInit := True;
    // LANG_ARABIC = 0x01, GEOCLASS_NATION = 16, Fas = 159
    if ((GetUILanguage and $3FF) = $01) or (GetUserGeoID(16) = 159) then
      WizardSelectTasks('darija');
  end;
end;

// Güncellemede çalışan Kumkat exe'yi kilitler: kurulumdan önce kapat
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im Kumkat.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  Result := '';
end;
