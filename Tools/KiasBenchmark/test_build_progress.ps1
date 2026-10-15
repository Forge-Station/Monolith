$ErrorActionPreference='Stop'
$taskErrors=$null
$taskAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'build_pair.ps1'),[ref]$null,[ref]$taskErrors)
if ($taskErrors) { throw 'Build pair syntax is invalid.' }
$taskFunction=$taskAst.Find({param($taskNode) $taskNode -is [Management.Automation.Language.FunctionDefinitionAst] -and $taskNode.Name -eq 'Invoke-TrackedReleaseBuild'},$true)
if ($null -eq $taskFunction) { throw 'Tracked build helper missing.' }
. ([ScriptBlock]::Create($taskFunction.Extent.Text))
$taskOutput=[IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '../../.kias-benchmark/build-progress-tests') ([Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $taskOutput 'Smoke.csproj') -Encoding utf8
$taskLog=Join-Path $taskOutput 'success.log'
Invoke-TrackedReleaseBuild $taskOutput 'Smoke.csproj' $taskLog 'tiny-real-build'
$taskState=Get-Content -LiteralPath ($taskLog+'.progress.json') -Raw | ConvertFrom-Json
if ($taskState.status -ne 'COMPLETE' -or $taskState.exitCode -ne 0) { throw 'Successful native build was not reported correctly.' }
$taskLog=Join-Path $taskOutput 'failure.log'
$taskCaught=$false
try { Invoke-TrackedReleaseBuild $taskOutput 'Missing.csproj' $taskLog 'expected-failure' }
catch { $taskCaught=$_.Exception.Message -like 'Release build expected-failure failed;*' }
$taskState=Get-Content -LiteralPath ($taskLog+'.progress.json') -Raw | ConvertFrom-Json
if (-not $taskCaught -or $taskState.status -ne 'FAILED' -or $taskState.exitCode -eq 0) { throw 'Native build failure was hidden.' }
Write-Host "Tracked build tests: PASS; evidence $taskOutput"
