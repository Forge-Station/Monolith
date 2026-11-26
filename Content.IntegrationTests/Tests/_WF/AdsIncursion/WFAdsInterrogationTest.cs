#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>
/// Interrogating zones, as every ADS ship has: a player ship in the zone is never fired on or warned until it says it is a
/// threat when hailed, or fires on the ship. The ADS crews go bare, armed from slots that need no clothing.
/// </summary>
public sealed class WFAdsInterrogationTest : WFAdsTestBase
{
    private const string Interrogator = "WFTestAdsInterrogator";

    /// <summary>Where the player's ship lies, well clear of the sector's cells in use.</summary>
    private static readonly Vector2 At = new(9000f, 9000f);

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfEncounter
  id: WFTestAdsInterrogator
  name: wf-ads-name-sentinel
  start: Manual
  lifetime: Persistent
  icon: Shield
  ships:
  - key: lead
    vessel: WFDredger
    side: ads
    distress: false
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot, WFCrewGunner ]
    evades: false
    warnRange: 1200
    attackRange: 700
    zoneLines: wf-ads-zone
    zoneTargets: Interrogate
    objectives:
    - kind: Hold
";

    /// <summary>An interrogating ship 400 m from the test player's ship, which has a powered helm the player has open.</summary>
    private sealed class Scene
    {
        public EntityUid Encounter;
        public EntityUid PlayerShip;
        public EntityUid Helm;
        public WFEncounterShipState Ship = default!;
    }

    /// <summary>Puts the player at the helm of a small ship and starts an interrogating ship inside both its zones. With <paramref name="fire"/> the player's ship has fired on it first.</summary>
    private async Task<Scene> Setup(bool fire = false)
    {
        await StartAds();
        var scene = new Scene();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(MapData.MapId);
            scene.PlayerShip = grid.Owner;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            maps.SetTiles(grid, tiles);
            Server.System<MetaDataSystem>().SetEntityName(scene.PlayerShip, "Test Hauler");
            Place(scene.PlayerShip, At);
            scene.Helm = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(scene.PlayerShip, new Vector2(1.5f, 0.5f)));
            Power(scene.Helm);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(scene.PlayerShip, new Vector2(1.5f, 1.5f)));
        });

        await RunTicks(5);
        await Server.WaitPost(() => ui.OpenUi(scene.Helm, ShuttleConsoleUiKey.Key, SPlayer));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(scene.Helm, ShuttleConsoleUiKey.Key, SPlayer), Is.True, "The player needs the helm open to hold a call.");
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Interrogator);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(proto, new MapCoordinates(At + new Vector2(400f, 0f), MapData.MapId),
                out scene.Encounter), Is.True);
            scene.Ship = SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Ships["lead"];
            Assert.That(scene.Ship.ZoneTargets, Is.EqualTo(WFEncounterZoneTargets.Interrogate));
            var helms = SEntMan.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (helms.MoveNext(out var helm, out _, out var xform))
            {
                if (xform.GridUid == scene.Ship.Grid)
                    Power(helm);
            }

            if (fire)
                Assert.That(Server.System<WFCrewAlertSystem>().ReportIncomingFire(scene.Ship.Grid, scene.Ship.Group, scene.PlayerShip), Is.True);
        });

        return scene;
    }

    /// <summary>Powers a console at once; the power update is throttled.</summary>
    private void Power(EntityUid console)
    {
        var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(console);
        power.NeedsPower = false;
        power.Powered = true;
    }

    /// <summary>Waits for the interrogation to ring at the player's helm, checking the ship is left alone meanwhile, and answers it.</summary>
    private async Task AnswerHail(Scene scene)
    {
        var hailing = Server.System<HailingSystem>();
        await WaitUntilServer(() => hailing.GetState(scene.Helm) == HailState.Ringing, 600);
        await Server.WaitAssertion(() =>
        {
            AssertLeftAlone(scene, "While it is asked");
            Assert.That(scene.Ship.Strangers, Does.Contain(scene.PlayerShip));
            Assert.That(Server.System<WFEncounterInterrogationSystem>().IsAsking(scene.Ship, scene.PlayerShip), Is.True);
            hailing.Answer(scene.Helm, SPlayer, true);
            var hail = SEntMan.GetComponent<HailConsoleComponent>(scene.Helm);
            Assert.That(hail.Options.Select(option => option.Id),
                Is.EqualTo(new[] { WFEncounterInterrogationSystem.Yes, WFEncounterInterrogationSystem.No }));
            Assert.That(hail.Options.Select(option => option.Text),
                Is.EqualTo(new[] { Loc.GetString("wf-ads-zone-hail-yes"), Loc.GetString("wf-ads-zone-hail-no") }));
            Assert.That(hail.Log.Where(line => !line.Own).Select(line => line.Text),
                Does.Contain(Loc.GetString("wf-ads-zone-hail-question")), "The question is put down the channel.");
        });

        // Waiting on an answer, the ship still holds its fire.
        await RunTicks(120);
        await Server.WaitAssertion(() => AssertLeftAlone(scene, "While the answer is awaited"));
    }

    /// <summary>The ship is neither fired on nor warned, by this ship or its side.</summary>
    private void AssertLeftAlone(Scene scene, string when)
    {
        var encounter = SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter);
        Assert.That(scene.Ship.Engaged, Is.Empty, $"{when} it is not engaged.");
        Assert.That(scene.Ship.Warned, Is.Empty, $"{when} it gets no warning lines.");
        Assert.That(encounter.Hailed, Is.Empty, $"{when} nobody on the side warns it.");
        Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(scene.Ship.Grid, scene.Ship.Group), Is.Empty,
            $"{when} the crew has no hostile ship to fire on.");
    }

    private async Task Cleanup(Scene scene)
    {
        await Server.WaitPost(() =>
        {
            Disembark();
            Server.System<WFEncounterSystem>().End(scene.Encounter);
            SEntMan.DeleteEntity(scene.PlayerShip);
        });
        await RunTicks(10);
    }

    /// <summary>A stranger inside both zones is not fired on or warned; it is hailed, and answering YES declares it a threat the zone then engages.</summary>
    [Test]
    public async Task StrangerIsAskedAndYesMakesItATarget()
    {
        var scene = await Setup();
        await AnswerHail(scene);
        await Server.WaitAssertion(() =>
        {
            var hailing = Server.System<HailingSystem>();
            Assert.That(hailing.Choose(scene.Helm, SPlayer, WFEncounterInterrogationSystem.Yes), Is.True);
            Assert.That(scene.Ship.Declared, Does.Contain(scene.PlayerShip), "YES declares the ship a threat.");
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(scene.Helm).Log.Select(line => line.Text),
                Does.Contain(Loc.GetString("wf-ads-zone-hail-declared")));
        });

        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Ship.Engaged, Does.Contain(scene.PlayerShip), "A declared threat in the attack zone is engaged.");
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(scene.Ship.Grid, scene.Ship.Group), Does.Contain(scene.PlayerShip));
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "The channel is closed once the answer is in.");
            Assert.That(scene.Ship.Strangers, Is.Empty, "A declared threat is not asked again.");
        });

        await Cleanup(scene);
    }

    /// <summary>Answering NO leaves the ship alone and puts it on the cleared cooldown; masking its IFF after changes nothing.</summary>
    [Test]
    public async Task NoLeavesItAloneOnCooldown()
    {
        var scene = await Setup();
        await AnswerHail(scene);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<HailingSystem>().Choose(scene.Helm, SPlayer, WFEncounterInterrogationSystem.No), Is.True);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter);
            Assert.That(scene.Ship.Declared, Is.Empty);
            Assert.That(encounter.Interrogated.TryGetValue(scene.PlayerShip, out var until), Is.True, "NO puts the ship on cooldown.");
            Assert.That(until, Is.GreaterThanOrEqualTo(STiming.CurTime + WFEncounterInterrogationSystem.ClearedCooldown - TimeSpan.FromSeconds(1)));
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(scene.Helm).Log.Select(line => line.Text),
                Does.Contain(Loc.GetString("wf-ads-zone-hail-cleared")));
        });

        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            AssertLeftAlone(scene, "Cleared");
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "It is not asked again while on cooldown.");
            Server.System<ShuttleSystem>().AddIFFFlag(scene.PlayerShip, IFFFlags.HideLabel);
        });

        await RunTicks(150);
        await Server.WaitAssertion(() => AssertLeftAlone(scene, "Cleared and running dark"));
        await Cleanup(scene);
    }

    /// <summary>A ship that fires on an interrogating ship is engaged at once, with no question put.</summary>
    [Test]
    public async Task AttackerIsEngagedWithoutAHail()
    {
        var scene = await Setup(fire: true);
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Ship.Engaged, Does.Contain(scene.PlayerShip), "An attacker in the attack zone is engaged.");
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(scene.Ship.Grid, scene.Ship.Group), Does.Contain(scene.PlayerShip));
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "An attacker is not asked.");
            Assert.That(Server.System<WFEncounterInterrogationSystem>().IsAsking(scene.Ship, scene.PlayerShip), Is.False);
            Assert.That(scene.Ship.Strangers, Is.Empty);
        });

        await Cleanup(scene);
    }

    /// <summary>The ADS zones interrogate, its loadouts carry no clothing, and its bare automatons draw their guns from their slots and are never suited, even at a post with no air.</summary>
    [Test]
    public async Task AdsCrewsAreBareRobotsThatStillFight()
    {
        await StartAds();
        var loadouts = new (ProtoId<WFCrewRolePrototype> Role, string Gear)[]
        {
            (WFCrewRoles.Deckhand, "WFCrewAdsUnit"),
            (WFCrewRoles.Pilot, "WFCrewAdsOfficer"),
            (WFCrewRoles.Marine, "WFCrewAdsSentry"),
        };
        EntityUid deck = default, control = default;
        var mobs = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "WFAdsNode", "WFAdsSentinel", "WFAdsSweep", "WFAdsInterdictor", "WFAdsDeployment" })
            {
                Assert.That(prototypes.Index<WFEncounterPrototype>(id).Ships.All(ship => ship.ZoneTargets == WFEncounterZoneTargets.Interrogate),
                    Is.True, $"{id} interrogates");
            }

            foreach (var (_, gear) in loadouts)
            {
                Assert.That(prototypes.Index<StartingGearPrototype>(gear).Equipment.Keys, Is.SubsetOf(new[] { "belt", "back" }),
                    $"{gear} carries weapons only");
            }

            // A bare grid has no air: every post on it is in vacuum.
            deck = SpawnGrid(At + new Vector2(0f, 3000f), "Test Vacuum Deck");
            var crew = Server.System<WFCrewSystem>();
            foreach (var (role, gear) in loadouts)
            {
                var mob = crew.SpawnCrewman(role, new EntityCoordinates(deck, new Vector2(0.5f)), "ads-bare", gear, "WFMobCrewBodyIpc");
                Assert.That(mob, Is.Not.Null, gear);
                mobs.Add(mob!.Value);
            }

            control = crew.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(0.5f)), "ads-control")!.Value;
            foreach (var mob in mobs.Append(control))
            {
                SEntMan.GetComponent<HTNComponent>(mob).Enabled = false;
            }

            var inventory = Server.System<InventorySystem>();
            var eva = Server.System<WFCrewEvaSystem>();
            Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(control), Is.True, "Control: a human at the post is suited.");
            foreach (var mob in mobs)
            {
                Assert.That(eva.IsImmune(mob), Is.True, "The automaton needs no air.");
                Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(mob), Is.False, "An automaton is never suited.");
                foreach (var slot in new[] { "jumpsuit", "shoes", "gloves", "outerClothing", "head", "mask" })
                {
                    Assert.That(inventory.TryGetSlotEntity(mob, slot, out _), Is.False, $"{SEntMan.ToPrettyString(mob)} wears nothing in {slot}");
                }

                Assert.That(inventory.TryGetSlotEntity(mob, "belt", out _), Is.True, "The sidearm is on the belt.");
                Assert.That(Server.System<WFCrewWeaponSystem>().TryDraw(mob), Is.True, "The crewman can draw a gun.");
            }
        });

        await Server.WaitPost(() =>
        {
            foreach (var mob in mobs.Append(control))
            {
                SEntMan.DeleteEntity(mob);
            }

            SEntMan.DeleteEntity(deck);
        });
    }
}
