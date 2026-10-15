param([switch]$CheckOnly, [switch]$Quick, [switch]$Legacy, [switch]$FullGame, [switch]$EssentialPreflight, [switch]$Diagnostic, [switch]$Profile,
    [ValidateRange(1,40)][int]$Ships=40, [ValidateRange(2,120)][int]$Minutes=30, [int]$Seed=20261010,
    [ValidateRange(1800,108000)][int]$QuickWarmupTicks=1800)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $repo
$bench=Join-Path $repo '.kias-benchmark'
$pointer=Join-Path $bench 'CURRENT_PAIR.txt'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskBenchmarkLease=$null
$current=$null
$exitCode=0
$taskTranscript=$false
$taskBenchmarkKind=if ($Diagnostic) {'OWNER_DIAGNOSTIC_ABBA'} elseif ($EssentialPreflight) {'INTERMEDIATE_FULL_GAME_ABBA'} else {'STRICT_V4_1'}
function Set-LauncherStage([int]$Number,[string]$Name,[string]$Status='RUNNING') {
    Write-Host "ЭТАП $Number/7: $Name | $Status"
    @{stage=$Number;stages=7;name=$Name;updatedUtc=[DateTime]::UtcNow.ToString('o');status=$Status} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $current 'launcher-stage.tmp') -Encoding utf8
    Move-Item -LiteralPath (Join-Path $current 'launcher-stage.tmp') -Destination (Join-Path $current 'launcher-stage.json') -Force
}
try {
    if ($EssentialPreflight) { throw 'EssentialPreflight retired by v4.1; strict preflight cannot be waived implicitly.' }
    $taskBenchmarkLease=Enter-KiasBenchmarkLease $repo
    if ($Profile -and ($Quick -or $Legacy -or $CheckOnly -or $Diagnostic -or $FullGame)) {throw 'Profile is a separate measurement mode, not an ABBA readiness waiver.'}
    if ($Diagnostic -and ($CheckOnly -or $Quick -or $Legacy -or $EssentialPreflight)) { throw 'Diagnostic requires a separate full ABBA launch; CheckOnly remains strict.' }
    if (($Quick -and $Legacy) -or ($FullGame -and ($Quick -or $Legacy))) { throw 'Выберите один режим: FullGame, Quick или Legacy.' }
    if (-not $Quick -and -not $Legacy -and $Minutes -lt 13) { throw 'FullGame требует минимум 13 минут измерения, чтобы вместить все игровые сценарии.' }
    Get-Command python,dotnet -ErrorAction Stop | Out-Null
    & python -c 'import sys; assert sys.version_info >= (3,10)'
    if ($LASTEXITCODE -ne 0) { throw 'Нужен Python 3.10+.' }
    $taskReadinessExit=0
    if ($CheckOnly) {
        & python (Join-Path $PSScriptRoot 'readiness.py')
        $taskReadinessExit=$LASTEXITCODE
    }
    foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
        $parseErrors=$null
        [void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$null,[ref]$parseErrors)
        if ($parseErrors) { throw "Синтаксис $($script.Name): $parseErrors" }
    }
    $active=@(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq 'Content.Server.exe' -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($bench+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
    })
    if ($active.Count) { throw 'Тестовый сервер уже работает. Дождитесь завершения серии.' }
    $taskWorkspaceProcesses=@(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -in @('Content.Server.exe','Content.Client.exe') -and $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith((Join-Path $repo 'bin')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
    })
    if ($taskWorkspaceProcesses.Count) {
        throw "BUILD_BLOCKED: закройте запущенный клиент/сервер Monolith перед проверкой; DLL заняты. PID: $($taskWorkspaceProcesses.ProcessId -join ', ')."
    }
    if (-not $CheckOnly -and -not $Quick) {
        $taskExternalGames=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'SS14.Loader.exe' })
        if ($taskExternalGames.Count) { throw "PERFORMANCE_BLOCKED: закройте запущенную игру SS14 перед нагрузочной серией. PID: $($taskExternalGames.ProcessId -join ', ')." }
    }
    Test-KiasBenchmarkPorts
    if ($CheckOnly) {
        if (-not (Test-Path -LiteralPath $pointer)) { throw 'REBUILD_REQUIRED: нет актуальной пары. Обычный запуск соберёт её автоматически.' }
        $manifestPath=(Get-Content -LiteralPath $pointer -Raw).Trim()
        $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $fingerprint=& python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $fingerprint.sha256 -ne $manifest.sourceFingerprint) { throw 'REBUILD_REQUIRED: исходники изменились после сборки.' }
        foreach ($build in $manifest.builds) {
            if (-not $build.compiled) { throw "REBUILD_REQUIRED: $($build.label) не собрана." }
            if ((Get-FileHash -LiteralPath (Join-Path $build.source 'bin/Content.Server/Content.Server.dll')).Hash.ToLowerInvariant() -ne $build.assemblySha256) { throw "REBUILD_REQUIRED: $($build.label) изменилась." }
        }
        if ($taskReadinessExit -ne 0) { throw 'NOT_READY: обязательные предварительные проверки v4.1 отсутствуют или устарели.' }
        Write-Host 'Строгая проверка v4.1: READY. Файлы и результаты не изменялись.'
    } else {
        New-Item -ItemType Directory -Path $bench -Force | Out-Null
        $current=Join-Path $bench ('rerun/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
        New-Item -ItemType Directory -Path $current | Out-Null
        $current | Set-Content -LiteralPath (Join-Path $bench 'CURRENT_RUN.txt')
        Start-Transcript -LiteralPath (Join-Path $current 'detailed.log') | Out-Null
        $taskTranscript=$true
        Set-LauncherStage 1 'Проверка обязательных доказательств'
        @{status='PREPARING';benchmarkKind=$taskBenchmarkKind;fullGameplayCoverage=$false;startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
        Write-Host "Результаты: $current"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DEFERRED_CHECKS.md') -Destination (Join-Path $current 'DEFERRED_CHECKS.md')
        @{policy=$(if ($Diagnostic) {'OWNER_DIAGNOSTIC_20261012'} elseif ($EssentialPreflight) {'OWNER_ESSENTIAL_20261011'} else {'STRICT_V4_1'});benchmarkKind=$taskBenchmarkKind;
          expandedV3Certified=$false;diagnostic=[bool]$Diagnostic;strictReady=$false;preflightsDeferred=[bool]$Diagnostic;mandatoryRuntimeGates=$(if ($Diagnostic) {@('matched-release-builds','source-parity','watchdogs','per-run-evidence')} else {@('matched-release-builds','regression','physical-programs','native-A-B','ABBA-native-postconditions')})} |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $current 'preflight-policy.json') -Encoding utf8
        if (-not $Legacy -and -not $Quick -and -not $EssentialPreflight -and -not $Diagnostic -and -not $Profile) {
            & python (Join-Path $PSScriptRoot 'readiness.py')
            if ($LASTEXITCODE -ne 0) { throw 'NOT_READY: полный запуск запрещён до прохождения всех обязательных проверок v3.' }
        }
        if ($EssentialPreflight) {
            Write-Host 'OWNER_ESSENTIAL: расширенная сертификация v3 не завершена. Сборки, регрессия, физические программы и native A/B остаются обязательными перед ABBA.' -ForegroundColor Yellow
        }
        Write-Host 'Предыдущие результаты сохранены. Подготовка свежих Release A/B...'
        Set-LauncherStage 2 'Сборка свежих серверов и клиентов A/B'
        & (Join-Path $PSScriptRoot 'build_pair.ps1') -OutputDirectory (Join-Path $current 'pair')
        $manifestPath=Join-Path $current 'pair/BASELINE_MANIFEST.json'
        $manifestPath | Set-Content -LiteralPath $pointer
        if ($Profile) {
            Set-LauncherStage 3 'Целевое исследование стоимости KIAS'
            & (Join-Path $PSScriptRoot 'profile.ps1') -ManifestPath $manifestPath -OutputDirectory (Join-Path $current 'profile') -Seed $Seed
            @{status='PROFILE_SAMPLES_COMPLETE_ATTRIBUTION_PENDING';strictReady=$false;benchmarkKind='PROFILE'} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
            return
        }
        if ($Diagnostic) {
            Write-Host 'Диагностический запуск: проверяем инструменты и защиты; игровые предварительные проверки отложены.'
            Set-LauncherStage 3 'Проверка инструментов и защит'
        } else {
            Write-Host 'Функциональный gate: все KIAS-тесты, физические программы, комнаты и экспедиция...'
            Set-LauncherStage 3 'Регрессионные и физические проверки'
        }
        & python -m unittest discover -s $PSScriptRoot -p 'test_*.py' *> (Join-Path $current 'tool-tests.log')
        if ($LASTEXITCODE -ne 0) { throw "Проверки инструментов не прошли: $current/tool-tests.log" }
        & (Join-Path $PSScriptRoot 'test_benchmark_lock.ps1') *> (Join-Path $current 'benchmark-lock-tests.log')
        & (Join-Path $PSScriptRoot 'test_metric_scrape.ps1') *> (Join-Path $current 'metric-scrape-tests.log')
        & (Join-Path $PSScriptRoot 'test_build_progress.ps1') *> (Join-Path $current 'build-progress-tests.log')
        & (Join-Path $PSScriptRoot 'test_native_log_watch.ps1') *> (Join-Path $current 'native-log-watch-tests.log')
        if (-not $Diagnostic) {
        $previousLab=$env:KIAS_LAB_OUTPUT
        try {
            $env:KIAS_LAB_OUTPUT=Join-Path $current 'functional'
            & dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~Content.IntegrationTests.Tests._Forge.KIAS' --logger 'trx;LogFileName=gate.trx' --results-directory (Join-Path $current 'tests') -m:4 -nr:false *> (Join-Path $current 'functional-gate.log')
            if ($LASTEXITCODE -ne 0) { throw "Gate не прошёл: $current/functional-gate.log" }
        } finally { $env:KIAS_LAB_OUTPUT=$previousLab }
        [xml]$trx=Get-Content -LiteralPath (Join-Path $current 'tests/gate.trx') -Raw
        $counts=$trx.TestRun.ResultSummary.Counters
        if ([int]$counts.total -eq 0 -or [int]$counts.total -ne [int]$counts.passed) { throw 'Gate содержит пропуски или ошибки.' }
        & python (Join-Path $PSScriptRoot 'validate_physical_cards.py') (Join-Path $current 'functional') (Join-Path $current 'tests/gate.trx') --output (Join-Path $current 'program-proof-summary.json')
        if ($LASTEXITCODE -ne 0) { throw 'Gate не подтвердил все 27 физических программ на актуальной карте и прототипах.' }
        & python (Join-Path $PSScriptRoot 'validate_hulls.py') (Join-Path $current 'functional') (Join-Path $current 'tests/gate.trx') --output (Join-Path $current 'hull-proof-summary.json')
        if ($LASTEXITCODE -ne 0) { throw 'Gate не подтвердил восемь настоящих корпусов с питанием, физической программой и восстановлением DATA.' }
        Write-Host "Gate: $($counts.passed)/$($counts.total) PASS."
        } else { Write-Host 'OWNER_DIAGNOSTIC: регрессия и игровые preflight отложены. Свежие сборки и проверки защит выполнены.' -ForegroundColor Yellow }
        if ($Quick) {
            Set-LauncherStage 4 'Короткие игровые проверки'
            $Ships=[Math]::Max(4,$Ships)
            Write-Host 'Quick: сначала повторное пересечение дверей и разгерметизации A/B с четырьмя клиентами.'
            & (Join-Path $PSScriptRoot 'native_suite.ps1') -Variants @('A','B') -Groups @('room_overlap') -Ships $Ships -NetworkClients 4 -OutputDirectory (Join-Path $current 'quick-room-controls') -ManifestPath $manifestPath -Seed $Seed
            Write-Host "Quick: общий игровой сценарий A/B, $Ships кораблей, четыре настоящих клиента; затем кассетная и повёрнутая проверки A/B."
            $taskQuickScenario=Join-Path $current 'quick-fullgame.jsonl'
            & python (Join-Path $PSScriptRoot 'fullgame_scenario.py') --ships $Ships --ticks 45000 --seed $Seed --output $taskQuickScenario
            if ($LASTEXITCODE -ne 0) { throw 'Quick full-game scenario failed.' }
            foreach($taskQuickVariant in @('A','B')) {
                & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $taskQuickVariant -Ships $Ships -WarmupTicks $QuickWarmupTicks -MeasuredTicks 45000 -Scenario $taskQuickScenario -OutputDirectory (Join-Path $current "quick-fullgame-$taskQuickVariant") -ManifestPath $manifestPath -Seed $Seed -FunctionalOnly -KeepNativeNpcAwake -NetworkClients 4
                & python (Join-Path $PSScriptRoot 'native_proof.py') (Join-Path $current "quick-fullgame-$taskQuickVariant") $taskQuickScenario *> (Join-Path $current "quick-fullgame-proof-$taskQuickVariant.json")
                if ($LASTEXITCODE -ne 0) { throw "Quick combined native proof failed $taskQuickVariant" }
            }
            & (Join-Path $PSScriptRoot 'native_suite.ps1') -Variants @('A','B') -Groups @('pdc_operator','collision_rotated') -Ships $Ships -OutputDirectory (Join-Path $current 'quick-controls') -ManifestPath $manifestPath -Seed $Seed
            & python (Join-Path $PSScriptRoot 'quick_report.py') $current
            if ($LASTEXITCODE -ne 0) {throw 'Quick A/B category, network or repeat proof failed'}
            @{status='QUICK_A_B_PASS';fullGameplayCoverage=$false;nativeCategoryCoverage=$true;fullABBA=$false;completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
        }
        if (-not $Quick) {
        Set-LauncherStage 4 'Проверка свежей пары в игре'
        if ($Legacy) {
        Write-Host 'Проверка настоящего раунда A/B и сохранения 6000 админ-записей...'
        $preflight = Join-Path $current 'preflight'
        New-Item -ItemType Directory -Path $preflight | Out-Null
        $preflightScenario = Join-Path $preflight 'scenario.jsonl'
        & python (Join-Path $PSScriptRoot 'expanded_scenario.py') --ticks 1200 --output $preflightScenario
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать короткий проверочный сценарий.' }
        foreach ($variant in @('A','B')) {
            $preflightRun = Join-Path $preflight $variant
            & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $variant -Ships 40 -WarmupTicks 1800 -MeasuredTicks 1200 -Scenario $preflightScenario -OutputDirectory $preflightRun -ManifestPath $manifestPath -Seed $Seed -AdminLogProbe 6000
            & python (Join-Path $PSScriptRoot 'validate_adminlog_probe.py') $preflightRun
            if ($LASTEXITCODE -ne 0) { throw "Админ-логи $variant не прошли проверку сохранения." }
        }
        } elseif (-not $Diagnostic) {
            Write-Host 'Игровые проверки свежей пары A/B перед полной серией...'
            & (Join-Path $PSScriptRoot 'native_suite.ps1') -Variants @('A','B') -Ships 1 -OutputDirectory (Join-Path $current 'native-preflight') -ManifestPath $manifestPath -Seed $Seed
            $taskNetworkScenario=Join-Path $current 'network-idle.jsonl'
            [IO.File]::WriteAllText($taskNetworkScenario,'')
            $taskNetworkCount=[Math]::Min(4,$Ships)
            foreach ($taskNetworkVariant in @('A','B')) {
                Write-Host "Проверка $taskNetworkCount одновременных localhost-клиентов $taskNetworkVariant перед полной серией..."
                & (Join-Path $PSScriptRoot 'native_replay.ps1') -Variant $taskNetworkVariant -Ships $taskNetworkCount -WarmupTicks 1800 -MeasuredTicks 600 -Scenario $taskNetworkScenario -OutputDirectory (Join-Path $current "network-preflight-$taskNetworkVariant") -ManifestPath $manifestPath -Seed $Seed -NetworkClients $taskNetworkCount -FunctionalOnly -KeepNativeNpcAwake
            }
        }
        $warmup=10800
        $measured=$Minutes*3600
        if ($Legacy) { $warmup=7200; $measured=72000 }
        Write-Host "A–B–B–A: $Ships кораблей, $($warmup/3600) мин прогрева + $($measured/3600) мин измерения на запуск."
        Set-LauncherStage 5 'Серия A–B–B–A и проверка отчётов'
        Write-Host 'Полное игровое покрытие учитывается отдельно от длительности серии.'
        & (Join-Path $PSScriptRoot 'load_series.ps1') -OutputDirectory (Join-Path $current 'series') -ReplayRoot (Join-Path $current 'runs') -ManifestPath $manifestPath -Ships $Ships -WarmupTicks $warmup -MeasuredTicks $measured -Seed $Seed -Quick:$Quick -Legacy:$Legacy -FullGame:(-not $Legacy) -Diagnostic:$Diagnostic
        if ($Diagnostic) {
            $taskDiagnosticState=Get-Content -LiteralPath (Join-Path $current 'series/status.json') -Raw | ConvertFrom-Json
            @{status=$taskDiagnosticState.status;benchmarkKind=$taskBenchmarkKind;strictReady=$false;fullGameplayCoverage=$false;completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
            if ($taskDiagnosticState.status -ne 'DIAGNOSTIC_ABBA_COMPLETE') { $exitCode=1 }
        } else {
        @{status='ABBA_COMPLETE';benchmarkKind=$taskBenchmarkKind;fullGameplayCoverage=$false;completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
        }
        }
    }
} catch {
    $exitCode=1
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
    if ($current) { @{status='FAILED_INCOMPLETE';error=$_.Exception.Message} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json') }
    if ($current) {
        $taskStagePath=Join-Path $current 'launcher-stage.json'
        if (Test-Path -LiteralPath $taskStagePath) {
            $taskFailedStage=Get-Content -LiteralPath $taskStagePath -Raw | ConvertFrom-Json
            Set-LauncherStage $taskFailedStage.stage $taskFailedStage.name 'FAILED_INCOMPLETE'
        } else { Set-LauncherStage 1 'Подготовка запуска' 'FAILED_INCOMPLETE' }
    }
} finally {
    if ($current) {
        try {
            if ($exitCode -eq 0) { Set-LauncherStage 6 'Подготовка архива результатов' }
            if ($taskTranscript) { Stop-Transcript | Out-Null; $taskTranscript=$false }
        } catch { $exitCode=1; Write-Host "Ошибка сохранения журнала/этапа: $($_.Exception.Message)" -ForegroundColor Red }
        if ($Diagnostic -and (Test-Path -LiteralPath (Join-Path $current 'series/status.json'))) {
            $taskFinalSeries=Get-Content -LiteralPath (Join-Path $current 'series/status.json') -Raw | ConvertFrom-Json
            $taskFinalStageStatus=if ($exitCode -eq 0) {'COMPLETE'} elseif (@($taskFinalSeries.attemptedRuns).Count -eq 4) {'FINISHED_WITH_ERRORS'} else {'FAILED_INCOMPLETE'}
            Set-LauncherStage 7 'Сохранение итогового архива; итог серии в launcher-status.json' $taskFinalStageStatus
        }
        & python (Join-Path $PSScriptRoot 'partial_archive.py') $current
        if ($LASTEXITCODE -ne 0) {
            $exitCode=1
            @{status='FAILED_INCOMPLETE';error='Не удалось подготовить итоговый архив.'} | ConvertTo-Json | Set-Content (Join-Path $current 'launcher-status.json')
        }
        if ($exitCode -eq 0) { Set-LauncherStage 7 'Запуск завершён, архив готов' 'COMPLETE' }
    }
    Exit-KiasBenchmarkLease $taskBenchmarkLease
}
exit $exitCode
