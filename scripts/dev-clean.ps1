$ErrorActionPreference = "SilentlyContinue"

Get-Process dotnet | Stop-Process -Force

$root = Split-Path -Parent $PSScriptRoot
$obj = Join-Path $root "TigangReminder.App\obj"
$bin = Join-Path $root "TigangReminder.App\bin"

if (Test-Path $obj) {
    Remove-Item -Recurse -Force $obj
}

if (Test-Path $bin) {
    Remove-Item -Recurse -Force $bin
}
