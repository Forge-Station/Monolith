#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    [TestPrototypes]
    private const string GunneryPrototype = @"
- type: entity
  parent: ComputerGunneryConsole
  id: WFTestGunnery
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>A gunner walks to the console, targets only an attacker, and stops on console loss.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task GunnerOperatesConsoleUntilLost(bool directSpawn)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var attacker = await CreateDeck(new Vector2(25, 0), 3, gravity: true);
        EntityUid gunner = default, console = default;
        await Server.WaitPost(() =>
        {
            console = SEntMan.SpawnAtPosition("WFTestGunnery", new EntityCoordinates(deck, new Vector2(3.5f)));
            var position = new EntityCoordinates(deck, new Vector2(1.5f, 3.5f));
            gunner = directSpawn
                ? SEntMan.SpawnAtPosition("WFMobCrewGunner", position)
                : Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Gunner, position, "guns")!.Value;
        });
        await WaitUntil(() => SEntMan.GetComponent<WFGunnerDutyComponent>(gunner).AtConsole,
            600, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.False);
            var ev = new WFCrewHullHitEvent(deck, attacker);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref ev, true);
        });
        await WaitUntil(() => SEntMan.HasComponent<ShipTargetingComponent>(gunner), 120, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShipTargetingComponent>(gunner).Target.EntityId, Is.EqualTo(attacker));
            var transforms = Server.System<SharedTransformSystem>();
            var towardConsole = (transforms.GetWorldPosition(console) - transforms.GetWorldPosition(gunner)).ToWorldAngle();
            Assert.That(Math.Abs(ShipSteeringSystem.ShortestAngleDistance(transforms.GetWorldRotation(gunner), towardConsole).Degrees), Is.LessThan(1));
            SEntMan.DeleteEntity(console);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.False));
    }

    /// <summary>Encounter and admin spawning reject invalid plans atomically and use the selected real loadout.</summary>
    [Test]
    public async Task CrewSetupValidatesAndSpawnsMission()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var setup = Server.System<WFCrewSetupSystem>();
            var mission = new WFCrewMission { Group = "setup", Callsign = "Test crew", Order = WFPilotOrder.GoTo,
                Destination = new Vector2(100, 100) };
            var posts = new List<WFCrewSetupPost>
            {
                new() { Role = "WFCrewDeckhand", Loadout = "WFCrewMarineGear", Position = new Vector2(1.5f) },
                new() { Role = "WFCrewPilot", Position = new Vector2(float.NaN, 2.5f) },
            };
            Assert.That(setup.TrySpawn(deck, posts, mission, out var rejected), Is.False);
            Assert.That(rejected, Is.Empty);
            posts[1].Position = new Vector2(2.5f);
            Assert.That(setup.TrySpawn(deck, posts, mission, out var spawned), Is.True);
            Assert.That(spawned, Has.Count.EqualTo(2));
            foreach (var uid in spawned)
                SEntMan.GetComponent<HTNComponent>(uid).Enabled = false;
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(spawned[0], "back", out var rifle), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(rifle!.Value).EntityPrototype!.ID, Is.EqualTo("WeaponRifleLecter"));
            var pilot = SEntMan.GetComponent<WFPilotDutyComponent>(spawned[1]);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.GoTo));
            Assert.That(pilot.Waypoints[0].Position, Is.EqualTo(mission.Destination));
            mission.Order = WFPilotOrder.Hold;
            Assert.That(setup.TryApplyMission(deck, mission), Is.True);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.Hold));
            mission.Order = WFPilotOrder.Follow;
            mission.Target = null;
            Assert.That(setup.TryApplyMission(deck, mission), Is.False);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.Hold));
        });
    }

    /// <summary>
    /// Planning keeps off vacuum, oxygen-free, hot and contaminated tiles while the hull has better; a roster that
    /// posts a crewman on one gets him a void kit.
    /// </summary>
    [Test]
    public async Task CrewSetupKitsOutPostsInUnsafeAir()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var planner = Server.System<WFCrewPlannerSystem>();
            var setup = Server.System<WFCrewSetupSystem>();
            var tile = new Vector2i(1, 1);
            var air = Server.System<AtmosphereSystem>().GetTileMixture(deck, MapData.MapUid, tile)!.Clone();
            var posts = new List<WFCrewSetupPost> { new() { Position = new Vector2(1.5f) } };
            var mission = new WFCrewMission();
            Assert.That(setup.Validate(deck, posts, mission), Is.True);
            var nitrogen = air.Clone();
            nitrogen.SetMoles(Gas.Nitrogen, nitrogen.TotalMoles);
            nitrogen.SetMoles(Gas.Oxygen, 0);
            var hot = air.Clone();
            hot.Temperature = 500;
            var contaminated = air.Clone();
            contaminated.SetMoles(Gas.Plasma, 10);
            var tiles = SEntMan.GetComponent<GridAtmosphereComponent>(deck).Tiles;
#pragma warning disable RA0002 // Set per-tile atmosphere fixtures without simulating diffusion.
            foreach (var unsafeAir in new[] { GasMixture.SpaceGas, nitrogen, hot, contaminated })
            {
                tiles[tile].Air = unsafeAir;
                Assert.That(planner.IsSafePost(deck, tile), Is.False);
                Assert.That(planner.Plan(deck, 32).Exists(post => post.Coordinates.Position == new Vector2(1.5f)), Is.False);
                Assert.That(setup.TrySpawn(deck, posts, mission, out var spawned), Is.True);
                Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(spawned.Single()), Is.True);
                Assert.That(Server.System<WFCrewEvaSystem>().Prepare(spawned.Single()), Is.True,
                    "A crewman posted in bad air should be suited and on internals.");
            }
            tiles[tile].Air = air;
#pragma warning restore RA0002
            Assert.That(setup.Validate(deck, posts, mission), Is.True);
        });
    }

    /// <summary>Pressurizes the procedural floor grid before synchronous spawn planning assertions.</summary>
    private void FillCrewTestAir(EntityUid grid)
    {
        var air = new GasMixture(2500) { Temperature = Atmospherics.T20C };
        air.SetMoles(Gas.Oxygen, 21.824779f);
        air.SetMoles(Gas.Nitrogen, 82.10312f);
        foreach (var tile in SEntMan.GetComponent<GridAtmosphereComponent>(grid).Tiles.Values)
        {
#pragma warning disable RA0002 // Procedural test grids start with vacuum; install the room-air fixture.
            tile.Air = air.Clone();
#pragma warning restore RA0002
        }
    }
}
