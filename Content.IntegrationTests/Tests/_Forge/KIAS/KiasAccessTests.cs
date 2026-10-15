#pragma warning disable RA0002
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.Access;
using Content.Server.Power.Components;
using Content.Server.Wires;
using Content.Shared._Forge.KIAS;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.PDA;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasAccessTests
{
    [Test]
    public async Task DeedCompanyPoiClaimAndNativeWireAccessMatrix()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var access = em.System<KiasAccessSystem>();
        EntityUid actor = default;
        EntityUid core = default;
        await pair.Server.WaitAssertion(() =>
        {
            var position = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            actor = em.SpawnEntity("MobHuman", position);
            var mind = em.System<SharedMindSystem>().CreateMind(session.UserId);
            em.System<SharedMindSystem>().TransferTo(mind, actor);
            core = em.SpawnEntity("KiasCore", position);
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            em.System<KiasSystem>().Rebuild(map.Grid);
            var id = em.SpawnEntity("PassengerIDCard", position);
            var hands = em.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(actor, id), Is.True);
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.OpenUnclaimed));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor, security: true), Is.False);
            Assert.That(access.Claim(map.Grid, actor, id), Is.True);
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.Claimed));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor, security: true), Is.True);
            var receiver = em.SpawnEntity("KiasWirelessTransceiver", position);
            var transmitter = em.SpawnEntity("KiasWirelessTransceiver", position);
            var stranger = em.SpawnEntity("MobCat", position);
            em.RemoveComponent<ApcPowerReceiverComponent>(receiver);
            em.RemoveComponent<ApcPowerReceiverComponent>(transmitter);
            em.SpawnEntity("KiasDataCable", position);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<KiasSystem>().IsOnline(receiver), Is.True);
            Assert.That(em.System<KiasSystem>().IsOnline(transmitter), Is.True);
            Assert.That(access.SetWirelessTrust(receiver, transmitter, actor), Is.True);
            Assert.That(em.GetComponent<KiasWirelessComponent>(receiver).TrustedTransmitters, Does.Contain(transmitter));
            Assert.That(access.SetWirelessTrust(receiver, transmitter, stranger), Is.False);
            Assert.That(access.SetWirelessTrust(receiver, transmitter, actor), Is.True);
            Assert.That(em.GetComponent<KiasWirelessComponent>(receiver).TrustedTransmitters, Is.Empty);
            em.DeleteEntity(id);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            id = em.SpawnEntity("PassengerIDCard", position);
            Assert.That(hands.TryPickup(actor, id), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);

            em.EnsureComponent<ShuttleDeedComponent>(map.Grid).ShuttleUid = map.Grid.Owner;
            em.EnsureComponent<ShipOwnershipComponent>(map.Grid).OwnerUserId = session.UserId;
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.ShipDeed));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            Assert.That(access.Claim(map.Grid, actor, id), Is.False);
            em.EnsureComponent<ShuttleDeedComponent>(id).ShuttleUid = map.Grid.Owner;
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            var pda = em.SpawnEntity("PassengerPDA", position);
            var pdaComp = em.GetComponent<PdaComponent>(pda);
            if (pdaComp.ContainedId is { } previous)
                em.DeleteEntity(previous);
            var container = em.System<SharedContainerSystem>().GetContainer(pda, PdaComponent.PdaIdSlotId);
            Assert.That(em.System<SharedContainerSystem>().Insert(id, container), Is.True);
            Assert.That(hands.TryPickup(actor, pda), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            em.GetComponent<ShuttleDeedComponent>(id).ShuttleUid = core;
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            var guests = em.EnsureComponent<ShipGuestAccessComponent>(map.Grid);
            guests.GuestIdCards.Add(id);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor, security: true), Is.False);
            guests.GuestIdCards.Clear();
            em.RemoveComponent<ShuttleDeedComponent>(map.Grid);

            em.EnsureComponent<CompanyComponent>(map.Grid).CompanyName = "TSF";
            em.GetComponent<IdCardComponent>(id).CompanyName = "Rogue";
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.Company));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            em.GetComponent<IdCardComponent>(id).CompanyName = "TSF";
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            em.RemoveComponent<CompanyComponent>(map.Grid);

            var poiReader = em.EnsureComponent<AccessReaderComponent>(map.Grid);
            poiReader.AccessLists.Add(new() { "Captain" });
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.Poi));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            Assert.That(em.System<Content.Shared.Access.Systems.SharedAccessSystem>().TrySetTags(id, new Robust.Shared.Prototypes.ProtoId<Content.Shared.Access.AccessLevelPrototype>[] { "Captain" }), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            poiReader.AccessLists.Clear();
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.Claimed));
            em.RemoveComponent<AccessReaderComponent>(map.Grid);

            var maps = em.System<SharedMapSystem>();
            var purchased = maps.CreateGridEntity(map.MapId);
            maps.SetTile(purchased, purchased.Comp, Vector2i.Zero, map.Tile.Tile);
            em.EnsureComponent<ShuttleDeedComponent>(purchased).ShuttleUid = purchased.Owner;
            var purchasedCore = em.SpawnEntity("KiasCore", new EntityCoordinates(purchased, 0.5f, 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(purchasedCore);
            em.System<KiasSystem>().Rebuild(purchased);
            Assert.That(access.ResolvePolicy(purchased), Is.EqualTo(KiasAccessPolicy.ShipDeed));
            Assert.That(access.CanConfigure(purchased, actor), Is.False);
            em.GetComponent<ShuttleDeedComponent>(id).ShuttleUid = purchased.Owner;
            Assert.That(access.CanConfigure(purchased, actor), Is.True);
            em.DeleteEntity(purchasedCore);
            purchasedCore = em.SpawnEntity("KiasCore", new EntityCoordinates(purchased, 0.5f, 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(purchasedCore);
            em.System<KiasSystem>().Rebuild(purchased);
            Assert.That(access.CanConfigure(purchased, actor), Is.True);
            em.System<SharedTransformSystem>().SetLocalPosition(purchased, new System.Numerics.Vector2(100, 20));
            Assert.That(access.CanConfigure(purchased, actor), Is.True);

            em.EnsureComponent<KiasPoiAccessComponent>(map.Grid);
            Assert.That(access.ResolvePolicy(map.Grid), Is.EqualTo(KiasAccessPolicy.Poi));
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            var wire = em.GetComponent<WiresComponent>(core).WiresList.Single(w => w.Action is AccessWireAction);
            var action = (AccessWireAction) wire.Action!;
            var reader = em.GetComponent<AccessReaderComponent>(core);
            wire.IsCut = true;
            Assert.That(action.Cut(actor, wire, reader), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
            Assert.That(access.Claim(map.Grid, actor, id), Is.False);
            wire.IsCut = false;
            Assert.That(action.Mend(actor, wire, reader), Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
            action.Pulse(actor, wire, reader);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.True);
        });
        await pair.RunTicksSync(31 * 60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<AccessReaderComponent>(core).Enabled, Is.True);
            Assert.That(access.CanConfigure(map.Grid, actor), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
