#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships;
using Content.Shared._WF.CargoLoad;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.CargoLoad;

/// <summary>
/// Load effects and readouts: the FTL spool penalty, the helm readout and its band warnings, scaled thrust in space,
/// the encounter lift exemption, and every trade good's weight class.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCargoLoadSystem))]
public sealed class CargoReadoutTest
{
    private const string Ballast = "WFTestCargoBallast";
    private const string Helm = "ComputerShuttle";

    // A weightless marker whose tonnage the test sets through PerUnit.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestCargoBallast
  name: cargo ballast
  components:
  - type: WFCargoMass
    perUnit: 1
";

    /// <summary>Sets a ballast's weight to <paramref name="tonnes"/>.</summary>
    private static void SetTonnes(IEntityManager entMan, WFCargoLoadPrototype profile, EntityUid ballast, float tonnes)
    {
        entMan.GetComponent<WFCargoMassComponent>(ballast).PerUnit = tonnes / profile.MassScale;
    }

    /// <summary>Tonnes that leave a grid of this hull with <paramref name="factor"/> of its thrust.</summary>
    private static float LoadFor(IEntityManager entMan, EntityUid grid, float factor)
    {
        var hull = entMan.GetComponent<PhysicsComponent>(grid).FixturesMass;
        return hull * (1f - factor) / factor;
    }

    /// <summary>Cargo adds SpoolPerTonne seconds a tonne up to SpoolCap, and nothing to a drive mass doesn't affect.</summary>
    [Test]
    public async Task SpoolPenaltyScalesWithLoadUpToCap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid ship = default, ballast = default;
        const float tonnes = 20f;

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
            SetTonnes(entMan, profile, ballast, tonnes);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(tonnes).Within(0.01f));
            Assert.That(profile.SpoolPerTonne * tonnes, Is.LessThan(profile.SpoolCap), "The test load should sit under the cap.");
            Assert.That(cargo.SpoolPenalty(ship, new FTLDriveComponent()),
                Is.EqualTo(System.Math.Min(profile.SpoolCap, profile.SpoolPerTonne * tonnes)).Within(0.001f));
            Assert.That(cargo.SpoolPenalty(ship, new FTLDriveComponent { MassAffectedDrive = false }), Is.EqualTo(0f),
                "A drive mass doesn't affect gets no cargo spool.");

            SetTonnes(entMan, profile, ballast, 100000f);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.SpoolPenalty(ship, new FTLDriveComponent()), Is.EqualTo(profile.SpoolCap).Within(0.001f),
                "A huge load is capped.");
            Assert.That(cargo.GetSpaceFactor(ship), Is.EqualTo(profile.ThrustFloor).Within(0.001f),
                "A huge load leaves the thrust floor.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A helm on a loaded ship gets the readout; each band warns once as the factor drops below it, re-arms only past
    /// band + hysteresis, and the readout goes when the load does.
    /// </summary>
    [Test]
    public async Task HelmReadoutTracksBands()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        Assert.That(profile.Bands, Has.Count.GreaterThanOrEqualTo(2), "The test walks the first two bands.");
        var high = profile.Bands[0];
        var low = profile.Bands[1];
        var between = (high + low) / 2f;
        var below = System.Math.Max(profile.ThrustFloor + 0.02f, low - 0.05f);
        var withinHysteresis = high + profile.Hysteresis / 2f;
        var clear = high + profile.Hysteresis + 0.05f;
        Assert.That(below, Is.LessThan(low));
        Assert.That(clear, Is.LessThan(1f));

        EntityUid ship = default, helm = default, ballast = default;

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            helm = entMan.SpawnEntity(Helm, new EntityCoordinates(ship, 5.5f, 5.5f));
        });

        await pair.RunTicksSync(90);

        // Moves the ship to a factor, sweeps, and checks the bands told.
        async Task Step(float factor, int[] told, string because)
        {
            await server.WaitPost(() => SetTonnes(entMan, profile, ballast, LoadFor(entMan, ship, factor)));
            await pair.RunTicksSync(90);
            await server.WaitAssertion(() =>
            {
                Assert.That(cargo.GetSpaceFactor(ship), Is.EqualTo(factor).Within(0.005f));
                Assert.That(entMan.TryGetComponent<WFCargoHelmComponent>(helm, out var readout), Is.True, "A loaded ship's helm has the readout.");
                Assert.That(readout!.Told, Is.EquivalentTo(told), because);
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFCargoHelmComponent>(helm), Is.False, "An empty ship's helm has no readout.");
            ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
        });

        await Step(between, new[] { 0 }, "Below the first band, it warns.");
        await Step(below, new[] { 0, 1 }, "Below the second band, it warns again.");
        await Step(withinHysteresis, new[] { 0 }, "Past the second band's hysteresis it re-arms; inside the first's it holds.");
        await Step(clear, System.Array.Empty<int>(), "Past the first band's hysteresis it re-arms.");
        await Step(between, new[] { 0 }, "A re-armed band warns again.");

        await server.WaitPost(() => entMan.DeleteEntity(ballast));
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFCargoHelmComponent>(helm), Is.False, "The readout goes when the load does.");
            Assert.That(cargo.GetSpaceFactor(ship), Is.EqualTo(1f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Unloading every crate gives full thrust back at the next sweep and drops the load, gauge and helm components; a
    /// load under the gauge's threshold keeps no gauge.
    /// </summary>
    [Test]
    public async Task UnloadingRestoresThrustAndClearsReadouts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid ship = default, helm = default, ballast = default;
        var crates = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            helm = entMan.SpawnEntity(Helm, new EntityCoordinates(ship, 5.5f, 5.5f));
            for (var i = 0; i < 3; i++)
            {
                crates.Add(entMan.SpawnEntity("TradeGoodMetals", new EntityCoordinates(ship, 1.5f + i * 2, 2.5f)));
            }
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetSpaceFactor(ship), Is.LessThan(1f), "Three Bulk crates slow the ship.");
            Assert.That(entMan.HasComponent<WFCargoLoadComponent>(ship), Is.True);
            Assert.That(entMan.HasComponent<WFCargoReadoutComponent>(ship), Is.True, "A loaded ship shows the gauge.");
            Assert.That(entMan.HasComponent<WFCargoHelmComponent>(helm), Is.True);

            foreach (var crate in crates)
            {
                entMan.DeleteEntity(crate);
            }
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetSpaceFactor(ship), Is.EqualTo(1f), "Unloaded, the ship gets full thrust back.");
            Assert.That(entMan.HasComponent<WFCargoLoadComponent>(ship), Is.False);
            Assert.That(entMan.HasComponent<WFCargoReadoutComponent>(ship), Is.False, "An unloaded ship shows no gauge.");
            Assert.That(entMan.HasComponent<WFCargoHelmComponent>(helm), Is.False);

            ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
            SetTonnes(entMan, profile, ballast, WFCargoReadoutComponent.ShownLoad / 2f);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(WFCargoReadoutComponent.ShownLoad / 2f).Within(0.01f));
            Assert.That(entMan.HasComponent<WFCargoReadoutComponent>(ship), Is.False, "A load under the gauge's threshold isn't networked.");

            SetTonnes(entMan, profile, ballast, WFCargoReadoutComponent.ShownLoad * 2f);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFCargoReadoutComponent>(ship), Is.True, "A load past the threshold shows the gauge."));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A pilot gets one popup a sweep however many bands the load crosses at once, one when it falls back past them,
    /// and the standing warning once on taking a loaded helm.
    /// </summary>
    [Test]
    public async Task BandWarningsPopOncePerSweep()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var consoles = entMan.System<ShuttleConsoleSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var popups = pair.Client.System<WFCargoPopupProbeSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        Assert.That(profile.Bands, Has.Count.GreaterThanOrEqualTo(2), "The test crosses two bands at once.");
        var below = System.Math.Max(profile.ThrustFloor + 0.02f, profile.Bands[^1] - 0.05f);
        var clear = profile.Bands[0] + profile.Hysteresis + 0.05f;
        Assert.That(below, Is.LessThan(profile.Bands[^1]));
        Assert.That(clear, Is.LessThan(1f));

        EntityUid ship = default, helm = default, pilot = default, ballast = default;

        // Seats the pilot at the helm.
        void Board()
        {
            entMan.EnsureComponent<PilotComponent>(pilot);
            consoles.AddPilot(helm, pilot, entMan.GetComponent<ShuttleConsoleComponent>(helm));
        }

        // The band and restored popups the helm sends for the ship's current load.
        (string Band, string Restored) Expected()
        {
            var load = entMan.GetComponent<WFCargoLoadComponent>(ship);
            var percent = (int) System.MathF.Round((1f - load.SpaceFactor) * 100f);
            return (loc.GetString("wf-cargo-load-band", ("tonnes", (int) System.MathF.Round(load.SetLoad)), ("percent", percent)),
                loc.GetString("wf-cargo-load-restored", ("percent", percent)));
        }

        // Records the popups across a step, then counts the band and restored ones.
        async Task Step(System.Action act, int bands, int restores, string because)
        {
            await pair.Client.WaitPost(popups.Start);
            await server.WaitPost(act);
            await pair.RunTicksSync(90);

            var expected = (Band: string.Empty, Restored: string.Empty);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<ShuttleConsoleComponent>(helm).SubscribedPilots, Does.Contain(pilot), "The pilot stays at the helm.");
                expected = Expected();
            });

            await pair.Client.WaitAssertion(() =>
            {
                var received = string.Join(" | ", popups.Received);
                Assert.That(popups.Received.Count(message => message == expected.Band), Is.EqualTo(bands), $"{because} Received: {received}");
                Assert.That(popups.Received.Count(message => message == expected.Restored), Is.EqualTo(restores), $"{because} Received: {received}");
            });

            await pair.Client.WaitPost(popups.Stop);
        }

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            helm = entMan.SpawnEntity(Helm, new EntityCoordinates(ship, 5.5f, 5.5f));
            pilot = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ship, 4.5f, 5.5f));
            entMan.System<SharedGodmodeSystem>().EnableGodmode(pilot);
            server.PlayerMan.SetAttachedEntity(pair.Player!, pilot);
            Board();
        });

        await pair.RunTicksSync(90);

        await Step(() =>
        {
            ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
            SetTonnes(entMan, profile, ballast, LoadFor(entMan, ship, below));
        }, 1, 0, "Crossing every band in one sweep warns once.");

        await server.WaitPost(() => consoles.RemovePilot(pilot));
        await pair.RunTicksSync(90);
        await Step(Board, 1, 0, "Taking a loaded helm gives the standing warning once.");

        await Step(() => SetTonnes(entMan, profile, ballast, LoadFor(entMan, ship, clear)), 0, 1,
            "Climbing back past every band in one sweep tells the pilot once.");

        await server.WaitPost(() =>
        {
            consoles.RemovePilot(pilot);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entMan.DeleteEntity(pilot);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Out of atmosphere, the mover's thrust in any direction is scaled by the cargo factor.</summary>
    [Test]
    public async Task ThrustScalesWithCargoInSpace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var mover = entMan.System<MoverController>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid ship = default;
        float empty = 0f;

        // Thrust north from rest with a fixed thruster rating, so only the scaling varies.
        float Thrust()
        {
            var shuttle = entMan.GetComponent<ShuttleComponent>(ship);
            for (var i = 0; i < shuttle.LinearThrust.Length; i++)
            {
                shuttle.LinearThrust[i] = 1000f;
            }

            physics.SetLinearVelocity(ship, Vector2.Zero);
            return mover.GetDirectionThrust(new Vector2(0f, 1f), shuttle,
                entMan.GetComponent<PhysicsComponent>(ship), entMan.GetComponent<TransformComponent>(ship)).Y;
        }

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            empty = Thrust();
            Assert.That(empty, Is.GreaterThan(0f));

            var ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
            SetTonnes(entMan, profile, ballast, LoadFor(entMan, ship, 0.7f));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var factor = cargo.GetSpaceFactor(ship);
            Assert.That(factor, Is.EqualTo(0.7f).Within(0.005f));
            Assert.That(Thrust(), Is.EqualTo(empty * factor).Within(empty * 0.001f), "Loaded thrust is the empty thrust times the cargo factor.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An encounter hull's cargo doesn't count against planet lift until it stops being an encounter hull.</summary>
    [Test]
    public async Task EncounterHullsDontCountCargoAgainstLift()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var levels = entMan.System<CEZLevelsSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid ship = default;
        const float tonnes = 12f;

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            entMan.EnsureComponent<WFEncounterGridComponent>(ship);
            var ballast = entMan.SpawnEntity(Ballast, new EntityCoordinates(ship, 2.5f, 2.5f));
            SetTonnes(entMan, profile, ballast, tonnes);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(tonnes).Within(0.01f), "An encounter hull still carries its load.");

            var lift = new WFGridLiftLoadEvent(0f);
            entMan.EventBus.RaiseLocalEvent(ship, ref lift);
            Assert.That(lift.Mass, Is.EqualTo(0f), "An encounter hull's cargo doesn't count against lift.");
            Assert.That(levels.WfGetSetLiftLoad(ship), Is.EqualTo(0f));

            entMan.RemoveComponent<WFEncounterGridComponent>(ship);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var lift = new WFGridLiftLoadEvent(0f);
            entMan.EventBus.RaiseLocalEvent(ship, ref lift);
            Assert.That(lift.Mass, Is.EqualTo(tonnes).Within(0.01f), "A freed hull's cargo counts against lift.");
            Assert.That(levels.WfGetSetLiftLoad(ship), Is.EqualTo(tonnes).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every trade good has an explicit weight class, from the table or its own component, and every class exists.</summary>
    [Test]
    public async Task EveryTradeGoodHasAnExplicitClass()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var profile = protos.Index(WFCargoLoadSystem.DefaultProfile);
            var missing = new List<string>();
            var checkedGoods = 0;

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || protos.EnumerateAllParents<EntityPrototype>(proto.ID).All(parent => parent.id != "BaseTradeItem"))
                    continue;

                checkedGoods++;
                if (!proto.TryComp<WFCargoMassComponent>(out var mass, factory))
                {
                    missing.Add($"{proto.ID} (no WFCargoMass)");
                    continue;
                }

                if (mass.Class != null && !profile.Classes.ContainsKey(mass.Class))
                    missing.Add($"{proto.ID} (unknown class {mass.Class})");
                else if (mass.PerUnit <= 0f && mass.Class == null && !profile.Goods.ContainsKey(proto.ID))
                    missing.Add(proto.ID);
            }

            foreach (var (good, cls) in profile.Goods)
            {
                if (!profile.Classes.ContainsKey(cls))
                    missing.Add($"{good} (table class {cls} unknown)");
            }

            Assert.That(checkedGoods, Is.GreaterThan(0), "There should be trade goods to check.");
            Assert.That(missing, Is.Empty, "Trade goods without an explicit weight class: " + string.Join(", ", missing));
        });

        await pair.CleanReturnAsync();
    }
}

/// <summary>Records the entity popups a connected test client receives, only while a test asks it to.</summary>
public sealed partial class WFCargoPopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    private bool _recording;

    /// <summary>Every popup message received while recording, oldest first.</summary>
    public readonly List<string> Received = new();

    public override void Initialize()
    {
        base.Initialize();

        if (!_net.IsClient)
            return;

        SubscribeNetworkEvent<PopupEntityEvent>(OnPopup);
    }

    /// <summary>Clears what was recorded and starts recording.</summary>
    public void Start()
    {
        Received.Clear();
        _recording = true;
    }

    /// <summary>Stops recording and drops what was recorded.</summary>
    public void Stop()
    {
        _recording = false;
        Received.Clear();
    }

    private void OnPopup(PopupEntityEvent ev)
    {
        if (_recording)
            Received.Add(ev.Message);
    }
}
