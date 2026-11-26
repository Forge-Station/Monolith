#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Armour on the worlds: APCs drive only Verda's and Merak's roads; every world still parks civilian vehicles.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetVehicleSpawnTest
{
    private static readonly string[] WithApcs = { "WFPoisAsclepiu", "WFPoisMerak" };

    [Test]
    public async Task OnlyVerdaAndMerakGetApcs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var tables = server.System<EntityTableSystem>();
            var random = new System.Random(1977);
            // The tables the shipped worlds roll.
            var live = proto.EnumeratePrototypes<WFPlanetSurfacePrototype>()
                .Where(surface => surface.Pois != null)
                .Select(surface => proto.Index(surface.Pois!.Value))
                .Where(table => !table.ID.Contains("Test"))
                .DistinctBy(table => table.ID)
                .ToList();
            Assert.That(live.Select(table => table.ID), Is.SupersetOf(WithApcs));
            foreach (var table in live)
            {
                // Everything the table can put on its ground that drives: parked vehicles and patrol crews.
                var vehicles = new HashSet<string>();
                if (table.Vehicles is { } parked)
                {
                    for (var i = 0; i < 500; i++)
                        vehicles.UnionWith(tables.GetSpawns(parked.Spawns, random).Select(id => id.Id));
                    Assert.That(vehicles.Any(id => !IsApc(proto, id)), Is.True, $"{table.ID} parks no civilian vehicles.");
                }

                foreach (var patrol in table.RoadPatrols)
                    vehicles.Add(proto.Index<WFVehicleCrewPrototype>(patrol.Crew).Vehicle.Id);

                var apcs = vehicles.Where(id => IsApc(proto, id)).ToList();
                if (WithApcs.Contains(table.ID))
                    Assert.That(apcs, Is.Not.Empty, $"{table.ID} lost its APC patrol.");
                else
                    Assert.That(apcs, Is.Empty, $"{table.ID} puts APCs on its world.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Whether a vehicle is an APC or built on one.</summary>
    private static bool IsApc(IPrototypeManager proto, string id)
    {
        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.TryPop(out var next))
        {
            if (next.Contains("APC", System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (proto.TryIndex<EntityPrototype>(next, out var entity) && entity.Parents is { } parents)
            {
                foreach (var parent in parents)
                    pending.Push(parent);
            }
        }

        return false;
    }
}
