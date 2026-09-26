# A Pilaster asztali alkalmazás összekötése a HELYBEN futó hibabejelentő bottal.
#
# A bot .env fájljából kiolvassa az API_KEY-t (és a PORT-ot), és beírja a
# Pilaster hibabejelentő-konfigurációjába:
#   %APPDATA%\Pilaster\bugreport-api.txt   (1. sor: URL, 2. sor: kulcs)
# Enélkül az app „nincs beállítva" üzenetet ad, és a Küldés gomb tiltva marad.
#
# A kulcsot a szkript NEM írja ki a képernyőre.
# Futtatás: jobbklikk → „Futtatás PowerShell-lel", vagy:
#   powershell -ExecutionPolicy Bypass -File discord-bot\connect-app.ps1

$ErrorActionPreference = 'Stop'

$envFile = Join-Path $PSScriptRoot '.env'
if (-not (Test-Path $envFile)) {
    Write-Host "Nem találom a bot .env fájlját: $envFile" -ForegroundColor Red
    Write-Host 'Masold a .env.example-t .env neven, es toltsd ki.' -ForegroundColor Red
    exit 1
}

$values = @{}
foreach ($line in Get-Content $envFile) {
    if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)\s*$') {
        $values[$Matches[1]] = $Matches[2].Trim('"', "'", ' ')
    }
}

if (-not $values['API_KEY']) {
    Write-Host 'A .env-ben nincs API_KEY.' -ForegroundColor Red
    exit 1
}

$port = if ($values['PORT']) { $values['PORT'] } else { '3000' }
$url = "http://localhost:$port"

$configDir = Join-Path $env:APPDATA 'Pilaster'
New-Item -ItemType Directory -Force $configDir | Out-Null
$configFile = Join-Path $configDir 'bugreport-api.txt'
Set-Content -Path $configFile -Value @($url, $values['API_KEY']) -Encoding ascii

Write-Host "Kesz: a Pilaster a hibajelenteseket a $url cimre kuldi." -ForegroundColor Green
Write-Host 'Inditsd ujra a Pilastert, hogy beolvassa.' -ForegroundColor Green

# Gyors ellenőrzés: fut-e a bot ezen a porton?
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect('localhost', [int]$port)
    $client.Close()
    Write-Host "A bot fut (a $port port valaszol)." -ForegroundColor Green
}
catch {
    Write-Host "FIGYELEM: a $port porton most nem valaszol semmi — inditsd el a botot (start-bot.vbs vagy npm start)." -ForegroundColor Yellow
}
