using System.Linq;
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetAmbiencePrototypeTest
{
    private static readonly string[] Worlds =
    {
        "Asclepiu",
        "Fervidus",
        "Merak",
        "Aerumna",
        "Thrascias",
        "Carcinoma",
    };

    [Test]
    public async Task SixWorldsHaveValidBoundedProfiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var resources = server.ResolveDependency<IResourceManager>();
            // A profile without a planet type belongs to no surface, only to whatever names it, but its files must exist too.
            var all = prototypes.EnumeratePrototypes<WFPlanetAmbiencePrototype>().ToList();
            foreach (var profile in all)
            {
                foreach (var sound in profile.Loops.Concat(profile.DayLoops).Concat(profile.NightLoops)
                             .Concat(profile.OneShots).Concat(profile.DayOneShots).Concat(profile.NightOneShots))
                    AssertSoundExists(sound, resources, profile.ID);
            }

            var profiles = all.Where(p => p.PlanetType != null).ToList();
            Assert.That(profiles, Has.Count.EqualTo(Worlds.Length));
            var imported = new HashSet<string>();
            foreach (var sound in profiles.SelectMany(p => p.Loops.Concat(p.DayLoops).Concat(p.NightLoops).Concat(p.OneShots).Concat(p.DayOneShots).Concat(p.NightOneShots)))
                if (sound is SoundPathSpecifier path && path.Path.ToString().StartsWith("/Audio/_WF/Planets/Worlds/"))
                    imported.Add(path.Path.ToString());
            Assert.That(imported.Count, Is.EqualTo(27), "Every supplied bed and random accent must be referenced.");

            foreach (var world in Worlds)
            {
                var surface = prototypes.Index<WFPlanetSurfacePrototype>($"WFSurface{world}");
                var profile = profiles.Single(candidate => candidate.PlanetType == surface.PlanetType);
                Assert.Multiple(() =>
                {
                    Assert.That(profile.ID, Is.EqualTo($"WFPlanetAmbience{world}"));
                    Assert.That(profile.PlanetType, Is.EqualTo(surface.PlanetType));
                    Assert.That(profile.GetOneShots(false), Is.Not.Empty, $"{world} has no daytime accents.");
                    Assert.That(profile.GetOneShots(true), Is.Not.Empty, $"{world} has no night accents.");
                    Assert.That(profile.MinInterval, Is.GreaterThan(0f));
                    Assert.That(profile.MaxInterval, Is.GreaterThanOrEqualTo(profile.MinInterval));
                    Assert.That(profile.LoopVolume, Is.InRange(WFPlanetAmbience.SilentVolume, 0f));
                    Assert.That(profile.OneShotVolume, Is.InRange(WFPlanetAmbience.SilentVolume, 0f));
                    Assert.That(profile.HullVolumeOffset, Is.LessThanOrEqualTo(0f));
                    Assert.That(profile.GetLoops(false), Is.Not.Empty, $"{world} has no daytime bed.");
                    Assert.That(profile.GetLoops(true), Is.Not.Empty, $"{world} has no night bed.");
                    Assert.That(profile.CrossfadeSeconds, Is.InRange(0.1f, 10f));
                    foreach (var sound in profile.Loops.Concat(profile.DayLoops).Concat(profile.NightLoops))
                        AssertSoundExists(sound, resources, world);

                    foreach (var sound in profile.GetOneShots(false).Concat(profile.GetOneShots(true)))
                        AssertSoundExists(sound, resources, world);
                });
            }

            foreach (var (minute, night) in new[] { (0, true), (359, true), (360, false), (1079, false), (1080, true), (1439, true) })
                Assert.That(new WFPlanetEnvironmentComponent { MinuteOfDay = minute }.IsNight, Is.EqualTo(night));
            Assert.That(WFPlanetAmbience.LayerVolumeOffset(0, 4), Is.EqualTo(0f));
            Assert.That(WFPlanetAmbience.LayerVolumeOffset(1, 4), Is.GreaterThan(WFPlanetAmbience.LayerVolumeOffset(2, 4)));
            Assert.That(WFPlanetAmbience.LayerVolumeOffset(4, 4), Is.EqualTo(WFPlanetAmbience.SilentVolume));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NetworkStampsProfileAcrossEveryLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var layers = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            for (var depth = 0; depth < layers.Count; depth++)
            {
                var ambience = entities.GetComponent<WFPlanetAmbienceComponent>(layers[depth]);
                Assert.Multiple(() =>
                {
                    Assert.That(ambience.Profile.Id, Is.EqualTo("WFPlanetAmbienceAsclepiu"));
                    Assert.That(
                        ambience.VolumeOffset,
                        Is.EqualTo(WFPlanetAmbience.LayerVolumeOffset(depth, layers.Count - 1)));
                });
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static void AssertSoundExists(SoundSpecifier sound, IResourceManager resources, string world)
    {
        Assert.That(sound, Is.TypeOf<SoundPathSpecifier>(), $"{world} uses an unexpected sound collection.");
        var path = ((SoundPathSpecifier) sound).Path;
        Assert.That(resources.ContentFileExists(path), Is.True, $"{world} references missing audio {path}.");
    }
}
