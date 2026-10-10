param([switch]$NoDatabase, [switch]$LaunchDesktop)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    uv sync --frozen; Assert-NativeSuccess 'Python setup'
    if (-not (Test-Path .env)) { Copy-Item .env.example .env }
    New-Item -ItemType Directory -Force .local | Out-Null
    if (-not $NoDatabase) {
        docker compose up -d --wait db; Assert-NativeSuccess 'PostgreSQL startup (start Docker Desktop first)'
        uv run alembic -c services/api/alembic.ini upgrade head; Assert-NativeSuccess 'Database migrations'
    }
    $env:PYTHONPATH = "$script:ProjectRoot/services/api"
    foreach ($name in @('api', 'sandbox')) {
        $pidPath = ".local/$name.pid"
        if (Test-Path $pidPath) {
            $existingProcess = Get-Process -Id ([int](Get-Content $pidPath)) -ErrorAction SilentlyContinue
            if ($existingProcess) { throw "$name is already running; use dev-down.ps1 first" }
        }
    }
    $apiProcess = Start-Process "$script:ProjectRoot/.venv/Scripts/python.exe" -ArgumentList '-m','uvicorn','app.main:create_app','--factory','--host','127.0.0.1','--port','8000','--no-access-log','--no-proxy-headers' -WorkingDirectory $script:ProjectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput .local/api.stdout.log -RedirectStandardError .local/api.stderr.log
    $sandboxProcess = Start-Process "$script:ProjectRoot/.venv/Scripts/python.exe" -ArgumentList 'scripts/sandbox-server.py' -WorkingDirectory $script:ProjectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput .local/sandbox.stdout.log -RedirectStandardError .local/sandbox.stderr.log
    $apiProcess.Id | Set-Content .local/api.pid; $sandboxProcess.Id | Set-Content .local/sandbox.pid
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { $null = Invoke-RestMethod http://127.0.0.1:8000/health/ready -TimeoutSec 2; $ready = $true; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'API did not become ready. Inspect .local/api.stderr.log; no desktop input is armed.' }
    if ($LaunchDesktop) { & $script:Dotnet run --project apps/desktop/DesktopGuide.App/DesktopGuide.App.csproj }
    Write-Output 'API http://127.0.0.1:8000 (labelled mock by default); sandbox http://127.0.0.1:8765. Desktop is disarmed on startup.'
} finally { Pop-Location }
