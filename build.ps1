# Build the DSH LinkStart startup launcher.
#
# Toolchain is the .NET Framework 4.x csc.exe that ships with Windows, linking WPF
# directly. No SDK, no NuGet, no project file — the whole program is three .cs files.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1
#
# Output: out\DSH LinkStart Startup.exe  (plus the assets and config it reads at runtime)
#
# Optional: -DshPath "<full path to DeepSeek Harness.exe>" writes that path into the
# copied out\launcher.json, so a local build runs without editing anything by hand.
# The tracked launcher.json is deliberately left generic (no machine-specific path or
# coordinates in a default) — see docs/SPEC.md §5.7.

param(
    [string]$DshPath = ''
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$out      = Join-Path $root 'out'
$fx       = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc      = Join-Path $fx 'csc.exe'
$exeName  = 'DSH LinkStart Startup.exe'
$sources  = @('Launcher.cs', 'Panels.cs', 'Account.cs')
$assetSet = @('intro_dsh.mp4', 'link-start.wav', 'title.png', 'dsh.ico')

# ---- preconditions ---------------------------------------------------------
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
foreach ($n in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) {
    if (-not (Test-Path (Join-Path $fx ('WPF\' + $n)))) { throw "missing WPF reference $n" }
}
foreach ($s in $sources) {
    if (-not (Test-Path (Join-Path $root $s))) { throw "missing source file $s" }
}
$ico      = Join-Path $root 'assets\dsh.ico'
$manifest = Join-Path $root 'app.manifest'
foreach ($f in @($ico, $manifest, (Join-Path $root 'launcher.json'), (Join-Path $root 'README.md'))) {
    if (-not (Test-Path $f)) { throw "missing build input $f" }
}
foreach ($f in $assetSet) {
    if (-not (Test-Path (Join-Path $root ('assets\' + $f)))) { throw "missing asset assets\$f" }
}

New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out $exeName

# ---- compile ---------------------------------------------------------------
$argsList = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    ('/win32icon:'   + $ico),
    ('/win32manifest:' + $manifest),
    ('/out:' + $exe)
)
foreach ($n in @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll')) {
    $argsList += ('/reference:' + $n)
}
foreach ($n in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) {
    $argsList += ('/reference:' + (Join-Path $fx ('WPF\' + $n)))
}
foreach ($s in $sources) { $argsList += (Join-Path $root $s) }

Write-Output 'compiling ...'
$log = & $csc @argsList 2>&1
$code = $LASTEXITCODE
if ($log) { $log | ForEach-Object { Write-Output $_ } }

# Assert on the compiler's own words, not just the exit code: a patch that silently
# failed to land once produced a build that "succeeded" against a stale file.
$text = ($log | Out-String)
if ($text -match ': error ') { throw 'Compilation failed (see errors above)' }
if ($code -ne 0)              { throw "csc.exe exited with $code" }
if ($text -match ': warning ') { Write-Output 'WARNING: compiler warnings above - review them, they are not expected' }
if (-not (Test-Path $exe))    { throw "compiler reported success but $exe does not exist" }
if ((Get-Item $exe).Length -lt 20KB) { throw "compiled exe is suspiciously small ($((Get-Item $exe).Length) bytes)" }

# ---- runtime payload -------------------------------------------------------
$dstAssets = Join-Path $out 'assets'
New-Item -ItemType Directory -Force -Path $dstAssets | Out-Null
foreach ($f in $assetSet) {
    Copy-Item (Join-Path $root ('assets\' + $f)) (Join-Path $dstAssets $f) -Force
    if (-not (Test-Path (Join-Path $dstAssets $f))) { throw "failed to stage asset $f" }
}
Copy-Item (Join-Path $root 'launcher.json') (Join-Path $out 'launcher.json') -Force
Copy-Item (Join-Path $root 'README.md')     (Join-Path $out 'README.md')     -Force

# Optional local convenience: bake the caller's DSH path into the *output* config only.
# The tracked launcher.json stays generic so the build stays portable.
$outConfig = Join-Path $out 'launcher.json'
if ($DshPath -ne '') {
    if (-not (Test-Path $DshPath)) { throw "-DshPath not found: $DshPath" }
    $json  = Get-Content $outConfig -Raw -Encoding UTF8
    $esc   = $DshPath -replace '\\', '\\'
    $new   = [regex]::Replace($json, '("DshPath"\s*:\s*)"[^"]*"', ('${1}"' + $esc + '"'))
    if ($new -eq $json) { throw 'could not patch DshPath in the output launcher.json' }
    Set-Content -Path $outConfig -Value $new -Encoding UTF8 -NoNewline
    Write-Output "DshPath: $DshPath  (written to out\launcher.json only)"
}

# A stale geometry.json from a previous machine must never ship.
$staleGeometry = Join-Path $out 'geometry.json'
if (Test-Path $staleGeometry) { Remove-Item $staleGeometry -Force }

$size = [math]::Round((Get-Item $exe).Length / 1KB)
Write-Output ''
Write-Output "built:  $exe  ($size KB)"
Write-Output "assets: $dstAssets"
Write-Output "config: $(Join-Path $out 'launcher.json')"
