param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "TigangReminder.App\TigangReminder.App.csproj"

dotnet build $project -c $Configuration -p:Platform=$Platform
