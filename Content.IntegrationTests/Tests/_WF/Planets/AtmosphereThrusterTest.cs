#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
[TestOf(typeof(ThrusterSystem))]
public sealed class AtmosphereThrusterTest
{
    private const float Gravity = 9.81f;

    [Test]
    public async Task OverloadRestReleasesDemandAndRetryRequiresRealPower()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 981f);
        await server.WaitAssertion(() =>
        {
            var system = server.System<ThrusterSystem>();
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            system.WfRefreshAtmosphereThruster(engine, thruster);
            var state = em.GetComponent<WFAtmosphereThrusterComponent>(engine);
            var receiver = em.GetComponent<ApcPowerReceiverComponent>(engine);
            system.WfSetPowerPulse(state, true);
            system.WfRefreshAtmosphereThruster(engine, thruster);
            Assert.That(receiver.Load, Is.EqualTo(1f));
            Assert.That(thruster.Enabled, Is.True, "The regulator must not alter the player's enable switch.");
            Assert.That(thruster.IsOn, Is.False);
            Assert.That(system.CanEnable(engine, thruster), Is.False);
            Assert.That(system.WfAtmosphericForce(engine, thruster), Is.Zero);
            state.PulseAt = TimeSpan.Zero;
            system.WfSetPowerPulse(state, true);
            system.WfRefreshAtmosphereThruster(engine, thruster);
            Assert.That(receiver.Load, Is.EqualTo(state.RatedLoad * 3f));
            Assert.That(thruster.IsOn, Is.True);
            Assert.That(system.WfAtmosphericForce(engine, thruster), Is.GreaterThan(0f));
            state.PulseAt = TimeSpan.Zero;
            system.WfSetPowerPulse(state, true);
            system.WfRefreshAtmosphereThruster(engine, thruster);
            Assert.That(thruster.IsOn, Is.False, "Sustained overload must rest again, not resume permanent full draw.");
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(engine, true);
            system.WfSetPowerPulse(state, false);
            system.WfRefreshAtmosphereThruster(engine, thruster);

        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.75f));
        await server.WaitAssertion(() =>
        {
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            Assert.That(server.System<ThrusterSystem>().CanEnable(engine, thruster), Is.False, "A retry cannot create power.");
            Assert.That(thruster.IsOn, Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task RealApcOverloadPulsesAndImprovedSupplyRestoresContinuousOperation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 981f);
        var apc = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var coordinates = em.GetComponent<TransformComponent>(engine).Coordinates;
            em.SpawnEntity("CableApcExtension", coordinates);
            apc = em.SpawnEntity("APCBasic", coordinates);
            var batteries = server.System<BatterySystem>();
            batteries.SetMaxCharge(apc, 1000000f);
            batteries.SetCharge(apc, 1000000f);
            var supply = em.GetComponent<PowerNetworkBatteryComponent>(apc);
            supply.MaxSupply = em.GetComponent<WFAtmosphereThrusterComponent>(engine).RatedLoad * 2f;
            supply.SupplyRampTolerance = 100000f;
            supply.SupplyRampRate = 100000f;
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(engine, true);
        });
        var rests = 0;
        var retries = 0;
        for (var i = 0; i < 32; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
            await server.WaitAssertion(() =>
            {
                var state = em.GetComponent<WFAtmosphereThrusterComponent>(engine);
                var receiver = em.GetComponent<ApcPowerReceiverComponent>(engine);
                if (!state.PowerLimited) return; // Allow node attachment and first power solve.
                if (state.Cooling)
                {
                    rests++;
                    Assert.That(receiver.Load, Is.EqualTo(1f));
                    Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.False);
                }
                else
                {
                    retries++;
                    Assert.That(receiver.Load, Is.EqualTo(state.RatedLoad * 3f));
                }
            });
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Provider, Is.Not.Null);
            Assert.That(rests, Is.GreaterThan(2));
            Assert.That(retries, Is.GreaterThan(1));
            server.System<WFCrashApcFaultSystem>().DamageOverloadedApcs(hull);
            Assert.That(em.HasComponent<WFCrashApcFaultComponent>(apc), Is.True, "An overloaded crash must damage its supplying APC.");
            em.RemoveComponent<WFCrashApcFaultComponent>(apc);
            em.GetComponent<PowerNetworkBatteryComponent>(apc).MaxSupply = 100000f;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFAtmosphereThrusterComponent>(engine).PowerLimited, Is.False);
            Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.True);
            // Crash destruction frees the battery before network membership is rebuilt.
            em.DeleteEntity(apc);
            Assert.DoesNotThrow(() => server.System<CEZLevelsSystem>().WfGetAtmospherePower(hull, out _, out _));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Thrust and APC demand track the engine, hover and re-entry included, without compounding.</summary>
    [Test]
    public async Task GroundedWreckStopsHoverDrawAndAscentRestoresIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-8, -8), new Vector2i(32, 32));
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 4000f);
        await server.WaitPost(() => em.EnsureComponent<WFCrashImpactComponent>(hull));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<WFAtmosphereThrusterComponent>(engine);
            Assert.That(state.PowerLimited, Is.False);
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Load, Is.EqualTo(state.RatedLoad));
        });
        var pilot = await HoldVertical(pair, hull, Content.Shared.Movement.Systems.ShuttleButtons.None);
        var console = FindShuttleConsole(em, hull);
        var levels = server.System<CEZLevelsSystem>();
        var started = false;
        string? reason = null;
        await server.WaitPost(() => started = levels.WfTryBeginLiftoff(hull, console, pilot, out reason));
        Assert.That(started, Is.True, $"The wreck's Liftoff request was refused: {reason}");
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<WFAtmosphereThrusterComponent>(engine);
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Load, Is.EqualTo(state.RatedLoad * 3f));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepeatedAirGroundTransitOrbitAndSpaceTransitionsRestoreThrustAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();
        var transform = server.System<SharedTransformSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var space = await pair.CreateTestMap();
        var transit = await pair.CreateTestMap();
        var hull = await BuildHull(pair, space.MapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 4000f);
        var normalLoad = 0f;

        await server.WaitAssertion(() =>
        {
            var gap = em.EnsureComponent<CEZTransitMapComponent>(transit.MapUid);
            gap.LowerMap = layers[^2];
            gap.UpperMap = layers[^1];
            gap.PrimaryGrid = hull;
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            normalLoad = thruster.OriginalLoad;
            Assert.That(normalLoad, Is.GreaterThan(0f), "A debug engine's zero load cannot test the power multiplier.");
            Assert.That(thruster.Thrust, Is.EqualTo(4000f).Within(0.01f));
            Assert.That(thruster.IsOn, Is.True);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                foreach (var destination in new[]
                         {
                             (Map: layers[^2], Atmosphere: true),
                             (Map: layers[^1], Atmosphere: false),
                             (Map: layers[0], Atmosphere: true),
                             (Map: space.MapUid, Atmosphere: false),
                             (Map: transit.MapUid, Atmosphere: true),
                             (Map: layers[^1], Atmosphere: false),
                         })
                {
                    transform.SetCoordinates(hull, new EntityCoordinates(destination.Map, Vector2.Zero));
                    for (var refresh = 0; refresh < 3; refresh++)
                    {
                        thrusters.WfRefreshAtmosphereThruster(engine, thruster);
                        var force = destination.Atmosphere ? 2000f : 4000f;
                        var load = normalLoad * (destination.Atmosphere ? 3f : 1f);
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(thruster.Thrust, Is.EqualTo(force).Within(0.01f));
                            Assert.That(thruster.OriginalLoad, Is.EqualTo(load).Within(0.01f));
                            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Load,
                                Is.EqualTo(load).Within(0.01f));
                            AssertSingleEngineBank(em, hull, engine, force);
                            if (destination.Atmosphere)
                                Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(force / Gravity).Within(0.01f));
                        }
                    }
                }
            }

            // Let the normal update path apply the final air entry, without calling refresh.
            transform.SetCoordinates(hull, new EntityCoordinates(layers[^2], Vector2.Zero));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(layers[^2]),
                    "This engine has enough lift to hover; the hull must hold its air layer.");
                Assert.That(em.GetComponent<ShuttleComponent>(hull).ThrustDirections, Is.EqualTo(DirectionFlag.None));
                Assert.That(thruster.Thrust, Is.EqualTo(2000f).Within(0.01f));
                Assert.That(thruster.OriginalLoad, Is.EqualTo(normalLoad * 3f).Within(0.01f));
                Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Load,
                    Is.EqualTo(normalLoad * 3f).Within(0.01f), "Hover must pay the atmospheric load without directional input.");
                AssertSingleEngineBank(em, hull, engine, 2000f);
            }
            transform.SetCoordinates(hull, new EntityCoordinates(space.MapUid, Vector2.Zero));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThrusterComponent>(engine).Thrust, Is.EqualTo(4000f).Within(0.01f));
            Assert.That(em.GetComponent<ThrusterComponent>(engine).OriginalLoad, Is.EqualTo(normalLoad).Within(0.01f));
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Load, Is.EqualTo(normalLoad).Within(0.01f));
            AssertSingleEngineBank(em, hull, engine, 4000f);
        });

        // The pool only deletes the last test map, so the first one (and the hull on it) goes here.
        await server.WaitPost(() => em.DeleteEntity(space.MapUid));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedLiftUsesHalfOrdinaryAndFullConvertedForceAndOnlyEnabledRunningEngines()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        var converted = await AddLandingThrusters(pair, hull, 2);

        await server.WaitAssertion(() =>
        {
            var ordinary = Children(em, hull).Where(uid =>
                em.TryGetComponent<ThrusterComponent>(uid, out var engine)
                && engine.Type == ThrusterType.Linear
                && !em.HasComponent<WFLandingThrusterComponent>(uid)).ToArray();
            Assert.That(ordinary, Has.Length.EqualTo(4));
            Assert.That(em.GetComponent<ShuttleComponent>(hull).AngularThrust, Is.GreaterThan(0f),
                "The powered gyroscope must be present to prove it does not contribute lift.");
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(400f / Gravity + 100f).Within(0.01f));

            em.EventBus.RaiseLocalEvent(ordinary[0], new ActivateInWorldEvent(ordinary[0], ordinary[0], true));
            Assert.That(em.GetComponent<ThrusterComponent>(ordinary[0]).Enabled, Is.False);
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(300f / Gravity + 100f).Within(0.01f));

            em.EventBus.RaiseLocalEvent(converted[0], new ActivateInWorldEvent(converted[0], converted[0], true));
            Assert.That(em.GetComponent<ThrusterComponent>(converted[0]).Enabled, Is.False);
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(300f / Gravity + 50f).Within(0.01f));

        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualPowerLossStopsLiftAndPowerReturnWithholdsItForTwoSeconds(bool converted)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var receiver = server.System<SharedPowerReceiverSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 981f);
        await server.WaitPost(() =>
        {
            if (converted)
                em.EnsureComponent<WFLandingThrusterComponent>(engine);
            server.System<ThrusterSystem>().WfRefreshAtmosphereThruster(engine, em.GetComponent<ThrusterComponent>(engine));
        });
        var capacity = converted ? 100f : 50f;
        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(capacity).Within(0.01f));
            // There is no wired APC on this hull: requiring power causes real PowerChangedEvent delivery.
            receiver.SetNeedsPower(engine, true);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Powered, Is.False);
            Assert.That(em.GetComponent<ThrusterComponent>(engine).Enabled, Is.True);
            Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.False);
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.Zero);
            Assert.That(em.GetComponent<ShuttleComponent>(hull).LinearThrust.Sum(), Is.EqualTo(0f).Within(0.01f));
            receiver.SetNeedsPower(engine, false);
        });

        // Allow the APC poll to observe returning power, while remaining inside the recovery window.
        await server.WaitRunTicks(pair.SecondsToTicks(0.75f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine).Powered, Is.True);
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.Zero, "Returning power must not immediately recover lift.");
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.Zero, "The two-second recovery buffer ended early."));
        await server.WaitRunTicks(pair.SecondsToTicks(1.25f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.True);
            Assert.That(zLevels.WfGetLandingThrust(hull), Is.EqualTo(capacity).Within(0.01f));
            AssertSingleEngineBank(em, hull, engine, capacity * Gravity);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConversionPreservesPaidEngineAndItsRatingAndBypassesAtmosphericPenalty()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();
        var transform = server.System<SharedTransformSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[^1]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var engine = await AddPaidEngine(pair, hull, 981f);
        ThrusterComponent original = default!;
        ApcPowerReceiverComponent originalReceiver = default!;
        var normalLoad = 0f;
        var baseThrust = 0f;
        var coords = default(EntityCoordinates);
        var rotation = Angle.Zero;
        var kit = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            original = em.GetComponent<ThrusterComponent>(engine);
            originalReceiver = em.GetComponent<ApcPowerReceiverComponent>(engine);
            normalLoad = original.OriginalLoad;
            baseThrust = original.BaseThrust;
            transform.SetCoordinates(hull, new EntityCoordinates(layers[0], Vector2.Zero));
            thrusters.WfRefreshAtmosphereThruster(engine, original);
            Assert.That(original.Thrust, Is.EqualTo(490.5f).Within(0.01f));
            Assert.That(originalReceiver.Load, Is.EqualTo(normalLoad * 3f).Within(0.01f));
            coords = em.GetComponent<TransformComponent>(engine).Coordinates;
            rotation = em.GetComponent<TransformComponent>(engine).LocalRotation;
            var user = em.SpawnEntity(ViewerProto, new EntityCoordinates(hull, coords.Position + Vector2.One));
            kit = em.SpawnEntity("WFLandingThrusterKit", em.GetComponent<TransformComponent>(user).Coordinates);
            server.System<SharedInteractionSystem>().InteractUsing(user, kit, engine, coords);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(engine), Is.True, "The conversion must preserve the engine entity.");
            Assert.That(em.GetComponent<ThrusterComponent>(engine), Is.SameAs(original));
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(engine), Is.SameAs(originalReceiver));
            Assert.That(em.HasComponent<WFLandingThrusterComponent>(engine), Is.True);
            Assert.That(em.EntityExists(kit), Is.False);
            Assert.That(em.GetComponent<MetaDataComponent>(engine).EntityPrototype?.ID, Is.EqualTo("Thruster"));
            Assert.That(em.GetComponent<TransformComponent>(engine).Coordinates, Is.EqualTo(coords));
            Assert.That(em.GetComponent<TransformComponent>(engine).LocalRotation, Is.EqualTo(rotation));
            Assert.That(em.GetComponent<TransformComponent>(engine).Anchored, Is.True);
            Assert.That(original.BaseThrust, Is.EqualTo(baseThrust));
            Assert.That(original.Enabled && original.IsOn, Is.True);
            Assert.That(originalReceiver.NeedsPower, Is.False);

            foreach (var map in new[] { layers[0], layers[^1], layers[1], layers[0], layers[^1] })
            {
                transform.SetCoordinates(hull, new EntityCoordinates(map, Vector2.Zero));
                thrusters.WfRefreshAtmosphereThruster(engine, original);
                Assert.That(original.Thrust, Is.EqualTo(981f).Within(0.01f));
                Assert.That(original.OriginalLoad, Is.EqualTo(normalLoad).Within(0.01f));
                Assert.That(originalReceiver.Load, Is.EqualTo(normalLoad).Within(0.01f));
                AssertSingleEngineBank(em, hull, engine, 981f);
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConversionRejectsAngularEngineWithoutConsumingKit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var hull = await BuildHull(pair, map.MapId);
        await MapInitHull(pair, hull);
        var engine = EntityUid.Invalid;
        var kit = EntityUid.Invalid;
        ThrusterComponent original = default!;
        var angularThrust = 0f;
        await server.WaitPost(() =>
        {
            engine = Children(em, hull).Single(uid =>
                em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && thruster.Type == ThrusterType.Angular);
            original = em.GetComponent<ThrusterComponent>(engine);
            angularThrust = em.GetComponent<ShuttleComponent>(hull).AngularThrust;
            var coords = em.GetComponent<TransformComponent>(engine).Coordinates;
            var user = em.SpawnEntity(ViewerProto, new EntityCoordinates(hull, coords.Position + Vector2.One));
            kit = em.SpawnEntity("WFLandingThrusterKit", em.GetComponent<TransformComponent>(user).Coordinates);
            server.System<SharedInteractionSystem>().InteractUsing(user, kit, engine, coords);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(kit), Is.True);
            Assert.That(em.GetComponent<ThrusterComponent>(engine), Is.SameAs(original));
            Assert.That(em.HasComponent<WFLandingThrusterComponent>(engine), Is.False);
            Assert.That(em.GetComponent<ShuttleComponent>(hull).AngularThrust, Is.EqualTo(angularThrust));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AtmospherePowerSumsMixedEnginesAndFlagsOnlyRequiredUnlinkedPower()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var map = await pair.CreateTestMap();
        var hull = await BuildHull(pair, map.MapId);
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        var ordinary = await AddPaidEngine(pair, hull, 4000f);
        var converted = (await AddLandingThrusters(pair, hull, 1))[0];

        await server.WaitAssertion(() =>
        {
            var ordinaryLoad = em.GetComponent<ThrusterComponent>(ordinary).OriginalLoad;
            var convertedLoad = em.GetComponent<ThrusterComponent>(converted).OriginalLoad;
            Assert.That(ordinaryLoad, Is.GreaterThan(0f));
            Assert.That(convertedLoad, Is.GreaterThan(0f));
            var expected = 3f * ordinaryLoad + convertedLoad;

            // Forecast atmosphere demand while still in space, before any multipliers have been applied.
            zLevels.WfGetAtmospherePower(hull, out var demand, out var deficit);
            Assert.That(demand, Is.EqualTo(expected).Within(0.01f));
            Assert.That(deficit, Is.False, "NeedsPower=false engines are exempt from network headroom checks.");

            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(ordinary).Provider, Is.Null);
            receiver.SetNeedsPower(ordinary, true);
            zLevels.WfGetAtmospherePower(hull, out demand, out deficit);
            Assert.That(demand, Is.EqualTo(expected).Within(0.01f));
            Assert.That(deficit, Is.True, "An engine requiring power without a linked network must warn.");

            receiver.SetNeedsPower(ordinary, false);
            receiver.SetNeedsPower(converted, true);
            zLevels.WfGetAtmospherePower(hull, out demand, out deficit);
            Assert.That(demand, Is.EqualTo(expected).Within(0.01f));
            Assert.That(deficit, Is.True, "Conversion removes the multiplier, not the need for a power network.");

            em.EventBus.RaiseLocalEvent(converted, new ActivateInWorldEvent(converted, converted, true));
            zLevels.WfGetAtmospherePower(hull, out demand, out deficit);
            Assert.That(demand, Is.EqualTo(3f * ordinaryLoad).Within(0.01f));
            Assert.That(deficit, Is.False, "A disabled engine contributes neither demand nor a deficit.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(0.5f, 0f)]
    [TestCase(1f, 0f)]
    [TestCase(2f, 0.5f)]
    public async Task ManeuveringUsesOnlyForceLeftAfterHover(float liftRatio, float expectedFactor)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var zLevels = server.System<CEZLevelsSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var space = await pair.CreateTestMap();
        var hull = await BuildHull(pair, await MapIdOf(pair, layers[0]));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        // 225 half-mass hull tiles; ordinary engines retain half their rated force.
        await AddPaidEngine(pair, hull, 2f * 112.5f * Gravity * liftRatio);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.WfTryGetLiftRatio(hull, out var actualRatio), Is.True);
            Assert.That(actualRatio, Is.EqualTo(liftRatio).Within(0.01f));
            Assert.That(zLevels.WfManeuveringFactor(hull), Is.EqualTo(expectedFactor).Within(0.01f));
            foreach (var map in new[] { layers[^1], space.MapUid })
            {
                server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(map, Vector2.Zero));
                Assert.That(zLevels.WfManeuveringFactor(hull), Is.EqualTo(1f));
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A real nonzero-load engine, placed at the deck edge with its nozzle exposed.</summary>
    private static async Task<EntityUid> AddPaidEngine(TestPair pair, EntityUid hull, float force)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var engine = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var nozzle = Angle.Zero.Opposite().ToWorldVec();
            var position = new Vector2(
                MathF.Abs(nozzle.X) > 0.5f ? (nozzle.X > 0f ? 14.5f : 0.5f) : 7.5f,
                MathF.Abs(nozzle.Y) > 0.5f ? (nozzle.Y > 0f ? 14.5f : 0.5f) : 7.5f);
            engine = em.SpawnEntity("Thruster", new EntityCoordinates(hull, position));
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(engine, false);
            server.System<ThrusterSystem>().WfSetRatedThrust(engine, force);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() => Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.True));
        return engine;
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var mapId = MapId.Nullspace;
        await pair.Server.WaitPost(() => mapId = pair.Server.EntMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    private static void AssertSingleEngineBank(IEntityManager em, EntityUid hull, EntityUid engine, float force)
    {
        var shuttle = em.GetComponent<ShuttleComponent>(hull);
        Assert.That(shuttle.LinearThrusters.Sum(bank => bank.Count(uid => uid == engine)), Is.EqualTo(1),
            "Refreshing the environment duplicated or dropped the engine's bank registration.");
        for (var direction = 0; direction < shuttle.LinearThrust.Length; direction++)
        {
            var expected = shuttle.LinearThrusters[direction].Contains(engine) ? force : 0f;
            Assert.That(shuttle.LinearThrust[direction], Is.EqualTo(expected).Within(0.01f),
                $"Directional bank {direction} does not contain the effective force used for acceleration.");
        }
    }
}
