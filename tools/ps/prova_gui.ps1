# Collaudo dell'interfaccia grafica del patcher su una COPIA dei file del gioco:
# preme "Installa", chiude i messaggi, verifica, poi preme "Ripristina" e controlla che i file tornino identici.
# -Dati 'Disco Elysium_Data' rinomina la cartella dei dati nella copia, come nella versione Epic.
param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Disco Elysium', [string]$Dati = 'disco_Data')
$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $proj 'build\DiscoElysium_Traduzione_ITA.exe'
$test = Join-Path $proj 'work\gioco_prova'
$rels = 'disco_Data\resources.assets',
        'disco_Data\StreamingAssets\AssetBundles\Windows\lockits\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images\spanish',
        'disco_Data\StreamingAssets\AssetBundles\Windows\images_dyslexic\spanish'
function T($r) { Join-Path $test ($r -replace '^disco_Data', $Dati) }
# gli originali: dal backup della patch se il gioco è già tradotto, altrimenti dal gioco stesso
$src = Join-Path $GameDir '_backup_traduzione_ITA'
if (-not (Test-Path $src)) { $src = $GameDir }
if (Test-Path $test) { Remove-Item -Recurse -Force $test -Confirm:$false }
foreach ($r in $rels) { $d = T $r; New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null; Copy-Item (Join-Path $src $r) $d }

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    static string Text(IntPtr h) { StringBuilder s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    static string Class(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
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
    // finestra di messaggio (classe #32770) del processo
    public static IntPtr Dialog(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h) && Class(h) == "#32770") { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    // preme l'n-esimo pulsante di una finestra di messaggio (BM_CLICK)
    public static bool PressButton(IntPtr dlg, int index) {
        List<IntPtr> btns = new List<IntPtr>();
        EnumChildWindows(dlg, delegate(IntPtr h, IntPtr l) { if (Class(h) == "Button") btns.Add(h); return true; }, IntPtr.Zero);
        if (index >= btns.Count) return false;
        PostMessage(btns[index], 0x00F5, IntPtr.Zero, IntPtr.Zero);
        return true;
    }
    public static string TopWindows(uint pid) {
        List<string> t = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) t.Add("[" + Class(h) + "] " + Text(h) + " :: " + ChildTexts(h)); return true; }, IntPtr.Zero);
        return string.Join("\n   ", t.ToArray());
    }
    // Attiva il pulsante con la barra spaziatrice: un clic simulato verrebbe ignorato da WinForms
    // se in quel punto dello schermo c'è sopra un'altra finestra.
    public static void Click(IntPtr h) { PostMessage(h, 0x100, (IntPtr)0x20, IntPtr.Zero); PostMessage(h, 0x101, (IntPtr)0x20, IntPtr.Zero); }
}
'@

function Wait-Dialog($proc, $seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) { $d = [W]::Dialog([uint32]$proc.Id); if ($d -ne [IntPtr]::Zero) { return $d }; Start-Sleep -Milliseconds 300 }
    throw ("nessuna finestra di messaggio entro $seconds secondi. Finestre del processo:`n   " + [W]::TopWindows([uint32]$proc.Id))
}
# preme il primo pulsante della finestra di messaggio (OK, oppure Sì) e aspetta che si chiuda davvero
function Close-Dialog($proc, $dlg) {
    if (-not [W]::PressButton($dlg, 0)) { throw "nessun pulsante nella finestra di messaggio" }
    $end = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $end) { if ([W]::Dialog([uint32]$proc.Id) -ne $dlg) { return }; Start-Sleep -Milliseconds 200 }
    throw "la finestra di messaggio non si chiude"
}
function Hash-Test { ($rels | ForEach-Object { (Get-FileHash (T $_)).Hash }) -join ',' }

$orig = Hash-Test
$p = Start-Process -FilePath $exe -ArgumentList '/cartella', "`"$test`"" -PassThru
$end = (Get-Date).AddSeconds(20); while ((Get-Date) -lt $end) { Start-Sleep -Milliseconds 400; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break } }
Start-Sleep -Seconds 3
try {
    $main = $p.MainWindowHandle
    "controlli: " + [W]::ChildTexts($main)
    $btn = [W]::Child($main, 'Installa traduzione'); if ($btn -eq [IntPtr]::Zero) { throw "pulsante Installa non trovato" }
    [W]::Click($btn)
    $d = Wait-Dialog $p 120
    "messaggio dopo l'installazione: " + ([W]::ChildTexts($d) -replace '\s+', ' ').Substring(0, 70) + '...'
    Close-Dialog $p $d
    "file cambiati dopo l'installazione: " + ((Hash-Test) -ne $orig) + "; backup creato: " + (Test-Path (Join-Path $test '_backup_traduzione_ITA\stato.txt'))
    "controlli: " + [W]::ChildTexts($main)

    $btn = [W]::Child($main, 'Ripristina file originali'); [W]::Click($btn)
    $d = Wait-Dialog $p 20
    "conferma: " + ([W]::ChildTexts($d) -replace '\s+', ' ')
    Close-Dialog $p $d
    $d = Wait-Dialog $p 60
    "messaggio dopo il ripristino: " + ([W]::ChildTexts($d) -replace '\s+', ' ')
    Close-Dialog $p $d
    "file identici agli originali dopo il ripristino: " + ((Hash-Test) -eq $orig) + "; backup rimosso: " + (-not (Test-Path (Join-Path $test '_backup_traduzione_ITA')))
}
finally {
    if (-not $p.HasExited) { [void]$p.CloseMainWindow(); Start-Sleep -Seconds 1; if (-not $p.HasExited) { $p.Kill() } }
    Remove-Item -Recurse -Force $test -Confirm:$false
}

