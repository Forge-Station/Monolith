#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Planets;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.LivingWorld;

/// <summary>The imported Black Mesa sounds resolve, and every world's ambience uses some of them.</summary>
[TestFixture]
public sealed class LivingWorldAudioTest
{
    private const string Root = "/Audio/_WF/LivingWorld/";

    [Test]
    public async Task PlanetAmbienceAndPlantSoundsResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var resources = server.ResolveDependency<IResourceManager>();
            var used = new HashSet<string>();

            foreach (var collection in prototypes.EnumeratePrototypes<SoundCollectionPrototype>().Where(c => c.ID.StartsWith("WFFlora")))
            {
                Assert.That(collection.PickFiles, Is.Not.Empty, $"{collection.ID} is empty.");
                foreach (var file in collection.PickFiles)
                {
                    Assert.That(resources.ContentFileExists(file), Is.True, $"{collection.ID} names missing {file}.");
                    used.Add(file.ToString());
                }
            }

            foreach (var profile in prototypes.EnumeratePrototypes<WFPlanetAmbiencePrototype>().Where(p => p.PlanetType != null))
            {
                var sounds = profile.Loops.Concat(profile.DayLoops).Concat(profile.NightLoops)
                    .Concat(profile.OneShots).Concat(profile.DayOneShots).Concat(profile.NightOneShots)
                    .OfType<SoundPathSpecifier>().Select(s => s.Path.ToString()).Where(p => p.StartsWith(Root)).ToList();
                Assert.That(sounds, Is.Not.Empty, $"{profile.ID} uses none of the Black Mesa ambience.");
                foreach (var path in sounds)
                {
                    Assert.That(resources.ContentFileExists(new ResPath(path)), Is.True, $"{profile.ID} names missing {path}.");
                    used.Add(path);
                }
            }

            // Everything imported is used somewhere, so the import stays curated.
            var imported = resources.ContentFindFiles(new ResPath(Root)).Where(p => p.Extension == "ogg").Select(p => p.ToString()).ToList();
            Assert.That(imported, Is.Not.Empty);
            Assert.That(imported.Count, Is.LessThanOrEqualTo(60), "Keep the Black Mesa import curated.");
            Assert.That(imported.Where(p => !used.Contains(p)), Is.Empty, "An imported sound is used by no collection or ambience.");
        });
        await pair.CleanReturnAsync();
    }
}
