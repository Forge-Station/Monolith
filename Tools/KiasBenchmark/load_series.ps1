param([Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$ReplayRoot,
    [Parameter(Mandatory)][string]$ManifestPath,[int]$Ships=40,[int]$WarmupTicks=10800,
    [int]$MeasuredTicks=108000,[int]$Seed=20261010,[switch]$Quick,[switch]$Legacy,[switch]$FullGame,[switch]$Diagnostic)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskBenchmarkLease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
    Test-KiasBenchmarkPorts
if ($FullGame -and ($Legacy -or $Quick)) { throw 'FullGame cannot be combined with Legacy or Quick.' }
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$seriesOutput=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $seriesOutput) { throw 'Каталог серии уже существует.' }
New-Item -ItemType Directory -Path $seriesOutput | Out-Null
Set-Location -LiteralPath $repoRoot
$seriesState=@{status='LIMITED_WORKLOAD_ABBA_RUNNING';startedUtc=[DateTime]::UtcNow.ToString('o');order=@('A','B','B','A');ships=$Ships;warmupTicks=$WarmupTicks;measuredTicksPerRun=$MeasuredTicks;tickRate=60;seed=$Seed;fullGameplayCoverage=$false;completedRuns=@();collectorPid=$PID;quick=[bool]$Quick;legacy=[bool]$Legacy}
$seriesState.fullGame=[bool]$FullGame
$seriesState.diagnostic=[bool]$Diagnostic
$seriesState.attemptedRuns=@()
$seriesState.strictReady=$false
if ($FullGame) { $seriesState.status='NATIVE_FULLGAME_ABBA_RUNNING' }
$statePath=Join-Path $seriesOutput 'status.json'
function Save-SeriesState {
    $seriesState | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath ($statePath+'.tmp') -Encoding utf8
    Move-Item -LiteralPath ($statePath+'.tmp') -Destination $statePath -Force
}
try {
    Save-SeriesState
    $scenarioPath=Join-Path $seriesOutput 'scenario.jsonl'
    if ($FullGame) { & python (Join-Path $PSScriptRoot 'fullgame_scenario.py') --ticks $MeasuredTicks --ships $Ships --seed $Seed --output $scenarioPath }
    elseif ($Legacy) { & python (Join-Path $PSScriptRoot 'expanded_scenario.py') --ticks $MeasuredTicks --output $scenarioPath }
    else { & python (Join-Path $PSScriptRoot 'wave_scenario.py') --ticks $MeasuredTicks --ships $Ships --seed $Seed --output $scenarioPath }
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось подготовить сценарий.' }
    $seriesState.scenarioSha256=(Get-FileHash -LiteralPath $scenarioPath).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $seriesOutput 'baseline-manifest.json')
    $taskPair=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    for ($index=0;$index -lt 4;$index++) {
        if ($FullGame) {
            $taskCurrentSource=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0 -or $taskCurrentSource.sha256 -ne $taskPair.sourceFingerprint) { throw 'FullGame source changed after the matched build; current run was not started.' }
        }
        $variant=$seriesState.order[$index]
        $seriesState.currentRun=$index+1
        $seriesState.currentVariant=$variant
        if ($seriesState.completedRuns.Count -gt 0) {
            $taskMeanWall=($seriesState.completedRuns | Measure-Object -Property wallSeconds -Average).Average
            $seriesState.seriesEtaSeconds=$taskMeanWall*(4-$index)
            Write-Host ('Оценка оставшегося времени серии по завершённым прогонам: '+[TimeSpan]::FromSeconds($seriesState.seriesEtaSeconds).ToString('hh\:mm\:ss'))
        } else { Write-Host 'ETA всей серии: вычисляется после первого прогона.' }
        Save-SeriesState
        Write-Host ("Старт {0}/4 — {1} {2}" -f ($index+1),$variant,(Get-Date -Format 'HH:mm:ss'))
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $runOutput=Join-Path ([IO.Path]::GetFullPath($ReplayRoot)) ("{0}-{1}" -f ($index+1),$variant)
        $taskNetworkClients=if ($FullGame) {[Math]::Min(4,$Ships)} else {0}
        $taskRunError=$null
        try {
        & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $variant -Ships $Ships -WarmupTicks $WarmupTicks -MeasuredTicks $MeasuredTicks -Scenario $scenarioPath -OutputDirectory $runOutput -ManifestPath $ManifestPath -Seed $Seed -KeepNativeNpcAwake:$FullGame -NetworkClients $taskNetworkClients -Diagnostic:$Diagnostic
        } catch {
            if (-not $Diagnostic) { throw }
            $taskRunError=$_.Exception.Message
            Write-Host "Прогон $($index+1) завершился ошибкой: $taskRunError. Сохраняем данные, продолжаем серию." -ForegroundColor Yellow
        }
        if ($FullGame -and (Test-Path -LiteralPath $runOutput)) {
            & python (Join-Path $PSScriptRoot 'native_proof.py') $runOutput $scenarioPath *> (Join-Path $runOutput 'native-proof.json')
            if ($LASTEXITCODE -ne 0) {
                if (-not $Diagnostic) { throw "Native evidence integrity failed for run $($index+1)." }
                $taskRunError=(@($taskRunError,'Native evidence integrity failed') | Where-Object {$_}) -join '; '
            }
        }
        $taskAttempt=@{index=$index+1;variant=$variant;path=$runOutput;wallSeconds=$clock.Elapsed.TotalSeconds;error=$taskRunError;valid=($null -eq $taskRunError)}
        $seriesState.attemptedRuns+=$taskAttempt
        if (-not $taskRunError) { $seriesState.completedRuns+=$taskAttempt }
        Save-SeriesState
        if ($Diagnostic) {
            & python (Join-Path $PSScriptRoot 'diagnostic_report.py') $seriesOutput
            if ($LASTEXITCODE -ne 0) { throw 'Diagnostic checkpoint report failed.' }
            & python (Join-Path $PSScriptRoot 'partial_archive.py') ([IO.Path]::GetDirectoryName($seriesOutput))
            if ($LASTEXITCODE -ne 0) { throw 'Diagnostic checkpoint archive failed.' }
            Test-KiasBenchmarkPorts
        }
        if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($seriesOutput)).AvailableFreeSpace -lt 1GB) { throw 'Less than 1 GiB free disk space; series stopped after evidence checkpoint.' }
        Write-Host ("Завершён {0}/4 — {1} {2}, прошло {3:mm\:ss}" -f ($index+1),$variant,(Get-Date -Format 'HH:mm:ss'),$clock.Elapsed)
        Save-SeriesState
    }
    $taskReporter=if ($FullGame) {'fullgame_report.py'} else {'load_report.py'}
    if ($FullGame) {
        $taskCurrentSource=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $taskCurrentSource.sha256 -ne $taskPair.sourceFingerprint) { throw 'FullGame tools or source changed during the series; evidence retained, report certification stopped.' }
    }
    if ($Diagnostic -and $seriesState.completedRuns.Count -ne 4) {
        $seriesState.status='DIAGNOSTIC_ABBA_FINISHED_WITH_ERRORS'
        $seriesState.completedUtc=[DateTime]::UtcNow.ToString('o')
        Save-SeriesState
        & python (Join-Path $PSScriptRoot 'diagnostic_report.py') $seriesOutput
        if ($LASTEXITCODE -ne 0) { throw 'Diagnostic final report failed.' }
        return
    }
    & python (Join-Path $PSScriptRoot $taskReporter) $seriesOutput
    if ($LASTEXITCODE -ne 0) { throw 'Проверка данных ABBA не прошла.' }
    $seriesState.status=if ($Diagnostic) {'DIAGNOSTIC_ABBA_COMPLETE'} elseif ($FullGame) {'NATIVE_FULLGAME_ABBA_COMPLETE'} else {'LIMITED_WORKLOAD_ABBA_COMPLETE'}
    $seriesState.completedUtc=[DateTime]::UtcNow.ToString('o')
    Save-SeriesState
    if ($Diagnostic) {
        & python (Join-Path $PSScriptRoot 'diagnostic_report.py') $seriesOutput
        if ($LASTEXITCODE -ne 0) { throw 'Diagnostic final report failed.' }
    }
} catch {
    $seriesState.status='FAILED_INCOMPLETE'
    $seriesState.error=$_.Exception.Message
    $seriesState.stoppedUtc=[DateTime]::UtcNow.ToString('o')
    Save-SeriesState
    if ($Diagnostic) { & python (Join-Path $PSScriptRoot 'diagnostic_report.py') $seriesOutput }
    throw
}

} finally { Exit-KiasBenchmarkLease $taskBenchmarkLease }
