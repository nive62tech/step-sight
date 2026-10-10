param([string]$ConfigurationPath = '', [string]$SigningThumbprint = '')
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    & $script:Dotnet publish apps/desktop/DesktopGuide.App/DesktopGuide.App.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=false -o artifacts/windows; Assert-NativeSuccess 'Windows publish'
    $publicConfig = if ($ConfigurationPath) { $ConfigurationPath } else { 'apps/desktop/DesktopGuide.App/desktop-config.json' }
    Copy-Item -LiteralPath $publicConfig -Destination artifacts/windows/desktop-config.json
    if ($SigningThumbprint) {
        signtool sign /sha1 $SigningThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 artifacts/windows/DesktopGuide.App.exe; Assert-NativeSuccess 'Code signing'
        signtool verify /pa artifacts/windows/DesktopGuide.App.exe; Assert-NativeSuccess 'Signature verification'
    } else { Write-Warning 'Unsigned development package. Do not describe it as a signed release.' }
    $hashes = Get-ChildItem artifacts/windows -File | Where-Object { $_.Name -ne 'SHA256SUMS.csv' } | Get-FileHash -Algorithm SHA256 | Select-Object Hash,@{Name='File';Expression={Split-Path $_.Path -Leaf}}
    $hashes | Export-Csv artifacts/windows/SHA256SUMS.csv -NoTypeInformation
    Compress-Archive -Path artifacts/windows/* -DestinationPath artifacts/DesktopGuide-windows-x64.zip -Force
} finally { Pop-Location }
