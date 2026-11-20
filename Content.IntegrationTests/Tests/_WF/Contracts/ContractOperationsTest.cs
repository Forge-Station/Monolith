#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Contracts;
using Content.Shared._WF.Contracts;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>Mail runs, sweeps, clearing, repairs, downloads and twin surveys end to end; chain links stay off the board.</summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractOperationsTest
{
    [Test]
    public async Task NewObjectivesRun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contracts = entMan.System<WFContractSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        EntityUid central = default, sevastopol = default, hokkaido = default, colony = default, lab = default, board = default, mob = default;
        WFContractHolder holder = default;

        await server.WaitAssertion(() =>
        {
            central = ContractFlowTest.Station(server, map, Vector2.Zero, "Frontier");
            sevastopol = ContractFlowTest.Station(server, map, new Vector2(4000f, 0f), "Sevastopol");
            hokkaido = ContractFlowTest.Station(server, map, new Vector2(0f, 3000f), "Hokkaido");
            colony = ContractFlowTest.Station(server, map, new Vector2(-600f, 0f), "TSFMCIndustry");
            lab = ContractFlowTest.Station(server, map, new Vector2(0f, -4000f), "AnomalousLab");
            board = entMan.SpawnEntity("WFContractBoard", new EntityCoordinates(ContractFlowTest.Largest(server, central), 4.5f, 4.5f));
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Operator");
            minds.TransferTo(mind.Owner, mob);
            holder = contracts.HolderOf(mob)!.Value;
        });

        // Mail run: one parcel per stop, each counted only at its own station.
        WFContract mail = default!;
        await server.WaitAssertion(() =>
        {
            mail = Take(contracts, mob, board, "WFContractCaelMailSweep");
            Assert.That(mail.Stops, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(mail.Stops, Does.Not.Contain(central), "A mail stop was the board's own station.");
            Assert.That(mail.ParcelStops.Values, Is.EquivalentTo(mail.Stops));
            // A parcel at another stop's station must not count.
            var (first, stop) = mail.ParcelStops.First();
            var wrong = mail.Stops.First(s => s != stop);
            xforms.SetCoordinates(first, new EntityCoordinates(ContractFlowTest.Largest(server, wrong), 1.5f, 1.5f));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(mail.Delivered, Is.Zero, "A parcel counted at another stop's station.");
            foreach (var (parcel, stop) in mail.ParcelStops)
            {
                xforms.SetCoordinates(parcel, new EntityCoordinates(ContractFlowTest.Largest(server, stop), 1.5f, 1.5f));
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "Delivering every parcel did not complete the run."));

        // Sweep: each point is logged as the holder flies past it.
        WFContract sweep = default!;
        await server.WaitAssertion(() =>
        {
            sweep = Take(contracts, mob, board, "WFContractCaelLaneSurvey");
            Assert.That(sweep.Points, Has.Count.EqualTo(sweep.Count));
        });
        for (var i = 0; i < sweep.Points.Count; i++)
        {
            var point = sweep.Points[i];
            await server.WaitAssertion(() => xforms.SetMapCoordinates(mob, new MapCoordinates(point.Position + new Vector2(20f, 0f), point.MapId)));
            await pair.RunTicksSync(90);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "Passing every point did not complete the sweep.");
            xforms.SetCoordinates(mob, new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
        });

        // Clearing: done once every nest is gone.
        WFContract clear = default!;
        await server.WaitAssertion(() =>
        {
            clear = Take(contracts, mob, board, "WFContractMarshalsCarpNests");
            Assert.That(clear.Targets, Has.Count.GreaterThan(0));
            Assert.That(clear.Targets.All(t => entMan.GetComponent<TransformComponent>(t).GridUid == clear.WreckGrid), Is.True);
            foreach (var nest in clear.Targets)
            {
                entMan.DeleteEntity(nest);
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "Destroying every nest did not complete the clearing."));

        // Repair: welded whole is not enough without a cell, and the cell alone is not enough while damaged.
        WFContract repair = default!;
        EntityUid beacon = default;
        await server.WaitAssertion(() =>
        {
            repair = Take(contracts, mob, board, "WFContractCaelBeacon");
            beacon = repair.Targets.Single();
            Assert.That(entMan.GetComponent<DamageableComponent>(beacon).TotalDamage > 0, Is.True, "The beacon started whole.");
            var cell = entMan.SpawnEntity("PowerCellSmall", entMan.GetComponent<TransformComponent>(beacon).Coordinates);
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(beacon, "cell_slot", cell, null), Is.True);
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder).Single().Id, Is.EqualTo(repair.Id), "A damaged beacon with a cell counted as repaired.");
            entMan.System<DamageableSystem>().SetAllDamage(beacon, entMan.GetComponent<DamageableComponent>(beacon), 0);
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "A whole, powered beacon did not complete the repair."));

        // Download: no progress while no one stands by, progress once someone does.
        WFContract download = default!;
        await server.WaitAssertion(() =>
        {
            download = Take(contracts, mob, board, "WFContractSevArchivePull");
            Assert.That(download.Targets, Has.Count.EqualTo(1));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(download.Started, Is.False, "The download ran with no one beside the terminal.");
            var terminal = entMan.GetComponent<TransformComponent>(download.Targets[0]).Coordinates;
            xforms.SetCoordinates(mob, terminal.Offset(new Vector2(1f, 0f)));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(download.Started && download.Progress > System.TimeSpan.Zero, Is.True, "Standing by the terminal did not run the download.");
            Assert.That(contracts.TryAbandon(holder, download.Id), Is.True);
            xforms.SetCoordinates(mob, new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
        });

        // Freight crates each take their own tile; parcels would share one.
        await server.WaitAssertion(() =>
        {
            var freight = Take(contracts, mob, board, "WFContractMMCMachineParts", crewCheck: false);
            var tiles = freight.Items.Select(i => entMan.GetComponent<TransformComponent>(i).Coordinates.Position).Select(p => (System.MathF.Floor(p.X), System.MathF.Floor(p.Y))).Distinct().Count();
            Assert.That(tiles, Is.EqualTo(freight.Items.Count), "Freight crates were stacked on one tile.");
            Assert.That(contracts.TryAbandon(holder, freight.Id), Is.True);
        });

        // Twin survey: two derelicts, anomalies on both. Chain links never post on their own.
        await server.WaitAssertion(() =>
        {
            var twin = Take(contracts, mob, board, "WFContractLabTwinSurvey", crewCheck: false);
            Assert.That(twin.ExtraWrecks, Has.Count.EqualTo(1));
            var grids = twin.Anomalies.Select(a => entMan.GetComponent<TransformComponent>(a).GridUid).Distinct().Count();
            Assert.That(grids, Is.EqualTo(2), "The twin survey's anomalies were not spread over both derelicts.");
            Assert.That(contracts.TryAbandon(holder, twin.Id), Is.True);
            Assert.That(contracts.All.Any(c => c.Prototype == "WFContractMarshalsWarlordChain"), Is.False,
                "A chain link was posted without its first link.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Posts an offer, waives its deposit (the test character has no bank) and takes it.</summary>
    private static WFContract Take(WFContractSystem contracts, EntityUid mob, EntityUid board, string id, bool crewCheck = true)
    {
        var offer = contracts.PostOffer(id)!;
        Assert.That(offer, Is.Not.Null, $"{id} could not be posted.");
        offer.Deposit = 0;
        if (!crewCheck)
        {
            Assert.That(contracts.ForceAccept(mob, offer.Id, board), Is.True, $"{id} did not start.");
            return offer;
        }

        Assert.That(contracts.TryAccept(mob, offer.Id, board), Is.Null, $"{id} was not accepted.");
        return offer;
    }
}
