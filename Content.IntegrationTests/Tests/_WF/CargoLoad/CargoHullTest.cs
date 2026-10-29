#nullable enable
using System.Linq;
using Content.Server._WF.CargoLoad;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.CargoLoad;
using Content.Shared._WF.Encounters;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.CargoLoad;

/// <summary>
/// Real hulls against the cargo tuning: every NPC freighter filled to its cargo count with its run's heaviest crate keeps
/// thrust above the floor. Hull masses are written to the test output for tuning.
/// </summary>
[TestFixture]
public sealed class CargoHullTest
{
    private static readonly string[] Haulers = { "Piva", "Plutomkii", "Hermes", "Twilight" };

    [Test]
    public async Task LoadedFreightersKeepThrustAboveTheFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var cargo = entMan.System<WFCargoLoadSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var profile = protoMan.Index(WFCargoLoadSystem.DefaultProfile);

            float HullMass(ResPath path)
            {
                mapSys.CreateMap(out var mapId);
                Assert.That(loader.TryLoadGrid(mapId, path, out var grid), $"Couldn't load {path}.");
                var mass = entMan.GetComponent<PhysicsComponent>(grid!.Value).FixturesMass;
                mapSys.DeleteMap(mapId);
                return mass;
            }

            float CrateWeight(EntProtoId crate)
            {
                return protoMan.Index(crate).TryGetComponent<WFCargoMassComponent>(out _, factory) ? cargo.GoodWeight(crate) : 0f;
            }

            Assert.Multiple(() =>
            {
                foreach (var encounter in protoMan.EnumeratePrototypes<WFEncounterPrototype>())
                {
                    if (encounter.Manifests.Count == 0)
                        continue;

                    var heaviest = encounter.Manifests.SelectMany(manifest => protoMan.Index(manifest).Crates)
                        .Select(CrateWeight)
                        .DefaultIfEmpty(0f)
                        .Max();

                    foreach (var ship in encounter.Ships.Where(ship => ship.Cargo > 0))
                    {
                        foreach (var vessel in ship.Vessels)
                        {
                            var hull = HullMass(protoMan.Index(vessel).ShuttlePath);
                            var load = ship.Cargo * heaviest;
                            var factor = hull / (hull + load);
                            TestContext.Out.WriteLine($"{encounter.ID} {vessel}: hull {hull:F0}, worst load {load:F0}t, thrust {factor:P0}");
                            Assert.That(factor, Is.GreaterThan(profile.ThrustFloor),
                                $"{vessel} in {encounter.ID} fully loaded with {heaviest}t crates sits on the thrust floor.");
                        }
                    }
                }

                foreach (var hauler in Haulers)
                {
                    if (protoMan.TryIndex<VesselPrototype>(hauler, out var vessel))
                        TestContext.Out.WriteLine($"Hauler {hauler}: hull {HullMass(vessel.ShuttlePath):F0}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
