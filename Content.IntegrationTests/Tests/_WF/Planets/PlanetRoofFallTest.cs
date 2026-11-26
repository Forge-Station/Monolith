#nullable enable
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A roof one layer down is stood on, not dropped through: no one falls into a vessel from the air above it.</summary>
[TestFixture]
[TestOf(typeof(CESharedZLevelsSystem))]
public sealed class PlanetRoofFallTest
{
    [Test]
    public async Task NobodyDropsIntoAVesselFromAbove()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var air = layers[1];
        var hullAt = new Vector2(40, 40);
        await BuildHull(pair, entMan.GetComponent<MapComponent>(ground).MapId, hullAt);
        await LayTiles(pair, ground, new Vector2i(-34, -34), new Vector2i(-26, -26));

        var overHull = EntityUid.Invalid;
        var overGround = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFPlanetLayerComponent>(air).Gravity = 1f;
            overHull = entMan.SpawnEntity("MobHuman", new EntityCoordinates(air, hullAt + new Vector2(7.5f, 4.5f)));
            overGround = entMan.SpawnEntity("MobHuman", new EntityCoordinates(air, new Vector2(-30.5f, -30.5f)));
        });

        // Spawned bodies sleep until something moves them.
        await server.WaitPost(() =>
        {
            var zLevels = server.System<CESharedZLevelsSystem>();
            foreach (var body in new[] { overHull, overGround })
            {
                zLevels.WfRecacheGround((body, entMan.GetComponent<CEZPhysicsComponent>(body)));
            }
        });

        await pair.RunTicksSync(pair.SecondsToTicks(4));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(overGround).MapUid, Is.EqualTo(ground),
                "Nothing held up a body over open ground; the test proves nothing.");
            Assert.That(entMan.GetComponent<TransformComponent>(overHull).MapUid, Is.EqualTo(air),
                "A body dropped through a vessel's roof.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
