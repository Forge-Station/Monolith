#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Decals;
using Content.Server._WF.Wolfmed.Damage;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// GORE: the blood a hit throws (G1), what a body bleeding hard does about it (G2), and what a treated
/// limb looks like from outside (G3).
/// </summary>
/// <remarks>
/// Nothing here renders anything, so what is asserted is the selection and the state: which way the
/// spray goes, what colour it is, whether it ends on a wall or on the deck, whether a janitor can wash it
/// off, whether the spurt clock starts and stops with the bleed, and which overlay each limb is wearing.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedGoreSystem))]
public sealed class WolfmedGoreTest : GameTest
{
    // --- G1 ---------------------------------------------------------------------------------------

    /// <summary>
    /// The spray goes the way the hit was going: along a projectile's travel, or away from whoever was
    /// holding the knife. Nothing usable behind it still picks a direction rather than failing.
    /// </summary>
    /// <summary>
    /// Playtest 5: "blood splatters cannot be cleaned". A mop only has water, and water had no tile reaction, so the
    /// wall splats and Wolfmed's floor decals stayed. Water now takes both, and only Wolfmed's decals: a mapper's
    /// cleanable paint is left for space cleaner.
    /// </summary>
    [Test]
    public async Task WaterWashesTheBloodOffTheTileTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var decals = entities.System<DecalSystem>();
            var coords = new EntityCoordinates(map.Grid, 0, 0);
            Assert.That(decals.TryAddDecal("WFWolfmedBloodFloor1", coords, out _, Color.Red, cleanable: true), Is.True);
            Assert.That(decals.TryAddDecal("Dirt", coords, out _, null, cleanable: true), Is.True, "no Dirt decal to keep.");
            // A wall splat sits on its tile's centre, where the spray put it.
            var wall = entities.SpawnEntity("WFWolfmedBloodSplatWall", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            Assert.That(entities.HasComponent<WolfmedCleanableComponent>(wall), Is.True);

            var water = protoMan.Index<ReagentPrototype>("Water");
            Assert.That(water.TileReactions.Any(r => r is WolfmedCleanSplats), Is.True, "water does not clean Wolfmed's blood.");
            var grid = entities.GetComponent<MapGridComponent>(map.Grid);
            var tile = entities.System<SharedMapSystem>().GetTileRef(map.Grid, grid, coords);
            var spent = new WolfmedCleanSplats().TileReact(tile, water, FixedPoint2.New(5), entities, null);
            Assert.That(spent, Is.GreaterThan(FixedPoint2.Zero), "the water cleaned nothing.");

            var left = decals.GetDecalsInRange(map.Grid, coords.Position + new Vector2(0.5f, 0.5f), 1f).Select(d => d.Decal.Id).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(left, Does.Not.Contain("WFWolfmedBloodFloor1"), "the blood decal survived the water.");
                Assert.That(left, Does.Contain("Dirt"), "water took a decal that is not Wolfmed's.");
                Assert.That(entities.Deleted(wall) || entities.IsQueuedForDeletion(wall), Is.True, "the wall splat survived the water.");
            });
        });
    }

    [Test]
    public async Task SplatterFacesAwayFromTheSourceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            // Attacker to the west: the spray goes east, away from him.
            var attacker = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -3, 0));
            transform.SetWorldPosition(body, new Vector2(0.5f, 0.5f));
            transform.SetWorldPosition(attacker, new Vector2(-2.5f, 0.5f));

            var away = gore.GetHitDirection(body, attacker, null);
            Assert.That(away, Is.Not.Null, "a source standing somewhere gives a direction.");
            Assert.That(away!.Value.X, Is.GreaterThan(0f), "the spray goes away from the attacker, not at him.");

            // Nothing behind the hit at all: environmental damage, a self-inflicted burn.
            Assert.That(gore.GetHitDirection(body, null, null), Is.Null);
            Assert.That(gore.GetHitDirection(body, body, body), Is.Null, "your own body is not a direction.");

            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;
            var splatter = gore.TrySpawnSplatter(body, profile.HitSplatter, away, FixedPoint2.New(30));
            Assert.That(splatter, Is.Not.Null);

            var comp = entities.GetComponent<WolfmedHitSplatterComponent>(splatter!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Angle, Is.EqualTo(0f).Within(0.001f), "due east is an angle of zero.");
                Assert.That(comp.Distance, Is.EqualTo(3f), "a heavy hit throws it three tiles.");
                Assert.That(profile.HitSplatter.States, Does.Contain(comp.State));
            });

            // FIX1: an attacker off the diagonal throws blood off the diagonal, not at the nearest cardinal.
            var corner = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -2, -2));
            transform.SetWorldPosition(corner, new Vector2(-2.5f, -2.5f));
            var diagonal = gore.GetHitDirection(body, corner, null);
            Assert.That(diagonal, Is.Not.Null);

            var offAxis = gore.TrySpawnSplatter(body, profile.HitSplatter, diagonal, FixedPoint2.New(30));
            Assert.That(offAxis, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedHitSplatterComponent>(offAxis!.Value).Angle,
                Is.EqualTo(MathF.PI / 4f).Within(0.001f),
                "an attacker to the south-west throws it north-east, at 45 degrees exactly.");

            // Even with no direction the spray still happens, pointed somewhere.
            var random = gore.TrySpawnSplatter(body, profile.HitSplatter, null, FixedPoint2.New(5));
            Assert.That(random, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedHitSplatterComponent>(random!.Value).Distance,
                Is.EqualTo(1f), "a graze throws it one tile.");
        });
    }

    /// <summary>
    /// FIX1: a projectile answers with its own velocity, because by the time it wounds anything it is
    /// standing on top of the victim and its position says nothing.
    /// </summary>
    [Test]
    public async Task ProjectileSpraysAlongItsTravelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            transform.SetWorldPosition(body, new Vector2(0.5f, 0.5f));

            // A round already at the victim, still carrying the line it came in on: up and to the right.
            var round = entities.SpawnEntity("Crowbar", map.GridCoords);
            transform.SetWorldPosition(round, new Vector2(0.5f, 0.5f));
            physics.SetLinearVelocity(round, new Vector2(6f, 6f));

            var travel = gore.GetHitDirection(body, body, round);
            Assert.That(travel, Is.Not.Null, "the round's velocity is the direction.");

            var splatter = gore.TrySpawnSplatter(body, profile.HitSplatter, travel, FixedPoint2.New(30));
            Assert.That(splatter, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedHitSplatterComponent>(splatter!.Value).Angle,
                Is.EqualTo(MathF.PI / 4f).Within(0.001f),
                "the spray carries on the way the round was going.");
        });
    }

    /// <summary>
    /// FIX1: the landing is walked along the exact line, so a spray thrown at 45 degrees ends on the tile
    /// a 45 degree line reaches and not on the one due east of the victim.
    /// </summary>
    [Test]
    public async Task SplatterLandsAlongTheExactLineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitPost(() => Floor(entities, map, 4));

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;

            // Plating on the diagonal, so the walk has somewhere to end up.
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 1), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 2), map.Tile.Tile);

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            transform.SetWorldPosition(body, new Vector2(0.5f, 0.5f));

            // A wall on the diagonal two tiles out, and clear floor due east of it.
            entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 2.5f));
            gore.TrySpawnSplatter(body, profile.HitSplatter, new Vector2(1f, 1f), FixedPoint2.New(30));
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(Splats(entities, map, new Vector2i(2, 2)), Has.Count.EqualTo(1),
                "the diagonal spray should have marked the wall on the diagonal.");
            Assert.That(Splats(entities, map, new Vector2i(2, 0)), Is.Empty,
                "and nothing due east of the victim.");
        });
    }

    /// <summary>
    /// The art is greyscale, so the colour is the whole tint: whatever the victim's bloodstream says it
    /// bleeds. An IPC's oil and a human's blood come out of the same code path.
    /// </summary>
    [Test]
    public async Task SplatterTakesTheBloodReagentColourTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;

            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);

            var blood = entities.GetComponent<BloodstreamComponent>(human).BloodReagent;
            var oil = entities.GetComponent<BloodstreamComponent>(machine).BloodReagent;
            Assert.That(blood.Id, Is.Not.EqualTo(oil.Id), "the two bodies must not bleed the same thing.");

            Assert.Multiple(() =>
            {
                Assert.That(gore.GetBloodColor(human),
                    Is.EqualTo(prototypes.Index<ReagentPrototype>(blood).SubstanceColor));
                Assert.That(gore.GetBloodColor(machine),
                    Is.EqualTo(prototypes.Index<ReagentPrototype>(oil).SubstanceColor));
            });

            var splatter = gore.TrySpawnSplatter(human, profile.HitSplatter, Vector2.UnitX, FixedPoint2.New(20));
            Assert.That(splatter, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedHitSplatterComponent>(splatter!.Value).Color,
                Is.EqualTo(prototypes.Index<ReagentPrototype>(blood).SubstanceColor),
                "the colour has to be on the effect, not on the prototype, or a late joiner sees grey.");
        });
    }

    /// <summary>
    /// The spray follows the hit's line in WORLD space on a turned grid. Every other test here uses an unturned
    /// map, which is how a screen-space (noRot) sprite passed them all and then flew off sideways on a ship,
    /// where the grid, and the camera with it, are rotated.
    /// </summary>
    [Test]
    public async Task SprayHoldsItsWorldLineOnATurnedGridTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var spray = EntityUid.Invalid;

        await server.WaitPost(() => Floor(entities, map, 4));

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;

            // A ship lying a quarter turn off the map's axes.
            transform.SetWorldRotation(map.Grid, Angle.FromDegrees(90));

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            spray = gore.TrySpawnSplatter(body, profile.HitSplatter, Vector2.UnitX, FixedPoint2.New(30))!.Value;
            Assert.That(entities.GetComponent<WolfmedHitSplatterComponent>(spray).Angle, Is.EqualTo(0f).Within(0.001f),
                "the networked angle is the world angle of the hit: due +X.");
        });

        await Pair.RunTicksSync(20);

        await Pair.Client.WaitAssertion(() =>
        {
            var mirror = clientEntities.GetEntity(entities.GetNetEntity(spray));
            var sprite = clientEntities.GetComponent<SpriteComponent>(mirror);
            var world = clientEntities.System<SharedTransformSystem>().GetWorldRotation(mirror);

            // What is on screen is the entity's world rotation plus the sprite's own.
            var drawn = (world + sprite.Rotation).Reduced();
            Assert.Multiple(() =>
            {
                Assert.That(Math.Abs(Angle.ShortestDistance(drawn, Angle.Zero).Theta), Is.LessThan(0.01),
                    $"the art must point along world +X whatever the grid is doing; it points at {drawn.Degrees:0} degrees.");

                var travelled = world.RotateVec(sprite.Offset);
                Assert.That(travelled.X, Is.GreaterThan(0.05f), "and it travels along world +X");
                Assert.That(Math.Abs(travelled.Y), Is.LessThan(0.05f), "not sideways.");
            });
        });
    }

    /// <summary>
    /// The spray that reaches a wall leaves an entity on it, facing back the way it came; the spray that
    /// does not leaves a cleanable decal where it stopped. Both carry the blood colour.
    /// </summary>
    [Test]
    public async Task SplatterLandsOnWallsAndFloorsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var colour = Color.White;
        var wallSplat = EntityUid.Invalid;

        await server.WaitPost(() => Floor(entities, map, 4));

        await server.WaitAssertion(() =>
        {
            var gore = entities.System<WolfmedGoreSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            transform.SetWorldPosition(body, new Vector2(0.5f, 0.5f));
            colour = gore.GetBloodColor(body)!.Value;

            // A wall two tiles east, inside a three-tile spray's reach.
            entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 0.5f));

            gore.TrySpawnSplatter(body, profile.HitSplatter, Vector2.UnitX, FixedPoint2.New(30));
        });

        // Past the travel time, so the landing has been placed.
        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var splats = Splats(entities, map, new Vector2i(2, 0));
            Assert.That(splats, Has.Count.EqualTo(1), "the spray should have marked the wall it hit.");

            wallSplat = splats[0];
            var splat = entities.GetComponent<WolfmedBloodSplatComponent>(splats[0]);
            Assert.Multiple(() =>
            {
                Assert.That(MathF.Abs(splat.Angle), Is.EqualTo(MathF.PI).Within(0.001f),
                    "a wall splatter faces back towards whatever threw it: due west of a spray going east.");
                Assert.That(splat.Color, Is.EqualTo(colour));
                Assert.That(entities.HasComponent<WolfmedCleanableComponent>(splats[0]), Is.True,
                    "a janitor has to be able to wash it off.");
            });

            // Nothing in the way: the same spray ends on the deck as a decal instead.
            var gore = entities.System<WolfmedGoreSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            gore.TrySpawnSplatter(body, profile.HitSplatter, Vector2.UnitX, FixedPoint2.New(5));
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;
            var decals = entities.System<DecalSystem>()
                .GetDecalsIntersecting(map.Grid.Owner, Box2.FromDimensions(new Vector2(1, 1), Vector2.One))
                .Where(entry => profile.HitSplatter.FloorDecals.Contains(entry.Decal.Id))
                .ToList();

            Assert.That(decals, Has.Count.EqualTo(1), "the spray should have left a splat on the deck.");
            Assert.Multiple(() =>
            {
                Assert.That(decals[0].Decal.Cleanable, Is.True, "space cleaner has to take it.");
                Assert.That(decals[0].Decal.Color, Is.EqualTo(colour), "and it has to be the right colour.");
            });
        });

        // The tint has to survive the wire, or anyone who was not watching when it landed sees grey.
        await Pair.Client.WaitAssertion(() =>
        {
            var mirror = clientEntities.GetEntity(entities.GetNetEntity(wallSplat));
            Assert.Multiple(() =>
            {
                Assert.That(clientEntities.GetComponent<WolfmedBloodSplatComponent>(mirror).Color,
                    Is.EqualTo(colour));
                Assert.That(clientEntities.GetComponent<SpriteComponent>(mirror).Color, Is.EqualTo(colour),
                    "the client has to have painted the sprite with it.");
            });
        });
    }

    /// <summary>
    /// Space cleaner takes the wall splats the same way it takes the floor decals, and one tile only ever
    /// holds so many of them however long the firefight lasted.
    /// </summary>
    [Test]
    public async Task SplatsAreCleanableAndCappedPerTileTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitPost(() => Floor(entities, map, 4));

        await server.WaitAssertion(() =>
        {
            // The reagent that purges cleanable decals also runs the Wolfmed reaction, so one mop stroke
            // takes both halves of a splat.
            var cleaner = prototypes.Index<ReagentPrototype>("SpaceCleaner");
            Assert.That(cleaner.TileReactions.Any(reaction => reaction is WolfmedCleanSplats), Is.True,
                "space cleaner does not know about the wall splats.");

            var gore = entities.System<WolfmedGoreSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            transform.SetWorldPosition(body, new Vector2(0.5f, 0.5f));
            entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 0.5f));

            // Far more sprays than the cap allows, all at the same piece of wall.
            for (var i = 0; i < profile.HitSplatter.MaxPerTile + 4; i++)
                gore.TrySpawnSplatter(body, profile.HitSplatter, Vector2.UnitX, FixedPoint2.New(30));
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var profile = entities.System<WolfmedWoundSfxSystem>().Profile!;
            var splats = Splats(entities, map, new Vector2i(2, 0));
            Assert.That(splats, Has.Count.LessThanOrEqualTo(profile.HitSplatter.MaxPerTile),
                "a wall must not collect dozens of them.");
            Assert.That(splats, Is.Not.Empty);

            var grid = entities.GetComponent<MapGridComponent>(map.Grid.Owner);
            var spent = entities.System<WolfmedGoreSystem>()
                .CleanTile(map.Grid.Owner, grid, new Vector2i(2, 0), 100f);
            Assert.That(spent, Is.GreaterThan(0f), "cleaning has to cost the reagent something.");
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(Splats(entities, map, new Vector2i(2, 0)), Is.Empty, "the wall should be clean."));
    }

    // --- G2 ---------------------------------------------------------------------------------------

    /// <summary>
    /// An open stump throws blood every few seconds until something is done about it, and the clock is
    /// only ever on a body that has a reason for it.
    /// </summary>
    [Test]
    public async Task SpurtsStartAndStopWithTheBleedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bleeding = entities.System<WoundBleedingSystem>();
            var spurts = entities.System<WolfmedBleedSpurtSystem>();
            var wounds = entities.System<WoundSystem>();
            var spec = entities.System<WolfmedWoundSfxSystem>().Profile!.BleedSpurt;

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            spurts.Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedBleedSpurtComponent>(body), Is.False,
                    "an unhurt body has nothing to spurt.");
                Assert.That(spurts.TrySpurt(body), Is.False);
            });

            // An untreated stump: the one source that qualifies whatever its rate is.
            // Playtest 5: a heavy ordinary bleed drips, it does not spurt; a cut artery does whatever its rate.
            var slash = wounds.CreateOrMergeWound(arm, "SlashWound", FixedPoint2.New(60));
            Assert.That(slash, Is.Not.Null);
            var slashRate = entities.GetComponent<WoundBleedingComponent>(slash!.Value).CurrentRate;
            Assert.That(slashRate, Is.GreaterThan(0f), "the fixture's slash does not bleed.");
            Assert.That(spurts.HasSpurtSource(body, spec, out _, out _), Is.False,
                $"an ordinary bleed at {slashRate:0.00} spurted.");
            var artery = wounds.CreateOrMergeWound(arm, "WFWolfmedArterialBleedWound", FixedPoint2.New(10));
            Assert.That(artery, Is.Not.Null);
            Assert.That(spurts.HasSpurtSource(body, spec, out var arteryStump, out _), Is.True, "a cut artery is not a spurt source.");
            Assert.That(arteryStump, Is.False);
            wounds.RemoveWound(artery!.Value);
            wounds.RemoveWound(slash.Value);
            Assert.That(spurts.HasSpurtSource(body, spec, out _, out _), Is.False);

            var stump = wounds.CreateOrMergeWound(arm, "DismembermentWound", FixedPoint2.New(40));
            Assert.That(stump, Is.Not.Null);

            Assert.That(spurts.HasSpurtSource(body, spec, out var isStump, out _), Is.True,
                "an open stump is a spurt source.");
            Assert.That(isStump, Is.True);

            spurts.Refresh(body);
            Assert.That(entities.HasComponent<WolfmedBleedSpurtComponent>(body), Is.True,
                "the clock should be running.");
            Assert.That(spurts.TrySpurt(body), Is.True);

            // Clamped by a tourniquet: the stump stops counting, and so does the clock.
            Assert.That(bleeding.SetTreatment(stump!.Value, BleedingTreatment.Clamped), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(spurts.HasSpurtSource(body, spec, out _, out _), Is.False,
                    "a treated stump is not a spurt source.");
                Assert.That(spurts.TrySpurt(body), Is.False, "and a spurt on it does nothing.");
            });

            spurts.Refresh(body);
            Assert.That(entities.HasComponent<WolfmedBleedSpurtComponent>(body), Is.False,
                "the clock has to come off, or the tick keeps walking this body forever.");
        });
    }

    /// <summary>The spurt profile is shipped data, and the CVar turns the whole thing off.</summary>
    [Test]
    public async Task SpurtProfileAndCVarTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;

        Assert.That(server.CfgMan.GetCVar(WolfmedCVars.BleedSpurts), Is.True);

        await server.WaitAssertion(() =>
        {
            var spec = entities.System<WolfmedWoundSfxSystem>().Profile!.BleedSpurt;
            Assert.Multiple(() =>
            {
                Assert.That(spec.Sound, Is.InstanceOf<SoundCollectionSpecifier>());
                Assert.That(spec.StumpSound, Is.InstanceOf<SoundCollectionSpecifier>());
                Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(
                    ((SoundCollectionSpecifier) spec.Sound!).Collection!), Is.True);
                Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(
                    ((SoundCollectionSpecifier) spec.StumpSound!).Collection!), Is.True);

                foreach (var wound in spec.StumpWounds)
                    Assert.That(prototypes.HasIndex<WoundPrototype>(wound), Is.True, $"{wound} does not exist.");

                Assert.That(spec.Interval, Is.GreaterThan(spec.Jitter),
                    "the jitter must not be able to make the interval negative.");
            });

            var splatter = entities.System<WolfmedWoundSfxSystem>().Profile!.HitSplatter;
            Assert.Multiple(() =>
            {
                Assert.That(prototypes.HasIndex<EntityPrototype>(splatter.Effect), Is.True);
                Assert.That(prototypes.HasIndex<EntityPrototype>(splatter.WallSplat), Is.True);
                Assert.That(splatter.FloorDecals, Is.Not.Empty);
                foreach (var decal in splatter.FloorDecals)
                {
                    Assert.That(prototypes.TryIndex(decal, out DecalPrototype? proto), Is.True,
                        $"{decal} does not exist.");
                    Assert.That(proto!.DefaultCleanable, Is.True, $"{decal} is not cleanable.");
                    Assert.That(proto.DefaultCustomColor, Is.True, $"{decal} cannot take the blood colour.");
                }
            });
        });
    }

    // --- G3 ---------------------------------------------------------------------------------------

    /// <summary>
    /// A dressing and a splint show on the limb that is wearing them, the improvised splint reads as one,
    /// and the overlay goes away when the treatment does.
    /// </summary>
    [Test]
    public async Task TreatmentOverlaysFollowTheTreatmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var bleeding = entities.System<WoundBleedingSystem>();
            var wounds = entities.System<WoundSystem>();

            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var hand = Part(entities, body, BodyPartType.Hand, BodyPartSymmetry.Right);

            Assert.That(Overlay(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartTreatment.None), "an untreated arm wears nothing.");

            // Gauze over a cut.
            var cut = wounds.CreateOrMergeWound(arm, "SlashWound", FixedPoint2.New(25));
            Assert.That(cut, Is.Not.Null);
            Assert.That(bleeding.SetTreatment(cut!.Value, BleedingTreatment.Bandaged), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Overlay(entities, body, HumanoidVisualLayers.LArm),
                    Is.EqualTo(WolfmedPartTreatment.Gauze));
                Assert.That(Overlay(entities, body, HumanoidVisualLayers.RArm),
                    Is.EqualTo(WolfmedPartTreatment.None), "the other arm is untouched.");
            });

            // A dressed hand shows on its arm: the art has one band per limb.
            var handCut = wounds.CreateOrMergeWound(hand, "SlashWound", FixedPoint2.New(20));
            Assert.That(handCut, Is.Not.Null);
            Assert.That(bleeding.SetTreatment(handCut!.Value, BleedingTreatment.Sutured), Is.True);
            Assert.That(Overlay(entities, body, HumanoidVisualLayers.RArm),
                Is.EqualTo(WolfmedPartTreatment.Gauze), "a hand folds into its arm.");

            // Healing the wound leaves the dressing on: it lingers (profile.DressingLinger) so the medic can see
            // the work was done, and comes off when its mark expires.
            wounds.RemoveWound(cut.Value);
            Assert.That(Overlay(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartTreatment.Gauze), "the bandage outlives the wound it closed.");

            var leftArm = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .First(part => part.Component.PartType == BodyPartType.Arm &&
                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            entities.GetComponent<Content.Server._WF.Wolfmed.Damage.WolfmedDressingMarkComponent>(leftArm).Until =
                System.TimeSpan.Zero;
            entities.System<WolfmedTreatmentVisualsSystem>().Update(0f);
            entities.System<WolfmedTreatmentVisualsSystem>().Refresh(body);
            Assert.That(Overlay(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartTreatment.None), "and comes off once its time is up.");
        });

        await Pair.RunTicksSync(10);

        await Pair.Client.WaitAssertion(() =>
        {
            var clientBody = clientEntities.GetEntity(entities.GetNetEntity(body));
            Assert.That(clientEntities.GetComponent<PartDamageVisualsComponent>(clientBody)
                    .Treatments.GetValueOrDefault(HumanoidVisualLayers.RArm),
                Is.EqualTo(WolfmedPartTreatment.Gauze), "the overlay has to reach the client to be drawn.");

            var sprites = clientEntities.System<SpriteSystem>();
            var sprite = clientEntities.GetComponent<SpriteComponent>(clientBody);
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedTreatmentRArm",
                out var overlay, false), Is.True, "the client never added the overlay layer.");

            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), HumanoidVisualLayers.RArm,
                    out var limb, false), Is.True);
                Assert.That(overlay, Is.GreaterThan(limb), "a bandage draws over the arm it is on.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "jumpsuit",
                    out var jumpsuit, false), Is.True);
                // Over the jumpsuit so a dressed chest can be seen at all, under a coat or a hardsuit.
                Assert.That(overlay, Is.GreaterThan(jumpsuit), "a dressing under the jumpsuit showed nothing.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "outerClothing", out var outer, false), Is.True);
                Assert.That(overlay, Is.LessThan(outer), "outer clothing still covers it.");
                Assert.That(sprites.TryGetLayer((clientBody, sprite), "WolfmedTreatmentRArm",
                    out var layer, false) && layer.Visible, Is.True, "the overlay layer is off.");
            });
        });
    }

    /// <summary>
    /// A splint outranks a dressing on the same limb, and the two splints are told apart, which a
    /// FractureTreatment alone cannot do.
    /// </summary>
    [Test]
    public async Task SplintOverlaysAreTheSplintThatWasUsedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            var splints = entities.System<WolfmedSplintSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            // 60 Blunt clears the Comminuted threshold at creationChance 1, as WolfmedSplintTest relies on.
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            Blunt(entities, body, TargetBodyPart.RightArm, 60);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);
            Assert.That(fractures.GetFracture(leg), Is.Not.Null, "the leg did not break.");
            Assert.That(fractures.GetFracture(arm), Is.Not.Null, "the arm did not break.");

            Assert.That(Overlay(entities, body, HumanoidVisualLayers.LLeg),
                Is.EqualTo(WolfmedPartTreatment.None), "an untreated break shows no splint.");

            var splint = entities.SpawnEntity("WFWolfmedSplint", map.GridCoords);
            var improvised = entities.SpawnEntity("WFWolfmedSplintImprovised", map.GridCoords);
            Assert.That(splints.TryApply((splint, entities.GetComponent<WolfmedSplintComponent>(splint)),
                body, leg, body), Is.True);
            Assert.That(splints.TryApply(
                (improvised, entities.GetComponent<WolfmedSplintComponent>(improvised)), body, arm, body), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(Overlay(entities, body, HumanoidVisualLayers.LLeg),
                    Is.EqualTo(WolfmedPartTreatment.Splint));
                Assert.That(Overlay(entities, body, HumanoidVisualLayers.RArm),
                    Is.EqualTo(WolfmedPartTreatment.SplintImprovised),
                    "a rod and a rag must not read as a medical brace.");
            });

            // A hard hit undoes the reduction, exactly as it undoes a bonesetter's; the overlay goes too.
            Blunt(entities, body, TargetBodyPart.LeftLeg, 20);
            Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment,
                Is.EqualTo(FractureTreatment.None));
            Assert.Multiple(() =>
            {
                Assert.That(Overlay(entities, body, HumanoidVisualLayers.LLeg),
                    Is.EqualTo(WolfmedPartTreatment.None));
                Assert.That(entities.HasComponent<WolfmedSplintMarkComponent>(leg), Is.False,
                    "the mark has to be cleaned up, or the next splint inherits it.");
            });
        });
    }

    /// <summary>
    /// Every (layer, treatment) pair the profile can select resolves to a state that exists. A missing
    /// state is a client error log, which is a failure everywhere else in this suite.
    /// </summary>
    [Test]
    public async Task TreatmentArtCoversEveryLayerAndTreatmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var profile = server.ProtoMan.Index<WolfmedTreatmentOverlayProfilePrototype>(
            WolfmedTreatmentVisualsComponent.DefaultProfile);
        var cache = Pair.Client.ResolveDependency<IResourceCache>();

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / profile.Rsi, out var rsi),
                Is.True, $"{profile.ID}: RSI {profile.Rsi} not found.");

            Assert.Multiple(() =>
            {
                foreach (var layer in WolfmedTreatmentLayers.All)
                {
                    foreach (var treatment in Enum.GetValues<WolfmedPartTreatment>())
                    {
                        if (treatment == WolfmedPartTreatment.None)
                            continue;

                        var state = profile.GetState(layer, treatment);
                        Assert.That(state, Is.Not.Null, $"{profile.ID}: no state for {layer}/{treatment}.");
                        Assert.That(rsi!.RSI.TryGetState(state!, out _), Is.True,
                            $"{profile.ID}: state {state} is missing from {profile.Rsi}.");
                    }
                }

                Assert.That(profile.Dressings, Does.Contain(BleedingTreatment.Bandaged));
                Assert.That(profile.Dressings, Does.Not.Contain(BleedingTreatment.None));
            });
        });
    }

    // --- helpers ----------------------------------------------------------------------------------

    /// <summary>Lays plating east along y = 0 and y = 1 so a spray has somewhere to land.</summary>
    private static void Floor(IEntityManager entities, TestMapData map, int length)
    {
        var maps = entities.System<SharedMapSystem>();
        var tile = map.Tile.Tile;
        for (var x = 0; x < length; x++)
        {
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 1), tile);
        }
    }

    /// <summary>Every Wolfmed wall splat currently sitting on one tile.</summary>
    private static List<EntityUid> Splats(IEntityManager entities, TestMapData map, Vector2i tile)
    {
        var found = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<WolfmedBloodSplatComponent, TransformComponent>();
        var maps = entities.System<SharedMapSystem>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (entities.IsQueuedForDeletion(uid) || xform.GridUid != map.Grid.Owner)
                continue;

            if (maps.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, xform.Coordinates) == tile)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>The overlay a limb is wearing, with the coalesced refresh drained first.</summary>
    private static WolfmedPartTreatment Overlay(IEntityManager entities, EntityUid body,
        HumanoidVisualLayers layer)
    {
        entities.System<WolfmedTreatmentVisualsSystem>().Update(0f);
        return entities.GetComponent<PartDamageVisualsComponent>(body).Treatments.GetValueOrDefault(layer);
    }

    /// <summary>Blunt damage on one limb, which is what makes a fracture and what resets its treatment.</summary>
    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        var spec = new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
        };
        entities.System<DamageableSystem>().TryChangeDamage(body, spec, origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }
}
