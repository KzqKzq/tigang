param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "TigangReminder.App\TigangReminder.App.csproj"
$manifestPath = Join-Path $root "TigangReminder.App\Package.appxmanifest"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

[xml]$manifest = Get-Content $manifestPath -Encoding UTF8
$identityName = $manifest.Package.Identity.Name
$publisher = $manifest.Package.Identity.Publisher
$appId = $manifest.Package.Applications.Application.Id
$version = $manifest.Package.Identity.Version

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $publisher } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating self-signed developer certificate: $publisher"
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -KeyUsage DigitalSignature `
        -FriendlyName "TigangReminder Dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
}

$trusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
    Select-Object -First 1

$rootTrusted = Get-ChildItem Cert:\CurrentUser\Root |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
    Select-Object -First 1

if (-not $trusted -or -not $rootTrusted) {
    Write-Host "Trusting developer certificate in CurrentUser\\TrustedPeople and CurrentUser\\Root"
    $cerPath = Join-Path $root "TigangReminder.App\AppPackages\AppPublisher.cer"
    New-Item -ItemType Directory -Force -Path (Split-Path $cerPath -Parent) | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
}

Write-Host "Packing app..."
dotnet publish $project `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false

$packageDir = Get-ChildItem (Join-Path $root "TigangReminder.App\AppPackages") -Directory |
    Where-Object { $_.Name -like "TigangReminder.App_${version}_${Platform}*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $packageDir) {
    throw "Package directory not found under AppPackages."
}

$msixPath = Get-ChildItem $packageDir.FullName -Filter *.msix |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    ForEach-Object { $_.FullName }

if (-not (Test-Path $msixPath)) {
    throw "MSIX package not found: $msixPath"
}

if (-not (Test-Path $signtool)) {
    throw "signtool.exe not found: $signtool"
}

$pfxPassword = [Guid]::NewGuid().ToString("N")
$pfxPath = Join-Path $packageDir.FullName "AppPublisher.pfx"

Write-Host "Exporting developer certificate..."
$securePassword = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null

Write-Host "Signing msix..."
& $signtool sign /fd SHA256 /f $pfxPath /p $pfxPassword $msixPath

if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE"
}

$installed = Get-AppxPackage | Where-Object { $_.Name -eq $identityName } | Select-Object -First 1
if ($installed) {
    Write-Host "Removing previous package: $($installed.PackageFullName)"
    Remove-AppxPackage -Package $installed.PackageFullName
}

Write-Host "Installing package..."
Add-AppxPackage -Path $msixPath

$installed = Get-AppxPackage | Where-Object { $_.Name -eq $identityName } | Select-Object -First 1
if (-not $installed) {
    throw "Package installed but not found in Get-AppxPackage."
}

$aumid = "$($installed.PackageFamilyName)!$appId"
Write-Host "Launching app: $aumid"
Start-Process "explorer.exe" "shell:AppsFolder\$aumid"
