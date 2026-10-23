@echo off
rem Kumkat'i derler: publish\Kumkat.exe (tek dosya, .NET 10 Desktop Runtime gerektirir)
setlocal
cd /d "%~dp0"
rem Calisan Kumkat exe'yi kilitler; yenisini yazabilmek icin once kapat
tasklist /fi "imagename eq Kumkat.exe" | find /i "Kumkat.exe" >nul && (
  echo Calisan Kumkat kapatiliyor...
  taskkill /im Kumkat.exe /f >nul
  timeout /t 1 /nobreak >nul
)
dotnet publish src\Kumkat\Kumkat.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if errorlevel 1 (
  echo.
  echo Derleme basarisiz.
  pause
  exit /b 1
)
echo.
echo Hazir: %CD%\publish\Kumkat.exe
echo Calistirmak icin: start "" "%CD%\publish\Kumkat.exe"
pause
