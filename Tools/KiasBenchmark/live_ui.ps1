param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskLiveLease = Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
Test-KiasBenchmarkPorts
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = Join-Path $repo ('.kias-benchmark/live-ui/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$oldOutput = $env:KIAS_LAB_LIVE_OUTPUT
$oldSandbox = $env:ROBUST_DISABLE_SANDBOX
$server = $null
$client = $null
$sourceHashes = @{}
$sourceFiles = @('Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml', 'Resources/Prototypes/_Forge/KIAS/controller_presets.yml', 'Tools/KiasBenchmark/Client/LiveUiLab.cs', 'Tools/KiasBenchmark/Server/LiveUiLab.cs')
Push-Location $repo
try {
    foreach ($sourceFile in $sourceFiles) { $sourceHashes[$sourceFile] = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant() }
    if (-not $SkipBuild) {
        & dotnet build Content.Server/Content.Server.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabLiveUi=true *> (Join-Path $output 'server-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Diagnostic server build failed.' }
        & dotnet build Content.Client/Content.Client.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabLiveUi=true *> (Join-Path $output 'client-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Diagnostic client build failed.' }
    }
    foreach ($sourceFile in $sourceFiles) { if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHashes[$sourceFile]) { throw 'Diagnostic sources changed during compilation; rerun the live check.' } }
    $fingerprint = @{ sources = $sourceHashes; serverAssemblySha256 = (Get-FileHash -LiteralPath 'bin/Content.Server/Content.Server.dll' -Algorithm SHA256).Hash.ToLowerInvariant(); clientAssemblySha256 = (Get-FileHash -LiteralPath 'bin/Content.Client/Content.Client.dll' -Algorithm SHA256).Hash.ToLowerInvariant(); scope = 'Dedicated local server and native desktop client; automatic UI input handlers and real network BUI' }
    $fingerprint | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'run-manifest.json') -Encoding utf8
    $env:KIAS_LAB_LIVE_OUTPUT = $output
    $env:ROBUST_DISABLE_SANDBOX = '1'
    $serverArgs = @('--data-dir', (Join-Path $output 'data'), '--cvar', 'net.port=12301', '--cvar', 'net.bindto=127.0.0.1', '--cvar', 'game.auto_pause_empty=false', '--cvar', 'game.lobbyenabled=true', '--cvar', 'game.lobbyduration=999999', '--cvar', 'auth.mode=0', '+kias_lab_ui_server')
    $server = Start-Process -FilePath (Join-Path $repo 'bin/Content.Server/Content.Server.exe') -WorkingDirectory $repo -ArgumentList $serverArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'server.log') -RedirectStandardError (Join-Path $output 'server-error.log')
    $deadline = (Get-Date).AddMinutes(3)
    while (-not (Test-Path -LiteralPath (Join-Path $output 'server-bootstrap.json')) -and -not $server.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2; $server.Refresh() }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'server-bootstrap.json'))) { throw 'Live server fixture did not initialize.' }
    $clientArgs = @('--connect', '--connect-address', 'udp://127.0.0.1:12301', '--username', 'KiasLiveLab', '--cvar', 'display.windowmode=0', '--cvar', 'culture=ru-RU', '--cvar', 'display.uiScale=1', '--cvar', 'interface.resolutionAutoScaleEnabled=false', '+kias_lab_ui_client')
    $client = Start-Process -FilePath (Join-Path $repo 'bin/Content.Client/Content.Client.exe') -WorkingDirectory $repo -ArgumentList $clientArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'client.log') -RedirectStandardError (Join-Path $output 'client-error.log')
    $deadline = (Get-Date).AddMinutes(5)
    while (-not (Test-Path -LiteralPath (Join-Path $output 'client-completed.json')) -and -not (Test-Path -LiteralPath (Join-Path $output 'client-failed.json')) -and -not (Test-Path -LiteralPath (Join-Path $output 'server-failed.json')) -and -not $client.HasExited -and -not $server.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2; $client.Refresh(); $server.Refresh() }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'client-completed.json'))) { throw "Live UI failed or timed out. See $output" }
    $writes = Get-Content -LiteralPath (Join-Path $output 'server-writes.json') -Raw | ConvertFrom-Json
    if ($writes.writes.Count -ne 3) { throw 'The server did not prove three actual card writes.' }
    Write-Host "Live desktop/network programmer check passed: $output"
} finally {
    if ($null -ne $client -and -not $client.HasExited) { Stop-Process -Id $client.Id }
    if ($null -ne $server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
    $env:KIAS_LAB_LIVE_OUTPUT = $oldOutput
    $env:ROBUST_DISABLE_SANDBOX = $oldSandbox
    & dotnet build Content.Server/Content.Server.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabLiveUi=false *> (Join-Path $output 'server-restore-build.log')
    $serverRestore = $LASTEXITCODE
    & dotnet build Content.Client/Content.Client.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabLiveUi=false *> (Join-Path $output 'client-restore-build.log')
    if ($serverRestore -ne 0 -or $LASTEXITCODE -ne 0) { Write-Error "Normal builds did not restore; see $output" }
    Pop-Location
}
} finally { Exit-KiasBenchmarkLease $taskLiveLease }
