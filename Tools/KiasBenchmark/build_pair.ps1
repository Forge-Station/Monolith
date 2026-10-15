param([Parameter(Mandatory)][string]$OutputDirectory, [string]$Base)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'benchmark_lock.ps1')
$taskBenchmarkLease=Enter-KiasBenchmarkLease (Join-Path $PSScriptRoot '../..')
try {
    Test-KiasBenchmarkPorts
function Invoke-TrackedReleaseBuild([string]$Source,[string]$Project,[string]$Log,[string]$Label) {
    $taskBuildClock=[Diagnostics.Stopwatch]::StartNew()
    $taskDotnet=(Get-Command dotnet -ErrorAction Stop).Source
    $taskBuildProcess=$null
    try {
        $taskBuildProcess=Start-Process -FilePath $taskDotnet -WorkingDirectory $Source -ArgumentList @('build',$Project,'-c','Release','-m:4','-nr:false') -WindowStyle Hidden -PassThru -RedirectStandardOutput $Log -RedirectStandardError ($Log+'.stderr.log')
        while (-not $taskBuildProcess.HasExited) {
            Start-Sleep -Seconds 5
            $taskBuildProcess.Refresh()
            $taskCompletedProjects=if (Test-Path -LiteralPath $Log) {@(Select-String -LiteralPath $Log -SimpleMatch ' -> ').Count} else {0}
            $taskHeartbeat=@{label=$Label;status='BUILDING';wallSeconds=$taskBuildClock.Elapsed.TotalSeconds;projectsReported=$taskCompletedProjects;etaSeconds=$null;updatedUtc=[DateTime]::UtcNow.ToString('o');log=$Log}
            $taskHeartbeat | ConvertTo-Json | Set-Content -LiteralPath ($Log+'.progress.tmp') -Encoding utf8
            Move-Item -LiteralPath ($Log+'.progress.tmp') -Destination ($Log+'.progress.json') -Force
            Write-Host ('Сборка {0} | прошло {1} | завершённых проектов {2} | ETA вычисляется | журнал {3}' -f $Label,$taskBuildClock.Elapsed.ToString('hh\:mm\:ss'),$taskCompletedProjects,$Log)
        }
        $taskBuildProcess.WaitForExit()
        $taskCode=$taskBuildProcess.ExitCode
        @{label=$Label;status=$(if ($taskCode -eq 0) {'COMPLETE'} else {'FAILED'});exitCode=$taskCode;wallSeconds=$taskBuildClock.Elapsed.TotalSeconds;updatedUtc=[DateTime]::UtcNow.ToString('o');log=$Log} | ConvertTo-Json | Set-Content -LiteralPath ($Log+'.progress.json') -Encoding utf8
        if ($taskCode -ne 0) { throw "Release build $Label failed; see $Log and $Log.stderr.log" }
    } finally {
        if ($null -ne $taskBuildProcess -and -not $taskBuildProcess.HasExited) { Stop-Process -Id $taskBuildProcess.Id }
    }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath (Join-Path $output 'builds')) { throw 'BuildPair refuses to overwrite existing builds.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $repo
try {
    $taskEngineStatus=@(& git -C (Join-Path $repo 'RobustToolbox') status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify engine working tree.' }
    if ($taskEngineStatus.Count) { throw 'Engine working tree contains changes; isolated builds require the recorded clean engine revision.' }
    $fingerprint = & python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Source fingerprint failed.' }
    $fingerprint | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'source-fingerprint.json') -Encoding utf8
    if (-not $Base) {
        $reference = Get-Content (Join-Path $repo '.kias-benchmark/BASELINE_MANIFEST.json') -Raw | ConvertFrom-Json
        $Base = $reference.base
    }
    $base = (& git merge-base $Base HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine common source baseline.' }
    & python (Join-Path $PSScriptRoot 'prepare_pair.py') --directory $output --base $base *> (Join-Path $output 'prepare.log')
    if ($LASTEXITCODE -ne 0) { throw "Matched source preparation failed: $output/prepare.log" }
    $manifestPath = Join-Path $output 'BASELINE_MANIFEST.json'
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName sourceFingerprint -NotePropertyValue $fingerprint.sha256
    $manifest | Add-Member -NotePropertyName sdk -NotePropertyValue ((& dotnet --version).Trim())
    foreach ($build in $manifest.builds) {
        Write-Host "Сборка Release $($build.label)..."
        Invoke-TrackedReleaseBuild $build.source 'Content.Server/Content.Server.csproj' (Join-Path $output "build-$($build.label).log") ('server-'+$build.label)
        & python (Join-Path $PSScriptRoot 'instrument_headless_client.py') (Join-Path $build.source 'RobustToolbox') *> (Join-Path $output "client-instrumentation-$($build.label).json")
        if ($LASTEXITCODE -ne 0) { throw 'Headless client instrumentation failed.' }
        Invoke-TrackedReleaseBuild $build.source 'Content.Client/Content.Client.csproj' (Join-Path $output "build-client-$($build.label).log") ('client-'+$build.label)
        $taskClientHashes=@{}
        foreach ($taskAssembly in Get-ChildItem -LiteralPath (Join-Path $build.source 'bin/Content.Client') -Filter '*.dll') {
            $taskClientHashes[$taskAssembly.Name]=(Get-FileHash -LiteralPath $taskAssembly.FullName).Hash.ToLowerInvariant()
        }
        $build | Add-Member -NotePropertyName clientRuntimeAssemblyHashes -NotePropertyValue $taskClientHashes
        $build.compiled = $true
        $taskRuntimeHashes = @{}
        foreach ($taskAssembly in Get-ChildItem -LiteralPath (Join-Path $build.source 'bin/Content.Server') -Filter '*.dll') {
            $taskRuntimeHashes[$taskAssembly.Name] = (Get-FileHash -LiteralPath $taskAssembly.FullName).Hash.ToLowerInvariant()
        }
        $build | Add-Member -NotePropertyName runtimeAssemblyHashes -NotePropertyValue $taskRuntimeHashes
        foreach ($entry in @{
            assemblySha256 = 'bin/Content.Server/Content.Server.dll'
            nativeLabSha256 = 'Tools/KiasBenchmark/Server/NativeLab.cs'
            nativeGameDriversSha256 = 'Tools/KiasBenchmark/Server/NativeGameDrivers.cs'
            nativeProfilerSha256 = 'Tools/KiasBenchmark/Server/NativeProfiler.cs'
        }.GetEnumerator()) {
            $build | Add-Member -NotePropertyName $entry.Key -NotePropertyValue (Get-FileHash (Join-Path $build.source $entry.Value)).Hash.ToLowerInvariant()
        }
        if ($build.label -eq 'B') {
            $build | Add-Member -NotePropertyName telemetrySha256 -NotePropertyValue (Get-FileHash (Join-Path $build.source 'Tools/KiasBenchmark/Server/KiasTelemetry.cs')).Hash.ToLowerInvariant()
        }
        $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding utf8
    }
    $current = & python (Join-Path $PSScriptRoot 'source_fingerprint.py') | ConvertFrom-Json
    if ($current.sha256 -ne $fingerprint.sha256) { throw 'REBUILD_REQUIRED: source changed while compiling the pair.' }
    $manifest.status = 'MATCHED_RELEASE_BUILDS_COMPLETE_RUNTIME_NOT_TESTED'
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding utf8
    Write-Host "Готовы свежие A/B. Манифест: $manifestPath"
} catch {
    @{ status='FAIL'; error=$_.Exception.Message } | ConvertTo-Json | Set-Content (Join-Path $output 'build-failed.json') -Encoding utf8
    throw
} finally { Pop-Location }

} finally { Exit-KiasBenchmarkLease $taskBenchmarkLease }
