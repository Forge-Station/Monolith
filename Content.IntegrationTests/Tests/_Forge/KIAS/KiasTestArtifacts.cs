using System.IO;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

internal static class KiasTestArtifacts
{
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml")))
                    return directory.FullName;
            throw new DirectoryNotFoundException("Cannot locate Briar source map above the integration test assembly.");
        }
    }
}
