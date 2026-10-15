param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,10)][int]$Replicates=3,[ValidateRange(900,7200)][int]$MeasuredTicks=1800,
    [ValidateRange(300,10800)][int]$WarmupTicks=1800,[int[]]$Fleets=@(1,10,40),[int]$Seed=20261010,[switch]$Smoke)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$lease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
$root=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) {throw 'Profile output exists; refusing overwrite'}
New-Item -ItemType Directory -Path $root | Out-Null
$state=@{status='PROFILE_RUNNING';completedRuns=@();strictReady=$false;seed=$Seed;replicates=$Replicates;measuredTicks=$MeasuredTicks;warmupTicks=$WarmupTicks}
function Save-ProfileState {
    $state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'profile-status.tmp') -Encoding utf8
    Move-Item -LiteralPath (Join-Path $root 'profile-status.tmp') -Destination (Join-Path $root 'profile-status.json') -Force
}
try {
    Test-KiasBenchmarkPorts
    $pair=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $root 'BASELINE_MANIFEST.json')
    if ($Smoke) {$Fleets=@(1);$Replicates=1}
    $state.replicates=$Replicates; $state.smoke=[bool]$Smoke
    Save-ProfileState
    foreach ($fleet in $Fleets) {
        if ($fleet -lt 1 -or $fleet -gt 40) {throw 'Profile fleets must be 1..40'}
        foreach ($regime in @('IDLE','MIXED')) {
            $scenario=Join-Path $root "$fleet-$regime.jsonl"
            $items=@()
            if ($regime -eq 'MIXED') {
                $steps=@(@(120,'door.open'),@(240,'door.close'),@(300,'crew.critical'),@(480,'crew.recover'),@(600,'crew.depart'),@(720,'crew.return'))
                foreach ($step in $steps) {foreach ($ship in 1..$fleet) {$items+=@{id="profile-$($step[0])-$ship";tick=[int]$step[0];ship=('ship-{0:0000}' -f $ship);type=$step[1]}}}
            }
            [IO.File]::WriteAllText($scenario,(($items | ForEach-Object {$_ | ConvertTo-Json -Compress}) -join "`n"),[Text.UTF8Encoding]::new($false))
            foreach ($replicate in 1..$Replicates) {
                $order=if ($replicate % 2) {@('A','B')} else {@('B','A')}
                foreach ($variant in $order) {
                    $current=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
                    if ($LASTEXITCODE -ne 0 -or $current.sha256 -ne $pair.sourceFingerprint) {throw 'Profile source differs from matched pair'}
                    $run=Join-Path $root "runs/$fleet-$regime-$replicate-$variant-NORMAL"
                    & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $variant -Ships $fleet -WarmupTicks $WarmupTicks -MeasuredTicks $MeasuredTicks -Scenario $scenario -OutputDirectory $run -ManifestPath $ManifestPath -Seed $Seed -Profile -NetworkClients ([Math]::Min(4,$fleet)) -KeepNativeNpcAwake -DisablePhysicsQueryCounters
                    & python (Join-Path $PSScriptRoot 'native_proof.py') $run $scenario *> (Join-Path $run 'native-proof.json')
                    if ($LASTEXITCODE -ne 0) {throw "Profile native proof failed $run"}
                    $state.completedRuns+=@{ships=$fleet;regime=$regime;replicate=$replicate;variant=$variant;probe='NORMAL';path=$run}
                    Save-ProfileState
                }
            }
        }
    }
    if (-not $Smoke -and 40 -in $Fleets) {
        $scenario=Join-Path $root '40-IDLE.jsonl'
        foreach ($probe in @('INSTRUMENT_OFF','PHASE_OFF','RUNTIME_OFF','ROOM_SCAN_OFF')) {
            foreach ($replicate in 1..$Replicates) {
                $variants=if ($probe -eq 'INSTRUMENT_OFF') {@('A','B')} else {@('B')}
                foreach ($variant in $variants) {
                    $current=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
                    if ($LASTEXITCODE -ne 0 -or $current.sha256 -ne $pair.sourceFingerprint) {throw 'Calibration source changed'}
                    $run=Join-Path $root "runs/40-IDLE-$replicate-$variant-$probe"
                    $mode=if ($probe -eq 'RUNTIME_OFF') {'C'} else {'B'}
                    $ablation=if ($probe -eq 'ROOM_SCAN_OFF') {'RoomScanOff'} else {'None'}
                    & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $variant -Ships 40 -WarmupTicks $WarmupTicks -MeasuredTicks $MeasuredTicks -Scenario $scenario -OutputDirectory $run -ManifestPath $ManifestPath -Seed $Seed -Profile -NetworkClients 4 -KeepNativeNpcAwake -DisablePhysicsQueryCounters -DisablePhaseCounters:($probe -in @('INSTRUMENT_OFF','PHASE_OFF')) -DisableSystemCounters:($probe -eq 'INSTRUMENT_OFF') -PhysicsMode $mode -Ablation $ablation
                    & python (Join-Path $PSScriptRoot 'native_proof.py') $run $scenario *> (Join-Path $run 'native-proof.json')
                    if ($LASTEXITCODE -ne 0) {throw "Calibration native proof failed $run"}
                    $state.completedRuns+=@{ships=40;regime='IDLE';replicate=$replicate;variant=$variant;probe=$probe;path=$run}
                    Save-ProfileState
                }
            }
        }
    }
    $state.status='PROFILE_SAMPLES_COMPLETE_ATTRIBUTION_PENDING'
    Save-ProfileState
    & python (Join-Path $PSScriptRoot 'cost_attribution.py') $root
    if ($LASTEXITCODE -ne 0) {throw 'Exclusive attribution validation failed'}
} catch {
    $state.status='FAILED_INCOMPLETE';$state.error=$_.Exception.Message
    Save-ProfileState
    throw
} finally {
    try {
        if ($state.status -eq 'FAILED_INCOMPLETE' -and $state.completedRuns.Count -gt 0) {
            & python (Join-Path $PSScriptRoot 'cost_attribution.py') $root
            if ($LASTEXITCODE -ne 0) {Write-Warning 'Partial attribution failed; preserving original run failure and raw evidence.'}
        }
        & python (Join-Path $PSScriptRoot 'partial_archive.py') $root
        if ($LASTEXITCODE -ne 0) {throw "Profile archive creation failed: $root"}
    } finally {Exit-KiasBenchmarkLease $lease}
}
