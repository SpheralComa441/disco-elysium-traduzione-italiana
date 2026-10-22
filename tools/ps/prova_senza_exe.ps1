# Collaudo della variante senza .exe: estrae l'archivio in una cartella nuova (come farebbe un utente),
# poi su una COPIA dei file originali del gioco prova Installa.cmd in modalita' silenziosa e l'interfaccia grafica.
# -Dati 'Disco Elysium_Data' rinomina la cartella dei dati nella copia, come nella versione Epic.
param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Disco Elysium', [string]$Versione = '2.0.1', [string]$Dati = 'disco_Data')
$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$zip = Join-Path $proj "pubblicazione\DiscoElysium_Traduzione_ITA_v${Versione}_senza_exe.zip"
$pkg = Join-Path $proj 'work\pacchetto_prova'
$test = Join-Path $proj 'work\gioco_prova'
$log = Join-Path $proj 'work\patcher_test.log'
$rels = 'disco_Data\resources.assets',
        'disco_Data\StreamingAssets\AssetBundles\Windows\lockits\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images_dyslexic\spanish'
function T($r) { Join-Path $test ($r -replace '^disco_Data', $Dati) }
$backup = Join-Path $GameDir '_backup_traduzione_ITA'
$installed = Test-Path $backup
$src = $GameDir; if ($installed) { $src = $backup }

foreach ($d in $pkg, $test) { if (Test-Path $d) { Remove-Item -Recurse -Force $d -Confirm:$false } }
Expand-Archive -Path $zip -DestinationPath $pkg
"contenuto estratto: " + ((Get-ChildItem $pkg | ForEach-Object { $_.Name }) -join ', ')
"file .exe o .dll nell'archivio: " + @(Get-ChildItem -Recurse -File $pkg | Where-Object { $_.Extension -in '.exe', '.dll' }).Count
foreach ($r in $rels) { $d = T $r; New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null; Copy-Item (Join-Path $src $r) $d }
function Hash-Test { ($rels | ForEach-Object { (Get-FileHash (T $_)).Hash }) -join ',' }
$orig = Hash-Test
$cmd = Join-Path $pkg 'Installa.cmd'

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W2 {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    static string Text(IntPtr h) { StringBuilder s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    static string Class(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    // finestra visibile con quel titolo la cui classe inizia per il prefisso dato
    public static IntPtr Top(string title, string classPrefix) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) { if (IsWindowVisible(h) && Text(h).StartsWith(title) && Class(h).StartsWith(classPrefix)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    public static IntPtr Child(IntPtr parent, string text) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { if (Text(h) == text) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    public static string ChildTexts(IntPtr parent) {
        List<string> t = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { string s = Text(h); if (s.Length > 0) t.Add(s); return true; }, IntPtr.Zero);
        return string.Join(" | ", t.ToArray());
    }
    public static void Space(IntPtr h) { PostMessage(h, 0x100, (IntPtr)0x20, IntPtr.Zero); PostMessage(h, 0x101, (IntPtr)0x20, IntPtr.Zero); }
    public static bool PressFirstButton(IntPtr dlg) {
        IntPtr b = IntPtr.Zero;
        EnumChildWindows(dlg, delegate(IntPtr h, IntPtr l) { if (Class(h) == "Button") { b = h; return false; } return true; }, IntPtr.Zero);
        if (b == IntPtr.Zero) return false;
        PostMessage(b, 0x00F5, IntPtr.Zero, IntPtr.Zero); return true;
    }
    public static void Close(IntPtr h) { PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); }
}
'@
$title = 'Disco Elysium - Traduzione italiana'
function Wait-Top($classPrefix, $seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) { $h = [W2]::Top($title, $classPrefix); if ($h -ne [IntPtr]::Zero) { return $h }; Start-Sleep -Milliseconds 300 }
    throw "finestra '$classPrefix' non comparsa entro $seconds secondi"
}
function Close-Message {
    $d = Wait-Top '#32770' 120
    $text = ([W2]::ChildTexts($d) -replace '\s+', ' '); if ($text.Length -gt 80) { $text = $text.Substring(0, 80) + '...' }
    [void][W2]::PressFirstButton($d)
    $end = (Get-Date).AddSeconds(10); while ((Get-Date) -lt $end -and [W2]::Top($title, '#32770') -eq $d) { Start-Sleep -Milliseconds 200 }
    $text
}

try {
    "=== senza finestra"
    $p = Start-Process -FilePath $cmd -ArgumentList '/installa', '/cartella', "`"$test`"", '/log', "`"$log`"" -PassThru -WindowStyle Minimized; if (-not $p.WaitForExit(240000)) { $p.Kill(); throw "installazione senza finestra: tempo scaduto" }
    "installazione: codice $($p.ExitCode)"
    Get-Content $log -Encoding UTF8 | Select-Object -Skip 1
    if ($installed) {
        $all = $true
        foreach ($r in $rels) { if ((Get-FileHash (T $r)).Hash -ne (Get-FileHash (Join-Path $GameDir $r)).Hash) { $all = $false; "  DIVERSO dal file tradotto nel gioco: $r" } }
        "tutti i file identici a quelli tradotti nel gioco: $all"
    }
    $p = Start-Process -FilePath $cmd -ArgumentList '/rimuovi', '/cartella', "`"$test`"", '/log', "`"$log`"" -PassThru -WindowStyle Minimized; if (-not $p.WaitForExit(240000)) { $p.Kill(); throw "ripristino senza finestra: tempo scaduto" }
    "ripristino: codice $($p.ExitCode); file identici agli originali: " + ((Hash-Test) -eq $orig)

    "=== con la finestra"
    $p = Start-Process -FilePath $cmd -ArgumentList '/cartella', "`"$test`"" -PassThru -WindowStyle Minimized
    $main = Wait-Top 'WindowsForms' 60
    Start-Sleep -Seconds 3
    "controlli: " + [W2]::ChildTexts($main)
    [W2]::Space([W2]::Child($main, 'Installa traduzione'))
    "messaggio: " + (Close-Message)
    "file cambiati: " + ((Hash-Test) -ne $orig) + "; backup creato: " + (Test-Path (Join-Path $test '_backup_traduzione_ITA\stato.txt'))
    [W2]::Space([W2]::Child($main, 'Ripristina file originali'))
    "conferma: " + (Close-Message)
    "messaggio: " + (Close-Message)
    "file identici agli originali: " + ((Hash-Test) -eq $orig) + "; backup rimosso: " + (-not (Test-Path (Join-Path $test '_backup_traduzione_ITA')))
    [W2]::Close($main)
    $p.WaitForExit(15000) | Out-Null
    "processo terminato alla chiusura della finestra: " + $p.HasExited
}
finally {
    $h = [W2]::Top($title, 'WindowsForms'); if ($h -ne [IntPtr]::Zero) { [W2]::Close($h); Start-Sleep -Seconds 2 }
    foreach ($d in $pkg, $test) { if (Test-Path $d) { Remove-Item -Recurse -Force $d -Confirm:$false -ErrorAction SilentlyContinue } }
}
