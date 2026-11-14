#nullable enable
using System.Threading.Tasks;
using System.Linq;
using Content.Client._WF.Wolfmed.Audio;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The crit heartbeat only tracks the local player's own body: it starts on entering MobState.Critical and
/// stops on recovering, dying, or re-entering crit again after recovery.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedCritHeartbeatSystem))]
public sealed class WolfmedCritHeartbeatTest
{
    [Test]
    public async Task HeartbeatTracksLocalPlayerCritTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var mindSys = sEntMan.System<SharedMindSystem>();
        var mobState = sEntMan.System<MobStateSystem>();
        var heartbeat = client.System<WolfmedCritHeartbeatSystem>();
        var map = await pair.CreateTestMap();

        Assert.That(client.Session, Is.Not.Null, "These tests need a connected pair.");
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });
        await pair.RunTicksSync(30);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the new body.");

        await AssertHeartbeat(pair, body, MobState.Alive, false,
            "Precondition: an alive player hears no heartbeat.");
        await AssertHeartbeat(pair, body, MobState.Critical, true,
            "Entering crit must start the loop.");
        await AssertHeartbeat(pair, body, MobState.Alive, false,
            "Recovering from crit must stop the loop.");
        await AssertHeartbeat(pair, body, MobState.Critical, true,
            "Re-entering crit must restart the loop.");
        await AssertHeartbeat(pair, body, MobState.Dead, false,
            "Dying out of crit must stop the loop too.");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// M1a: a pain faint is Critical now, but a few seconds under from pain is not the dying heartbeat; and a
    /// machine never hears a human heart (plan §3.11).
    /// </summary>
    [Test]
    public async Task HeartbeatSilentForFaintsAndMachinesTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var mindSys = sEntMan.System<SharedMindSystem>();
        var heartbeat = client.System<WolfmedCritHeartbeatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            human = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, human);

            // 100 on the torso and 100 on the head: 200 summed, past the 189 faint line.
            var pain = sEntMan.System<PainSystem>();
            foreach (var (part, comp) in sEntMan.System<SharedBodySystem>().GetBodyChildren(human))
            {
                if (comp.PartType is BodyPartType.Torso or BodyPartType.Head)
                    pain.SetPain(part, FixedPoint2.New(100));
            }
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var consciousness = sEntMan.GetComponent<WolfmedConsciousnessComponent>(human);
            Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.PainFaint), "the fixture did not faint.");
            Assert.That(sEntMan.System<MobStateSystem>().IsCritical(human), Is.True);
        });
        await client.WaitPost(() =>
        {
            Assert.That(client.EntMan.GetComponent<MobStateComponent>(pair.ToClientUid(human)).CurrentState,
                Is.EqualTo(MobState.Critical), "the client never saw the faint.");
            Assert.That(heartbeat.Active, Is.False, "a pain faint started the dying heartbeat.");
        });

        EntityUid ipc = default;
        await server.WaitPost(() =>
        {
            ipc = sEntMan.SpawnEntity("MobIPC", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, ipc);
        });
        await pair.RunTicksSync(30);

        // Held in Critical the way the organic test holds it: a machine still never hears it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await server.WaitPost(() => sEntMan.System<MobStateSystem>().ChangeMobState(ipc, MobState.Critical));
            await pair.RunTicksSync(10);
            await client.WaitPost(() =>
                Assert.That(heartbeat.Active, Is.False, "a machine heard the human heartbeat."));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Playtest 4: the flat tone marks the arrest, not death. The client reads the arrest marker on its own body
    /// the moment the server starts one, the loop is off while the heart is stopped, and a restart clears it.
    /// </summary>
    [Test]
    public async Task FlatlineMarksTheArrestTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var mindSys = sEntMan.System<SharedMindSystem>();
        var life = sEntMan.System<WolfmedLifeSystem>();
        var heartbeat = client.System<WolfmedCritHeartbeatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, body);
        });
        await pair.RunTicksSync(30);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the new body.");
        await client.WaitPost(() => Assert.That(heartbeat.Flatlined, Is.False, "Precondition: a beating heart reads as flatlined."));

        await server.WaitPost(() => Assert.That(life.StartArrest(body, "test"), Is.True, "the fixture did not arrest."));
        await WaitFlatline(pair, true, "The client never saw the arrest.");
        await server.WaitAssertion(() =>
            Assert.That(sEntMan.System<MobStateSystem>().IsCritical(body), Is.True, "an arrest is not Critical."));
        await client.WaitPost(() => Assert.That(heartbeat.Active, Is.False, "the heartbeat loop ran through the arrest."));

        await server.WaitPost(() => Assert.That(life.EndArrest(body), Is.True, "the fixture did not restart."));
        await WaitFlatline(pair, false, "The client never saw the restart.");

        await pair.CleanReturnAsync();
    }

    private static async Task WaitFlatline(TestPair pair, bool expected, string because)
    {
        var heartbeat = pair.Client.System<WolfmedCritHeartbeatSystem>();
        var flatlined = !expected;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await pair.RunTicksSync(5);
            await pair.Client.WaitPost(() => flatlined = heartbeat.Flatlined);
            if (flatlined == expected)
                return;
        }

        Assert.Fail($"{because} (flatlined {flatlined}).");
    }

    /// <summary>
    /// Puts the body in a state through its consciousness and waits until the client's copy agrees and the
    /// heartbeat reads as expected.
    /// </summary>
    /// <remarks>
    /// Consciousness owns a wound host's mob state and writes it on every evaluation, so a state set on the
    /// component directly is undone by the next evaluation, on the same tick when one lands there, before the
    /// client ever sees it. An outside pressure of 1 is Unconscious, and so Critical, for as long as it is set.
    /// </remarks>
    private static async Task AssertHeartbeat(TestPair pair, EntityUid body, MobState state, bool active, string because)
    {
        var mobState = pair.Server.EntMan.System<MobStateSystem>();
        var consciousness = pair.Server.EntMan.System<SharedWolfmedConsciousnessSystem>();
        var heartbeat = pair.Client.System<WolfmedCritHeartbeatSystem>();
        var clientState = MobState.Invalid;
        var heard = !active;

        await pair.Server.WaitPost(() =>
        {
            if (state == MobState.Dead)
                mobState.ChangeMobState(body, MobState.Dead);
            else
                consciousness.SetExternalPressure(body, "test", state == MobState.Critical ? 1f : 0f);
        });

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await pair.RunTicksSync(10);
            await pair.Client.WaitPost(() =>
            {
                heard = heartbeat.Active;
                clientState = pair.Client.EntMan.TryGetComponent(pair.ToClientUid(body), out MobStateComponent? comp)
                    ? comp.CurrentState
                    : MobState.Invalid;
            });

            if (clientState == state && heard == active)
                return;
        }

        Assert.Fail($"{because} (client state {clientState}, heartbeat {heard}).");
    }
}
