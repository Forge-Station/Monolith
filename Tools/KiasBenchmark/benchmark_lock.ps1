function Enter-KiasBenchmarkLease([string]$Repository) {
    $taskLeasePath=[IO.Path]::GetFullPath((Join-Path $Repository '.kias-benchmark/execution.lock'))
    if (-not (Get-Variable -Name KiasBenchmarkLeases -Scope Global -ErrorAction SilentlyContinue)) {
        $global:KiasBenchmarkLeases=@{}
    }
    if ($global:KiasBenchmarkLeases.ContainsKey($taskLeasePath)) {
        $global:KiasBenchmarkLeases[$taskLeasePath].Depth++
        return $taskLeasePath
    }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskLeasePath)) -Force | Out-Null
    try { $taskLeaseStream=[IO.File]::Open($taskLeasePath,'OpenOrCreate','ReadWrite','None') }
    catch { throw 'BENCHMARK_BUSY: другая сборка или проверка KIAS уже работает. Дождитесь её завершения.' }
    try {
        $taskLeaseStream.SetLength(0)
        $taskLeaseBytes=[Text.Encoding]::UTF8.GetBytes((@{pid=$PID;startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress))
        $taskLeaseStream.Write($taskLeaseBytes,0,$taskLeaseBytes.Length)
        $taskLeaseStream.Flush()
        $global:KiasBenchmarkLeases[$taskLeasePath]=@{Stream=$taskLeaseStream;Depth=1}
    } catch { $taskLeaseStream.Dispose(); throw }
    return $taskLeasePath
}

function Exit-KiasBenchmarkLease([string]$Lease) {
    if (-not $Lease -or -not $global:KiasBenchmarkLeases.ContainsKey($Lease)) { return }
    $taskLeaseEntry=$global:KiasBenchmarkLeases[$Lease]
    $taskLeaseEntry.Depth--
    if ($taskLeaseEntry.Depth -eq 0) {
        $taskLeaseEntry.Stream.Dispose()
        $global:KiasBenchmarkLeases.Remove($Lease)
    }
}

function Test-KiasBenchmarkPorts([int]$GamePort=12299,[int]$MetricsPort=44880) {
    foreach ($taskPort in @($GamePort,$MetricsPort)) {
        $taskListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$taskPort)
        $taskListener.Server.ExclusiveAddressUse=$true
        try { $taskListener.Start() }
        catch { throw "BENCHMARK_PORT_BUSY: TCP $taskPort занят; сервер не запущен." }
        finally { $taskListener.Stop() }
    }
    $taskUdp=[Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.SocketType]::Dgram,[Net.Sockets.ProtocolType]::Udp)
    $taskUdp.ExclusiveAddressUse=$true
    try { $taskUdp.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,$GamePort)) }
    catch { throw "BENCHMARK_PORT_BUSY: UDP $GamePort занят; сервер не запущен." }
    finally { $taskUdp.Dispose() }
}
