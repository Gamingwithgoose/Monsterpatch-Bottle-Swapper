@echo off
setlocal
cd /d "%~dp0"
set "MOD_LIB_DIR=%~1"
if not defined MOD_LIB_DIR set "MOD_LIB_DIR=%~dp0..\lib"
set "GAME=%~2"
if not defined GAME set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET SDK is required.
  goto :failed
)
dotnet build "src\Monsterpatch_BottleSwapper_Patcher.csproj" -c Release -p:SharedLibDir="%MOD_LIB_DIR%" -p:GameDir="%GAME%"
if errorlevel 1 goto :failed
if not exist "Release\BepInEx\patchers\Monsterpatch_BottleSwapper_Patcher.dll" goto :failed
echo SUCCESS: Drag Release\BepInEx into Monsterpatch.
pause
exit /b 0

:failed
echo.
echo BUILD FAILED. See the error messages above.
pause
exit /b 1
