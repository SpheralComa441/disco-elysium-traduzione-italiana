# Disco Elysium - The Final Cut: traduzione italiana.
#
# Questo script NON contiene programmi gia' compilati. Legge il codice C# che trovi nella
# cartella "sorgenti", lo compila sul momento con il compilatore incluso in Windows
# (.NET Framework) e apre la finestra di installazione.
#
# Il programma legge la traduzione dalla cartella "dati", modifica quattro file nella cartella
# del gioco dopo averne fatto una copia, e non si collega a Internet.
#
# Uso senza finestra:  Installa.ps1 /installa [/cartella "percorso"] [/log file.txt]   (oppure /rimuovi)

$ErrorActionPreference = 'Stop'
$qui = Split-Path -Parent $MyInvocation.MyCommand.Path
$sorgenti = Get-ChildItem (Join-Path $qui 'sorgenti') -Filter *.cs | ForEach-Object { $_.FullName }
if (-not $sorgenti) { Write-Host "Cartella 'sorgenti' non trovata: estrai tutto l'archivio prima di avviare."; exit 1 }

Write-Host 'Preparazione del programma di installazione...'
Add-Type -Path $sorgenti -ReferencedAssemblies System.Windows.Forms, System.Drawing -IgnoreWarnings

[DE.Program]::BaseDir = $qui
Write-Host 'Fatto. Usa la finestra che si e'' aperta; questa si chiudera'' da sola.'
exit [DE.Program]::Main([string[]]$args)
