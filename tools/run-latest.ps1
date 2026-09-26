# A Pilaster LEGFRISSEBB, fejlesztés alatti változatának indítása (nem a
# GitHubon kiadott verzióé): lefordítja a jelenlegi forráskódot, átmásolja egy
# KÜLÖN mappába, és onnan indítja.
#
# Miért külön mappából? A futó program zárolja a saját DLL-jeit. Ha a build
# kimenetéből (bin\Release) futna, a következő fordítás nem tudná felülírni őket
# — így viszont a fejlesztés közben is nyitva maradhat, és ez a szkript csak
# akkor cseréli le, amikor újra lefuttatod.
#
# Indítás: dupla kattintás a gyökérben lévő "Pilaster-legujabb.bat"-ra.

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Pilaster.App\Pilaster.App.csproj'
$buildOutput = Join-Path $repo 'src\Pilaster.App\bin\Release\net10.0-windows'
$live = Join-Path $env:LOCALAPPDATA 'PilasterLive'
$exe = Join-Path $live 'Pilaster.exe'

Write-Host ''
Write-Host '  Pilaster - legfrissebb fejlesztoi valtozat' -ForegroundColor Cyan
Write-Host '  -----------------------------------------' -ForegroundColor Cyan
Write-Host ''

Write-Host '  [1/3] Forditas...' -ForegroundColor Yellow
& dotnet build $project -c Release -nologo -v quiet
$buildOk = $LASTEXITCODE -eq 0

if (-not $buildOk) {
    Write-Host ''
    Write-Host '  A forditas NEM sikerult (a hibauzenetek fent lathatok).' -ForegroundColor Red

    if (Test-Path $exe) {
        Write-Host '  Az utolso mukodo valtozat indul el helyette.' -ForegroundColor Red
        Start-Process $exe
        Start-Sleep -Seconds 4
    }
    else {
        Read-Host '  Nyomj Entert a bezarashoz'
    }
    exit 1
}

Write-Host '  [2/3] Masolas...' -ForegroundColor Yellow

# CSAK az innen indított (élő) példányt zárjuk be — egy máshonnan futó
# Pilaster (pl. a telepített kiadás) érintetlen marad. Előbb szabályosan, hogy
# a beállítások és a munkamenet elmentődjenek.
Get-Process Pilaster -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($live, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(10000)) { Stop-Process -Id $_.Id -Force }
    }

New-Item -ItemType Directory -Force $live | Out-Null
robocopy $buildOutput $live /MIR /NFL /NDL /NJH /NJS /NP | Out-Null

if ($LASTEXITCODE -ge 8) {
    Write-Host "  A masolas nem sikerult (robocopy kod: $LASTEXITCODE)." -ForegroundColor Red
    Read-Host '  Nyomj Entert a bezarashoz'
    exit 1
}

Write-Host '  [3/3] Inditas...' -ForegroundColor Yellow
Start-Process $exe

Write-Host ''
Write-Host '  Kesz! Ez az ablak magatol bezarul.' -ForegroundColor Green
Start-Sleep -Seconds 2
