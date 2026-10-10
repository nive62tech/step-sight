param([string]$ConfigurationPath = '', [string]$SigningThumbprint = '',
    [string]$OutputDirectory = 'artifacts/windows', [string]$ArchivePath = 'artifacts/DesktopGuide-windows-x64.zip')
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    & $script:Dotnet publish apps/desktop/DesktopGuide.App/DesktopGuide.App.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=false -o $OutputDirectory; Assert-NativeSuccess 'Windows publish'
    $publicConfig = if ($ConfigurationPath) { $ConfigurationPath } else { 'apps/desktop/DesktopGuide.App/desktop-config.json' }
    Copy-Item -LiteralPath $publicConfig -Destination (Join-Path $OutputDirectory 'desktop-config.json')
    if ($SigningThumbprint) {
        $executable = Join-Path $OutputDirectory 'DesktopGuide.App.exe'
        signtool sign /sha1 $SigningThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $executable; Assert-NativeSuccess 'Code signing'
        signtool verify /pa $executable; Assert-NativeSuccess 'Signature verification'
    } else { Write-Warning 'Unsigned development package. Do not describe it as a signed release.' }
    $hashes = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.csv' } | Get-FileHash -Algorithm SHA256 | Select-Object Hash,@{Name='File';Expression={Split-Path $_.Path -Leaf}}
    $hashes | Export-Csv (Join-Path $OutputDirectory 'SHA256SUMS.csv') -NoTypeInformation
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $ArchivePath -Force
} finally { Pop-Location }
