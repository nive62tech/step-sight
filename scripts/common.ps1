$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'
$script:Dotnet = if (Test-Path "$script:ProjectRoot/.tools/dotnet/dotnet.exe") { "$script:ProjectRoot/.tools/dotnet/dotnet.exe" } else { 'dotnet' }
$script:Terraform = if (Test-Path "$script:ProjectRoot/.tools/terraform/terraform.exe") { "$script:ProjectRoot/.tools/terraform/terraform.exe" } else { 'terraform' }
function Assert-NativeSuccess([string]$Operation) { if ($LASTEXITCODE -ne 0) { throw "$Operation failed with exit code $LASTEXITCODE" } }
