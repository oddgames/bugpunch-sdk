param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$pluginDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# vcvars64.bat, not VsDevCmd.bat -arch=x64: VsDevCmd shells out to vswhere and,
# when that isn't resolvable, leaves the environment without cl.exe and exits 0
# — the build then "succeeds" without producing a DLL.
$vcvarsCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvars64.bat"
)

$vcvars = $vcvarsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $vcvars) {
    throw "Could not find vcvars64.bat. Install Visual Studio Build Tools with the Desktop development with C++ workload."
}

$dllPath = Join-Path $pluginDir "ODDRecorder.dll"

# A running Unity editor that has used the editor recorder keeps the DLL
# mapped, and the linker's LNK1104 on its own OUTPUT means cant open for
# write - but Windows still allows RENAMING a mapped DLL. Move the locked
# file into TEMP (same volume, so the rename is legal while mapped; and
# outside package/ so preflight's missing-.meta gate never sees it); sweep
# stale moves from earlier builds (deletable once no process maps them).
Get-ChildItem -LiteralPath ([System.IO.Path]::GetTempPath()) -Filter "ODDRecorder.dll.stale-*" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $dllPath) {
    try {
        $probe = [System.IO.File]::Open($dllPath, 'Open', 'ReadWrite', 'None')
        $probe.Close()
    } catch {
        $stale = Join-Path ([System.IO.Path]::GetTempPath()) "ODDRecorder.dll.stale-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
        Write-Host "ODDRecorder.dll is locked (Unity editor?) - moving aside to $stale"
        Move-Item -LiteralPath $dllPath -Destination $stale -Force
    }
}

$before = if (Test-Path -LiteralPath $dllPath) { (Get-Item -LiteralPath $dllPath).LastWriteTimeUtc } else { [datetime]::MinValue }

$defines = if ($Configuration -ieq "Debug") { "/Od /Zi /DDEBUG" } else { "/O2 /DNDEBUG" }
# Via a real .bat file: `cmd /c` with a multi-line string keeps only the first
# line, so vcvars ran and the compile silently never did.
$script = @"
@echo off
call "$vcvars" >nul
cd /d "$pluginDir"
cl /nologo /LD /EHsc /std:c++17 $defines ODDRecorder.cpp /Fe:ODDRecorder.dll /link mfreadwrite.lib mfplat.lib mfuuid.lib mf.lib d3d11.lib dxgi.lib ole32.lib
exit /b %ERRORLEVEL%
"@

$scriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "build-odd-recorder-$PID.bat"
Set-Content -LiteralPath $scriptPath -Value $script -Encoding ASCII
try {
    cmd /c "`"$scriptPath`""
    if ($LASTEXITCODE -ne 0) {
        throw "ODDRecorder native build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
}

$after = if (Test-Path -LiteralPath $dllPath) { (Get-Item -LiteralPath $dllPath).LastWriteTimeUtc } else { [datetime]::MinValue }
if ($after -le $before) {
    throw "ODDRecorder native build reported success but ODDRecorder.dll was not rewritten."
}

Remove-Item -LiteralPath `
    (Join-Path $pluginDir "ODDRecorder.obj"), `
    (Join-Path $pluginDir "ODDRecorder.exp"), `
    (Join-Path $pluginDir "ODDRecorder.lib") `
    -ErrorAction SilentlyContinue
