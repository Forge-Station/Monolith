#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._DV.Planet;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Guard: a planet's masked biome marker layers must mask an entity its biome lays. A marker with nothing to replace
/// scans its whole chunk, places nothing and logs a warning for every vein. No planet has marker layers today.
/// </summary>
[TestFixture]
[TestOf(typeof(PlanetPrototype))]
public sealed class PlanetOreMarkerTest
{
    [Test]
    public async Task MarkerMasksMatchWhatTheBiomeLays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            foreach (var planet in server.ProtoMan.EnumeratePrototypes<PlanetPrototype>().OrderBy(p => p.ID))
            {
                if (planet.BiomeMarkerLayers.Count == 0)
                    continue;

                var laid = new HashSet<string>();
                AddLaid(server.ProtoMan, planet.Biome, laid, new HashSet<string>());

                foreach (var layerId in planet.BiomeMarkerLayers)
                {
                    var marker = server.ProtoMan.Index(layerId);
                    // An unmasked marker spawns on open ground instead.
                    if (marker.EntityMask.Count == 0)
                        continue;

                    Assert.That(marker.EntityMask.Keys.Any(key => laid.Contains(key.Id)),
                        $"{planet.ID}'s {layerId} masks {string.Join(", ", marker.EntityMask.Keys)}, none of which {planet.Biome} lays");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Adds every entity a biome template's entity layers can lay, through its meta layers' templates.</summary>
    private static void AddLaid(IPrototypeManager proto, string template, HashSet<string> laid, HashSet<string> visited)
    {
        if (!visited.Add(template))
            return;

        foreach (var layer in proto.Index<BiomeTemplatePrototype>(template).Layers)
        {
            switch (layer)
            {
                case BiomeEntityLayer entities:
                    laid.UnionWith(entities.Entities);
                    break;
                case BiomeMetaLayer meta:
                    AddLaid(proto, meta.Template, laid, visited);
                    break;
            }
        }
    }
}
