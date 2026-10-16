# Compila gli strumenti con il compilatore C# incluso in Windows (.NET Framework 4.x).
# Non richiede e non installa nulla.
#   .\tools\build.ps1            -> tools\bin\detool.exe (strumento di sviluppo)
#   .\tools\build.ps1 -Patcher   -> anche build\DiscoElysium_Traduzione_ITA.exe
# Il patcher non contiene dati: li legge dalla cartella "dati" accanto all'exe (creata da costruisci.ps1).
param([switch]$Patcher)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Split-Path -Parent $root
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "csc.exe non trovato" }
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /platform:x64 /target:exe "/out:$bin\detool.exe" /r:System.Drawing.dll $src
if ($LASTEXITCODE -ne 0) { throw "compilazione fallita" }
"OK -> $bin\detool.exe"
if ($Patcher) {
    New-Item -ItemType Directory -Force (Join-Path $proj 'build') | Out-Null
    $out = Join-Path $proj 'build\DiscoElysium_Traduzione_ITA.exe'
    # il patcher contiene solo il motore (lettura/scrittura file Unity + logica della patch), non gli strumenti di analisi
    $core = 'UnityLib.cs', 'Loc.cs', 'PatchCore.cs' | ForEach-Object { Join-Path $root "src\$_" }
    $psrc = @($core) + (Join-Path $root 'patcher\Patcher.cs')
    $icon = Join-Path $proj 'build\icona.ico'
    & "$bin\detool.exe" mkicon $icon
    if ($LASTEXITCODE -ne 0) { throw "generazione dell'icona fallita" }
    & $csc /nologo /optimize+ /platform:anycpu /target:winexe "/out:$out" "/win32manifest:$root\patcher\app.manifest" "/win32icon:$icon" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll $psrc
    if ($LASTEXITCODE -ne 0) { throw "compilazione del patcher fallita" }
    "OK -> $out ({0:N0} KB)" -f ((Get-Item $out).Length / 1KB)
}
