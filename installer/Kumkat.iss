; Kumkat kurulum programı (Inno Setup 6)
; Derlemek için: build-installer.cmd  (önce self-contained yayın alır, sonra bu betiği derler)
;
; İki ayrı kurulum dosyası üretir:
;   ISCC Kumkat.iss              -> Kumkat-Kurulum-x.y.z.exe   (Türkçe sihirbaz, Darija seçeneği)
;   ISCC /DARABIC Kumkat.iss     -> Kumkat-Setup-Arabic-x.y.z.exe (Arapça sihirbaz, Kumkat Darija açılır)

#define AppName "Kumkat"
#define AppVersion "0.1.0"
#ifndef PublishDir
  #define PublishDir "..\publish-sc"
#endif

; Dile göre metinler
#ifdef ARABIC
  #define IconComment "مبدّل الفيشيات"
  #define MsgSetup "كنوجد كومكات ف ويندوز..."
  #define MsgFfmpeg "كنركّب FFmpeg (يقدر ياخد شي دقايق)..."
  #define MsgLibre "كنركّب LibreOffice (يقدر ياخد شي دقايق)..."
  #define MsgLaunch "شعّل كومكات دابا"
  ; konsol penceresi Arapça harfleri gösteremeyebilir: İngilizce
  #define EchoFail(str Tool) Tool + " could not be installed. Try again from the Kumkat tray menu ^> Tools."
#else
  #define IconComment "Dosya dönüştürücü · مبدّل الفيشيات"
  #define MsgSetup "Windows entegrasyonu ayarlanıyor..."
  #define MsgFfmpeg "FFmpeg kuruluyor (birkaç dakika sürebilir)..."
  #define MsgLibre "LibreOffice kuruluyor (birkaç dakika sürebilir)..."
  #define MsgLaunch "Kumkat'ı şimdi başlat"
  #define EchoFail(str Tool) Tool + " kurulamadi. Kumkat tepsi menusu ^> Araclar bolumunden tekrar deneyebilirsiniz."
#endif

[Setup]
AppId={{8C3F2A51-6D2E-4B7A-9E41-3B0F7C2D9A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
#ifdef ARABIC
AppComments=مبدّل الفيشيات بحركة وحدة، كيخدم بلا أنترنيت
#else
AppComments=Tek hareketle yerel dosya dönüştürücü
#endif
; Yönetici izni istemez: kullanıcının kendi klasörüne kurulur
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
#ifdef ARABIC
OutputBaseFilename=Kumkat-Setup-Arabic-{#AppVersion}
#else
OutputBaseFilename=Kumkat-Kurulum-{#AppVersion}
#endif
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
#ifdef ARABIC
; Arapça sihirbaz (Inno Setup'ın resmi Arapça çevirisi, installer/Arabic.isl), sağdan sola
Name: "ar"; MessagesFile: "Arabic.isl"
#else
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
#endif

[Tasks]
#ifdef ARABIC
Name: "startup"; Description: "شعّل كومكات أوتوماتيكيا ملي يتشعل البيسي"; GroupDescription: "كومكات:"
Name: "sendto"; Description: "زيد كومكات لـ كليك ليمن › صيفط لـ"; GroupDescription: "كومكات:"
Name: "ctxmenu"; Description: "زيدو حتى للمينو الكلاسيكي ديال الكليك ليمن"; GroupDescription: "كومكات:"; Flags: unchecked
Name: "ffmpeg"; Description: "ركّب FFmpeg — للصوت والفيديو وWebP وAVIF (خاص الأنترنيت)"; GroupDescription: "أدوات مساعدة (winget):"
Name: "libreoffice"; Description: "ركّب LibreOffice — لـ Word و Excel و PowerPoint (~350 ميغا)"; GroupDescription: "أدوات مساعدة (winget):"; Flags: unchecked
#else
Name: "darija"; Description: "Kumkat'ı Fas Arapçasıyla kullan  ·  استعمل كومكات بالدارجة"; GroupDescription: "Dil / اللغة:"; Flags: unchecked
Name: "startup"; Description: "Bilgisayar açılınca Kumkat'ı otomatik başlat  ·  شعّل كومكات أوتوماتيكيا ملي يتشعل البيسي"; GroupDescription: "Kumkat:"
Name: "sendto"; Description: "Sağ tık › Gönder menüsüne ekle  ·  زيد كومكات لـ كليك ليمن › صيفط لـ"; GroupDescription: "Kumkat:"
Name: "ctxmenu"; Description: "Klasik sağ tık menüsüne de ekle  ·  زيدو حتى للمينو الكلاسيكي ديال الكليك ليمن"; GroupDescription: "Kumkat:"; Flags: unchecked
Name: "ffmpeg"; Description: "FFmpeg'i kur — ses, video, WebP, AVIF için  ·  ركّب FFmpeg — للصوت والفيديو (خاص الأنترنيت)"; GroupDescription: "Yardımcı araçlar · أدوات مساعدة (winget):"
Name: "libreoffice"; Description: "LibreOffice'i kur — Word, Excel, PowerPoint için (~350 MB)  ·  ركّب LibreOffice — لـ Word و Excel و PowerPoint"; GroupDescription: "Yardımcı araçlar · أدوات مساعدة (winget):"; Flags: unchecked
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\Kumkat.exe"; Comment: "{#IconComment}"

[Run]
Filename: "{app}\Kumkat.exe"; Parameters: "--setup{code:SetupArgs}"; StatusMsg: "{#MsgSetup}"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements || (echo. & echo {#EchoFail("FFmpeg")} & pause)"; StatusMsg: "{#MsgFfmpeg}"; Tasks: ffmpeg; Flags: waituntilterminated
Filename: "{cmd}"; Parameters: "/c winget install --id TheDocumentFoundation.LibreOffice -e --accept-source-agreements --accept-package-agreements || (echo. & echo {#EchoFail("LibreOffice")} & pause)"; StatusMsg: "{#MsgLibre}"; Tasks: libreoffice; Flags: waituntilterminated
Filename: "{app}\Kumkat.exe"; Description: "{#MsgLaunch}"; Flags: postinstall nowait skipifsilent

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
#ifdef ARABIC
  Result := Result + ' lang=ar';
#else
  if WizardIsTaskSelected('darija') then Result := Result + ' lang=ar' else Result := Result + ' lang=tr';
#endif
end;

#ifndef ARABIC
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
#endif

// Güncellemede çalışan Kumkat exe'yi kilitler: kurulumdan önce kapat
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im Kumkat.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  Result := '';
end;
