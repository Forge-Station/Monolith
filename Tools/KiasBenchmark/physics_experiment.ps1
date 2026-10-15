param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(2,10)][int]$Repetitions=2, [switch]$Probe, [switch]$Short)
$ErrorActionPreference='Stop'
if($Probe -and $Short){throw 'Short performance and functional Probe are separate modes'}
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskBenchmarkLease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
    Test-KiasBenchmarkPorts
$taskRepo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $taskRepo
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskManifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$taskFingerprint=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $taskFingerprint.sha256 -ne $taskManifest.sourceFingerprint) { throw 'REBUILD_REQUIRED: physics requires fresh matched builds.' }
$taskArgs=@('plan',$taskOutput,'--repetitions',"$Repetitions")
if ($Probe) { $taskArgs+='--probe' }
if ($Short) { $taskArgs+='--short' }
& python (Join-Path $PSScriptRoot 'physics_experiment.py') @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'Physics experiment planning failed.' }
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $taskOutput 'baseline-manifest.json')
$taskPlan=Get-Content (Join-Path $taskOutput 'experiment-plan.json') -Raw | ConvertFrom-Json
$taskState=@{status='RUNNING';finalABBAExecuted=$false;completed=@();functionalProbe=[bool]$Probe}
try {
    foreach ($taskRun in $taskPlan.runs) {
        $taskState.current=$taskRun.id
        $taskState | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput 'status.json') -Encoding utf8
        $taskCurrent=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $taskCurrent.sha256 -ne $taskManifest.sourceFingerprint) { throw 'Source changed during physics experiment.' }
        Write-Host "PHYSICS $($taskState.completed.Count+1)/$($taskPlan.runs.Count): $($taskRun.id), 40 ships; final ABBA is not running"
        & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $taskRun.variant -PhysicsMode $taskRun.physicsMode -PhysicsDeviceGroup KiasPrototypes -DisablePhysicsQueryCounters:(-not $taskRun.queryCounters) -Ships 40 -WarmupTicks $taskPlan.warmupTicks -MeasuredTicks $taskPlan.measuredTicks -Seed $taskPlan.seed -Scenario (Join-Path $taskOutput 'scenario.jsonl') -ManifestPath $ManifestPath -OutputDirectory (Join-Path $taskOutput $taskRun.id) -KeepNativeNpcAwake -FunctionalOnly:$Probe *>&1 | Tee-Object -FilePath (Join-Path $taskOutput ($taskRun.id+'.log'))
        & python (Join-Path $PSScriptRoot 'native_proof.py') (Join-Path $taskOutput $taskRun.id) (Join-Path $taskOutput 'scenario.jsonl') *> (Join-Path $taskOutput ($taskRun.id+'-proof.json'))
        if ($LASTEXITCODE -ne 0) { throw "Native physics proof failed: $($taskRun.id)" }
        $taskState.completed+=@($taskRun.id)
    }
    $taskCurrent=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $taskCurrent.sha256 -ne $taskManifest.sourceFingerprint) { throw 'Source changed before physics report.' }
    & python (Join-Path $PSScriptRoot 'physics_experiment.py') report $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Physics experiment report failed integrity checks.' }
    $taskState.status='REPLICATED_PHYSICS_DIAGNOSTIC_COMPLETE'
} catch {
    $taskState.status='FAILED_INCOMPLETE'
    $taskState.error=$_.Exception.Message
    throw
} finally {
    $taskState | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput 'status.json') -Encoding utf8
    & python (Join-Path $PSScriptRoot 'partial_archive.py') $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Physics archive failed.' }
}

} finally { Exit-KiasBenchmarkLease $taskBenchmarkLease }
