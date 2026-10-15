using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using Robust.Shared.Localization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasRelayTests
{
    [Test]
    public async Task ToolAndVerbReportSelectedChannelAndRespectAccess()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasDataCable"); var core = Spawn("KiasCore"); var relay = Spawn("KiasRelay");
            var actor = Spawn("MobHuman"); var tool = Spawn("Screwdriver");
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<KiasRelaySystem>().SetChannel(relay, CableType.HighVoltage), Is.True);
            var channels = Enum.GetValues<CableType>();
            foreach (var expected in channels.Skip(1).Concat(channels.Take(1)))
            {
                var interaction = new InteractUsingEvent(actor, tool, relay, em.GetComponent<TransformComponent>(relay).Coordinates);
                em.EventBus.RaiseLocalEvent(relay, interaction);
                Assert.That(interaction.Handled, Is.True);
                Assert.That(em.GetComponent<KiasRelayComponent>(relay).Channel, Is.EqualTo(expected));
                em.System<KiasSystem>().Rebuild(map.Grid);
                var examined = new ExaminedEvent(new FormattedMessage(), relay, actor, true, false);
                em.EventBus.RaiseLocalEvent(relay, examined);
                Assert.That(examined.GetTotalMessage().ToString(), Does.Contain(Loc.GetString($"kias-relay-channel-{expected.ToString().ToLowerInvariant()}")));
            }
            var verbs = new GetVerbsEvent<AlternativeVerb>(actor, relay, null, null, true, true, true, new());
            em.EventBus.RaiseLocalEvent(relay, verbs);
            verbs.Verbs.Single(verb => verb.Text == Loc.GetString("kias-relay-data")).Act!();
            Assert.That(em.GetComponent<KiasRelayComponent>(relay).Channel, Is.EqualTo(CableType.Data));
            em.System<KiasSystem>().Rebuild(map.Grid);
            em.EnsureComponent<KiasPoiAccessComponent>(map.Grid);
            Assert.That(em.System<KiasRelaySystem>().SetChannel(relay, CableType.HighVoltage, actor), Is.False);
            Assert.That(em.GetComponent<KiasRelayComponent>(relay).Channel, Is.EqualTo(CableType.Data));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SelectedChannelReallySplitsAndReconnectsNetworks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var kias = em.System<KiasSystem>();
        var relays = em.System<KiasRelaySystem>();
        EntityUid relay = default;
        EntityUid device = default;
        var cables = new EntityUid[3, 9];
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var types = new[] { "CableHV", "CableMV", "CableApcExtension" };
            for (var x = 0; x < 9; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                for (var channel = 0; channel < 3; channel++)
                    cables[channel, x] = em.SpawnEntity(types[channel], new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasCore", 0);
            relay = Spawn("KiasRelay", 4);
            device = Spawn("KiasCrewServer", 8);
            kias.Rebuild(map.Grid);
        });
        object? Group(int channel, int x) => em.GetComponent<NodeContainerComponent>(cables[channel, x]).Nodes.Values.Single().NodeGroup;
        await pair.RunTicksSync(3);
        foreach (var selected in Enum.GetValues<CableType>())
        {
            await pair.Server.WaitAssertion(() =>
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    Assert.That(Group(channel, 0), Is.Not.Null);
                    Assert.That(Group(channel, 0), Is.SameAs(Group(channel, 8)));
                }
                Assert.That(kias.IsOnline(device), Is.True);
                var revision = em.GetComponent<KiasGridComponent>(map.Grid).Revision;
                Assert.That(relays.SetChannel(relay, em.GetComponent<KiasRelayComponent>(relay).Channel), Is.True);
                Assert.That(kias.IsOnline(device), Is.True, "Selecting the current channel must not dirty the grid.");
                Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Revision, Is.EqualTo(revision));
                Assert.That(relays.SetChannel(relay, selected), Is.True);
                Assert.That(relays.SetClosed(relay, false), Is.True);
            });
            await pair.RunTicksSync(3);
            await pair.Server.WaitAssertion(() =>
            {
                kias.Rebuild(map.Grid);
                Assert.That(kias.IsOnline(device), Is.EqualTo(selected != CableType.Data));
                for (var channel = 0; channel < 3; channel++)
                {
                    if (channel == (int) selected)
                        Assert.That(Group(channel, 0), Is.Not.SameAs(Group(channel, 8)));
                    else
                        Assert.That(Group(channel, 0), Is.SameAs(Group(channel, 8)));
                }
                Assert.That(relays.SetClosed(relay, true), Is.True);
            });
            await pair.RunTicksSync(3);
        }
        await pair.CleanReturnAsync();
    }
}
