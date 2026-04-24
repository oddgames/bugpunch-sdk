# Build the Android AAR and copy it into the UPM package.
#
# Runs a standalone Gradle build of android-src/bugpunch and drops the
# release AAR at package/Plugins/Android/BugpunchPlugin.aar where
# Unity picks it up as a prebuilt plugin \u2014 no Java/NDK work happens in
# downstream game builds.
#
# Requires on PATH or via env:
#   * JDK 17            (AGP 8.x requirement)
#   * ANDROID_HOME      pointing at a platform-34 + build-tools-34 install
#   * ANDROID_NDK_ROOT  pointing at NDK 27.2.x
#
# First run: the gradle wrapper binaries aren't committed. If gradle/wrapper/
# is missing this script regenerates it via `gradle wrapper` (needs gradle 8.7+
# on PATH). Subsequent runs use the wrapper and need nothing else.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$androidSrc = Join-Path $root 'android-src'
$wrapperJar = Join-Path $androidSrc 'gradle/wrapper/gradle-wrapper.jar'
$pluginDir = Join-Path $root 'package/Plugins/Android'
$aarOut = Join-Path $pluginDir 'BugpunchPlugin.aar'
$aarMeta = "$aarOut.meta"
$androidUserHome = Join-Path $root '.android-user'

# Keep Android Gradle Plugin analytics / prefs out of the OS user profile so
# sandboxed and CI builds can run with only workspace write access.
New-Item -ItemType Directory -Force -Path $androidUserHome | Out-Null
if (-not $env:ANDROID_USER_HOME) { $env:ANDROID_USER_HOME = $androidUserHome }
if (-not $env:ANDROID_PREFS_ROOT) { $env:ANDROID_PREFS_ROOT = $androidUserHome }

Push-Location $androidSrc
try {
    if (-not (Test-Path $wrapperJar)) {
        Write-Host '[build-android] regenerating gradle wrapper (first run)'
        gradle wrapper --gradle-version 8.10.2 --distribution-type bin
    }

    $gradlewName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'gradlew.bat' } else { 'gradlew' }
    $gradlew = Join-Path $androidSrc $gradlewName
    Write-Host "[build-android] $gradlew :bugpunch:assembleRelease"
    & $gradlew ':bugpunch:assembleRelease' --no-daemon
    if ($LASTEXITCODE -ne 0) { throw "gradle assembleRelease failed ($LASTEXITCODE)" }

    $aar = Join-Path $androidSrc 'bugpunch/build/outputs/aar/bugpunch-release.aar'
    if (-not (Test-Path $aar)) { throw "expected AAR not produced: $aar" }

    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item $aar $aarOut -Force
    Write-Host "[build-android] copied -> $aarOut"

    if (-not (Test-Path $aarMeta)) {
        $guid = [guid]::NewGuid().ToString('N').Substring(0, 32)
        $metaTemplate = Join-Path $root 'android-src/BugpunchPlugin.aar.meta.template'
        if (-not (Test-Path $metaTemplate)) {
            throw "missing $metaTemplate \u2014 can't generate .aar.meta"
        }
        (Get-Content $metaTemplate -Raw) -replace '__GUID__', $guid | Set-Content $aarMeta -NoNewline
        Write-Host "[build-android] wrote .aar.meta (guid=$guid)"
    }
} finally {
    Pop-Location
}

Write-Host '[build-android] done.'
