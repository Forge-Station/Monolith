$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'native_log_watch.ps1')
$taskRoot=Join-Path ([IO.Path]::GetTempPath()) ('kias-log-watch-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot | Out-Null
$taskWatches=@()
try {
    foreach ($taskName in @('server.log','client.log','client-error.log')) {
        $taskPath=Join-Path $taskRoot $taskName
        [IO.File]::WriteAllText($taskPath,'[INFO] started')
        $taskWatches+=New-KiasNativeLogWatch $taskPath
    }
    Assert-KiasNativeLogs $taskWatches
    [IO.File]::AppendAllText($taskWatches[1].Path,'[ER')
    Assert-KiasNativeLogs $taskWatches
    [IO.File]::AppendAllText($taskWatches[1].Path,'RO] client failure')
    $taskCaught=$false
    try {Assert-KiasNativeLogs $taskWatches} catch {
        if ($_.Exception.Message -notlike '*client.log') {throw}
        $taskCaught=$true
    }
    if (-not $taskCaught) {throw 'Split client error was not rejected'}
    $taskWatches[1].Tail=''
    [IO.File]::AppendAllText($taskWatches[2].Path,'[FATL] client stderr failure')
    $taskCaught=$false
    try {Assert-KiasNativeLogs $taskWatches} catch {
        if ($_.Exception.Message -notlike '*client-error.log') {throw}
        $taskCaught=$true
    }
    if (-not $taskCaught) {throw 'Client stderr fatal error was not rejected'}
    $taskDiagnosticPath=Join-Path $taskRoot 'KiasBench0001.log'
    [IO.File]::WriteAllText($taskDiagnosticPath,'')
    $taskDiagnosticWatch=New-KiasNativeLogWatch $taskDiagnosticPath
    $taskWatches+=$taskDiagnosticWatch
    $taskIncidents=Join-Path $taskRoot 'incidents.jsonl'
    [IO.File]::AppendAllText($taskDiagnosticPath,"[ERRO] system.joint: While adding joint docking18008 to entity briarKIAS (15/n17997), the joint already existed for the connected entity grid (2440/n152977).`n")
    $taskCaught=$false
    try {Assert-KiasNativeLogs @($taskDiagnosticWatch) -Diagnostic -IncidentPath $taskIncidents} catch {$taskCaught=$true}
    if (-not $taskCaught) {throw 'Diagnostic mode suppressed duplicate docking joint error'}
    $taskRecorded=Get-Content -LiteralPath $taskIncidents | ConvertFrom-Json
    if ($taskRecorded.action -ne 'ABORT_RUN') {throw 'Client joint error was not recorded as fatal to run'}
    [IO.File]::AppendAllText($taskWatches[0].Path,'[ERRO] server failure')
    $taskCaught=$false
    try {Assert-KiasNativeLogs @($taskWatches[0]) -Diagnostic -IncidentPath $taskIncidents} catch {$taskCaught=$true}
    if (-not $taskCaught) {throw 'Diagnostic mode allowed server errors'}
    $taskDiagnosticWatch.Tail=''
    [IO.File]::AppendAllText($taskDiagnosticPath,'[FATL] client fatal')
    $taskCaught=$false
    try {Assert-KiasNativeLogs @($taskDiagnosticWatch) -Diagnostic -IncidentPath $taskIncidents} catch {$taskCaught=$true}
    if (-not $taskCaught) {throw 'Diagnostic mode allowed client fatal errors'}
    Write-Host 'PASS live logs: strict split ERRO/FATL; diagnostic docking/client/server errors rejected without whitelist'
} finally {
    foreach ($taskWatch in $taskWatches) {$taskWatch.Reader.Dispose()}
    $taskResolved=[IO.Path]::GetFullPath($taskRoot)
    if (-not $taskResolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($taskResolved) -notmatch '^kias-log-watch-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the temporary log test directory'
    }
    Remove-Item -LiteralPath $taskRoot -Recurse -Force
}
