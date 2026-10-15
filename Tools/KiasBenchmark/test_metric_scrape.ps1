$ErrorActionPreference='Stop'
$taskErrors=$null
$taskAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'native_replay.ps1'),[ref]$null,[ref]$taskErrors)
if ($taskErrors) { throw 'Native replay syntax is invalid.' }
$taskFunction=$taskAst.Find({param($taskNode) $taskNode -is [Management.Automation.Language.FunctionDefinitionAst] -and $taskNode.Name -eq 'Save-NativeMetrics'},$true)
if ($null -eq $taskFunction) { throw 'Metrics collector function missing.' }
. ([ScriptBlock]::Create($taskFunction.Extent.Text))
$taskOutput=[IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '../../.kias-benchmark/metric-scrape-tests') ([Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$script:taskCalls=0
$script:taskAlwaysFail=$false
function Invoke-WebRequest {
    param([string]$Uri,[switch]$UseBasicParsing,[int]$TimeoutSec,[string]$OutFile)
    if ($TimeoutSec -ne 45) { throw 'Unexpected request bound.' }
    $script:taskCalls++
    if ($script:taskAlwaysFail -or $script:taskCalls -eq 1) {
        [IO.File]::WriteAllText($OutFile,'PARTIAL_TEST_RESPONSE')
        throw 'Simulated HTTP timeout'
    }
    [IO.File]::WriteAllText($OutFile,'complete_test_metric 1')
}
$taskSuccess=Join-Path $taskOutput 'recovered'
New-Item -ItemType Directory -Path $taskSuccess | Out-Null
Save-NativeMetrics (Join-Path $taskSuccess 'metrics-0.prom') 0
$taskLog=@(Get-Content -LiteralPath (Join-Path $taskSuccess 'metric-scrapes.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
if ($script:taskCalls -ne 2 -or $taskLog.Count -ne 2 -or $taskLog[0].succeeded -or -not $taskLog[1].succeeded) { throw 'Bounded recovery proof failed.' }
if ((Get-Content -LiteralPath (Join-Path $taskSuccess 'metrics-0.prom') -Raw) -ne 'complete_test_metric 1') { throw 'Partial response was published.' }
$taskFailure=Join-Path $taskOutput 'failed'
New-Item -ItemType Directory -Path $taskFailure | Out-Null
$script:taskCalls=0
$script:taskAlwaysFail=$true
$taskCaught=$false
try { Save-NativeMetrics (Join-Path $taskFailure 'metrics-final.prom') 1800 }
catch { $taskCaught=$_.Exception.Message -like 'Native metrics unavailable after two bounded attempts:*' }
$taskLog=@(Get-Content -LiteralPath (Join-Path $taskFailure 'metric-scrapes.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
if (-not $taskCaught -or $script:taskCalls -ne 2 -or $taskLog.Count -ne 2 -or @($taskLog | Where-Object succeeded).Count) { throw 'Persistent metrics failure was hidden or unbounded.' }
if (Test-Path -LiteralPath (Join-Path $taskFailure 'metrics-final.prom')) { throw 'Failed response was published as complete.' }
Write-Host "Metrics collector tests: PASS; evidence $taskOutput"
