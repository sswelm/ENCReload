@echo off
REM ============================================================
REM  HAF headless CLI wrapper (Unity batch mode).
REM  Drives the editor's bake/clean functions with no GUI.
REM
REM  IMPORTANT: CLOSE the Unity editor first — a project can't be
REM  open twice. Batch mode does a full import, so give it a minute.
REM
REM  Point UNITY at your 2021.3.1f1 editor (Unity Hub > Installs >
REM  the 2021.3.1f1 gear > Show in Explorer > ...\Editor\Unity.exe),
REM  either by editing the line below or setting the UNITY env var.
REM
REM  Usage:
REM    haf rebuild ^<resourceName^> [-fresh]   re-bake one model (-fresh forces a full re-slim)
REM    haf rebuild-all                        re-bake every model with a source file
REM    haf build                              FULL mod build + deploy (the Mercury Mod Editor build, headless)
REM    haf clean                              remove the old ENCReload Community export
REM  (Deploy-only, no Unity: Tools\haf-deploy.bat)
REM ============================================================
setlocal
if "%UNITY%"=="" set "UNITY=C:\Program Files\Unity\Hub\Editor\2021.3.1f1\Editor\Unity.exe"
set "PROJECT=%~dp0.."

if not exist "%UNITY%" (
  echo ERROR: Unity not found at "%UNITY%".
  echo Set the UNITY env var to your 2021.3.1f1 Unity.exe and retry.
  exit /b 1
)

if /I "%1"=="clean" (
  "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -logFile - -executeMethod HAF.Cli.CleanExport
  exit /b %errorlevel%
)
if /I "%1"=="rebuild-all" (
  "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -logFile - -executeMethod HAF.Cli.RebuildModel -all
  exit /b %errorlevel%
)
if /I "%1"=="build" (
  "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -logFile - -executeMethod HAF.Cli.BuildMod
  exit /b %errorlevel%
)
if /I "%1"=="rebuild" (
  if "%2"=="" ( echo Usage: haf rebuild ^<resourceName^> [-fresh] & exit /b 1 )
  "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -logFile - -executeMethod HAF.Cli.RebuildModel -model %2 %3
  exit /b %errorlevel%
)

echo Usage: haf rebuild ^<resourceName^> [-fresh]  ^|  haf rebuild-all  ^|  haf build  ^|  haf clean
exit /b 1
