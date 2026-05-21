@echo off
setlocal
set "VSDEV=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
if not exist "%VSDEV%" (
  echo Visual Studio 2022 Community VsDevCmd.bat not found at %VSDEV%
  exit /b 1
)
call "%VSDEV%" -arch=x64 >nul
cd /d "%~dp0"
cl /nologo /LD /EHsc /std:c++17 /O2 /DNDEBUG ODDRecorder.cpp /Fe:ODDRecorder.dll /link mfreadwrite.lib mfplat.lib mfuuid.lib mf.lib d3d11.lib dxgi.lib ole32.lib
set CL_RC=%ERRORLEVEL%
del /q ODDRecorder.obj ODDRecorder.exp ODDRecorder.lib >nul 2>nul
exit /b %CL_RC%
