# Ricostruisce tutto: strumenti, testo italiano finale, controlli automatici, cartella dei dati,
# patcher .exe e archivio da pubblicare. Usa solo il compilatore C# incluso in Windows: non installa nulla.
#   .\costruisci.ps1                      (cerca il gioco nella libreria Steam predefinita)
#   .\costruisci.ps1 -GameDir "X:\...\Disco Elysium" -Versione "2.1"
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Disco Elysium',
    [string]$Versione = '2.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$detool = Join-Path $root 'tools\bin\detool.exe'
$win = 'disco_Data\StreamingAssets\AssetBundles\Windows'
$vecchia = Join-Path $root "patch_originale_v1.1\$win"

function Passo($titolo) { Write-Host ""; Write-Host "== $titolo" -ForegroundColor Cyan }
function Esegui { & $detool @args; if ($LASTEXITCODE -ne 0) { throw "detool $($args[0]) fallito" } }

Passo 'Compilazione degli strumenti'
& (Join-Path $root 'tools\build.ps1')

# Se la traduzione e' gia' installata, i file originali del gioco sono nella sua cartella di backup
$orig = $GameDir
if (Test-Path (Join-Path $GameDir "_backup_traduzione_ITA\$win\lockits\spanish")) { $orig = Join-Path $GameDir '_backup_traduzione_ITA' }
if (-not (Test-Path (Join-Path $orig "$win\lockits\spanish"))) { throw "Disco Elysium non trovato in: $GameDir (usa -GameDir)" }
New-Item -ItemType Directory -Force (Join-Path $root 'work'), (Join-Path $root 'build') | Out-Null

Passo 'Tabella inglese / spagnolo / italiano originale (dai file del gioco)'
if ($orig -ne $GameDir) {
    # la tabella si costruisce sui file originali: se il gioco e' gia' tradotto resta valida quella esistente
    if (-not (Test-Path 'work\master_Dialogues.tsv')) { throw "Il gioco e' gia' tradotto e manca work\master_*.tsv: ripristina gli originali con il patcher e riprova." }
    Write-Host "gioco gia' tradotto: uso la tabella esistente in work\"
} else {
    Esegui master $GameDir "$vecchia\lockits\spanish" work
}

Passo 'Unione delle correzioni'
Esegui fixmerge traduzione\da_rivedere.tsv traduzione\lotti traduzione\correzioni.tsv
Esegui mkitalian work traduzione

Passo 'Controlli automatici sul testo finale'
Esegui qa work traduzione

Passo 'Immagini per il font dislessia'
Esegui mkdyslexic "$vecchia\images\spanish" "$orig\$win\images_dyslexic\spanish" build\immagini_dislessia.bundle

Passo 'Cartella dei dati'
Esegui mkpayload traduzione $Versione build\dati `
    "immagini.bundle=$win\images\spanish=$vecchia\images\spanish" `
    "immagini_dislessia.bundle=$win\images_dyslexic\spanish=build\immagini_dislessia.bundle"

Passo 'Patcher'
& (Join-Path $root 'tools\build.ps1') -Patcher

Passo 'Archivio da pubblicare'
$pub = Join-Path $root 'pubblicazione'
$stage = Join-Path $pub 'pacchetto'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage -Confirm:$false }
New-Item -ItemType Directory -Force $stage, (Join-Path $stage 'sorgenti') | Out-Null
Copy-Item 'build\DiscoElysium_Traduzione_ITA.exe' $stage
Copy-Item (Join-Path $pub 'LEGGIMI.txt') $stage
Copy-Item 'build\dati' (Join-Path $stage 'dati') -Recurse
Copy-Item 'tools\src\UnityLib.cs', 'tools\src\Loc.cs', 'tools\src\PatchCore.cs', 'tools\patcher\Patcher.cs', 'tools\patcher\app.manifest', 'build\icona.ico' (Join-Path $stage 'sorgenti')
Copy-Item (Join-Path $pub 'COME_COMPILARE.txt') (Join-Path $stage 'sorgenti')
$zip = Join-Path $pub "DiscoElysium_Traduzione_ITA_v$Versione.zip"
if (Test-Path $zip) { Remove-Item $zip -Confirm:$false }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -Recurse -Force $stage -Confirm:$false
Write-Host ("archivio: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# Variante senza .exe: gli stessi sorgenti vengono compilati sul PC dell'utente da Installa.ps1.
# Serve quando l'archivio con l'exe viene bloccato dagli antivirus (falso positivo sugli exe non firmati).
New-Item -ItemType Directory -Force $stage, (Join-Path $stage 'sorgenti') | Out-Null
$tpl = Join-Path $pub 'senza_exe'
Copy-Item (Join-Path $tpl 'Installa.cmd'), (Join-Path $tpl 'Installa.ps1'), (Join-Path $tpl 'LEGGIMI.txt') $stage
Copy-Item 'build\dati' (Join-Path $stage 'dati') -Recurse
Copy-Item 'tools\src\UnityLib.cs', 'tools\src\Loc.cs', 'tools\src\PatchCore.cs', 'tools\patcher\Patcher.cs', 'build\icona.ico' (Join-Path $stage 'sorgenti')
$zip2 = Join-Path $pub "DiscoElysium_Traduzione_ITA_v${Versione}_senza_exe.zip"
if (Test-Path $zip2) { Remove-Item $zip2 -Confirm:$false }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip2 -CompressionLevel Optimal
Remove-Item -Recurse -Force $stage -Confirm:$false
Write-Host ("archivio: {0} ({1:N1} MB)" -f $zip2, ((Get-Item $zip2).Length / 1MB))
Write-Host ""
Write-Host "Fatto." -ForegroundColor Green
