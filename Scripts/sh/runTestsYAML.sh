cd ../../

mkdir Scripts/logs

rm Scripts/logs/Content.YAMLLinter.Debug.log
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c Debug -- NUnit.ConsoleOut=0 > Scripts/logs/Content.YAMLLinter.Debug.log

rm Scripts/logs/Content.YAMLLinter.Release.log
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c Release -- NUnit.ConsoleOut=0 > Scripts/logs/Content.YAMLLinter.Release.log

echo "Tests complete. Press enter to continue."
read
