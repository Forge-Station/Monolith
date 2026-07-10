cd ..\..\

mkdir Scripts\logs

del Scripts\logs\Content.YAMLLinter.Release.log
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c Release -- NUnit.ConsoleOut=0 > Scripts\logs\Content.YAMLLinter.Release.log

pause
