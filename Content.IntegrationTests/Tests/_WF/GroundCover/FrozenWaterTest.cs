#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.GroundCover;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared.Atmos;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.StepTrigger.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Standing water freezes from the shore in cold air, bears walkers, breaks under a pickaxe and thaws.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class FrozenWaterTest
{
    private const string Water = "MonoFloorWaterEntity";
    private const float Warm = 283.15f;
    private const float Cold = 265.15f;

    /// <summary>A 7x7 pond: its middle tile is four tiles from the shore.</summary>
    private static readonly Vector2i PondMin = new(-7, -7);
    private static readonly Vector2i Centre = new(-4, -4);
    private static readonly Vector2i Edge = new(-7, -4);
    private static readonly Vector2i Chunk = WFSnowCover.ChunkOf(Centre);

    [Test]
    public async Task WaterFreezesFromTheShoreBearsWalkersBreaksAndThaws()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var waters = new Dictionary<Vector2i, EntityUid>();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            waters = LayPond(pair, map.Grid);
            SetAir(pair, map, Warm);
            mob = entMan.SpawnEntity("MobHuman", Centred(map.Grid, Centre));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(cover.UpdateIceChunk(map.Grid, Chunk), Is.False, "Warm water froze.");
            var chunk = cover.GetIceChunk(map.Grid, Centre);
            Assert.That(chunk, Is.Not.Null, "The pond's chunk found no water.");
            Assert.That(chunk!.Shore[WFSnowCover.IndexIn(Edge)], Is.EqualTo(1));
            Assert.That(chunk.Shore[WFSnowCover.IndexIn(Centre)], Is.EqualTo(4));
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier, Is.EqualTo(0.5f).Within(0.01f),
                "Precondition: open water didn't slow the wader.");

            // A minute of frost is too short; past the delay the shore freezes first.
            SetAir(pair, map, Cold);
            Advance(cover, map.Grid, TimeSpan.FromMinutes(1));
            Assert.That(Frozen(entMan, waters[Edge]), Is.False, "Water froze before the cold had held a few minutes.");

            Advance(cover, map.Grid, WFGroundCoverSystem.IceDelay);
            Assert.That(Frozen(entMan, waters[Edge]), Is.True, "The shore didn't freeze after the delay.");
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "The middle froze as soon as the shore.");

            Advance(cover, map.Grid, TimeSpan.FromMinutes(10));
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "The pond didn't freeze over.");
        });

        // On the ice: no wading slowdown, no sinking in, no splash trigger, a little slippery.
        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
        {
            var centre = waters[Centre];
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier, Is.EqualTo(1f).Within(0.01f),
                "The ice still slowed its walker like water.");
            Assert.That(entMan.HasComponent<FloorOccluderComponent>(centre), Is.False, "Walkers still sink into the ice.");
            Assert.That(entMan.TryGetComponent<FloorOcclusionComponent>(mob, out var occlusion) && occlusion.Enabled, Is.False,
                "The walker still looks waist deep.");
            Assert.That(entMan.GetComponent<StepTriggerComponent>(centre).Active, Is.False);
            Assert.That(entMan.TryGetComponent<FrictionContactsComponent>(centre, out var friction), Is.True, "The ice isn't slippery.");
            Assert.That(friction!.MobFriction, Is.LessThan(1f));

            // A pickaxe breaks a hole back to water.
            var pickaxe = entMan.SpawnEntity("Pickaxe", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(mob, pickaxe), Is.True, "Precondition: the mob can't hold the pickaxe.");
            server.System<SharedInteractionSystem>().UserInteraction(mob, Centred(map.Grid, Centre), centre);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "The pickaxe didn't break the ice.");
            Assert.That(Frozen(entMan, waters[Centre + new Vector2i(1, 0)]), Is.True, "The pickaxe broke more than its tile.");
            Assert.That(entMan.HasComponent<FloorOccluderComponent>(waters[Centre]), Is.True, "Broken ice didn't turn back to water.");

            // The hole stays open through further frost, then freezes over.
            Advance(cover, map.Grid, TimeSpan.FromMinutes(1));
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "The hole froze over at once.");
            Advance(cover, map.Grid, WFGroundCoverSystem.HoleTime);
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "The hole never froze over.");

            // Above freezing, the warmth melts the ice's thickness, then the shore thaws first.
            var thickness = cover.GetIceChunk(map.Grid, Centre)!.Thickness;
            Assert.That(thickness, Is.GreaterThan(60f), "The ice didn't thicken in twenty minutes of frost.");
            var melt = TimeSpan.FromMinutes(thickness / (Warm - Atmospherics.T0C));
            SetAir(pair, map, Warm);
            Advance(cover, map.Grid, WFGroundCoverSystem.IceDelay);
            Advance(cover, map.Grid, melt - TimeSpan.FromMinutes(1));
            Assert.That(Frozen(entMan, waters[Edge]), Is.True, "Ice thawed before the warmth had melted its thickness.");
            Advance(cover, map.Grid, TimeSpan.FromMinutes(2));
            Assert.That(Frozen(entMan, waters[Edge]), Is.False, "The shore didn't thaw.");
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "The middle thawed as soon as the shore.");

            Advance(cover, map.Grid, TimeSpan.FromMinutes(10));
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "The pond didn't thaw.");
            Assert.That(entMan.HasComponent<FrictionContactsComponent>(waters[Centre]), Is.False, "Thawed water stayed slippery.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier, Is.EqualTo(0.5f).Within(0.01f),
                "Thawed water didn't slow its wader again."));

        await pair.CleanReturnAsync();
    }

    /// <summary>A lake frozen over a winter night holds through a mild afternoon and thaws in a warm spring.</summary>
    [Test]
    public async Task WinterIceOutlastsAMildSpellAndThawsInSpring()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var waters = new Dictionary<Vector2i, EntityUid>();

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            waters = LayPond(pair, map.Grid);
            SetAir(pair, map, Warm);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            cover.UpdateIceChunk(map.Grid, Chunk);
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "Precondition: warm water froze.");

            // A winter night at -8 C, worked a few minutes at a time as players would see it.
            SetAir(pair, map, Cold);
            AdvanceBy(cover, map.Grid, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2));
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "The lake didn't freeze over in a winter night.");

            // A quarter hour at 5 C barely dents it.
            SetAir(pair, map, Atmospherics.T0C + 5f);
            AdvanceBy(cover, map.Grid, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(2));
            Assert.That(Frozen(entMan, waters[Edge]), Is.True, "A mild winter afternoon thawed the shore.");
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "A mild winter afternoon thawed the lake.");
            Assert.That(cover.GetIceChunk(map.Grid, Centre)!.Thickness, Is.GreaterThan(WFGroundCoverSystem.IceMaxThickness / 4f),
                "A mild afternoon melted most of the ice.");

            // A sustained 12 C spring melts it through, shore first.
            SetAir(pair, map, Atmospherics.T0C + 12f);
            AdvanceBy(cover, map.Grid, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(2));
            Assert.That(Frozen(entMan, waters[Edge]), Is.False, "A warm spring didn't thaw the shore.");
            Assert.That(Frozen(entMan, waters[Centre]), Is.False, "A warm spring didn't thaw the lake.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WaterFreezesOnlyNearPlayersAndSwallowsSnowOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var waters = new Dictionary<Vector2i, EntityUid>();

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            waters = LayPond(pair, map.Grid);
            SetAir(pair, map, Cold);
            server.System<WFGroundCoverSystem>().SetDepth(map.Grid, Edge, WFSnowCover.UnitsPerLevel);
        });

        // Nobody near: nothing is looked at, however cold.
        await pair.RunTicksSync(pair.SecondsToTicks(4));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFIceCoverComponent>(map.Grid), Is.False, "Water froze with nobody near.");
            Assert.That(Frozen(entMan, waters[Centre]), Is.False);
        });

        // Someone arrives: water first seen in a cold spell is frozen already.
        await server.WaitPost(() =>
        {
            var mob = entMan.SpawnEntity("MobHuman", Centred(map.Grid, new Vector2i(4, 4)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(4));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(Frozen(entMan, waters[Centre]), Is.True, "Water near a player in long frost wasn't frozen.");
            Assert.That(cover.GetDepth(map.Grid, Edge), Is.EqualTo(WFSnowCover.UnitsPerLevel), "Snow on the ice went.");
            Assert.That(cover.IsOpenWater(map.Grid, Edge), Is.False);

            // Snow on water that's open melts into it.
            cover.BreakHole((waters[Edge], entMan.GetComponent<WFFreezableWaterComponent>(waters[Edge])));
            Assert.That(cover.IsOpenWater(map.Grid, Edge), Is.True);
            Assert.That(cover.GetDepth(map.Grid, Edge), Is.Zero, "Snow stayed on a hole in the ice.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await pair.Client.WaitAssertion(() =>
        {
            var water = pair.Client.EntMan.GetEntity(entMan.GetNetEntity(waters[Centre]));
            Assert.That(pair.Client.EntMan.GetComponent<WFFreezableWaterComponent>(water).Frozen, Is.True, "The client wasn't sent the ice.");
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    private static Dictionary<Vector2i, EntityUid> LayPond(TestPair pair, Entity<MapGridComponent> grid)
    {
        var waters = new Dictionary<Vector2i, EntityUid>();
        for (var x = PondMin.X; x < PondMin.X + 7; x++)
        {
            for (var y = PondMin.Y; y < PondMin.Y + 7; y++)
            {
                var tile = new Vector2i(x, y);
                waters[tile] = pair.Server.EntMan.SpawnEntity(Water, Centred(grid, tile));
            }
        }

        return waters;
    }

    /// <summary>Sets the map's air and, as atmos would in time, the air over the test grid's tiles.</summary>
    private static void SetAir(TestPair pair, TestMapData map, float temperature)
    {
        var atmos = pair.Server.System<AtmosphereSystem>();
        atmos.SetMapAtmosphere(map.MapUid, false, GroundCoverTest.Air(temperature));
        for (var x = -8; x <= 8; x++)
        {
            for (var y = -8; y <= 8; y++)
            {
                if (atmos.GetTileMixture((map.Grid.Owner, null, null), map.MapUid, new Vector2i(x, y)) is { } air)
                    air.Temperature = temperature;
            }
        }
    }

    /// <summary>Works the pond's chunk as if some time had passed since it was last worked.</summary>
    private static void Advance(WFGroundCoverSystem cover, EntityUid grid, TimeSpan time)
    {
        var chunk = cover.GetIceChunk(grid, Centre)!;
        chunk.Updated -= time;
        cover.UpdateIceChunk(grid, Chunk);
    }

    /// <summary>Works the pond's chunk through some time in steps.</summary>
    private static void AdvanceBy(WFGroundCoverSystem cover, EntityUid grid, TimeSpan time, TimeSpan step)
    {
        for (var passed = TimeSpan.Zero; passed < time; passed += step)
        {
            Advance(cover, grid, step);
        }
    }

    private static bool Frozen(IEntityManager entMan, EntityUid water)
    {
        return entMan.GetComponent<WFFreezableWaterComponent>(water).Frozen;
    }

    private static EntityCoordinates Centred(EntityUid grid, Vector2i tile)
    {
        return new EntityCoordinates(grid, tile + new Vector2(0.5f, 0.5f));
    }
}
