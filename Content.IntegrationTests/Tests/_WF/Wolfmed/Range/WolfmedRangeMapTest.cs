#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Range;

public static class WolfmedRangeMap
{
    /// <summary>The committed range, loaded in game with `wolfmedrange` (loadmap leaves it uninitialised).</summary>
    public static readonly ResPath Path = new("/Maps/_WF/Wolfmed/wolfmed_range.yml");
}

/// <summary>
/// The committed range loads with air, carries one mob per playable species, and its pods draw power from the RTG chain.
/// When a species is added, rerun <see cref="WolfmedRangeMapGenerator"/> and commit the map.
/// </summary>
[TestFixture]
public sealed class WolfmedRangeMapTest
{
    [Test]
    public async Task RangeLoadsWithEverySpeciesAndPoweredPodsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var loader = entMan.System<MapLoaderSystem>();

        var expected = protos.EnumeratePrototypes<SpeciesPrototype>()
            .Count(s => protos.TryIndex<EntityPrototype>(s.Prototype, out var p) && p.Components.ContainsKey("HumanoidAppearance"));

        EntityUid map = default;
        await server.WaitPost(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            Assert.That(loader.TryLoadMap(WolfmedRangeMap.Path, out var loaded, out _, options), Is.True, "the range did not load.");
            map = loaded!.Value.Owner;
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var xforms = entMan.GetEntityQuery<TransformComponent>();
            var species = entMan.EntityQuery<HumanoidAppearanceComponent>(true)
                .Count(h => xforms.GetComponent(h.Owner).MapUid == map);
            Assert.That(species, Is.EqualTo(expected),
                "the range does not carry one mob per playable species; rerun WolfmedRangeMapGenerator and commit the map.");

            var pods = entMan.EntityQuery<AutodocComponent>(true)
                .Where(a => xforms.GetComponent(a.Owner).MapUid == map)
                .ToList();
            Assert.That(pods, Has.Count.EqualTo(2), "the range does not carry two pods.");
            var atmos = entMan.System<AtmosphereSystem>();
            foreach (var pod in pods)
            {
                var air = atmos.GetTileMixture(pod.Owner);
                Assert.That(air?.GetMoles(Gas.Oxygen) ?? 0f, Is.GreaterThan(1f), "the range has no oxygen at a pod.");
                Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(pod.Owner).Powered, Is.True, "a range pod has no power.");
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(map));
        await pair.CleanReturnAsync();
    }
}
