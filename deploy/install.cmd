@echo off
rem Cai DwgToDxf vao AutoCAD: chep DwgToDxf.bundle vao thu muc ApplicationPlugins cua nguoi dung.
rem AutoCAD tu nap moi lan khoi dong, khong can NETLOAD, khong can TRUSTEDPATHS.
setlocal
set "SRC=%~dp0DwgToDxf.bundle"
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\DwgToDxf.bundle"
if not exist "%SRC%\Contents\DwgToDxf.dll" (
  echo Khong thay "%SRC%\Contents\DwgToDxf.dll". Hay chay file nay tu thu muc da giai nen.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq acad.exe" | find /I "acad.exe" >nul && (
  echo Hay dong AutoCAD truoc khi cai.
  pause
  exit /b 1
)
if exist "%DST%" rmdir /S /Q "%DST%"
xcopy "%SRC%" "%DST%\" /E /I /Y >nul
if errorlevel 1 (
  echo Cai dat loi.
  pause
  exit /b 1
)
echo Da cai vao: %DST%
echo Mo AutoCAD 2023 va go DWG2DXF.
pause
