@echo off
rem Go DwgToDxf khoi AutoCAD.
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\DwgToDxf.bundle"
if exist "%DST%" (
  rmdir /S /Q "%DST%"
  echo Da go: %DST%
) else (
  echo Chua cai DwgToDxf.
)
pause
