param(
    [ValidateSet('A','B')][string]$Variant,
    [int]$Ships = 40,
    [int]$WarmupTicks = 1800,
    [int]$MeasuredTicks = 600,
    [string]$Scenario = '.kias-benchmark/dryrun-scenario.jsonl',
    [ValidateRange(0.05,1)][float]$DiagnosticTimeScale = 1,
    [string]$OutputDirectory,
    [string]$ManifestPath,
    [int]$Seed = 20261010,
    [switch]$FunctionalOnly,
    [switch]$Profile,
    [switch]$DisablePhaseCounters,
    [switch]$DisableSystemCounters,
    [ValidateSet("None","RoomScanOff","CrewFilterOff","CoveragePassOff","RuntimeIdleOff")][string]$Ablation="None",
    [switch]$Diagnostic,
    [ValidateRange(0,480)][int]$MaxWallMinutes=0,
    [switch]$KeepNativeNpcAwake,
    [ValidateRange(0,40)][int]$NetworkClients = 0,
    [ValidateSet("A","B","C","D")][string]$PhysicsMode = "B",
    [ValidateSet("KiasPrototypes","BroadIntegrated")][string]$PhysicsDeviceGroup = "KiasPrototypes",
    [switch]$DisablePhysicsQueryCounters,
    [ValidateRange(0,10000)][int]$AdminLogProbe = 0
)
$ErrorActionPreference = 'Stop'
if ($Ablation -ne 'None' -and -not $Profile) {throw 'Ablation is restricted to explicit profiling; not full gameplay acceptance.'}
if ($MaxWallMinutes -eq 0) {
    $MaxWallMinutes=if ($Diagnostic) {120} else {[Math]::Max(120,[Math]::Ceiling(($WarmupTicks+$MeasuredTicks)/3600.0/$DiagnosticTimeScale*3+10))}
}
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
. (Join-Path $PSScriptRoot 'native_log_watch.ps1')
$taskBenchmarkLease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
    Test-KiasBenchmarkPorts
Write-Host "Native $Variant | проверка хешей сборки, карты и игровых драйверов..."
function Read-NativeProgress([string]$Path) {
    $taskStream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $taskReader = [IO.StreamReader]::new($taskStream)
    try { return $taskReader.ReadToEnd() | ConvertFrom-Json }
    finally { $taskReader.Dispose() }
}
function Save-NativeMetrics([string]$Path,[int]$Tick) {
    $taskMetricsRoot=[IO.Path]::GetDirectoryName($Path)
    foreach ($taskAttempt in 1..2) {
        $taskScrapeClock=[Diagnostics.Stopwatch]::StartNew()
        $taskStartedUtc=[DateTime]::UtcNow.ToString('o')
        $taskTemporary=$Path+'.attempt-'+$taskAttempt
        $taskScrapeError=$null
        try {
            Invoke-WebRequest -Uri 'http://127.0.0.1:44880/metrics' -UseBasicParsing -TimeoutSec 45 -OutFile $taskTemporary | Out-Null
            Move-Item -LiteralPath $taskTemporary -Destination $Path -Force
        } catch { $taskScrapeError=$_.Exception.Message }
        @{file=[IO.Path]::GetFileName($Path);requestedTick=$Tick;attempt=$taskAttempt;startedUtc=$taskStartedUtc;wallSeconds=$taskScrapeClock.Elapsed.TotalSeconds;succeeded=($null -eq $taskScrapeError);error=$taskScrapeError} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $taskMetricsRoot 'metric-scrapes.jsonl') -Encoding utf8
        if ($null -eq $taskScrapeError) { return }
        if ($taskAttempt -eq 2) { throw "Native metrics unavailable after two bounded attempts: $taskScrapeError. See $taskMetricsRoot/metric-scrapes.jsonl" }
        Write-Host 'Чтение метрик задержалось; повторная попытка без перезапуска сервера. Подробности: metric-scrapes.jsonl'
    }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ManifestPath) { $ManifestPath = Join-Path $repo '.kias-benchmark/BASELINE_MANIFEST.json' }
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$build = $manifest.builds | Where-Object label -eq $Variant
$source = $build.source
if ($NetworkClients -gt $Ships) { throw 'Network clients must not exceed ships.' }
$taskMaxIpConnections = [Math]::Max(8, $NetworkClients)
$taskMaxRapidConnections = [Math]::Max(3, $NetworkClients * 2)
if ($NetworkClients -gt 0 -and -not $build.clientRuntimeAssemblyHashes) { throw 'Matched native client build is required.' }
if ($NetworkClients -gt 0) {
    foreach ($taskHash in $build.clientRuntimeAssemblyHashes.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $source ('bin/Content.Client/' + $taskHash.Name))).Hash.ToLowerInvariant() -ne $taskHash.Value) { throw "Client assembly changed: $($taskHash.Name)" }
    }
}
if (-not $build.compiled) { throw "Variant $Variant is not recorded as successfully built." }
if ($build.runtimeAssemblyHashes) {
    foreach ($taskRuntimeHash in $build.runtimeAssemblyHashes.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $source ('bin/Content.Server/' + $taskRuntimeHash.Name))).Hash.ToLowerInvariant() -ne $taskRuntimeHash.Value) { throw "Runtime assembly changed: $($taskRuntimeHash.Name)" }
    }
}
$assembly = Join-Path $source 'bin/Content.Server/Content.Server.dll'
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.assemblySha256) { throw 'Assembly differs from the baseline manifest.' }
$mapHash = (Get-FileHash -LiteralPath (Join-Path $source 'Resources/Maps/_Forge/Shuttles/Archive/Mercenary/labBriar.yml') -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedMapHash = if ($Variant -eq 'A') { $manifest.staticParity.aMapSha256 } else { $manifest.staticParity.bMapSha256 }
if ($mapHash -ne $expectedMapHash) { throw 'Replay map differs from the verified static parity manifest.' }
$nativeLabHash = (Get-FileHash -LiteralPath (Join-Path $source 'Tools/KiasBenchmark/Server/NativeLab.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($nativeLabHash -ne $build.nativeLabSha256) { throw 'Native drivers differ from the compiled source manifest.' }
$nativeGameDriversHash = (Get-FileHash -LiteralPath (Join-Path $source 'Tools/KiasBenchmark/Server/NativeGameDrivers.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($nativeGameDriversHash -ne $build.nativeGameDriversSha256) { throw 'Native game drivers differ from the compiled source manifest.' }
if ($Profile) {
    if (-not $build.nativeProfilerSha256 -or (Get-FileHash -LiteralPath (Join-Path $source 'Tools/KiasBenchmark/Server/NativeProfiler.cs')).Hash.ToLowerInvariant() -ne $build.nativeProfilerSha256) { throw 'Matched profiler source hash is missing or changed.' }
}
if ($Variant -eq 'B') {
    $telemetryHash = (Get-FileHash -LiteralPath (Join-Path $source 'Tools/KiasBenchmark/Server/KiasTelemetry.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($telemetryHash -ne $build.telemetrySha256) { throw 'KIAS telemetry differs from the compiled source manifest.' }
}
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repo ('.kias-benchmark/replay/' + $Variant + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (Test-Path -LiteralPath $output) { throw 'Replay output already exists; refusing to overwrite.' }
New-Item -ItemType Directory -Path $output | Out-Null
$taskExternalGames=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'SS14.Loader.exe' } | Select-Object Name,ProcessId)
$runManifest = @{
    diagnostic = [bool]$Diagnostic
    maxWallMinutes = $MaxWallMinutes
    strictReady = $false
    ablation = $Ablation
    profiling = [bool]$Profile
    phaseCounters = -not $DisablePhaseCounters
    systemCounters = -not $DisableSystemCounters
    worldgenEnabled = $false
    physicsMode = $(if ($Variant -eq "A") { "A" } else { $PhysicsMode })
    physicsDeviceGroup = $PhysicsDeviceGroup
    physicsQueryCountersEnabled = -not $DisablePhysicsQueryCounters.IsPresent
    nativeNpcPauseWhenNoPlayers = -not $KeepNativeNpcAwake.IsPresent
    variant = $Variant
    assemblySha256 = $build.assemblySha256
    mapSha256 = $mapHash
    nativeLabSha256 = $nativeLabHash
    nativeGameDriversSha256 = $nativeGameDriversHash
    telemetrySha256 = if ($Variant -eq 'B') { $telemetryHash } else { $null }
    metricsEnabled = $true
    diagnosticTimeScale = $DiagnosticTimeScale
    performanceEligible = ($DiagnosticTimeScale -eq 1 -and $AdminLogProbe -eq 0 -and -not $FunctionalOnly -and $taskExternalGames.Count -eq 0)
    externalGameProcesses = $taskExternalGames
    adminLogProbe = $AdminLogProbe
    networkClients = $NetworkClients
    networkHistoryRequired = $NetworkClients -gt 0
    networkAdmission = @{ bindAddress = '127.0.0.1'; maxIpConnections = $taskMaxIpConnections; maxRapidConnections = $taskMaxRapidConnections }
    clientSandboxEnabled = $true
    clientDiscordEnabled = $false
    testOnlyInactivity = @{guestAfkMinutes=$MaxWallMinutes+15;pendingClockinMinutes=$MaxWallMinutes+15;afkSeconds=($MaxWallMinutes+15)*60;scope='Isolated localhost harness; timeout exceeds wall watchdog, production defaults unchanged'}
}
$runManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'run-manifest.json') -Encoding UTF8
$oldEnv = @{}
$settings = @{
    NATIVE_LAB_PHYSICS_MODE = $(if ($Variant -eq "A") { "A" } else { $PhysicsMode })
    NATIVE_LAB_PHYSICS_DEVICE_GROUP = $PhysicsDeviceGroup
    NATIVE_LAB_PHYSICS_QUERY_COUNTERS = $(if ($DisablePhysicsQueryCounters) { '0' } else { '1' })
    NATIVE_LAB_ABLATION = $Ablation
    NATIVE_LAB_PROFILE = $(if ($Profile) {'1'} else {'0'})
    NATIVE_LAB_PHASE_COUNTERS = $(if ($DisablePhaseCounters) {'0'} else {'1'})
    NATIVE_LAB_SYSTEM_COUNTERS = $(if ($DisableSystemCounters) {'0'} else {'1'})
    NATIVE_LAB_SEED = "$Seed"
    NATIVE_LAB_ADMINLOG_PROBE = "$AdminLogProbe"
    NATIVE_LAB_OUTPUT = $output
    NATIVE_LAB_SCENARIO = (Resolve-Path $(if ([IO.Path]::IsPathRooted($Scenario)) { $Scenario } else { Join-Path $repo $Scenario })).Path
    NATIVE_LAB_SHIPS = "$Ships"
    NATIVE_LAB_WARMUP = "$WarmupTicks"
    NATIVE_LAB_TICKS = "$MeasuredTicks"
    NATIVE_LAB_CLIENTS = "$NetworkClients"
    ROBUST_DISABLE_SANDBOX = '0'
}
$server = $null
$taskClients = @()
$taskLogWatches = @()
try {
    foreach ($name in $settings.Keys) {
        $oldEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    $arguments = @('--data-dir', (Join-Path $output 'data'), '--cvar', 'net.port=12299', '--cvar', 'net.bindto=127.0.0.1', '--cvar', 'net.tickrate=60', '--cvar', 'metrics.enabled=true', '--cvar', 'metrics.host=127.0.0.1', '--cvar', 'metrics.port=44880', '--cvar', 'game.auto_pause_empty=false', '--cvar', 'game.lobbyenabled=true', '--cvar', 'game.lobbyduration=999999', '--cvar', 'auth.mode=0', '--cvar', 'worldgen.enabled=false', '+native_lab_start')
    $arguments = @('--cvar', "autokick.guest_afk_minutes=$($MaxWallMinutes+15)", '--cvar', "autokick.pending_clockin_minutes=$($MaxWallMinutes+15)", '--cvar', "afk.time=$(($MaxWallMinutes+15)*60)") + $arguments
    $arguments = @('--cvar', "net.max_ip_connections=$taskMaxIpConnections", '--cvar', "net.max_rapid_connections=$taskMaxRapidConnections") + $arguments
    if ($KeepNativeNpcAwake) { $arguments = @('--cvar', 'npc.pause_when_no_players_in_range=false') + $arguments }
    if ($DiagnosticTimeScale -ne 1) { $arguments = @('--cvar', "game.time_scale=$($DiagnosticTimeScale.ToString([Globalization.CultureInfo]::InvariantCulture))") + $arguments }
    $server = Start-Process -FilePath (Join-Path $source 'bin/Content.Server/Content.Server.exe') -WorkingDirectory $source -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'server.log') -RedirectStandardError (Join-Path $output 'server-error.log')
    foreach ($taskLogName in @('server.log','server-error.log')) {
        $taskLogWatches+=New-KiasNativeLogWatch (Join-Path $output $taskLogName)
    }
    $completed = Join-Path $output 'completed.json'
    $failed = Join-Path $output 'failed.json'
    $lastScrapeTick = -1
    $taskClock = [Diagnostics.Stopwatch]::StartNew()
    $taskLastWall = 0.0
    $taskLastTick = -1
    $taskLastPhase = ''
    $taskLastAdvance = 0.0
    $taskRate = 0.0
    Write-Host "Native $Variant | ships=$Ships | warmup=$WarmupTicks ticks | measured=$MeasuredTicks ticks | ETA calculating..."
    Write-Host "Results: $output"
    while (-not (Test-Path -LiteralPath $completed) -and -not (Test-Path -LiteralPath $failed) -and -not $server.HasExited) {
        Start-Sleep -Seconds 2
        $server.Refresh()
        if ($NetworkClients -gt 0 -and $taskClients.Count -eq 0 -and (Test-Path -LiteralPath (Join-Path $output 'started.json'))) {
            for ($taskIndex=1;$taskIndex -le $NetworkClients;$taskIndex++) {
                $taskUser='KiasBench{0:0000}' -f $taskIndex
                $taskClientArgs=@('--headless','--connect','--connect-address','udp://127.0.0.1:12299','--username',$taskUser,'--cvar','discord.enabled=false','--cvar','display.vsync=false','--cvar','display.max_fps=60')
                $taskClients+=Start-Process -FilePath (Join-Path $source 'bin/Content.Client/Content.Client.exe') -WorkingDirectory $source -ArgumentList $taskClientArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output "$taskUser.log") -RedirectStandardError (Join-Path $output "$taskUser-error.log")
                foreach ($taskLogName in @("$taskUser.log","$taskUser-error.log")) {
                    $taskLogWatches+=New-KiasNativeLogWatch (Join-Path $output $taskLogName)
                }
            }
        }
        foreach ($taskClient in $taskClients) {
            $taskClient.Refresh()
            if ($taskClient.HasExited) { throw "Native network client exited prematurely: PID $($taskClient.Id)." }
        }
        if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($output)).AvailableFreeSpace -lt 1GB) { throw 'Less than 1 GiB free disk space; stopping run to preserve evidence.' }
        if ($taskClock.Elapsed.TotalMinutes -gt $MaxWallMinutes) { throw "Run exceeded wall limit $MaxWallMinutes minutes." }
        Assert-KiasNativeLogs $taskLogWatches -Diagnostic:$Diagnostic -IncidentPath (Join-Path $output 'log-incidents.jsonl')
        $progressPath = Join-Path $output 'progress.json'
        if (-not (Test-Path -LiteralPath $progressPath) -and $taskClock.Elapsed.TotalSeconds -gt 300) { throw 'Native bootstrap did not produce progress within 300 seconds.' }
        if (Test-Path -LiteralPath $progressPath) {
            $progress = Read-NativeProgress $progressPath
            $taskWall = $taskClock.Elapsed.TotalSeconds
            $taskTick = if ($null -ne $progress.currentTick) { [int]$progress.currentTick } else { [int]$progress.relativeTick }
            $taskPhase = if ($progress.phase) { $progress.phase } else { 'MEASURE' }
            if ($taskTick -ne $taskLastTick -or $taskPhase -ne $taskLastPhase) {
                if ($taskLastTick -ge 0 -and $taskPhase -eq $taskLastPhase -and $taskWall -gt $taskLastWall) {
                    $taskObserved = ($taskTick - $taskLastTick) / ($taskWall - $taskLastWall)
                    $taskRate = if ($taskRate -gt 0) { .5 * $taskRate + .5 * $taskObserved } else { $taskObserved }
                } else { $taskRate = 0.0 }
                $taskLastAdvance = $taskWall
                $taskLimit = if ($taskPhase -eq 'WARMUP') { $WarmupTicks } else { $MeasuredTicks }
                $taskPercent = if ($taskLimit -gt 0) { 100.0 * $taskTick / $taskLimit } else { 0 }
                $taskEta = if ($taskRate -gt 0) { [TimeSpan]::FromSeconds(($taskLimit - $taskTick)/$taskRate).ToString('hh\:mm\:ss') } else { 'calculating...' }
                $taskQueue = 0
                if ($progress.telemetry.queues) { foreach ($taskValue in $progress.telemetry.queues.PSObject.Properties.Value) { $taskQueue += [int]$taskValue } }
                Write-Host ("{0} {1} | tick {2}/{3} ({4:F1}%) | TPS {5:F1} | ETA {6} | native done {7}, pending {8} | queue {9} | faults {10} | progress retries {11}" -f
                    $Variant,$taskPhase,$taskTick,$taskLimit,$taskPercent,$taskRate,$taskEta,$progress.completedNative,$progress.pendingNative,$taskQueue,$progress.telemetry.faultedCards,[int]$progress.progressWriteFailures)
                $taskLastTick = $taskTick; $taskLastPhase = $taskPhase; $taskLastWall = $taskWall
            } elseif ($taskWall - $taskLastAdvance -gt 120) { throw "Native heartbeat stalled for 120 seconds at $taskPhase tick $taskTick." }
            if ($taskPhase -eq 'MEASURE' -and $progress.relativeTick -ne $lastScrapeTick) {
                if ($lastScrapeTick -lt 0 -or [int]$progress.relativeTick - [int]$lastScrapeTick -ge 3600) {
                    $lastScrapeTick = $progress.relativeTick
                    Save-NativeMetrics (Join-Path $output "metrics-$lastScrapeTick.prom") ([int]$lastScrapeTick)
                }
                if ($Variant -eq 'B' -and $progress.telemetry.activeGrids -lt ($Ships - $progress.telemetry.expectedOfflineGrids)) { throw "KIAS fleet lost availability at tick $taskTick; see $progressPath." }
                if ($Variant -eq 'B' -and ($progress.telemetry.activeMachines -lt (26 * ($Ships - $progress.telemetry.expectedOfflineGrids)) -or $progress.telemetry.faultedCards -ne 0)) { throw "KIAS machines stopped or faulted at tick $taskTick; see $progressPath." }
                if ($Variant -eq 'B' -and $progress.telemetry.runningCards -ne (26 * $Ships) -and -not $progress.telemetry.availabilityPending) { throw "KIAS cards unavailable without an expected native outage/rebuild at tick $taskTick; see $progressPath." }
            }
        }
    }
    Assert-KiasNativeLogs $taskLogWatches -Diagnostic:$Diagnostic -IncidentPath (Join-Path $output 'log-incidents.jsonl') -Final
    if (Test-Path -LiteralPath $failed) { throw "Native replay failed: $(Get-Content -LiteralPath $failed -Raw)" }
    if (-not (Test-Path -LiteralPath $completed)) { throw "Native replay did not complete. See $output/server.log" }
    $round = Get-Content -LiteralPath (Join-Path $output 'round.json') -Raw | ConvertFrom-Json
    if ($round.runLevel -ne 'InRound' -or $round.roundId -le 0 -or $round.preset -ne 'Sandbox') { throw 'Native replay did not run in a real sandbox round.' }
    Save-NativeMetrics (Join-Path $output 'metrics-final.prom') $MeasuredTicks
    $errors = Select-String -LiteralPath (Join-Path $output 'server.log') -Pattern '\[ERRO\]|\[FATL\]'
    if ($errors) { throw "Native replay logged server errors. Results are invalid: $output/server.log" }
    if ($NetworkClients -gt 0) {
        & python (Join-Path $PSScriptRoot 'network_proof.py') $output *> (Join-Path $output 'network-proof.json')
        if ($LASTEXITCODE -ne 0) { throw "Real client/serialization proof failed: $output/network-proof.json" }
    }
    if (Test-Path -LiteralPath (Join-Path $output 'log-incidents.jsonl')) { throw "Diagnostic run retained log errors: $output/log-incidents.jsonl" }
    Write-Host "Limited native replay completed: $output"
} catch {
    $runManifest.performanceEligible=$false
    $runManifest.failure=$_.Exception.Message
    $runManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'run-manifest.json') -Encoding utf8
    @{ status='FAILED_INCOMPLETE'; error=$_.Exception.Message; variant=$Variant } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'run-failed.json') -Encoding utf8
    throw
} finally {
    foreach ($taskWatch in $taskLogWatches) { $taskWatch.Reader.Dispose() }
    $taskCleanupFailures=@()
    foreach ($taskProcess in (@($server)+@($taskClients))) {
        if ($null -eq $taskProcess) { continue }
        try {
            if (-not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id }
            if (-not $taskProcess.WaitForExit(15000)) { throw "Owned benchmark process did not exit: PID $($taskProcess.Id)." }
        } catch { $taskCleanupFailures+=$_.Exception.Message }
    }
    foreach ($name in $oldEnv.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnv[$name], 'Process') }
    if ($taskCleanupFailures.Count) { throw ($taskCleanupFailures -join '; ') }
}

} finally { Exit-KiasBenchmarkLease $taskBenchmarkLease }
