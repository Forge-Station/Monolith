param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Diagnose', 'AuditShip', 'FunctionalGate', 'Regression', 'PersistenceCheck', 'CrossCheck', 'VisualCheck', 'PowerCheck', 'PhysicalGate', 'CompileScenario', 'Report', 'Status')]
    [string]$Action,
    [int]$Seed = 20261009,
    [ValidateRange(1, 40)][int]$Ships = 40,
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputDir = Join-Path $repoRoot '.kias-benchmark'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
Push-Location $repoRoot
try {
    if ($Action -eq 'VisualCheck') {
        & dotnet build Content.Client/Content.Client.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabVisual=true *> (Join-Path $outputDir 'ui-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Visual laboratory client did not build; see ui-build.log.' }
        $labClient = $null
        $oldSandbox = $env:ROBUST_DISABLE_SANDBOX
        $oldVisualOutput = $env:KIAS_LAB_UI_OUTPUT
        try {
            $env:ROBUST_DISABLE_SANDBOX = '1'
            $env:KIAS_LAB_UI_OUTPUT = Join-Path $outputDir 'ui'
            $complete = Join-Path $env:KIAS_LAB_UI_OUTPUT 'completed.json'
            if (Test-Path -LiteralPath $complete) { Remove-Item -LiteralPath $complete }
            $labClient = Start-Process -FilePath (Join-Path $repoRoot 'bin/Content.Client/Content.Client.exe') -ArgumentList '+kias_lab_visual', '--cvar', 'display.windowmode=0', '--cvar', 'culture=ru-RU', '--cvar', 'display.uiScale=1', '--cvar', 'interface.resolutionAutoScaleEnabled=false' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputDir 'ui-client.log') -RedirectStandardError (Join-Path $outputDir 'ui-client-error.log')
            $deadline = (Get-Date).AddMinutes(4)
            while (-not (Test-Path -LiteralPath $complete) -and -not $labClient.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2; $labClient.Refresh() }
            if (-not (Test-Path -LiteralPath $complete)) { throw 'Visual capture did not complete; see ui-client.log.' }
            Write-Host 'Desktop captures saved in .kias-benchmark/ui. Visual review is required.'
        } finally {
            if ($null -ne $labClient -and -not $labClient.HasExited) { Stop-Process -Id $labClient.Id }
            $env:ROBUST_DISABLE_SANDBOX = $oldSandbox
            $env:KIAS_LAB_UI_OUTPUT = $oldVisualOutput
            & dotnet build Content.Client/Content.Client.csproj -c Release --no-restore -m:4 -nr:false -p:KiasLabVisual=false *> (Join-Path $outputDir 'ui-restore-build.log')
            if ($LASTEXITCODE -ne 0) { throw 'Normal client rebuild failed; see ui-restore-build.log.' }
        }
        return
    }
    if ($Action -in @('FunctionalGate', 'Regression', 'PersistenceCheck', 'CrossCheck', 'PowerCheck', 'PhysicalGate')) {
        $env:KIAS_LAB_OUTPUT = Join-Path $outputDir 'live'
        $filter = 'FullyQualifiedName~SavedBriarLoadsAndScannerModulesSurviveRoundTrip'
        $logName = 'functional-gate.log'
        $trxName = 'briar.trx'
        if ($Action -eq 'FunctionalGate') {
            $filter = 'FullyQualifiedName~KiasBriar'
            $logName = 'briar-all.log'
            $trxName = 'briar-all.trx'
        }
        if ($Action -eq 'Regression') {
            $filter = 'FullyQualifiedName~Tests._Forge.KIAS'
            $logName = 'kias-regression.log'
            $trxName = 'kias-regression.trx'
        }
        if ($Action -eq 'CrossCheck') {
            $filter = 'FullyQualifiedName~KiasBriarCrossGateTests'
            $logName = 'cross-check.log'
            $trxName = 'cross.trx'
        }
        if ($Action -eq 'PowerCheck') {
            $filter = 'FullyQualifiedName~SavedBriarReceivesRealBatteryPowerAndResolvesGraphTargets'
            $logName = 'powered-check.log'
            $trxName = 'powered.trx'
        }
        if ($Action -eq 'PhysicalGate') {
            $filter = 'FullyQualifiedName~KiasBriarPhysicalGateTests'
            $logName = 'physical-gate.log'
            $trxName = 'physical.trx'
        }
        & dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore -m:4 -nr:false --filter $filter --logger "trx;LogFileName=$trxName" --results-directory (Join-Path $outputDir 'tests') *> (Join-Path $outputDir $logName)
        $gateExit = $LASTEXITCODE
        if ($Action -in @('PowerCheck', 'PhysicalGate', 'CrossCheck', 'FunctionalGate', 'Regression')) {
            & $Python (Join-Path $PSScriptRoot 'lab.py') Report
            if ($gateExit -ne 0) { throw "$Action failed. See $outputDir/$logName" }
            return
        }
        & $Python (Join-Path $PSScriptRoot 'lab.py') RecordGate --exit-code $gateExit
        if ($LASTEXITCODE -ne 0) { throw 'Could not record functional gate results.' }
        if ($gateExit -ne 0) { throw "Briar load/round-trip failed. See $outputDir/functional-gate.log" }
        Write-Host 'Briar persistence check completed. This is not the 27-program functional gate; see FUNCTIONAL_REPORT.md.'
    } else {
        & $Python (Join-Path $PSScriptRoot 'lab.py') $Action --seed $Seed --ships $Ships
        if ($LASTEXITCODE -ne 0) { throw "Laboratory action failed: $Action" }
    }
} finally {
    Pop-Location
}
