$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskTestRoot=Join-Path ([IO.Path]::GetTempPath()) ('kias-lease-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTestRoot | Out-Null
$taskHelper=Join-Path $PSScriptRoot 'benchmark_lock.ps1'
$taskChild=Join-Path $taskTestRoot 'probe.ps1'
@'
param([string]$Helper,[string]$Repository)
$ErrorActionPreference='Stop'
. $Helper
try { $lease=Enter-KiasBenchmarkLease $Repository }
catch { if ($_.Exception.Message -like 'BENCHMARK_BUSY:*') { exit 23 }; throw }
try { Write-Host 'ACQUIRED' } finally { Exit-KiasBenchmarkLease $lease }
exit 0
'@ | Set-Content -LiteralPath $taskChild -Encoding utf8
function Assert-KiasLeaseProbe([int]$Expected) {
    & (Get-Process -Id $PID).Path -NoProfile -File $taskChild -Helper $taskHelper -Repository $taskTestRoot
    if ($LASTEXITCODE -ne $Expected) { throw "Lease probe expected $Expected, got $LASTEXITCODE" }
}
$taskOuter=$null
$taskInner=$null
try {
    $taskOuter=Enter-KiasBenchmarkLease $taskTestRoot
    Assert-KiasLeaseProbe 23
    $taskInner=Enter-KiasBenchmarkLease $taskTestRoot
    Exit-KiasBenchmarkLease $taskOuter
    $taskOuter=$null
    Assert-KiasLeaseProbe 23
    Exit-KiasBenchmarkLease $taskInner
    $taskInner=$null
    Assert-KiasLeaseProbe 0
    try {
        $taskOuter=Enter-KiasBenchmarkLease $taskTestRoot
        try { throw 'expected-test-error' } finally { Exit-KiasBenchmarkLease $taskOuter; $taskOuter=$null }
    } catch { if ($_.Exception.Message -ne 'expected-test-error') { throw } }
    Assert-KiasLeaseProbe 0
    $taskUdp=[Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.SocketType]::Dgram,[Net.Sockets.ProtocolType]::Udp)
    $taskUdp.ExclusiveAddressUse=$true
    try {
        $taskUdp.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
        $taskOccupiedPort=$taskUdp.LocalEndPoint.Port
        $taskRejected=$false
        try { Test-KiasBenchmarkPorts -GamePort $taskOccupiedPort -MetricsPort 0 }
        catch { $taskRejected=$_.Exception.Message -like "BENCHMARK_PORT_BUSY: UDP $taskOccupiedPort*" }
        if (-not $taskRejected) { throw 'Occupied UDP port was not rejected.' }
    } finally { $taskUdp.Dispose() }
    Test-KiasBenchmarkPorts -GamePort $taskOccupiedPort -MetricsPort 0
    $taskTcp=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $taskTcp.Server.ExclusiveAddressUse=$true
    try {
        $taskTcp.Start()
        $taskOccupiedPort=$taskTcp.LocalEndpoint.Port
        $taskRejected=$false
        try { Test-KiasBenchmarkPorts -GamePort 0 -MetricsPort $taskOccupiedPort }
        catch { $taskRejected=$_.Exception.Message -like "BENCHMARK_PORT_BUSY: TCP $taskOccupiedPort*" }
        if (-not $taskRejected) { throw 'Occupied metrics port was not rejected.' }
    } finally { $taskTcp.Stop() }
    foreach ($taskEntry in @('rerun','build_pair','native_suite','native_replay','load_series','physics_experiment','profile','optimization_compare','live_ui','scanner_ui')) {
        $taskErrors=$null
        $taskAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot "$taskEntry.ps1"),[ref]$null,[ref]$taskErrors)
        if ($taskErrors) { throw "Syntax failure: $taskEntry" }
        $taskCommands=@($taskAst.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true) | ForEach-Object { $_.GetCommandName() })
        foreach ($taskRequired in @('Enter-KiasBenchmarkLease','Exit-KiasBenchmarkLease','Test-KiasBenchmarkPorts')) {
            if ($taskRequired -notin $taskCommands) { throw "$taskEntry missing $taskRequired" }
        }
    }
    Write-Host 'Benchmark lock: PASS (cross-process exclusion, nesting, exception cleanup, UDP/TCP ports, ten entrypoints)'
} finally {
    Exit-KiasBenchmarkLease $taskInner
    Exit-KiasBenchmarkLease $taskOuter
    Remove-Item -LiteralPath $taskChild
    Remove-Item -LiteralPath (Join-Path $taskTestRoot '.kias-benchmark/execution.lock') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $taskTestRoot '.kias-benchmark') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $taskTestRoot
}
