<#
.SYNOPSIS
    Microsoft Store-csomag (MSIX) készítése.

.DESCRIPTION
    Architektúránként publikálja az alkalmazást, legenerálja a Store által
    kötelező ikonméreteket az icon-1024.png-ből, kitölti a leírót
    (tools/store/AppxManifest.template.xml), majd a MakeAppx-szal .msix
    csomagokat és egy .msixbundle-t készít az artifacts/msix mappába.

    A Store-ba feltöltött csomagot a Microsoft írja alá, tanúsítvány nem kell.
    Az Identity/Publisher értékeket a Partner Center „Termékidentitás"
    oldaláról kell átmásolni.

.PARAMETER Test
    Helyi kipróbáláshoz: saját, önaláírt tanúsítvánnyal aláírt x64-csomag.
    Az -Install kapcsolóval rögtön telepíti is; első alkalommal a
    tanúsítványt a gép megbízható kiadói közé kell tenni, ehhez egyszer
    rendszergazdai jóváhagyást (UAC) kér.

.EXAMPLE
    .\tools\package-msix.ps1 -IdentityName 12345Rego.Pilaster -Publisher "CN=ABCD-1234" -PublisherDisplayName "Rego"

.EXAMPLE
    .\tools\package-msix.ps1 -Test -Install
#>
param(
    [string]$Version,
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$IdentityName = 'Pilaster',
    [string]$Publisher = 'CN=Pilaster',
    [string]$PublisherDisplayName = 'Pilaster',
    [string]$DisplayName = 'Pilaster',
    [string]$OutDir,
    [switch]$Test,
    [switch]$Install,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts\msix' }

if ($Test) {
    $Publisher = 'CN=Pilaster Test'
    $IdentityName = 'Pilaster.Test'
    $DisplayName = 'Pilaster (teszt)'
    $Architectures = @(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' })
}

if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $repo 'Directory.Build.props') -Raw)
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1)
}
# A Store négytagú verziót kér, az utolsó tagnak 0-nak kell lennie.
$parts = @($Version.Split('.') | ForEach-Object { [int]$_ })
while ($parts.Count -lt 3) { $parts += 0 }
$Version = '{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]

function Find-SdkTool([string]$name) {
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $tool = Get-ChildItem $root -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "$hostArch\$name" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $tool) { throw "$name nem található — telepítsd a Windows SDK-t." }
    return $tool
}

$makeAppx = Find-SdkTool 'makeappx.exe'
$makePri = Find-SdkTool 'makepri.exe'
$signTool = Find-SdkTool 'signtool.exe'

# A tesztcsomag aláírója: a felhasználó saját tanúsítványtárában, egyszer jön létre.
$testCert = $null
if ($Test) {
    $testCert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date).AddDays(7) } |
        Select-Object -First 1
    if (-not $testCert) {
        $testCert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher `
            -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(2)
    }
}

# ---------- Ikonok ----------

Add-Type -AssemblyName System.Drawing
$brand = Join-Path $repo 'docs\assets\brand'
$master = [System.Drawing.Image]::FromFile((Join-Path $brand 'icon-1024.png'))

# A kis méreteknél a kézzel hangolt PNG-k élesebbek az átméretezettnél.
function Get-SourceFor([int]$size) {
    $exact = Join-Path $brand "png\icon-$size.png"
    if (Test-Path $exact) { return [System.Drawing.Image]::FromFile($exact) }
    return $null
}

function New-Asset([string]$path, [int]$width, [int]$height, [double]$iconRatio) {
    $iconSize = [int][Math]::Round([Math]::Min($width, $height) * $iconRatio)
    $bmp = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $src = Get-SourceFor $iconSize
    if (-not $src) { $src = $master }
    $x = [int](($width - $iconSize) / 2); $y = [int](($height - $iconSize) / 2)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle $x, $y, $iconSize, $iconSize))
    if ($src -ne $master) { $src.Dispose() }
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$assetStage = Join-Path $OutDir 'assets-stage'
Remove-Item $assetStage -Recurse -Force -ErrorAction SilentlyContinue
$assetDir = Join-Path $assetStage 'Assets'
New-Item -ItemType Directory -Force $assetDir | Out-Null

$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }
foreach ($scale in $scales.Keys) {
    $f = $scales[$scale]
    # Az ikon maga is tartalmaz ~7% margót, a kis logók ezért kitöltik a vásznat.
    New-Asset "$assetDir\Square44x44Logo.scale-$scale.png" ([int](44 * $f)) ([int](44 * $f)) 1.0
    New-Asset "$assetDir\StoreLogo.scale-$scale.png" ([int](50 * $f)) ([int](50 * $f)) 1.0
    New-Asset "$assetDir\Square150x150Logo.scale-$scale.png" ([int](150 * $f)) ([int](150 * $f)) 0.62
    New-Asset "$assetDir\Wide310x150Logo.scale-$scale.png" ([int](310 * $f)) ([int](150 * $f)) 0.62
    New-Asset "$assetDir\SmallTile.scale-$scale.png" ([int](71 * $f)) ([int](71 * $f)) 0.70
    New-Asset "$assetDir\LargeTile.scale-$scale.png" ([int](310 * $f)) ([int](310 * $f)) 0.55
    New-Asset "$assetDir\SplashScreen.scale-$scale.png" ([int](620 * $f)) ([int](300 * $f)) 0.55
}
# Tálca, Start menü, fájllista: pontos pixelméretek, háttérlemez nélkül is.
foreach ($t in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    New-Asset "$assetDir\Square44x44Logo.targetsize-$t.png" $t $t 1.0
    Copy-Item "$assetDir\Square44x44Logo.targetsize-$t.png" "$assetDir\Square44x44Logo.targetsize-${t}_altform-unplated.png"
    Copy-Item "$assetDir\Square44x44Logo.targetsize-$t.png" "$assetDir\Square44x44Logo.targetsize-${t}_altform-lightunplated.png"
}
$master.Dispose()

# ---------- Csomagok ----------

$template = Get-Content (Join-Path $PSScriptRoot 'store\AppxManifest.template.xml') -Raw -Encoding UTF8
$esc = { param($s) [System.Security.SecurityElement]::Escape($s) }
$msixDir = Join-Path $OutDir 'packages'
Remove-Item $msixDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $msixDir | Out-Null
$built = @()

foreach ($arch in $Architectures) {
    $rid = "win-$arch"
    $publishDir = Join-Path $OutDir "publish\$rid"

    if (-not $SkipPublish -or -not (Test-Path $publishDir)) {
        Write-Host "Publikálás: $rid" -ForegroundColor Cyan
        Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
        & dotnet publish (Join-Path $repo 'src\Pilaster.App') -c Release -r $rid --self-contained true `
            -p:PublishReadyToRun=true -p:Version=$($Version.Substring(0, $Version.LastIndexOf('.'))) -o $publishDir --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish sikertelen ($rid)" }
    }

    $stage = Join-Path $OutDir "stage\$rid"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item "$publishDir\*" $stage -Recurse
    # Hordozható jelzőfájl a Store-csomagban nem lehet (az adatok a csomag mellett nem írhatók).
    Remove-Item (Join-Path $stage 'portable.marker') -ErrorAction SilentlyContinue
    Remove-Item "$stage\*.pdb" -ErrorAction SilentlyContinue
    Copy-Item $assetDir $stage -Recurse

    $manifest = $template.
        Replace('{{IdentityName}}', (& $esc $IdentityName)).
        Replace('{{Publisher}}', (& $esc $Publisher)).
        Replace('{{PublisherDisplayName}}', (& $esc $PublisherDisplayName)).
        Replace('{{DisplayName}}', (& $esc $DisplayName)).
        Replace('{{Version}}', $Version).
        Replace('{{Architecture}}', $arch)
    [System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding $false))

    # resources.pri: ebből tudja a Windows, melyik méretű ikont válassza.
    # Csak a manifest és az Assets kerül bele — a DLL-eket nem kell indexelni.
    $priStage = Join-Path $OutDir "pri\$rid"
    Remove-Item $priStage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $priStage | Out-Null
    Copy-Item $assetDir $priStage -Recurse
    Copy-Item (Join-Path $stage 'AppxManifest.xml') $priStage
    $priConfig = Join-Path $priStage 'priconfig.xml'
    & $makePri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
    & $makePri new /pr $priStage /cf $priConfig /mn (Join-Path $priStage 'AppxManifest.xml') /of (Join-Path $stage 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri sikertelen ($rid)" }

    $msix = Join-Path $msixDir "Pilaster_${Version}_$arch.msix"
    Write-Host "Csomagolás: $(Split-Path $msix -Leaf)" -ForegroundColor Cyan
    & $makeAppx pack /d $stage /p $msix /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack sikertelen ($rid)" }
    if ($testCert) {
        & $signTool sign /fd SHA256 /sha1 $testCert.Thumbprint /s My $msix | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Aláírás sikertelen ($rid)" }
    }
    $built += $msix
}

if (-not $Test -and $built.Count -gt 1) {
    $bundle = Join-Path $OutDir "Pilaster_$Version.msixbundle"
    & $makeAppx bundle /d $msixDir /p $bundle /bv $Version /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx bundle sikertelen' }
    Write-Host "Kész: $bundle" -ForegroundColor Green
    Write-Host 'Ezt töltsd fel a Partner Centerben (Csomagok oldal).'
}
else {
    $built | ForEach-Object { Write-Host "Kész: $_" -ForegroundColor Green }
}

if ($Install) {
    if (-not $Test) { throw 'Az -Install csak -Test módban működik (a Store-csomag aláíratlan).' }
    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $testCert.Thumbprint }
    if (-not $trusted) {
        $cer = Join-Path $OutDir 'Pilaster-Test.cer'
        Export-Certificate -Cert $testCert -FilePath $cer | Out-Null
        Write-Host 'A teszttanúsítványt megbízhatóvá kell tenni — jóváhagyást kér (UAC).' -ForegroundColor Yellow
        Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
            '-NoProfile', '-Command', "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople")
    }
    Get-AppxPackage -Name $IdentityName | Remove-AppxPackage -ErrorAction SilentlyContinue
    Add-AppxPackage -Path $built[0]
    Write-Host 'Telepítve — a Start menüben „Pilaster (teszt)" néven található.' -ForegroundColor Green
}
