@echo off
rem Kumkat kurulum dosyasini uretir: dist\Kumkat-Kurulum-x.y.z.exe
rem Aliciya .NET gerekmez (self-contained). Inno Setup 6 gerekir: winget install JRSoftware.InnoSetup
setlocal
cd /d "%~dp0"

set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo Inno Setup 6 bulunamadi. Kurmak icin:
  echo     winget install --id JRSoftware.InnoSetup -e
  echo sonra bu dosyayi tekrar calistirin.
  pause
  exit /b 1
)

tasklist /fi "imagename eq Kumkat.exe" | find /i "Kumkat.exe" >nul && (
  echo Calisan Kumkat kapatiliyor...
  taskkill /im Kumkat.exe /f >nul
  timeout /t 1 /nobreak >nul
)

echo [1/2] Uygulama derleniyor (self-contained)...
if exist publish-sc rmdir /s /q publish-sc
dotnet publish src\Kumkat\Kumkat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish-sc
if errorlevel 1 goto :fail

echo [2/2] Kurulum dosyasi olusturuluyor...
"%ISCC%" /Qp installer\Kumkat.iss
if errorlevel 1 goto :fail
"%ISCC%" /Qp /DARABIC installer\Kumkat.iss
if errorlevel 1 goto :fail

echo.
echo Hazir:
dir /b dist\*.exe
start "" explorer.exe "%CD%\dist"
pause
exit /b 0

:fail
echo.
echo Basarisiz.
pause
exit /b 1
