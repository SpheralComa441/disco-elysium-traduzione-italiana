# Confronta i nomi/dimensioni degli sprite fra gioco, vecchia patch e modalità dislessia
$work = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'work'
function Get-Sprites($file) {
    $map = [ordered]@{}
    foreach ($line in Get-Content (Join-Path $work $file)) {
        if ($line -match '^SPR (.+?)\s+rect=\(0,0,([\d.]+),([\d.]+)\)') { $map[$Matches[1].Trim()] = "$($Matches[2])x$($Matches[3])" }
    }
    $map
}
$game = Get-Sprites 'tex_game.txt'; $patch = Get-Sprites 'tex_patch.txt'; $dys = Get-Sprites 'tex_dys.txt'
"sprite gioco=$($game.Count) patch=$($patch.Count) dys=$($dys.Count)"
"nel gioco ma non nella patch: " + (($game.Keys | Where-Object { -not $patch.Contains($_) }) -join ', ')
"nella patch ma non nel gioco: " + (($patch.Keys | Where-Object { -not $game.Contains($_) }) -join ', ')
"in dys ma non nella patch: " + (($dys.Keys | Where-Object { -not $patch.Contains($_) }) -join ', ')
"--- valori del lockit Images (gioco) senza sprite nella patch:"
$lockGame = @{}
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $work 'tsv\es_Images.tsv'), [Text.Encoding]::UTF8)) {
    $c = $line.Split("`t"); $lockGame[$c[0]] = $c[2]
    if ($c[2] -and -not $patch.Contains($c[2])) { "  $($c[0]) => $($c[2])" }
}
"--- differenze lockit Images gioco vs patch:"
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $work 'tsv\it_old_Images.tsv'), [Text.Encoding]::UTF8)) {
    $c = $line.Split("`t")
    if ($lockGame[$c[0]] -ne $c[2]) { "  $($c[0]): gioco='$($lockGame[$c[0]])' patch='$($c[2])'" }
}
"--- sprite con dimensioni diverse (gioco / patch / dys):"
foreach ($n in $game.Keys) {
    $a = $game[$n]; $b = $patch[$n]; $c = $dys[$n]
    if ($a -ne $b -or $a -ne $c) { "  {0,-44} gioco={1,-10} patch={2,-10} dys={3}" -f $n, $a, $b, $c }
}
