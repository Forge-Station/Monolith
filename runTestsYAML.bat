cd ..\..\

mkdir Scripts\logs

del Scripts\logs\Content.YAMLLinter.Debug.log
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c Debug -- NUnit.ConsoleOut=0 > Scripts\logs\Content.YAMLLinter.Debug.log
if errorlevel 1 (
    type Scripts\logs\Content.YAMLLinter.Debug.log
    pause
    exit /b 1
)

del Scripts\logs\Content.YAMLLinter.Release.log
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c Release -- NUnit.ConsoleOut=0 > Scripts\logs\Content.YAMLLinter.Release.log
if errorlevel 1 (
    type Scripts\logs\Content.YAMLLinter.Release.log
    pause
    exit /b 1
)

echo Tests complete.
pause
