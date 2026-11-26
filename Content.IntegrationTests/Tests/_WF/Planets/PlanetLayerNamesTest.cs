#nullable enable
using System.Collections.Generic;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Every layer of a planet network has a name, and the shuttle map says what kind of place each map is.</summary>
[TestFixture]
[TestOf(typeof(WFMapLabelSystem))]
public sealed class PlanetLayerNamesTest
{
    [Test]
    public async Task EveryLayerIsNamedAndLabelledBySort()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var labels = server.System<WFMapLabelSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            var names = new HashSet<string>();
            var unnamed = new List<EntityUid>();

            foreach (var layer in layers)
            {
                var name = entMan.GetComponent<MetaDataComponent>(layer).EntityName;
                if (string.IsNullOrEmpty(name))
                    unnamed.Add(layer);
                else
                    names.Add(name);
            }

            Assert.That(unnamed, Is.Empty, "Layers of the planet network have no name.");
            Assert.That(names, Has.Count.EqualTo(layers.Count), $"Two layers share a name: {string.Join(", ", names)}.");

            var ground = layers[0];
            var groundName = entMan.GetComponent<MetaDataComponent>(ground).EntityName;
            Assert.That(labels.Label(ground, groundName), Is.EqualTo(loc.GetString("wf-shuttle-map-surface", ("name", groundName))),
                "The ground is not labelled as the surface.");

            var orbit = layers[^1];
            var orbitName = entMan.GetComponent<MetaDataComponent>(orbit).EntityName;
            Assert.That(labels.Label(orbit, orbitName), Is.EqualTo(orbitName), "The orbit layer's name was changed.");

            var air = layers[1];
            Assert.That(labels.Label(air, entMan.GetComponent<MetaDataComponent>(air).EntityName),
                Does.Contain(groundName), "An air layer does not name its world.");

            // A space map of the sector and a level with no name at all.
            var sector = entMan.Spawn();
            entMan.AddComponent<StarSystemMapComponent>(sector);
            Assert.That(labels.Label(sector, "Caelestinus"), Is.EqualTo(loc.GetString("wf-shuttle-map-sector", ("name", "Caelestinus"))));
            entMan.DeleteEntity(sector);

            var level = entMan.Spawn();
            entMan.AddComponent<CEZMapComponent>(level).Depth = -3;
            Assert.That(labels.Label(level, string.Empty), Is.EqualTo(loc.GetString("wf-shuttle-map-level", ("level", -3))));
            entMan.DeleteEntity(level);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
