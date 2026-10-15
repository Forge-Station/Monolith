function New-KiasNativeLogWatch([string]$Path) {
    $taskStream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    return [pscustomobject]@{Path=$Path;Reader=[IO.StreamReader]::new($taskStream);Tail=''}
}
function Assert-KiasNativeLogs($Watches,[switch]$Diagnostic,[string]$IncidentPath,[switch]$Final) {
    foreach ($taskWatch in $Watches) {
        $taskWatch.Tail += $taskWatch.Reader.ReadToEnd()
        if ($taskWatch.Tail -match '\[ERRO\]|\[FATL\]') {
            if ($IncidentPath) {
                @{utc=[DateTime]::UtcNow.ToString('o');path=$taskWatch.Path;action='ABORT_RUN';text=$taskWatch.Tail.Substring(0,[Math]::Min(4096,$taskWatch.Tail.Length))} | ConvertTo-Json -Compress | Add-Content -LiteralPath $IncidentPath -Encoding utf8
            }
            throw "Native replay logged an error; stopping immediately. See $($taskWatch.Path)"
        }
        if ($taskWatch.Tail.Length -gt 16) { $taskWatch.Tail=$taskWatch.Tail.Substring($taskWatch.Tail.Length-16) }
    }
}
