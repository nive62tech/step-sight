param([switch]$KeepDatabase)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    foreach ($name in @('api','sandbox')) {
        $path = ".local/$name.pid"
        if (Test-Path $path) {
            $processId = [int](Get-Content $path)
            $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$processId" -ErrorAction SilentlyContinue
            # A stale PID must not terminate an unrelated process.
            if ($processInfo -and $processInfo.ExecutablePath -eq "$script:ProjectRoot\.venv\Scripts\python.exe" -and $processInfo.CommandLine -match 'uvicorn|sandbox-server.py') { Stop-Process -Id $processId }
            Remove-Item -LiteralPath $path
        }
    }
    if (-not $KeepDatabase) { docker compose stop db; Assert-NativeSuccess 'Database stop' }
} finally { Pop-Location }
