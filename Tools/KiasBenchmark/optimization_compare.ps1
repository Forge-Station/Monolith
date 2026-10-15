param([Parameter(Mandatory)][string]$BeforeManifest,[Parameter(Mandatory)][string]$AfterManifest,
    [Parameter(Mandatory)][string]$ScenarioDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(3,10)][int]$Replicates=3)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$lease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
$root=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $root){Exit-KiasBenchmarkLease $lease;throw 'Refusing to overwrite optimization evidence'}
New-Item -ItemType Directory -Path $root | Out-Null
$before=Get-Content $BeforeManifest -Raw | ConvertFrom-Json
$after=Get-Content $AfterManifest -Raw | ConvertFrom-Json
$state=@{status='OPTIMIZATION_RUNNING';completedRuns=@();strictReady=$false;beforeFingerprint=$before.sourceFingerprint;afterFingerprint=$after.sourceFingerprint}
function Save-OptimizationState {
    $state | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'optimization-status.tmp') -Encoding utf8
    Move-Item (Join-Path $root 'optimization-status.tmp') (Join-Path $root 'optimization-status.json') -Force
}
function Invoke-OptimizationRun([string]$Regime,[int]$Replicate,[string]$Probe){
    $current=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or $current.sha256 -ne $after.sourceFingerprint){throw 'Optimization sources changed'}
    $manifest=if($Probe -eq 'BEFORE'){$BeforeManifest}else{$AfterManifest}
    $ablation=if($Probe -in @('BEFORE','AFTER')){'None'}else{$Probe}
    $scenario=Join-Path $root "40-$Regime.jsonl"
    $run=Join-Path $root "runs/$Regime-$Replicate-$Probe"
    & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant B -Ships 40 -WarmupTicks 1800 -MeasuredTicks 1800 -Scenario $scenario -OutputDirectory $run -ManifestPath $manifest -Seed 20261010 -Profile -NetworkClients 4 -KeepNativeNpcAwake -DisablePhysicsQueryCounters -Ablation $ablation
    & python (Join-Path $PSScriptRoot 'native_proof.py') $run $scenario *> (Join-Path $run 'native-proof.json')
    if($LASTEXITCODE -ne 0){throw "Optimization native proof failed $run"}
    $state.completedRuns+=@{regime=$Regime;replicate=$Replicate;probe=$Probe;path=$run}
    Save-OptimizationState
}
try {
    Test-KiasBenchmarkPorts
    if($before.engine -ne $after.engine -or $before.staticParity.bMapSha256 -ne $after.staticParity.bMapSha256){throw 'Optimization engine/map parity mismatch'}
    Copy-Item $BeforeManifest (Join-Path $root 'BEFORE_MANIFEST.json')
    Copy-Item $AfterManifest (Join-Path $root 'AFTER_MANIFEST.json')
    foreach($regime in @('IDLE','MIXED')){
        Copy-Item (Join-Path $ScenarioDirectory "40-$regime.jsonl") (Join-Path $root "40-$regime.jsonl")
        foreach($replicate in 1..$Replicates){
            $order=if($replicate%2){@('BEFORE','AFTER')}else{@('AFTER','BEFORE')}
            foreach($probe in $order){Invoke-OptimizationRun $regime $replicate $probe}
        }
    }
    foreach($replicate in 1..$Replicates){
        $probes=if($replicate%2){@('CrewFilterOff','CoveragePassOff','RuntimeIdleOff')}else{@('RuntimeIdleOff','CoveragePassOff','CrewFilterOff')}
        foreach($probe in $probes){Invoke-OptimizationRun 'IDLE' $replicate $probe}
    }
    $state.status='OPTIMIZATION_SAMPLES_COMPLETE';Save-OptimizationState
    & python (Join-Path $PSScriptRoot 'optimization_report.py') $root
    if($LASTEXITCODE -ne 0){throw 'Optimization accounting failed'}
} catch {
    $state.status='FAILED_INCOMPLETE';$state.error=$_.Exception.Message;Save-OptimizationState
    throw
} finally {
    try {
        & python (Join-Path $PSScriptRoot 'partial_archive.py') $root
        if($LASTEXITCODE -ne 0){throw "Optimization archive failed: $root"}
    } finally {Exit-KiasBenchmarkLease $lease}
}
