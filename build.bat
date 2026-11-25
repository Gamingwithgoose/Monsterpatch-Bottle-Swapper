@echo off
setlocal
cd /d "%~dp0"
set "LIB=%~1"
if not defined LIB set "LIB=%~dp0..\lib"
set "GAME=%~2"
if not defined GAME set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET SDK is required.
  exit /b 1
)
dotnet build "src\Monsterpatch_BottleSwapper_Patcher.csproj" -c Release -p:SharedLibDir="%LIB%" -p:GameDir="%GAME%"
if errorlevel 1 exit /b 1
if not exist "Release\BepInEx\patchers\Monsterpatch_BottleSwapper_Patcher.dll" exit /b 1
echo SUCCESS: Drag Release\BepInEx into Monsterpatch.
exit /b 0
