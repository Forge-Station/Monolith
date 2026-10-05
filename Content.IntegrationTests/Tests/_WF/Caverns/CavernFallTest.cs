#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Caverns;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Walking into a mouth drops you one level onto its landing tile, hurt a little by that world's landing.</summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernFallTest
{
    /// <summary>Section 3.5: the Blunt a one-level drop onto each world's landing tile deals.</summary>
    private static readonly Dictionary<string, int> ExpectedBlunt = new()
    {
        { "WFSurfaceAsclepiu", 0 },
        { "WFSurfaceFervidus", 10 },
        { "WFSurfaceMerak", 6 },
        { "WFSurfaceAerumna", 20 },
        { "WFSurfaceThrascias", 3 },
        { "WFSurfaceCarcinoma", 5 },
    };

    /// <summary>How far the measured Blunt may stray from the table.</summary>
    private const int BluntTolerance = 3;

    /// <summary>A human stepping into each world's gate lands in the cavern within 120 ticks, alive and lightly hurt.</summary>
    [Test]
    public async Task MobFallsAndIsHurtALittle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var gate = await Gate(pair, world);
                var mob = await StepIntoGate(pair, world, gate, "MobHuman");

                var landed = await WaitForLanding(pair, world, mob, ticks: 120);
                Assert.That(landed, Is.True, $"{surfaceId}: the mob never landed in the cavern within 120 ticks.");

                await server.WaitAssertion(() =>
                {
                    var damage = entMan.GetComponent<DamageableComponent>(mob);
                    var blunt = damage.Damage.DamageDict.GetValueOrDefault("Blunt", FixedPoint2.Zero).Int();
                    TestContext.Out.WriteLine($"{surfaceId}: measured {blunt} Blunt (table {ExpectedBlunt[surfaceId]}).");

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive),
                            $"{surfaceId}: the fall into the cavern left the mob {entMan.GetComponent<MobStateComponent>(mob).CurrentState}.");
                        Assert.That(blunt, Is.InRange(ExpectedBlunt[surfaceId] - BluntTolerance, ExpectedBlunt[surfaceId] + BluntTolerance),
                            $"{surfaceId}: the landing dealt {blunt} Blunt; section 3.5 says {ExpectedBlunt[surfaceId]}.");
                        Assert.That(TileOf(pair, world.Cavern, mob), Is.EqualTo(LandingIndex(gate)),
                            $"{surfaceId}: the mob landed off the landing tile.");
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An item dropped over the gate falls with it and ends on the landing tile.</summary>
    [Test]
    public async Task ItemFallsToPad()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var cavern = CavernOf(pair, surfaceId);
            var gate = await Gate(pair, world);
            var item = await StepIntoGate(pair, world, gate, "Crowbar");

            var landed = await WaitForLanding(pair, world, item, ticks: 120);
            Assert.That(landed, Is.True, "The dropped item never came to rest in the cavern.");

            await server.WaitAssertion(() =>
            {
                var index = TileOf(pair, world.Cavern, item);
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var tile = maps.GetTileRef(world.Cavern, grid, index).Tile;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(index, Is.EqualTo(LandingIndex(gate)), "The item came to rest off the landing tile.");
                    Assert.That(tileDefs[tile.TypeId].ID, Is.EqualTo(cavern.Mouths.LandingTile.Id),
                        "The tile under the item is not the world's landing tile.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns an entity on the gate's lip, lets it settle, then steps it onto the hole's bottom-left tile.</summary>
    // A step onto the hole is what walking in comes to; the z-physics sees the same empty tile either way.
    private static async Task<EntityUid> StepIntoGate(TestPair pair, World world, WFCavernMouth gate, string proto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var levels = server.System<CEZLevelsSystem>();
        var uid = EntityUid.Invalid;

        await server.WaitPost(() =>
            uid = entMan.SpawnEntity(proto, new EntityCoordinates(world.Ground, TileCentre(ClimbTile(gate)))));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(uid).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the entity fell off the lip before it stepped in.");
            var hurt = entMan.TryGetComponent(uid, out DamageableComponent? damage) ? damage.TotalDamage : FixedPoint2.Zero;
            Assert.That(hurt, Is.EqualTo(FixedPoint2.Zero), "Precondition: the entity was hurt before it stepped in.");
        });

        await server.WaitPost(() =>
        {
            transform.SetCoordinates(uid, new EntityCoordinates(world.Ground, TileCentre(gate.Origin)));
            levels.WakeBody(uid);
        });

        return uid;
    }

    /// <summary>Ticks until the entity stands still on the cavern floor, or the budget runs out.</summary>
    private static async Task<bool> WaitForLanding(TestPair pair, World world, EntityUid uid, int ticks)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        for (var tick = 0; tick < ticks && !landed; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                var physics = entMan.GetComponent<CEZPhysicsComponent>(uid);
                landed = entMan.GetComponent<TransformComponent>(uid).MapUid == world.Cavern
                         && physics.Velocity == 0f
                         && physics.LocalPosition < 0.05f;
            });
        }

        // A few more ticks, so the impact's damage and knockdown have landed too.
        await server.WaitRunTicks(5);
        return landed;
    }

    /// <summary>The tile an entity stands on in a map.</summary>
    private static Vector2i TileOf(TestPair pair, EntityUid map, EntityUid uid)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(map);

        return maps.TileIndicesFor(map, grid, entMan.GetComponent<TransformComponent>(uid).Coordinates);
    }
}
