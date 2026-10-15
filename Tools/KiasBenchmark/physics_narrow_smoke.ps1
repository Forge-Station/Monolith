param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$OutputDirectory,[int]$Seed=20261010)
$ErrorActionPreference='Stop'
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskOutput) { throw 'Physics output already exists; refusing to overwrite.' }
New-Item -ItemType Directory -Path $taskOutput | Out-Null
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $taskOutput 'baseline-manifest.json')
$taskScenario=Join-Path $taskOutput 'scenario.jsonl'
$taskInputs=@()
foreach ($taskEvent in @(@{tick=120;type='collision.start'},@{tick=780;type='collision.clear'})) {
    foreach ($taskShip in 1..40) {
        $taskInputs+=(@{id=('narrow-'+$taskEvent.type+'-{0:0000}' -f $taskShip);tick=$taskEvent.tick;ship=('ship-{0:0000}' -f $taskShip);type=$taskEvent.type} | ConvertTo-Json -Compress)
    }
}
[IO.File]::WriteAllLines($taskScenario,$taskInputs,[Text.UTF8Encoding]::new($false))
$taskState=@{status='RUNNING';scope='Restricted diagnostic native smoke; not performance acceptance';completed=@()}
try {
    foreach ($taskLabel in @('B','D2')) {
        $taskMode=if ($taskLabel -eq 'D2') {'D'} else {'B'}
        $taskRun=Join-Path $taskOutput $taskLabel
        & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant B -Ships 40 -WarmupTicks 1800 -MeasuredTicks 1800 -Scenario $taskScenario -ManifestPath $ManifestPath -OutputDirectory $taskRun -Seed $Seed -FunctionalOnly -KeepNativeNpcAwake -PhysicsMode $taskMode -PhysicsDeviceGroup KiasPrototypes *>&1 | Tee-Object -FilePath (Join-Path $taskOutput ($taskLabel+'-console.log'))
        & python (Join-Path $PSScriptRoot 'native_proof.py') $taskRun $taskScenario *> (Join-Path $taskOutput ($taskLabel+'-proof.json'))
        if ($LASTEXITCODE -ne 0) { throw "Restricted physics native proof failed: $taskLabel" }
        $taskState.completed+=@($taskLabel)
    }
    & python (Join-Path $PSScriptRoot 'physics_report.py') $taskOutput --narrow-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Restricted physics report failed integrity validation.' }
    $taskState.status='RESTRICTED_D2_NATIVE_SMOKE_COMPLETE'
} catch {
    $taskState.status='FAILED_INCOMPLETE'
    $taskState.error=$_.Exception.Message
    throw
} finally {
    $taskState | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'status.json') -Encoding utf8
    & python (Join-Path $PSScriptRoot 'partial_archive.py') $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Restricted physics archive failed.' }
}
