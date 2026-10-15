param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('room_overlap','navigation_collision','collision_rotated','pdc_operator','deferred','composite','systems','pdc_controls','magazine','fanout','vent','fire','combat','collision','world','crew_power','gun')][string[]]$Groups=@('systems','vent','fire','combat','collision','world','composite','deferred','pdc_operator','collision_rotated','navigation_collision','room_overlap'),
    [ValidateSet('A','B')][string[]]$Variants=@('A','B'),
    [ValidateRange(1,40)][int]$Ships=1,
    [int]$Seed=20261010,
    [ValidateRange(0,4)][int]$NetworkClients=0
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskBenchmarkLease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
    Test-KiasBenchmarkPorts
$taskSuiteRoot=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskSuiteRoot) { throw 'Suite output already exists; refusing to overwrite.' }
New-Item -ItemType Directory -Path $taskSuiteRoot | Out-Null
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $taskSuiteRoot 'baseline-manifest.json')
$taskManifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
foreach ($taskBuild in ($taskManifest.builds | Where-Object { $_.label -in $Variants })) {
    $taskFrozen=Join-Path $taskSuiteRoot "frozen-source/$($taskBuild.label)"
    New-Item -ItemType Directory -Path $taskFrozen -Force | Out-Null
    foreach ($taskFile in @('NativeLab.cs','NativeGameDrivers.cs','KiasTelemetry.cs')) {
        $taskInput=Join-Path $taskBuild.source "Tools/KiasBenchmark/Server/$taskFile"
        if (Test-Path -LiteralPath $taskInput) { Copy-Item -LiteralPath $taskInput -Destination $taskFrozen }
    }
}
$taskResults=@()
$taskClock=[Diagnostics.Stopwatch]::StartNew()
foreach ($taskGroup in $Groups) {
    $taskScenario=Join-Path $taskSuiteRoot "$taskGroup.jsonl"
    $taskScenarioSummary=& python (Join-Path $PSScriptRoot 'world_smoke_scenario.py') --ships $Ships --group $taskGroup --output $taskScenario | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Scenario generation failed: $taskGroup" }
    if ($taskScenarioSummary.measuredTicks -le 0 -or $taskScenarioSummary.events -le 0) { throw "Invalid scenario duration or event count: $taskGroup" }
    foreach ($taskVariant in $Variants) {
        $taskRun=Join-Path $taskSuiteRoot "$taskGroup-$taskVariant"
        $taskStarted=$taskClock.Elapsed.TotalSeconds
        $taskError=$null
        Write-Host "Native smoke $($taskResults.Count+1)/$($Groups.Count*$Variants.Count): $taskGroup $taskVariant"
        try {
            & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $taskVariant -Ships $Ships -WarmupTicks 1800 -MeasuredTicks $taskScenarioSummary.measuredTicks -Scenario $taskScenario -FunctionalOnly -KeepNativeNpcAwake -ManifestPath $ManifestPath -OutputDirectory $taskRun -Seed $Seed -NetworkClients $NetworkClients *>&1 | Tee-Object -FilePath (Join-Path $taskSuiteRoot "$taskGroup-$taskVariant.log")
            if (-not (Test-Path -LiteralPath (Join-Path $taskRun 'completed.json'))) { throw 'Missing completed native postconditions.' }
            & python (Join-Path $PSScriptRoot 'native_proof.py') $taskRun $taskScenario *> (Join-Path $taskSuiteRoot "$taskGroup-$taskVariant-proof.json")
            if ($LASTEXITCODE -ne 0) { throw 'Native dispatch/completion proof failed integrity validation.' }
            if ($taskGroup -eq 'room_overlap' -and $taskVariant -eq 'B') {
                $taskRoomProof=Get-Content (Join-Path $taskSuiteRoot "$taskGroup-$taskVariant-proof.json") -Raw | ConvertFrom-Json
                if ($taskRoomProof.doorsObservedDuringBreach -le 0) { throw 'Room regression did not observe native doors during a real breach.' }
                if ($Ships -ge 10 -and $taskRoomProof.roomObservationsStartedPending -le 0) { throw 'Room regression did not exercise pending topology.' }
            }
            if ($NetworkClients -gt 0) {
                & python (Join-Path $PSScriptRoot 'network_proof.py') $taskRun *> (Join-Path $taskSuiteRoot "$taskGroup-$taskVariant-network.json")
                if ($LASTEXITCODE -ne 0) { throw 'Native smoke real-client proof failed.' }
            }
        } catch { $taskError=$_.Exception.Message }
        $taskResults+=@{group=$taskGroup;variant=$taskVariant;status=$(if ($taskError) {'FAIL'} else {'PASS'});error=$taskError;wallSeconds=$taskClock.Elapsed.TotalSeconds-$taskStarted;path=$taskRun}
        @{scope='ISOLATED_NATIVE_SMOKE; not full-game certification';results=$taskResults} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskSuiteRoot 'suite-status.tmp') -Encoding UTF8
        Move-Item -LiteralPath (Join-Path $taskSuiteRoot 'suite-status.tmp') -Destination (Join-Path $taskSuiteRoot 'suite-status.json') -Force
        Write-Host "$taskGroup $taskVariant : $($taskResults[-1].status)"
    }
}
$taskReportError=$null
try {
    if (-not @($taskResults | Where-Object status -ne 'PASS').Count) {
        & python (Join-Path $PSScriptRoot 'fullgame_report.py') $taskSuiteRoot --smoke
        if ($LASTEXITCODE -ne 0) { throw 'Native smoke report failed integrity validation.' }
    }
} catch {
    $taskReportError=$_.Exception.Message
    @{status='FAILED_INCOMPLETE';error=$taskReportError} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskSuiteRoot 'report-status.json') -Encoding utf8
}
& python (Join-Path $PSScriptRoot 'partial_archive.py') $taskSuiteRoot
if ($LASTEXITCODE -ne 0) { throw 'Native smoke archive failed.' }
if ($taskReportError) { throw $taskReportError }
if (@($taskResults | Where-Object status -ne 'PASS').Count) { throw "Native smoke contains failures: $taskSuiteRoot/suite-status.json" }

} finally { Exit-KiasBenchmarkLease $taskBenchmarkLease }
