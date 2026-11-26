#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using System.Text;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Components;
using Content.Server.Damage.Systems;
using Content.Server.NPC.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._Mono.CCVar;
using Robust.Shared.Configuration;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>
/// NPC crew: a deckhand draws its holstered sidearm for a hostile and holsters it once the hostile is gone, the
/// planner puts a pilot at the helm, a radio officer beside it, a deckhand inside the airlock and the rest on deck, a
/// pilot takes the helm, steers for his orders and lets go when he dies, a pilot's docking plan mates the docks and
/// falls back to an FTL dock when the server allows it, and a radio officer reports docking, attacks, boarders and the
/// captain going down, once each, and nothing once he is dead.
/// </summary>
[TestOf(typeof(WFCrewSystem))]
public sealed partial class WFCrewTest : InteractionTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntProto = "Blunt";

    private const string Hostile = "WFTestHostileMob";
    private const string TestHelm = "WFTestHelm";
    private const string Helm = "ComputerShuttle";
    private const string Dock = "AirlockShuttle";
    private const string TestDock = "WFTestDock";

    // Spawned ships play sounds, and the client's room echo asserts on audio whose parent changes mid-setup.
    [SetUp]
    public async Task DisableRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, MonoCVars.AreaEchoEnabled.DefaultValue));
    }

    // A monster-faction human: the stock crew faction is hostile to SimpleHostile, so the deckhand fights it. Test
    // prototypes load after the faction system cached its table, so the factions themselves have to be real ones.
    // The helm counts as powered without an APC.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseMobHuman
  id: WFTestHostileMob
  components:
  - type: NpcFactionMember
    factions:
    - SimpleHostile

- type: entity
  parent: ComputerShuttle
  id: WFTestHelm
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: AirlockShuttle
  id: WFTestDock
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>
    /// Off duty the sidearm sits in the belt. A hostile in view gets it drawn; the hostile going away gets it holstered.
    /// </summary>
    [Test]
    public async Task DeckhandDrawsForHostileAndHolstersAfter()
    {
        var inventory = Server.System<InventorySystem>();
        var hands = Server.System<SharedHandsSystem>();
        var crewSystem = Server.System<WFCrewSystem>();

        // A deck with gravity near the test player (NPCs sleep with no player within 32 tiles), the deckhand at its
        // centre, spawned the way the planner and the command do it: with a post, so guard duty keeps him near it.
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid crew = default;
        await Server.WaitPost(() =>
        {
            crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)), "test")!.Value;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFCrewComponent>(crew), "The crew component should be on the mob.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(crew).EntityName, Does.StartWith("Deckhand "),
                "The role title should go in front of the name.");
            Assert.That(Holstered(crew), "A deckhand should spawn with a sidearm in the belt.");
            Assert.That(hands.TryGetActiveItem(crew, out _), Is.False, "Hands should be empty off duty.");
        });

        // Two tiles from the post; the whole deck is inside his ten-tile vision.
        EntityUid hostile = default;
        await Server.WaitPost(() =>
        {
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
        });

        await WaitUntil(() => hands.TryGetActiveItem(crew, out var held) && SEntMan.HasComponent<GunComponent>(held.Value),
            300, () => $"A deckhand should draw its sidearm for a hostile in view. {Describe(crew)}");

        await Server.WaitPost(() => SEntMan.DeleteEntity(hostile));

        await WaitUntil(() => !hands.TryGetActiveItem(crew, out _) && Holstered(crew),
            600, () => $"A deckhand should holster once the hostile is gone. {Describe(crew)}");

        bool Holstered(EntityUid uid)
        {
            return inventory.TryGetSlotEntity(uid, "belt", out var belt) && SEntMan.HasComponent<GunComponent>(belt.Value);
        }
    }

    /// <summary>
    /// A 7x7 deck with a helm and one airlock plans a pilot and a radio officer beside the helm, a deckhand just
    /// inside the airlock and two more spread over the deck.
    /// </summary>
    [Test]
    public async Task PlannerPlansHelmRadioDockAndDeck()
    {
        await AddAtmosphere();
        var planner = Server.System<WFCrewPlannerSystem>();

        var grid = await CreateDeck(new Vector2(40f, 40f), 7, gravity: false);
        await Server.WaitPost(() =>
        {
            // Helm in the middle; airlock on the bottom edge facing out (its local -Y), so inside is the tile above it.
            SEntMan.SpawnAtPosition(Helm, new EntityCoordinates(grid, new Vector2(3.5f, 3.5f)));
            SEntMan.SpawnAtPosition(Dock, new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));
        });
        await RunTicks(5);

        List<WFCrewPost> plan = new();
        await Server.WaitPost(() =>
        {
            FillCrewTestAir(grid);
            plan = planner.Plan(grid, 2);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Count(p => p.Role == WFCrewRoles.Pilot), Is.EqualTo(1), "One pilot at the helm.");
            Assert.That(plan.Count(p => p.Role == WFCrewRoles.RadioOperator), Is.EqualTo(1), "One radio officer beside the helm.");
            Assert.That(plan.Count(p => p.Kind == WFCrewPostKind.Dock), Is.EqualTo(1), "One deckhand inside the airlock.");
            Assert.That(plan.Count(p => p.Kind == WFCrewPostKind.Deck), Is.EqualTo(2), "Two deckhands on deck.");
            Assert.That(plan.All(p => p.Coordinates.EntityId == grid), "Posts are grid-relative.");
        });

        var dock = plan.Single(p => p.Kind == WFCrewPostKind.Dock);
        Assert.That(dock.Coordinates.Position, Is.EqualTo(new Vector2(3.5f, 1.5f)), "The airlock post is the tile inside the airlock.");

        var helm = plan.Single(p => p.Kind == WFCrewPostKind.Helm);
        Assert.That((helm.Coordinates.Position - new Vector2(3.5f, 3.5f)).Length(), Is.EqualTo(1f).Within(0.01f),
            "The helm post is next to the console.");

        var deck = plan.Where(p => p.Kind == WFCrewPostKind.Deck).Select(p => p.Coordinates.Position).ToList();
        foreach (var post in deck)
        {
            foreach (var other in plan.Where(p => p.Coordinates.Position != post))
            {
                var d = post - other.Coordinates.Position;
                Assert.That(Math.Max(Math.Abs(d.X), Math.Abs(d.Y)), Is.GreaterThanOrEqualTo(3f).Within(0.01f),
                    "Deck posts keep their distance from every other post.");
            }
        }
    }

    /// <summary>
    /// A pilot beside a powered helm takes it and steers for his GoTo waypoint; killed, he lets go and the steering
    /// stops. The deck has no thrusters, so the grid itself never moves.
    /// </summary>
    [Test]
    public async Task PilotTakesHelmAndReleasesOnDeath()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var pilotSystem = Server.System<WFPilotDutySystem>();
        var mobState = Server.System<MobStateSystem>();

        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid helm = default;
        EntityUid pilot = default;
        var waypoint = new EntityCoordinates(MapData.MapUid, new Vector2(206f, 0f));
        await Server.WaitPost(() =>
        {
            // Steering needs a shuttle under the pilot.
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)), "test")!.Value;
            pilotSystem.GoTo(pilot, new List<EntityCoordinates> { waypoint });
        });

        await WaitUntil(() => SEntMan.TryGetComponent<PilotComponent>(pilot, out var attached) && attached.Console == helm
                               && SEntMan.TryGetComponent<ShipSteererComponent>(pilot, out var steerer)
                               && steerer.Mode == ShipSteeringMode.GoToRange
                               && SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm,
            300, () => $"A pilot should take the helm and steer for his waypoint. {DescribePilot(pilot, helm)}");

        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
            var steerer = SEntMan.GetComponent<ShipSteererComponent>(pilot);
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(pilot).Duty, Is.EqualTo(WFCrewDuties.Pilot), "The pilot role works the Pilot duty.");
            Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.GoTo), "Still flying the GoTo.");
            Assert.That(steerer.Coordinates, Is.EqualTo(waypoint), "Steering for the waypoint.");
            Assert.That(steerer.Range, Is.EqualTo(duty.ArrivalRange), "Arrival range is the GoTo range.");
        });

        await Server.WaitPost(() => mobState.ChangeMobState(pilot, MobState.Dead));

        await WaitUntil(() => !SEntMan.HasComponent<ShipSteererComponent>(pilot)
                               && !SEntMan.HasComponent<PilotComponent>(pilot)
                               && !SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm,
            300, () => $"A dead pilot should let go of the helm and stop steering. {DescribePilot(pilot, helm)}");
    }

    /// <summary>
    /// A nearby hostile leaves the pilot working; a hit makes him release the helm and draw.
    /// </summary>
    [Test]
    public async Task PilotRetaliatesAfterBodyDamage()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var hands = Server.System<SharedHandsSystem>();
        var damageable = Server.System<DamageableSystem>();
        var blunt = new DamageSpecifier(ProtoMan.Index<DamageTypePrototype>(BluntProto), 10);
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid pilot = default;
        EntityUid hostile = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)), "test")!.Value;
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
        });
        await WaitUntil(() => SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm,
            300, () => $"An unprovoked pilot should take the helm beside a hostile. {Describe(pilot)}");
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm, Is.True);
            Assert.That(hands.TryGetActiveItem(pilot, out _), Is.False);
        });

        await Server.WaitPost(() => damageable.TryChangeDamage(pilot, blunt, origin: hostile));
        await WaitUntil(() => !SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm
                               && !SEntMan.HasComponent<PilotComponent>(pilot)
                               && !SEntMan.HasComponent<ShipSteererComponent>(pilot)
                               && hands.TryGetActiveItem(pilot, out var held)
                               && SEntMan.HasComponent<GunComponent>(held.Value),
            300, () => $"An attacked pilot should leave the helm and draw. {Describe(pilot)}");

        await Server.WaitPost(() => SEntMan.DeleteEntity(hostile));
        await WaitUntil(() => SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm
                               && !hands.TryGetActiveItem(pilot, out _),
            600, () => $"The pilot should holster and return after the attacker is gone. {Describe(pilot)}");
    }

    /// <summary>
    /// The plan picks the facing pair, puts our grid where its dock meets the target's, and starts the approach
    /// <c>DockStandoff</c> out along the target dock's normal. Only the maths: nothing moves.
    /// </summary>
    [Test]
    public async Task DockPlanPutsStandoffOutsideTheTargetDock()
    {
        var pilotSystem = Server.System<WFPilotDutySystem>();
        var xforms = Server.System<SharedTransformSystem>();
        var (deck, target, ownDock, targetDock) = await CreateDockingPair(gravity: false);
        var standoff = new WFPilotDutyComponent().DockStandoff;

        await Server.WaitAssertion(() =>
        {
            Assert.That(pilotSystem.TryPlanDock(deck, target, standoff, out var plan), "A facing pair should give a plan.");
            Assert.That(plan.OwnDock, Is.EqualTo(ownDock), "Our dock is the one facing the target.");
            Assert.That(plan.TargetDock, Is.EqualTo(targetDock), "The target dock is the one facing us.");
            Assert.That(plan.Final.EntityId, Is.EqualTo(target), "The final pose is on the target grid.");
            Assert.That(plan.Standoff.EntityId, Is.EqualTo(target), "The standoff is on the target grid.");

            // Where our dock would be with the grid at the final pose.
            var finalPosition = xforms.ToMapCoordinates(plan.Final).Position;
            var finalRotation = xforms.GetWorldRotation(target) + plan.FinalAngle;
            var ownDockLocal = SEntMan.GetComponent<TransformComponent>(ownDock).LocalPosition;
            var ownDockAtFinal = finalPosition + finalRotation.RotateVec(ownDockLocal);
            var targetDockPosition = xforms.GetWorldPosition(targetDock);
            Assert.That((ownDockAtFinal - targetDockPosition).Length(), Is.LessThanOrEqualTo(1.2f),
                "At the final pose the docks are within docking range.");

            var normal = xforms.GetWorldRotation(targetDock).RotateVec(new Vector2(0f, -1f));
            var standoffOffset = xforms.ToMapCoordinates(plan.Standoff).Position - finalPosition;
            Assert.That((standoffOffset - normal * standoff).Length(), Is.LessThan(0.01f),
                $"The standoff is {standoff} m out from the final pose along the target dock's normal, got {standoffOffset}.");
            Assert.That(normal.Y, Is.EqualTo(1f).Within(0.01f), "The target dock faces up, toward our deck.");
        });
    }

    /// <summary>
    /// With no attempts allowed and the fallback on, a pilot at the helm docks by FTL and holds. The test decks have no
    /// thrusters, so flying the docking by hand can't be tested here.
    /// </summary>
    [Test]
    public async Task DockFallsBackToFtlDockWhenAllowed()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var pilotSystem = Server.System<WFPilotDutySystem>();
        var (deck, target, ownDock, targetDock) = await CreateDockingPair(gravity: true);

        EntityUid helm = default;
        EntityUid pilot = default;
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(NpcCrewCVars.DockFtlFallback, true);
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f, 3.5f)));
            pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "test")!.Value;
            SEntMan.GetComponent<WFPilotDutyComponent>(pilot).DockMaxAttempts = 0;
            pilotSystem.Dock(pilot, target);
        });

        try
        {
            await WaitUntil(() => SEntMan.GetComponent<DockingComponent>(ownDock).DockedWith == targetDock
                                   && SEntMan.GetComponent<DockingComponent>(targetDock).DockedWith == ownDock
                                   && SEntMan.GetComponent<WFPilotDutyComponent>(pilot).Orders == WFPilotOrder.Hold,
                600, () => $"The pilot should dock by FTL and hold. {DescribePilot(pilot, helm)}");

            await Server.WaitAssertion(() =>
            {
                var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
                Assert.That(duty.OrdersCompleted, "Docking completes the orders.");
                Assert.That(duty.AtHelm, "The pilot stays at the helm.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(NpcCrewCVars.DockFtlFallback, false));
        }
    }

    /// <summary>
    /// A radio officer whose ship docks with another says so once on Shortband, with the callsign (his grid's name)
    /// and the station's name.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task RadioOperatorReportsDocking(bool outbound)
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var docking = Server.System<DockingSystem>();
        var shuttles = Server.System<ShuttleSystem>();
        var meta = Server.System<MetaDataSystem>();
        var (deck, target, _, _) = await CreateDockingPair(gravity: true);

        EntityUid radio = default;
        await Server.WaitPost(() =>
        {
            meta.SetEntityName(deck, "WF Test Freighter");
            meta.SetEntityName(target, "WF Test Station");
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            radio = crewSystem.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(3.5f, 3.5f)), "test")!.Value;
            if (!outbound)
                SEntMan.EnsureComponent<WFCrewSecurityComponent>(radio).Docking = WFCrewSecurityResponse.Hostile;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFRadioOperatorComponent>(radio), "The radio officer role adds the radio duty.");
            Assert.That(SEntMan.HasComponent<TelecomExemptComponent>(radio), "The radio officer carries his own radio set.");
            Assert.That(Sent(radio), Is.Empty, "Nothing has happened yet.");
        });

        // Put the decks together the way an FTL dock does: no jump, just the move and the docking.
        await Server.WaitPost(() =>
        {
            if (outbound)
            {
                var pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot,
                    new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "test")!.Value;
                SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
                Server.System<WFPilotDutySystem>().Dock(pilot, target);
            }
            var config = docking.GetDockingConfig(deck, target);
            Assert.That(config, Is.Not.Null, "The decks' docks should fit.");
            shuttles.FTLDock((deck, SEntMan.GetComponent<TransformComponent>(deck)), config!);
        });

        await RunTicks(60);

        await Server.WaitAssertion(() =>
        {
            var sent = Sent(radio);
            Assert.That(sent, Has.Count.EqualTo(2), $"Outgoing traffic or an incoming security incident, each line once: {Describe(sent)}");
            if (!outbound)
            {
                Assert.That(sent.Any(line => line.Line == WFRadioLine.Docking), Is.False);
                Assert.That(sent.Any(line => line.Line == WFRadioLine.DockWarning), Is.True);
                Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(deck, "test"), Does.Contain(target));
                return;
            }
            Assert.That(sent[0].Line, Is.EqualTo(WFRadioLine.Approach));
            Assert.That(sent[1].Line, Is.EqualTo(WFRadioLine.Docking));
            Assert.That(sent[1].Channel.Id, Is.EqualTo("Traffic"));
            Assert.That(sent[1].Text, Does.Contain("WF Test Freighter"));
            Assert.That(sent[1].Text, Does.Contain("WF Test Station"));
        });
    }

    /// <summary>
    /// A captain hit by a hostile on another ship gets one mayday on Broadband naming that ship, however often he is
    /// hit; the hostile coming aboard gets one boarding call; the captain dying gets one line; and once the radio officer
    /// is dead, another crewman hit and killed gets nothing.
    /// </summary>
    [Test]
    public async Task RadioOperatorMaydayThenCaptainDownThenSilence()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var damageable = Server.System<DamageableSystem>();
        var mobState = Server.System<MobStateSystem>();
        var meta = Server.System<MetaDataSystem>();
        var xforms = Server.System<SharedTransformSystem>();
        var blunt = new DamageSpecifier(ProtoMan.Index<DamageTypePrototype>(BluntProto), 10);

        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        var raider = await CreateDeck(new Vector2(6f, 12f), 3, gravity: true);
        EntityUid radio = default;
        EntityUid captain = default;
        EntityUid pilot = default;
        EntityUid hostile = default;
        await Server.WaitPost(() =>
        {
            meta.SetEntityName(deck, "WF Test Freighter");
            meta.SetEntityName(raider, "WF Test Raider");
            radio = crewSystem.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)), "test")!.Value;
            captain = crewSystem.SpawnCrewman(WFCrewRoles.Captain, new EntityCoordinates(deck, new Vector2(4.5f, 6.5f)), "test")!.Value;
            pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(2.5f, 4.5f)), "test")!.Value;
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(raider, new Vector2(1.5f, 1.5f)));
            // The captain shoots back; the hostile has to live to come aboard.
            Server.System<GodmodeSystem>().EnableGodmode(hostile);
        });
        await RunTicks(70);

        await Server.WaitAssertion(() =>
            Assert.That(Sent(radio), Is.Empty, $"A hostile on his own ship is nobody's business yet: {Describe(Sent(radio))}"));

        await Server.WaitPost(() => damageable.TryChangeDamage(captain, blunt, origin: hostile));
        await WaitUntil(() => Count(radio, WFRadioLine.Mayday) == 1, 120,
            () => $"A crewman hit from another ship should get a mayday: {Describe(Sent(radio))}");

        await Server.WaitAssertion(() =>
        {
            var mayday = Sent(radio).Single(t => t.Line == WFRadioLine.Mayday && t.Channel.Id == "Common");
            Assert.That(mayday.Channel.Id, Is.EqualTo("Common"), "The mayday goes out on Broadband.");
            Assert.That(mayday.Text, Does.Contain("WF Test Freighter"), "The mayday carries the callsign.");
            Assert.That(mayday.Text, Does.Contain("WF Test Raider"), "The mayday names the hostile vessel.");
            Assert.That(Count(radio, WFRadioLine.Boarded), Is.Zero, "Nobody is aboard yet.");
        });

        await Server.WaitPost(() => damageable.TryChangeDamage(captain, blunt, origin: hostile));
        await RunTicks(70);
        await Server.WaitAssertion(() =>
            Assert.That(Count(radio, WFRadioLine.Mayday), Is.EqualTo(1), "One mayday per attack, however many hits."));

        await Server.WaitPost(() => xforms.SetCoordinates(hostile, new EntityCoordinates(deck, new Vector2(7.5f, 2.5f))));
        await WaitUntil(() => Count(radio, WFRadioLine.Boarded) == 1, 120,
            () => $"A hostile aboard should get the boarding call: {Describe(Sent(radio))}");

        await Server.WaitPost(() => mobState.ChangeMobState(captain, MobState.Dead));
        await WaitUntil(() => Count(radio, WFRadioLine.CaptainDown) == 1, 120,
            () => $"The captain dying should be reported: {Describe(Sent(radio))}");
        await RunTicks(70);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Count(radio, WFRadioLine.CaptainDown), Is.EqualTo(1), "One line for the captain.");
            Assert.That(Count(radio, WFRadioLine.Boarded), Is.EqualTo(1), "One boarding call per attack.");
            Assert.That(Count(radio, WFRadioLine.Mayday), Is.EqualTo(1), "Still one mayday.");
            Assert.That(Sent(radio).Count(t => t.Line == WFRadioLine.Boarded), Is.EqualTo(1),
                "A boarding alert goes out once, on the alert channel alone.");
            Assert.That(Sent(radio).Single(t => t.Line == WFRadioLine.Boarded).Channel.Id, Is.EqualTo("Common"));
        });

        var before = 0;
        await Server.WaitPost(() =>
        {
            mobState.ChangeMobState(radio, MobState.Dead);
            before = Sent(radio).Count;
            damageable.TryChangeDamage(pilot, blunt, origin: hostile);
            mobState.ChangeMobState(pilot, MobState.Dead);
        });
        await RunTicks(70);

        await Server.WaitAssertion(() =>
            Assert.That(Sent(radio), Has.Count.EqualTo(before), $"A dead radio officer says nothing: {Describe(Sent(radio))}"));
    }

    /// <summary>Shared alerts stay on one ship and group, skip officers, and restore awareness after sixty quiet seconds.</summary>
    [TestCase("")]
    [TestCase("crew")]
    public async Task CrewAlertScopeAndDecay(string group)
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var alerts = Server.System<WFCrewAlertSystem>();
        var factions = Server.System<NpcFactionSystem>();
        var npc = Server.System<NPCSystem>();
        var deck = await CreateDeck(new Vector2(6f, 0f), 15, gravity: true);
        var otherDeck = await CreateDeck(new Vector2(6f, 20f), 3, gravity: true);
        var observer = Server.System<CrewAlertObserver>();
        var started = observer.Started;
        var cleared = observer.Cleared;
        EntityUid source = default, receiver = default, officer = default, otherGroup = default;
        EntityUid otherShip = default, optedOut = default, hostile = default;
        await Server.WaitPost(() =>
        {
            source = SpawnSleeping(WFCrewRoles.Deckhand, deck, group);
            receiver = SpawnSleeping(WFCrewRoles.Deckhand, deck, group);
            officer = SpawnSleeping(WFCrewRoles.Pilot, deck, group);
            otherGroup = SpawnSleeping(WFCrewRoles.Deckhand, deck, "different");
            otherShip = SpawnSleeping(WFCrewRoles.Deckhand, otherDeck, group);
            optedOut = SpawnSleeping(WFCrewRoles.Deckhand, deck, group);
            SEntMan.GetComponent<WFCrewComponent>(optedOut).ShareAlerts = false;
            Board(receiver).SetValue("VisionRadius", 7f);
            Board(receiver).SetValue("AggroVisionRadius", 8f);
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 6.5f)));
            Server.System<SharedTransformSystem>().SetCoordinates(source, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            factions.AggroEntity(source, hostile);
            Board(source).SetValue("Target", hostile);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsAlerted(deck, group), Is.True);
            Assert.That(observer.Started, Is.EqualTo(started + 1));
            Assert.That(factions.GetHostiles(receiver), Does.Contain(hostile));
            Assert.That(Board(receiver).GetValue<float>("VisionRadius"), Is.GreaterThan(20f));
            Assert.That(Board(receiver).GetValue<float>("AggroVisionRadius"), Is.GreaterThan(20f));
            foreach (var excluded in new[] { officer, otherGroup, otherShip, optedOut })
            {
                Assert.That(factions.GetHostiles(excluded), Does.Not.Contain(hostile));
                Assert.That(Board(excluded).ContainsKey("AggroVisionRadius"), Is.False);
            }
        });

        await Server.WaitPost(() =>
        {
            Board(source).Remove<EntityUid>("Target");
            Board(officer).SetValue("Target", hostile);
        });
        await RunTicks(120);
        await Server.WaitAssertion(() => Assert.That(alerts.IsAlerted(deck, group), Is.True,
            "Losing a target does not immediately end the alert."));
        await RunTicks(3600);
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsAlerted(deck, group), Is.False);
            Assert.That(observer.Started, Is.EqualTo(started + 1), "Repeated polling must not duplicate the alert.");
            Assert.That(observer.Cleared, Is.EqualTo(cleared + 1));
            Assert.That(factions.GetHostiles(receiver), Does.Not.Contain(hostile));
            Assert.That(factions.GetHostiles(source), Does.Contain(hostile), "Pre-existing hostility survives alert cleanup.");
            Assert.That(Board(receiver).GetValue<float>("VisionRadius"), Is.EqualTo(7f));
            Assert.That(Board(receiver).GetValue<float>("AggroVisionRadius"), Is.EqualTo(8f));
            Assert.That(Board(source).ContainsKey("VisionRadius"), Is.False,
                "Default vision must remain a default, not become a permanent local override.");
        });

        EntityUid SpawnSleeping(string role, EntityUid grid, string name)
        {
            var uid = crewSystem.SpawnCrewman(role, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)), name)!.Value;
            var htn = SEntMan.GetComponent<HTNComponent>(uid);
            htn.SleepPlayerCheckRangeOverride = 0f;
            SEntMan.GetComponent<WFCrewComponent>(uid).KeepActive = false;
            npc.SleepNPC(uid, htn);
            return uid;
        }
        Content.Server.NPC.NPCBlackboard Board(EntityUid uid) => SEntMan.GetComponent<HTNComponent>(uid).Blackboard;
    }

    /// <summary>A remote deckhand joins a spotted fight, while leaving the crew restores its previous awareness.</summary>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task CrewAlertRecruitsDistantDeckhandAndCleansUpDeparture(bool changeGroup, bool retaliated)
    {
        // Headless clients can assert when attaching the room-echo auxiliary effect to gunshot audio.
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var crewSystem = Server.System<WFCrewSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            var factions = Server.System<NpcFactionSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var deck = await CreateDeck(new Vector2(2f, 0f), 24, gravity: true);
            EntityUid source = default, receiver = default, hostile = default;
            await Server.WaitPost(() =>
            {
                source = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)), "crew")!.Value;
                receiver = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(18.5f, 2.5f)), "crew")!.Value;
                hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(5.5f, 2.5f)));
            });
            // While the spotter fights the hostile in sight nobody else sets out; once he is down the hunt is open.
            await WaitUntil(() => alerts.IsAlerted(deck, "crew"), 300, () => $"The spotter should raise the alert. {Describe(source)}");
            await Server.WaitPost(() => Server.System<Content.Shared.Mobs.Systems.MobStateSystem>()
                .ChangeMobState(source, Content.Shared.Mobs.MobState.Dead));
            await WaitUntil(() => alerts.IsAlerted(deck, "crew") && factions.GetHostiles(receiver).Contains(hostile)
                                   && hands.TryGetActiveItem(receiver, out var held)
                                   && SEntMan.HasComponent<GunComponent>(held.Value),
                300, () => $"The remote crewman should join the alerted fight. {Describe(receiver)}");

            await Server.WaitPost(() =>
            {
                if (retaliated)
                    Server.System<NPCRetaliationSystem>().TryRetaliate(
                        (receiver, SEntMan.GetComponent<NPCRetaliationComponent>(receiver)), hostile);
                if (changeGroup)
                    SEntMan.GetComponent<WFCrewComponent>(receiver).Group = "different";
                else
                    crewSystem.SetEngagement(receiver, WFCrewEngagement.WhenAttacked);
                // The departing crewman must not seed another alert from its old target.
                var htn = SEntMan.GetComponent<HTNComponent>(receiver);
                SEntMan.GetComponent<WFCrewComponent>(receiver).KeepActive = false;
                htn.SleepPlayerCheckRangeOverride = 0f;
                Server.System<NPCSystem>().SleepNPC(receiver, htn);
                htn.Blackboard.Remove<EntityUid>("Target");
            });
            await RunTicks(90);
            await Server.WaitAssertion(() =>
            {
                Assert.That(factions.GetHostiles(receiver).Contains(hostile), Is.EqualTo(retaliated),
                    "Cleanup preserves personal retaliation but removes hostility owned by the alert.");
                var board = SEntMan.GetComponent<HTNComponent>(receiver).Blackboard;
                Assert.That(board.ContainsKey("VisionRadius"), Is.False);
                Assert.That(board.ContainsKey("AggroVisionRadius"), Is.False);
                foreach (var mob in new[] { source, receiver, hostile })
                {
                    if (SEntMan.TryGetComponent<HTNComponent>(mob, out var htn))
                    {
                        htn.Enabled = false;
                        Server.System<NPCSystem>().SleepNPC(mob, htn);
                    }
                    SEntMan.RemoveComponent<NPCRangedCombatComponent>(mob);
                    SEntMan.RemoveComponent<NPCMeleeCombatComponent>(mob);
                }
            });
            await RunTicks(90);
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }

    /// <summary>Counts shared alert transitions across server ticks.</summary>
    public sealed class CrewAlertObserver : EntitySystem
    {
        public int Started;
        public int Cleared;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
            SubscribeLocalEvent<WFCrewAlertClearedEvent>(OnClear);
        }

        private void OnAlert(ref WFCrewAlertEvent args) => Started++;
        private void OnClear(ref WFCrewAlertClearedEvent args) => Cleared++;
    }

    private List<WFRadioTransmission> Sent(EntityUid radio)
    {
        return SEntMan.GetComponent<WFRadioOperatorComponent>(radio).Sent;
    }

    private int Count(EntityUid radio, WFRadioLine line)
    {
        return Sent(radio).Where(t => t.Line == line).Select(t => t.Text).Distinct().Count();
    }

    private static string Describe(List<WFRadioTransmission> sent)
    {
        return sent.Count == 0 ? "nothing sent" : string.Join(" | ", sent.Select(t => $"{t.Line}@{t.Channel.Id}: {t.Text}"));
    }

    /// <summary>
    /// Two 7x7 decks with a dock each, facing each other: the target near the test player with its dock on the top
    /// edge, ours above it with its dock on the bottom edge. Far enough apart that our grid's final pose doesn't overlap
    /// where it is now, which upstream's docking config rejects.
    /// </summary>
    private async Task<(EntityUid Deck, EntityUid Target, EntityUid OwnDock, EntityUid TargetDock)> CreateDockingPair(bool gravity)
    {
        var target = await CreateDeck(new Vector2(6f, 0f), 7, gravity: false);
        var deck = await CreateDeck(new Vector2(6f, 16f), 7, gravity);
        EntityUid ownDock = default;
        EntityUid targetDock = default;
        await Server.WaitPost(() =>
        {
            // A dock faces its local -Y; the target's is turned to face up.
            ownDock = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(3.5f, 0.5f)));
            targetDock = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(target, new Vector2(3.5f, 6.5f)));
            Server.System<SharedTransformSystem>().SetLocalRotation(targetDock, Angle.FromDegrees(180));
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(ownDock).Anchored, "Our dock is anchored.");
            Assert.That(SEntMan.GetComponent<TransformComponent>(targetDock).Anchored, "The target dock is anchored.");
        });

        return (deck, target, ownDock, targetDock);
    }

    /// <summary>A square of plating on the test map, optionally with gravity.</summary>
    private async Task<EntityUid> CreateDeck(Vector2 origin, int size, bool gravity)
    {
        var map = Server.System<SharedMapSystem>();
        var mapManager = Server.System<SharedMapSystem>();
        var tileDefs = Server.ResolveDependency<ITileDefinitionManager>();

        EntityUid grid = default;
        await Server.WaitPost(() =>
        {
            var created = mapManager.CreateGridEntity(MapData.MapId);
            grid = created.Owner;
            var plating = new Tile(tileDefs["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < size; x++)
            {
                for (var y = 0; y < size; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            map.SetTiles(grid, created, tiles);
            Server.System<SharedTransformSystem>().SetCoordinates(grid, new EntityCoordinates(MapData.MapUid, origin));
            if (gravity)
                Server.System<GravitySystem>().EnableGravity(grid);
        });
        await RunTicks(5);
        return grid;
    }

    private async Task WaitUntil(Func<bool> condition, int maxTicks, Func<string> message)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 10)
        {
            await RunTicks(10);
            await Server.WaitPost(() => met = condition());
        }

        if (met)
            return;

        var text = string.Empty;
        await Server.WaitPost(() => text = message());
        Assert.Fail(text);
    }

    /// <summary>What the crewman's AI is doing, for failure messages.</summary>
    private string Describe(EntityUid crew)
    {
        var npc = Server.System<NPCSystem>();
        var factions = Server.System<NpcFactionSystem>();
        var hands = Server.System<SharedHandsSystem>();
        var htn = SEntMan.GetComponent<HTNComponent>(crew);
        var sb = new StringBuilder();
        sb.Append($"awake={npc.IsAwake(crew, htn)} ");
        sb.Append($"plan={(htn.Plan == null ? "none" : htn.Plan.CurrentOperator.GetType().Name)} ");
        sb.Append($"target={(htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, SEntMan) ? target.ToString() : "none")} ");
        sb.Append($"hostiles={factions.GetNearbyHostiles(crew, 10f).Count()} ");
        sb.Append($"held={(hands.TryGetActiveItem(crew, out var held) ? SEntMan.ToPrettyString(held.Value).ToString() : "nothing")} ");
        var weapon = SEntMan.GetComponent<WFCrewWeaponComponent>(crew);
        sb.Append($"drawn={weapon.Drawn} slot={weapon.HolsterSlot ?? "-"} ");

        var member = SEntMan.GetComponent<NpcFactionMemberComponent>(crew);
        sb.Append($"factions=[{string.Join(",", member.Factions)}] hostileTo=[{string.Join(",", member.HostileFactions)}] ");
        var lookup = Server.System<EntityLookupSystem>();
        var xforms = Server.System<SharedTransformSystem>();
        var here = xforms.GetMapCoordinates(crew);
        foreach (var other in lookup.GetEntitiesInRange<NpcFactionMemberComponent>(here, 20f))
        {
            if (other.Owner == crew)
                continue;

            var there = xforms.GetMapCoordinates(other.Owner);
            sb.Append($"near:{SEntMan.ToPrettyString(other.Owner)}@{(there.Position - here.Position).Length():0.0}[{string.Join(",", other.Comp.Factions)}] ");
        }

        return sb.ToString();
    }

    /// <summary>What a pilot and his helm are doing, for failure messages.</summary>
    private string DescribePilot(EntityUid pilot, EntityUid helm)
    {
        var pilots = Server.System<WFPilotDutySystem>();
        var sb = new StringBuilder(Describe(pilot));
        var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
        sb.Append($"mobState={SEntMan.GetComponent<MobStateComponent>(pilot).CurrentState} ");
        sb.Append($"orders={duty.Orders} atHelm={duty.AtHelm} waypoint={duty.WaypointIndex}/{duty.Waypoints.Count} ");
        sb.Append(SEntMan.TryGetComponent<PilotComponent>(pilot, out var attached)
            ? $"console={attached.Console?.ToString() ?? "null"} "
            : "console=none ");
        sb.Append(SEntMan.TryGetComponent<ShipSteererComponent>(pilot, out var steerer)
            ? $"steering={steerer.Mode}/{steerer.Status} to {steerer.Coordinates} range={steerer.Range} "
            : "steering=none ");
        sb.Append($"helm={helm} exists={SEntMan.EntityExists(helm)} ");
        if (SEntMan.EntityExists(helm))
        {
            sb.Append($"anchored={SEntMan.GetComponent<TransformComponent>(helm).Anchored} ");
            sb.Append($"powered={Server.System<PowerReceiverSystem>().IsPowered(helm)} ");
        }

        sb.Append($"found={(pilots.TryFindHelm(pilot, out var found) ? found.ToString() : "none")} ");
        sb.Append($"blackboardHelm={(SEntMan.GetComponent<HTNComponent>(pilot).Blackboard.TryGetValue<EntityUid>(WFPilotDutySystem.HelmKey, out var picked, SEntMan) ? picked.ToString() : "none")} ");
        return sb.ToString();
    }
}
