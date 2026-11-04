#nullable enable
using System.Numerics;
using System.Text;
using Content.IntegrationTests.Pair;
using Content.Server.CombatMode;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.NpcFacing;

/// <summary>Mindless NPCs turn to face where they walk and whom they hit, as zombies in combat mode once did not.</summary>
[TestFixture]
[TestOf(typeof(CombatModeSystem))]
public sealed class NpcFacingTest
{
    private static async Task<(TestMapData Map, EntityUid Player)> Rig(TestPair pair, int size = 12)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -size; x <= size; x++)
            for (var y = -size; y <= size; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var gravity = server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
            server.EntMan.Dirty(map.Grid.Owner, gravity);
            player = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        return (map, player);
    }

    /// <summary>The angle between where <paramref name="npc"/> looks and the direction <paramref name="dir"/>, in degrees.</summary>
    private static double Off(TestPair pair, EntityUid npc, Vector2 dir)
    {
        var facing = pair.Server.System<SharedTransformSystem>().GetWorldRotation(npc);
        return Math.Abs(Angle.ShortestDistance(facing, dir.ToWorldAngle()).Degrees);
    }

    /// <summary>Strangers and crew, whose brains never touched combat mode before they existed, look the way a steering order sends them.</summary>
    [TestCase("WFStrangerDealer")]
    [TestCase("WFMobCrewDeckhand")]
    public async Task NpcLooksTheWayItIsSentWalking(string proto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false));
        var (map, _) = await Rig(pair);

        EntityUid npc = default;
        await server.WaitPost(() => npc = entMan.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, -6.5f, -6.5f)));
        await pair.RunSeconds(0.3f);

        var worst = 0.0;
        var walked = 0;
        for (var tick = 0; tick < 120; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                // North-east of where it stands, whatever its own brain would rather do.
                server.System<NPCSteeringSystem>().Register(npc, new EntityCoordinates(map.Grid.Owner, 6.5f, 6.5f));
                var vel = entMan.GetComponent<PhysicsComponent>(npc).LinearVelocity;
                if (tick >= 15 && vel.Length() > 1.5f)
                {
                    walked++;
                    worst = Math.Max(worst, Off(pair, npc, vel));
                }
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(walked, Is.GreaterThan(30), $"The {proto} hardly walked at all.");
            Assert.That(worst, Is.LessThan(60), $"The {proto} walked up to {worst:0} degrees off the way it looked.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A zombie that starts far off, or already next to the player, looks the way it walks and at the player it hits.</summary>
    [TestCase(-6.5f)]
    [TestCase(1.5f)]
    public async Task ZombieFacesThePlayerItWalksToAndHits(float startX)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false));
        var (map, player) = await Rig(pair);
        // Held in place, so the zombie has something to stand over rather than shoving it about.
        await server.WaitPost(() => server.System<SharedPhysicsSystem>().SetBodyType(player, BodyType.Static));

        EntityUid zombie = default;
        await server.WaitPost(() => zombie = entMan.SpawnEntity("WFWreckZombieVerda", new EntityCoordinates(map.Grid.Owner, startX, 0.5f)));
        await pair.RunSeconds(0.3f);

        var worstWalk = 0.0;
        var worstHit = 0.0;
        var hits = 0;
        var lastDamage = 0f;
        var trace = new StringBuilder();
        for (var tick = 0; tick < 420; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                var xforms = server.System<SharedTransformSystem>();
                var toPlayer = xforms.GetWorldPosition(player) - xforms.GetWorldPosition(zombie);
                var speed = entMan.GetComponent<PhysicsComponent>(zombie).LinearVelocity.Length();
                var damage = (float) entMan.GetComponent<DamageableComponent>(player).TotalDamage;
                var offPlayer = Off(pair, zombie, toPlayer);

                // Walking: it looks the way it moves.
                if (tick >= 15 && speed > 1.5f)
                    worstWalk = Math.Max(worstWalk, Off(pair, zombie, entMan.GetComponent<PhysicsComponent>(zombie).LinearVelocity));

                // A zombie swing lands for far more than the airless room's wear on the player.
                if (damage - lastDamage > 5f && toPlayer.Length() < 1.6f)
                {
                    // The first swing can land as the zombie is still coming round from wherever it spawned looking.
                    if (hits++ > 0)
                        worstHit = Math.Max(worstHit, offPlayer);
                    trace.Append($"\n tick {tick} hit: looking {offPlayer:0} degrees off the player, speed {speed:0.0}, {toPlayer.Length():0.0} tiles off");
                }

                lastDamage = damage;
            });
        }

        await server.WaitAssertion(() =>
        {
            var noRotate = entMan.HasComponent<NoRotateOnMoveComponent>(zombie);
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(zombie), Is.True, "The zombie never woke.");
            Assert.That(hits, Is.GreaterThan(1), "The zombie never kept hitting the player.");
            Assert.That(worstWalk, Is.LessThan(60), $"The zombie walked up to {worstWalk:0} degrees off the way it looked (NoRotateOnMove: {noRotate}).");
            Assert.That(worstHit, Is.LessThan(60), $"The zombie hit the player looking up to {worstHit:0} degrees away (NoRotateOnMove: {noRotate}).{trace}");
        });
        await pair.CleanReturnAsync();
    }
}
