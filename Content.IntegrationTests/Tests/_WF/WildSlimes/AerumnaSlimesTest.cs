#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Aerumna's wild slimes: the umbral slime and the lumen slime bred from it.</summary>
[TestFixture]
public sealed class AerumnaSlimesTest
{
    private static readonly SlimeWorldTestKit.Spec Spec = new("WFSurfaceAerumna", "WFWildSlimesAerumna",
        "WFUmbralMutation", "WFLumenMutation", "WFUmbralSlimeExtract", "WFLumenSlimeExtract");

    [Test]
    public async Task UmbralSlimesComeOnlyFromAerumna()
    {
        await SlimeWorldTestKit.CheckBreeds(Spec);
    }

    /// <summary>
    /// Aerumna has pink crystal patches over geode chambers with wild umbral slimes in both; umbral and lumen slimes
    /// left there take no harm from its thin air, the umbral ones are half unseen, the lumen ones glow, and a xenonid
    /// beside them doesn't take them for prey.
    /// </summary>
    [Test]
    public async Task UmbralSlimesLiveOnAerumna()
    {
        await using var pair = await PoolManager.GetServerClient();
        var formed = await SlimeWorldTestKit.CheckPatches(pair, Spec);
        var slimes = await SlimeWorldTestKit.CheckSurvival(pair, Spec, formed);

        var server = pair.Server;
        var entMan = server.EntMan;
        var factions = server.System<NpcFactionSystem>();
        var stealth = server.System<SharedStealthSystem>();
        EntityUid xeno = default;
        await server.WaitPost(() =>
        {
            var onGround = slimes.First(s => entMan.GetComponent<TransformComponent>(s).MapUid == formed.World.Ground);
            var at = entMan.GetComponent<TransformComponent>(onGround).Coordinates.Position + new Vector2(1f, 0f);
            xeno = entMan.SpawnEntity("MobXenoRunner", new EntityCoordinates(formed.World.Ground, at));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                var hostiles = factions.GetNearbyHostiles(xeno, 8f).ToHashSet();
                foreach (var slime in slimes)
                {
                    var breed = entMan.GetComponent<SlimeComponent>(slime).Breed.Id;
                    if (breed == Spec.Wild)
                        Assert.That(stealth.GetVisibility(slime), Is.LessThan(0.5f), "An umbral slime is plainly visible.");
                    else
                        Assert.That(entMan.HasComponent<PointLightComponent>(slime), Is.True, "A lumen slime doesn't glow.");

                    if (entMan.GetComponent<TransformComponent>(slime).MapUid == formed.World.Ground)
                        Assert.That(hostiles, Does.Not.Contain(slime), "A xenonid takes a wild slime for prey.");
                }
            }
        });

        await CavernFixture.Teardown(pair, formed.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Umbral and lumen slimes only split in Aerumna's cool air.</summary>
    [Test]
    public async Task UmbralSlimesSplitOnlyInTheCool()
    {
        await SlimeWorldTestKit.CheckClimate(Spec, 285.15f, new[] { 5f, 70f, 25f });
    }
}
