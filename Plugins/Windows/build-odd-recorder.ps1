param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$pluginDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$vsDevCmdCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat",
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\Tools\VsDevCmd.bat"
)

$vsDevCmd = $vsDevCmdCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $vsDevCmd) {
    throw "Could not find VsDevCmd.bat. Install Visual Studio Build Tools with the Desktop development with C++ workload."
}

$defines = if ($Configuration -ieq "Debug") { "/Od /Zi /DDEBUG" } else { "/O2 /DNDEBUG" }
$command = @"
call "$vsDevCmd" -arch=x64
cd /d "$pluginDir"
cl /nologo /LD /EHsc /std:c++17 $defines ODDRecorder.cpp /Fe:ODDRecorder.dll /link mfreadwrite.lib mfplat.lib mfuuid.lib mf.lib d3d11.lib dxgi.lib ole32.lib
"@

cmd /c $command
if ($LASTEXITCODE -ne 0) {
    throw "ODDRecorder native build failed with exit code $LASTEXITCODE"
}

Remove-Item -LiteralPath `
    (Join-Path $pluginDir "ODDRecorder.obj"), `
    (Join-Path $pluginDir "ODDRecorder.exp"), `
    (Join-Path $pluginDir "ODDRecorder.lib") `
    -ErrorAction SilentlyContinue
