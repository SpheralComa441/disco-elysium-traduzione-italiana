# Collaudo senza interfaccia su una COPIA dei file originali: installa, confronta il risultato con i file
# tradotti gia' presenti nel gioco (se la stessa versione e' installata), poi ripristina e controlla.
param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Disco Elysium')
$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $proj 'build\DiscoElysium_Traduzione_ITA.exe'
$test = Join-Path $proj 'work\gioco_prova'
$log = Join-Path $proj 'work\patcher_test.log'
$rels = 'disco_Data\resources.assets',
        'disco_Data\StreamingAssets\AssetBundles\Windows\lockits\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images_dyslexic\spanish'
$backup = Join-Path $GameDir '_backup_traduzione_ITA'
$installed = Test-Path $backup
$src = $GameDir; if ($installed) { $src = $backup }
if (Test-Path $test) { Remove-Item -Recurse -Force $test -Confirm:$false }
foreach ($r in $rels) { $d = Join-Path $test $r; New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null; Copy-Item (Join-Path $src $r) $d }
try {
    $p = Start-Process -FilePath $exe -ArgumentList '/installa', '/cartella', "`"$test`"", '/log', "`"$log`"" -Wait -PassThru
    "installazione: codice $($p.ExitCode)"
    Get-Content $log -Encoding UTF8 | Select-Object -Skip 1
    # ogni file previsto deve essere cambiato, e non devono comparire file in posti imprevisti
    foreach ($r in $rels) {
        $changed = (Get-FileHash (Join-Path $test $r)).Hash -ne (Get-FileHash (Join-Path $src $r)).Hash
        if (-not $changed) { "  ERRORE: file non modificato: $r" }
    }
    $root = (Resolve-Path $test).Path
    $extra = Get-ChildItem -Recurse -File $test | ForEach-Object { $_.FullName.Substring($root.Length + 1) } |
        Where-Object { $_ -notin $rels -and $_ -notlike '_backup_traduzione_ITA*' }
    if ($extra) { "  ERRORE: file imprevisti: " + ($extra -join ', ') } else { "  nessun file fuori posto" }
    if ($installed) {
        foreach ($r in $rels) {
            $same = (Get-FileHash (Join-Path $test $r)).Hash -eq (Get-FileHash (Join-Path $GameDir $r)).Hash
            "  identico al file tradotto nel gioco: $same  ($r)"
        }
    }
    $p = Start-Process -FilePath $exe -ArgumentList '/rimuovi', '/cartella', "`"$test`"", '/log', "`"$log`"" -Wait -PassThru
    "ripristino: codice $($p.ExitCode)"
    $ok = $true
    foreach ($r in $rels) { if ((Get-FileHash (Join-Path $test $r)).Hash -ne (Get-FileHash (Join-Path $src $r)).Hash) { $ok = $false } }
    "file identici agli originali dopo il ripristino: $ok"
}
finally { Remove-Item -Recurse -Force $test -Confirm:$false }
