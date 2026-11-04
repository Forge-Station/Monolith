#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetComms;
using Content.Server.Construction;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.CCVar;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.PlanetComms;

/// <summary>Planet comms: radio and announcements on a world only get through while a mass transceiver there runs.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetCommsSystem))]
public sealed class PlanetCommsTest
{
    /// <summary>A long-range channel, so no telecom server is needed and only the transceiver gates it.</summary>
    private const string Channel = "CentCom";
    private const string Handheld = "WFPlanetCommsTestHandheld";
    private const string Headset = "ClothingHeadsetGrey";
    private const string Mob = "MobHuman";
    private static readonly ProtoId<RadioChannelPrototype> CrewChannel = "WFCrew";
    private static readonly ProtoId<RadioChannelPrototype> HandheldChannel = "Handheld";
    private static readonly ProtoId<ConstructionGraphPrototype> Graph = "WFMassTransceiverGraph";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: WFPlanetCommsTestHandheld
  components:
  - type: TelecomExempt
  - type: RadioSpeaker
    channels:
    - CentCom
";

    /// <summary>Headset radio to and from a world follows its transceiver; the crew channel and handhelds ignore it.</summary>
    [Test]
    public async Task RadioFollowsTransceiver()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var radio = server.System<RadioSystem>();
        var comms = server.System<WFPlanetCommsSystem>();
        var power = server.System<PowerReceiverSystem>();
        var spy = server.System<WFPlanetCommsTestSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];
        await PlanetFixture.LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(8, 4));
        var offworld = await pair.CreateTestMap();

        try
        {
            EntityUid speaker = default, speakerSet = default, listenerSet = default, groundSet = default;
            EntityUid offSpeaker = default, offSpeakerSet = default, handheld = default, transceiver = default;
            RadioChannelPrototype channel = default!;
            var sent = 0;

            await server.WaitPost(() =>
            {
                channel = protos.Index<RadioChannelPrototype>(Channel);
                (speaker, speakerSet) = SpawnWithHeadset(server, new EntityCoordinates(ground, new Vector2(0.5f, 0.5f)));
                (_, groundSet) = SpawnWithHeadset(server, new EntityCoordinates(ground, new Vector2(2.5f, 0.5f)));
                (_, listenerSet) = SpawnWithHeadset(server, offworld.GridCoords);
                (offSpeaker, offSpeakerSet) = SpawnWithHeadset(server, offworld.GridCoords);

                handheld = entMan.SpawnEntity(Handheld, new EntityCoordinates(ground, new Vector2(1.5f, 1.5f)));
                entMan.EnsureComponent<ActiveRadioComponent>(handheld).Channels.Add(Channel);
                entMan.EnsureComponent<WFPlanetCommsTestReceiverComponent>(handheld);
            });
            await server.WaitRunTicks(2);

            // Each send uses fresh text: the radio system drops a message it is already relaying.
            async Task<List<EntityUid>> Send(EntityUid source, EntityUid radioSource)
            {
                spy.Deliveries.Clear();
                await server.WaitPost(() => radio.SendRadioMessage(source, $"planet comms check {sent++}", channel, radioSource));
                await server.WaitRunTicks(1);
                return spy.Deliveries.ToList();
            }

            var heard = await Send(speaker, speakerSet);
            Assert.That(heard, Does.Not.Contain(listenerSet), "A world with no transceiver got a headset message off it.");
            Assert.That(heard, Does.Not.Contain(groundSet), "A headset on a world with no transceiver heard its neighbour.");

            heard = await Send(offSpeaker, offSpeakerSet);
            Assert.That(heard, Does.Contain(listenerSet), "Off-world headsets stopped hearing each other.");
            Assert.That(heard, Does.Not.Contain(groundSet), "A headset on a dark world heard an off-world message.");
            Assert.That(heard, Does.Contain(handheld), "A handheld radio on a dark world went deaf.");

            heard = await Send(speaker, handheld);
            Assert.That(heard, Does.Contain(listenerSet), "A handheld radio on a dark world couldn't transmit.");

            await server.WaitAssertion(() =>
            {
                Assert.That(comms.CanTransmit(protos.Index(CrewChannel), speakerSet), Is.True,
                    "The crew channel needed a transceiver.");
                Assert.That(comms.CanHear(protos.Index(CrewChannel), groundSet), Is.True,
                    "The crew channel needed a transceiver.");
                Assert.That(comms.CanTransmit(protos.Index(HandheldChannel), speakerSet), Is.True,
                    "The handheld band needed a transceiver.");
                Assert.That(comms.CanTransmit(channel, speakerSet), Is.False);
            });

            // An unpowered transceiver does nothing.
            await server.WaitPost(() =>
            {
                transceiver = entMan.SpawnEntity("WFMassTransceiver", new EntityCoordinates(ground, new Vector2(5.5f, 0.5f)));
            });
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(transceiver).Anchored, Is.True, "The transceiver didn't anchor.");
                Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(transceiver).Powered, Is.False);
                Assert.That(comms.IsCommsUp(ground), Is.False);
            });
            heard = await Send(speaker, speakerSet);
            Assert.That(heard, Does.Not.Contain(listenerSet), "An unpowered transceiver carried a message.");

            // Powered, the world is up from the ground to orbit.
            // Mono runs the power net twice a second, so give it a while.
            await server.WaitPost(() => power.SetNeedsPower(transceiver, false));
            await server.WaitRunTicks(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(transceiver).Powered, Is.True);
                Assert.That(comms.IsCommsUp(ground), Is.True);
                Assert.That(comms.IsCommsUp(layers[^1]), Is.True, "The orbit layer isn't part of the world.");
            });
            heard = await Send(speaker, speakerSet);
            Assert.That(heard, Does.Contain(listenerSet), "A powered transceiver didn't carry a message off the world.");
            Assert.That(heard, Does.Contain(groundSet), "A powered transceiver didn't carry a message across the world.");
            heard = await Send(offSpeaker, offSpeakerSet);
            Assert.That(heard, Does.Contain(groundSet), "A powered transceiver didn't bring a message down.");

            // Wrecked, it carries nothing, powered or not.
            await server.WaitPost(() =>
            {
                Assert.That(server.System<ConstructionSystem>().ChangeNode(transceiver, null, "broken"), Is.True);
            });
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() => Assert.That(comms.IsCommsUp(ground), Is.False, "A wrecked transceiver kept the world up."));
            heard = await Send(speaker, speakerSet);
            Assert.That(heard, Does.Not.Contain(listenerSet), "A wrecked transceiver carried a message.");

            // With the gate off, nothing is cut.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCommsCVars.Enabled, false));
            heard = await Send(speaker, speakerSet);
            Assert.That(heard, Does.Contain(listenerSet), "wf.planet_comms false still gated radio.");
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCommsCVars.Enabled, true));
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Announcements drop a session on a dark world unless they come from that world.</summary>
    [Test]
    public async Task AnnouncementsSkipDarkWorlds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var comms = server.System<WFPlanetCommsSystem>();
        var power = server.System<PowerReceiverSystem>();

        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];
        await PlanetFixture.LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(8, 4));
        var offworld = await pair.CreateTestMap();

        try
        {
            await PlanetFixture.AttachViewer(pair, ground, new Vector2(0.5f, 0.5f));
            var player = pair.Player!;
            EntityUid local = default, remote = default, transceiver = default;

            await server.WaitPost(() =>
            {
                local = entMan.SpawnEntity(null, new EntityCoordinates(layers[1], Vector2.Zero));
                remote = entMan.SpawnEntity(null, offworld.GridCoords);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(comms.IsCommsUp(player), Is.False);
                Assert.That(comms.Reachable(Filter.Broadcast(), null).Recipients, Does.Not.Contain(player),
                    "A global announcement reached a dark world.");
                Assert.That(comms.Reachable(Filter.Broadcast(), remote).Recipients, Does.Not.Contain(player),
                    "An off-world announcement reached a dark world.");
                Assert.That(comms.Reachable(Filter.Broadcast(), local).Recipients, Does.Contain(player),
                    "An announcement from the same world was cut.");
            });

            await server.WaitPost(() =>
            {
                transceiver = entMan.SpawnEntity("WFMassTransceiver", new EntityCoordinates(ground, new Vector2(5.5f, 0.5f)));
                power.SetNeedsPower(transceiver, false);
            });
            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
            {
                Assert.That(comms.IsCommsUp(player), Is.True);
                Assert.That(comms.Reachable(Filter.Broadcast(), null).Recipients, Does.Contain(player),
                    "A global announcement missed a world whose transceiver runs.");
                Assert.That(comms.Reachable(Filter.Broadcast(), remote).Recipients, Does.Contain(player));
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A wreck is repaired along its graph, and enough damage wrecks a working one where it stands.</summary>
    [Test]
    public async Task BreaksAndRepairs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        EntityUid wreck = default;
        await server.WaitPost(() => wreck = entMan.SpawnEntity("WFMassTransceiverBroken", map.GridCoords));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var graph = protos.Index(Graph);
            Assert.That(graph.TryPath("broken", "working", out var path), Is.True);
            Assert.That(path!.Length, Is.EqualTo(1));
            Assert.That(graph.Nodes["broken"].Edges.Single().Steps, Has.Count.EqualTo(3), "Repair should take three easy steps.");
            Assert.That(entMan.GetComponent<TransformComponent>(wreck).Anchored, Is.True);
            Assert.That(server.System<ConstructionSystem>().ChangeNode(wreck, null, "working"), Is.True);
        });
        await server.WaitRunTicks(2);

        EntityUid working = default;
        await server.WaitAssertion(() =>
        {
            working = Single(entMan, "WFMassTransceiver");
            Assert.That(entMan.EntityExists(wreck), Is.False, "The wreck stayed after its repair.");
            Assert.That(entMan.GetComponent<TransformComponent>(working).Anchored, Is.True, "The repaired transceiver isn't anchored.");
            Assert.That(entMan.GetComponent<WFMassTransceiverComponent>(working).Broken, Is.False);

            var blunt = protos.Index(Blunt);
            server.System<DamageableSystem>().TryChangeDamage(working, new DamageSpecifier(blunt, 400), ignoreResistances: true);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(working), Is.False, "Damage didn't wreck the transceiver.");
            var broken = Single(entMan, "WFMassTransceiverBroken");
            Assert.That(entMan.GetComponent<WFMassTransceiverComponent>(broken).Broken, Is.True);
            Assert.That(entMan.GetComponent<TransformComponent>(broken).Anchored, Is.True, "The wreck isn't anchored.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid Single(IEntityManager entMan, string proto)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<WFMassTransceiverComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        Assert.That(found, Has.Count.EqualTo(1), $"Expected one {proto}.");
        return found[0];
    }

    /// <summary>A mob wearing a grey headset that listens on the test channel and is watched by the spy.</summary>
    private static (EntityUid Mob, EntityUid Headset) SpawnWithHeadset(RobustIntegrationTest.ServerIntegrationInstance server,
        EntityCoordinates coords)
    {
        var entMan = server.EntMan;
        var mob = entMan.SpawnEntity(Mob, coords);
        var headset = entMan.SpawnEntity(Headset, coords);
        Assert.That(server.System<InventorySystem>().TryEquip(mob, headset, "ears", silent: true, force: true), Is.True);
        entMan.EnsureComponent<ActiveRadioComponent>(headset).Channels.Add(Channel);
        entMan.EnsureComponent<WFPlanetCommsTestReceiverComponent>(headset);
        return (mob, headset);
    }
}
