param([switch]$CheckOnly, [switch]$Quick, [switch]$Legacy, [switch]$FullGame, [switch]$EssentialPreflight, [switch]$Diagnostic, [switch]$Profile,
    [ValidateRange(1,40)][int]$Ships=40, [ValidateRange(2,120)][int]$Minutes=30,
    [int]$Seed=20261010, [switch]$WaitForStart,
    [ValidateRange(1800,108000)][int]$QuickWarmupTicks=1800)
$ErrorActionPreference='Stop'
if ($EssentialPreflight) { throw 'EssentialPreflight retired by v4.1. Use strict default or explicit Diagnostic (NOT_CERTIFIED).' }
$taskOptions=@{}
foreach ($taskName in $PSBoundParameters.Keys) {
    if ($taskName -ne 'WaitForStart') { $taskOptions[$taskName]=$PSBoundParameters[$taskName] }
}
if ($WaitForStart -and -not $CheckOnly) {
    $taskHeading=if ($Profile) {'PROFILE'} elseif ($Diagnostic) {'DIAGNOSTIC NOT_CERTIFIED'} elseif ($Quick) {'QUICK'} else {'STRICT FULL GAME'}
    Write-Host "KIAS | $taskHeading | ОЖИДАНИЕ START" -ForegroundColor Cyan
    if ($Profile) {Write-Host "PROFILE | 1/10/40 кораблей | IDLE/MIXED | 3 повтора A/B + калибровка и причинные пробы | seed $Seed"}
    elseif ($Quick) {Write-Host "QUICK A/B | $([Math]::Max(4,$Ships)) кораблей | 4 клиента | seed $Seed | $QuickWarmupTicks тиков прогрева + 45000 тиков измерения"}
    else {Write-Host "A–B–B–A | $Ships кораблей | seed $Seed | 3 мин прогрева + $Minutes мин измерения"}
    $taskScenarioName=if($Profile){'profile.ps1: IDLE/MIXED'}else{'fullgame_scenario.py'}
    Write-Host "Сценарий: $taskScenarioName; свежие Release A/B и их SHA будут подготовлены после START."
    $taskAuthorMap=Join-Path $PSScriptRoot '../../Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml'
    Write-Host ('Briar SHA256: '+(Get-FileHash -LiteralPath $taskAuthorMap).Hash.ToLowerInvariant())
    Write-Host 'Сборка и тесты ещё не начались. Закройте игры и лишние приложения.'
    Write-Host 'START — начать выбранный запуск. EXIT — закрыть окно.'
    if ($Diagnostic) {
        Write-Host 'ДИАГНОСТИЧЕСКИЙ ABBA: предварительные игровые проверки отложены по решению владельца.' -ForegroundColor Yellow
        Write-Host "$Ships кораблей; A–B–B–A; 3 минуты прогрева + $Minutes минут измерения каждый."
        Write-Host 'Частичные результаты сохраняются после каждого прогона; ошибки не считаются PASS.'
    }
    if ($EssentialPreflight) {
        Write-Host 'ПОЛНЫЙ ПРОМЕЖУТОЧНЫЙ ABBA: сборки, регрессия и игровые проверки обязательны внутри запуска.'
        Write-Host "Параметры: $Ships кораблей; A–B–B–A; по 3 минуты прогрева + $Minutes минут измерения."
        Write-Host 'Расширенная сертификация v3 не завершена; подробности: Tools/KiasBenchmark/DEFERRED_CHECKS.md'
    }
    while ($true) {
        if ([Console]::IsInputRedirected) {
            Write-Host 'Команда'
            $taskCommand=[Console]::ReadLine()
        } else { $taskCommand=Read-Host 'Команда' }
        if ($null -eq $taskCommand) { exit 2 }
        if ($taskCommand.Trim() -ieq 'EXIT') { exit 0 }
        if ($taskCommand.Trim() -ieq 'START') { break }
        Write-Host 'Ожидается START или EXIT. Тесты не запущены.'
    }
}
& (Join-Path $PSScriptRoot 'rerun.ps1') @taskOptions
exit $LASTEXITCODE
