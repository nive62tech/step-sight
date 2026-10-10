param([switch]$AllowRealInput, [switch]$DisposableSession)
. "$PSScriptRoot/common.ps1"
if (-not $AllowRealInput -or -not $DisposableSession) { throw 'Not run. Supply -AllowRealInput -DisposableSession only in an explicitly approved disposable Windows session. This moves the real pointer and clicks its own test window once.' }
Push-Location $script:ProjectRoot
try {
    & $script:Dotnet run --project apps/desktop/DesktopGuide.WindowsSmoke/DesktopGuide.WindowsSmoke.csproj -- --allow-real-input --disposable-session
    Assert-NativeSuccess 'Real Windows smoke (report: artifacts/windows-smoke.json)'
} finally { Pop-Location }
