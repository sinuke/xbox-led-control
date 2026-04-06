#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $MyInvocation.MyCommand.Path)

$ProjectDir  = (Get-Item .).FullName
$PublishDir  = "$ProjectDir\publish"
$PkgStaging  = "$ProjectDir\pkg_staging"
$MsixPath    = "$ProjectDir\XboxLedExperiments.msix"
$CertPfxPath = "$ProjectDir\XboxLedDev.pfx"
$CertPass    = "xboxled123"
$Subject     = "CN=XboxLedDev"

# ── Helper: find SDK tool ─────────────────────────────────────────────────
function Get-SdkTool([string]$toolName) {
    $hit = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" `
        -Recurse -Filter $toolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } |
        Sort-Object FullName | Select-Object -Last 1
    if ($hit) { return $hit.FullName }

    $local = "$ProjectDir\.sdktools\bin\10.0.22621.0\x64\$toolName"
    if (Test-Path $local) { return $local }

    $hit = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" `
        -Recurse -Filter $toolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } |
        Sort-Object FullName | Select-Object -Last 1
    if ($hit) { return $hit.FullName }
    return $null
}

function Download-SdkBuildTools {
    $toolsDir = "$ProjectDir\.sdktools"
    $version  = "10.0.22621.756"
    $url      = "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/$version"
    $zip      = "$toolsDir\sdkbuildtools.zip"
    Write-Host "  Downloading Microsoft.Windows.SDK.BuildTools $version..."
    New-Item -ItemType Directory $toolsDir -Force | Out-Null
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $toolsDir -Force
    Remove-Item $zip
    Write-Host "  Done."
}

# ── 1. Publish experiments (self-contained) ───────────────────────────────
Write-Host "[1/5] Publishing experiments (self-contained win-x64)..."
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
& "C:\Program Files\dotnet\dotnet.exe" publish "experiments.csproj" `
    -c Release -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ── 2. Stage ──────────────────────────────────────────────────────────────
Write-Host "[2/5] Staging..."
if (Test-Path $PkgStaging) { Remove-Item $PkgStaging -Recurse -Force }
New-Item -ItemType Directory $PkgStaging | Out-Null
New-Item -ItemType Directory "$PkgStaging\Assets" | Out-Null
Copy-Item "$PublishDir\*" $PkgStaging -Recurse
Copy-Item "$ProjectDir\Package\AppxManifest.xml" $PkgStaging

# Minimal 1x1 transparent PNG for logo assets
$png1x1 = [Convert]::FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
foreach ($f in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
    [IO.File]::WriteAllBytes("$PkgStaging\Assets\$f", $png1x1)
}

# ── 3. Certificate ────────────────────────────────────────────────────────
Write-Host "[3/5] Certificate..."
if (-not (Test-Path $CertPfxPath)) {
    $cert = New-SelfSignedCertificate -Subject $Subject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsage DigitalSignature -Type CodeSigningCert `
        -NotAfter (Get-Date).AddYears(5)
    $pwd = ConvertTo-SecureString $CertPass -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $CertPfxPath -Password $pwd | Out-Null
    $store = New-Object Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $store.Open("ReadWrite"); $store.Add($cert); $store.Close()
    Write-Host "  Created and trusted: $($cert.Thumbprint)"
} else {
    Write-Host "  Reusing existing certificate."
}

# ── 4. SDK tools ──────────────────────────────────────────────────────────
$makeappx = Get-SdkTool "makeappx.exe"
$signtool  = Get-SdkTool "signtool.exe"
if (-not $makeappx -or -not $signtool) {
    Download-SdkBuildTools
    $makeappx = Get-SdkTool "makeappx.exe"
    $signtool  = Get-SdkTool "signtool.exe"
}
if (-not $makeappx) { throw "makeappx.exe not found." }
if (-not $signtool)  { throw "signtool.exe not found."  }
Write-Host "  makeappx: $makeappx"
Write-Host "  signtool:  $signtool"

# ── 5. Pack, sign, install ────────────────────────────────────────────────
Write-Host "[4/5] Packing..."
if (Test-Path $MsixPath) { Remove-Item $MsixPath }
& $makeappx pack /d $PkgStaging /p $MsixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

Write-Host "[5/5] Signing and installing..."
& $signtool sign /fd sha256 /f $CertPfxPath /p $CertPass $MsixPath
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

$existing = Get-AppxPackage -Name "XboxLedControl" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  Removing previous installation..."
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppPackage -Path $MsixPath
Write-Host ""
Write-Host "[OK] Installed. Run:"
Write-Host "  XboxLedControlExperiments.exe --legacy-led 23"
