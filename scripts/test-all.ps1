param([switch]$SkipDesktop)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    uv sync --frozen; Assert-NativeSuccess 'Python dependency sync'
    uv run ruff check services/api scripts; Assert-NativeSuccess 'Python lint'
    uv run pytest -q; Assert-NativeSuccess 'Python tests'
    if (-not $SkipDesktop) {
        & $script:Dotnet restore DesktopGuide.slnx --locked-mode; Assert-NativeSuccess '.NET restore'
        & $script:Dotnet build DesktopGuide.slnx --no-restore --nologo; Assert-NativeSuccess '.NET build'
        & $script:Dotnet test apps/desktop/DesktopGuide.Tests/DesktopGuide.Tests.csproj --no-build --nologo; Assert-NativeSuccess '.NET deterministic tests'
        & $script:Dotnet test apps/desktop/DesktopGuide.AppTests/DesktopGuide.AppTests.csproj --no-build --nologo; Assert-NativeSuccess 'Credential adapter tests'
    }
} finally { Pop-Location }
