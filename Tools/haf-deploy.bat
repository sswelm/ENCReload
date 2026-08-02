@echo off
REM ============================================================
REM  haf-deploy.bat — deploy the latest built ENCReload module to
REM  Humankind's Community folder. PURE FILE COPY (no Unity needed).
REM
REM  Run this AFTER an editor build. It copies the 4 core files
REM  (module descriptor + .manifest + .assetbundle + .assetbundle.manifest),
REM  stripping Unity .meta files AND the .assetbundle.txt — exactly what
REM  the editor's own deploy produces. Cleans any older ENCReload export first.
REM ============================================================
setlocal
set "ROOT=%~dp0..\Assets\AssetBundles\StandaloneWindows64"
set "COMM=C:\GameData\Humankind\Community"
set "GUID=cd3480e932114f8084db755ddd65f2d8"

REM newest built ENCReload.<GUID>.<version> folder (by date; excludes the raw 'ENCReload' folder)
set "SRC="
for /f "delims=" %%D in ('dir /b /ad /o-d "%ROOT%\ENCReload.%GUID%.*" 2^>nul') do if not defined SRC set "SRC=%%D"
if not defined SRC (
  echo ERROR: no built ENCReload.%GUID%.* module found in
  echo   %ROOT%
  echo Build the mod in the editor first, then re-run this.
  exit /b 1
)

echo Deploying %SRC%
for /d %%D in ("%COMM%\ENCReload.%GUID%.*") do ( echo   - removing old %%~nxD & rmdir /s /q "%%D" )
mkdir "%COMM%\%SRC%" 2>nul
for %%F in ("%ROOT%\%SRC%\*") do (
  if /i not "%%~xF"==".meta" if /i not "%%~xF"==".txt" ( copy /y "%%F" "%COMM%\%SRC%\" >nul & echo   + %%~nxF )
)
echo Done — deployed to %COMM%\%SRC%
exit /b 0
