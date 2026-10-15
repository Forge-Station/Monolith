using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Security.Cryptography;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Radio.Components;
using Content.Client.UserInterface.Systems.Chat;
using Robust.Client.UserInterface;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radiation.Components;
using Content.Server.Radiation.Components;
using Content.Server.Radiation.Systems;
using Content.Server.Medical;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Server._Forge.KIAS;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server._Mono.NPC.HTN.Operators;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Dynamics;
using Content.Shared._Crescent.ShipShields;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._Mono.Company;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasBriarPhysicalGateTests
{
    [TestCase("boot", "", false)]
    [TestCase("shutdown", "Shutdown", false)]
    [TestCase("hull-damage", "HullDamage", false)]
    [TestCase("crew-critical", "CrewCritical", false)]
    [TestCase("crew-dead", "CrewDead", false)]
    [TestCase("fire", "Fire", false)]
    [TestCase("fire-clear", "FireClear", false)]
    [TestCase("atmosphere", "AtmosDanger", false)]
    [TestCase("atmos-clear", "AtmosClear", false)]
    [TestCase("battle-manual", "Manual", false)]
    [TestCase("quiet", "QuietMode", false)]
    [TestCase("radiation", "Radiation", false)]
    [TestCase("local-threat", "LocalThreat", false)]
    [TestCase("power-lost", "PowerLost", false)]
    [TestCase("greeting", "CaptainGreeting", false)]
    [TestCase("anomaly", "AnomalyGrowth", false)]
    [TestCase("boarding", "Boarding", false)]
    [TestCase("medical-assistance", "CrewUnavailable", false)]
    [TestCase("vessel-critical", "VesselCritical", false)]
    [TestCase("vessel-critical", "VesselCritical", true)]
    [TestCase("battle-impact", "HullImpact", false)]
    [TestCase("battle-flash", "WeaponFlash", false)]
    [TestCase("flash-unknown", "WeaponFlash", false)]
    [TestCase("collision", "Collision", false)]
    [TestCase("contact", "Contact", false)]
    [TestCase("arrival", "Arrival", false)]
    [TestCase("proximity", "Proximity", false)]
    [TestCase("power-deficit", "PowerDeficit", false)]
    public async Task RealBriarStimulusReachesPhysicalCardAndWorldOutput(string presetId, string triggerPort, bool clearClock)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 20261009, ClientSeed = 20261009 });
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var io = em.System<KiasControllerIoSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid grid = default, core = default, card = default, actor = default, console = default, damagedWall = default;
        MapId mapId = default;
        EntityUid externalGrid = default, externalGun = default, impactWall = default;
        EntityUid impactProjectile = default, powerTarget = default, ownerMind = default;
        EntityUid lifecycleTarget = default;
        Vector2 foreignStart = default, arrivalTarget = default;
        float impactBefore = 0;
        ShipMoveToOperator? autopilot = null;
        NPCBlackboard? steeringBoard = null;
        var autopilotFinished = false;
        float? arrivalDistance = null;
        var signals = new List<object>();
        object[] startupSignals = Array.Empty<object>();
        object? runtimeBeforeStimulus = null;
        var commands = new List<object>();
        var receivedRadio = new List<ChatMessage>();
        await pair.Client.WaitAssertion(() => pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded += receivedRadio.Add);
        var commandedSpeakers = new HashSet<EntityUid>();
        var detectorSeen = false;
        uint? detectedTick = null, graphTick = null;
        uint stimulusTick = 0;
        var inputConfirmed = false;
        var inputEvidence = new List<string>();
        var programmedRecorders = new HashSet<EntityUid>();
        var lightTargets = new HashSet<EntityUid>();
        var speakerToneObserved = new HashSet<EntityUid>();
        var completed = false;
        var crossChecks = new Dictionary<string, bool>();
        var flashDetectorsSeen = new HashSet<EntityUid>();
        var automationValues = new Dictionary<string, KiasGraphValue>();
        Dictionary<string, KiasGraphValue>? nativeEventSnapshot = null;
        string? worldBefore = null;
        var causalId = $"{presetId}:{(clearClock ? "clear" : "primary")}:boot";

        string CaptureWorld()
        {
            return JsonSerializer.Serialize(new
            {
                grid = grid.ToString(), map = mapId.ToString(), card = card.ToString(),
                active = em.System<KiasSystem>().ActiveGrids.Contains(grid),
                alert = em.TryGetComponent<KiasProtocolComponent>(core, out var protocol) ? protocol.Alert.ToString() : null,
                entities = OnGrid<MetaDataComponent>().OrderBy(uid => uid.ToString()).Select(uid => new
                {
                    uid = uid.ToString(), prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                    position = em.GetComponent<TransformComponent>(uid).LocalPosition.ToString(),
                    damage = em.TryGetComponent<DamageableComponent>(uid, out var damage) ? (float?) damage.TotalDamage : null,
                    light = em.TryGetComponent<PoweredLightComponent>(uid, out var light) ? (bool?) light.On : null,
                    mob = em.TryGetComponent<MobStateComponent>(uid, out var mob) ? (MobState?) mob.CurrentState : null,
                    tone = em.TryGetComponent<KiasSpeakerComponent>(uid, out var speaker) && speaker.Tone is { } tone && em.EntityExists(tone),
                    entries = em.TryGetComponent<KiasRecorderComponent>(uid, out var recorder) ? recorder.Entries.ToArray() : null
                }).ToArray()
            });
        }

        string StateHash(string state) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

        EntityCoordinates ScannerInterior(KiasScannerModules module)
        {
            foreach (var scanner in OnGrid<KiasRoomScannerComponent>())
            {
                if (!em.System<KiasSystem>().IsOnline(scanner)
                    || (em.GetComponent<KiasRoomScannerComponent>(scanner).Modules & module) == 0
                    || !em.System<KiasRoomTopologySystem>().TryGetScannerRoom(scanner, out var room, out _)) continue;
                var tile = em.System<KiasRoomTopologySystem>().ScannerCells(scanner).First(cell => !room.Doors.Contains(cell));
                return new EntityCoordinates(grid, tile.X + .5f, tile.Y + .5f);
            }
            throw new InvalidOperationException($"Briar has no valid online room with {module} coverage.");
        }

        EntityUid[] OnGrid<T>() where T : Component
        {
            var list = new List<EntityUid>();
            var query = em.AllEntityQueryEnumerator<T, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
                if (transform.GridUid == grid)
                    list.Add(uid);
            return list.ToArray();
        }

        EntityUid CreateForeignGrid(Vector2 position)
        {
            var foreign = maps.CreateGridEntity(mapId);
            var tile = maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-5, 0)).Tile;
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++) maps.SetTile(foreign, new Vector2i(x, y), tile);
            em.EnsureComponent<ShuttleComponent>(foreign);
            em.System<SharedPhysicsSystem>().SetBodyType(foreign, BodyType.Dynamic);
            em.System<SharedTransformSystem>().SetWorldPosition(foreign, position);
            externalGrid = foreign;
            foreignStart = position;
            return foreign;
        }

        object RuntimeSnapshot()
        {
            var runtime = em.System<KiasControllerRuntimeSystem>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var states = (System.Collections.IDictionary) typeof(KiasControllerRuntimeSystem).GetField("_cards", flags)!.GetValue(runtime)!;
            var state = states[card];
            return new { eventEpoch = typeof(KiasControllerRuntimeSystem).GetField("_eventEpoch", flags)!.GetValue(runtime),
                machineEpoch = state?.GetType().GetField("Epoch")!.GetValue(state),
                machineIdentity = state?.GetType().GetField("Machine")!.GetValue(state)?.GetHashCode(),
                status = runtime.Status(card), pending = em.System<KiasSystem>().TopologyPending(grid) };
        }

        void Emission(EntityUid device, string profile, string port, KiasGraphValue value)
        {
            if (profile == "WeaponFlashDetector" && port == "Triggered" && em.GetComponent<TransformComponent>(device).GridUid == grid)
                flashDetectorsSeen.Add(device);
            if (device == core && profile == "Automation" && value.Type != KiasPortType.Signal)
                automationValues[port] = value;
            if (device != core || profile != "Automation" || port != triggerPort)
                return;
            nativeEventSnapshot ??= new Dictionary<string, KiasGraphValue>(automationValues);
            detectorSeen = true;
            detectedTick ??= timing.CurTick.Value;
            causalId = $"{presetId}:{(clearClock ? "clear" : "primary")}:native-{signals.Count + 1}";
            signals.Add(new { causalId, tick = timing.CurTick.Value, device = device.ToString(), profile, port,
                context = new Dictionary<string, KiasGraphValue>(automationValues), runtime = RuntimeSnapshot() });
        }

        void Dispatch(EntityUid sourceGrid, EntityUid sourceCard, EntityUid target, string profile, string port)
        {
            if (sourceGrid != grid || sourceCard != card)
                return;
            graphTick ??= timing.CurTick.Value;
            commands.Add(new { causalId, tick = timing.CurTick.Value, card = sourceCard.ToString(), target = target.ToString(), profile, port });
            if (profile == "Recorder" && port == "Record") programmedRecorders.Add(target);
            if (profile == "Speaker" && port == "Alarm") commandedSpeakers.Add(target);
            if (profile == "Lighting" && port is "On" or "Off") lightTargets.Add(target);
        }

        void ObserveSound()
        {
            foreach (var uid in commandedSpeakers)
                if (em.GetComponent<KiasSpeakerComponent>(uid).Tone is { } tone && em.EntityExists(tone))
                    speakerToneObserved.Add(uid);
        }

        void SaveResult(string status, string? error = null)
        {
            var output = Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var worldAfter = CaptureWorld();
            var stateFile = $"world-{presetId}{(clearClock ? "-clear" : string.Empty)}.json";
            File.WriteAllText(Path.Combine(output, stateFile), JsonSerializer.Serialize(new
            {
                before = worldBefore == null ? (JsonElement?) null : JsonSerializer.Deserialize<JsonElement>(worldBefore),
                after = JsonSerializer.Deserialize<JsonElement>(worldAfter),
                beforeRaw = worldBefore, afterRaw = worldAfter
            }, new JsonSerializerOptions { WriteIndented = true }));
            var records = programmedRecorders.Where(em.EntityExists).Select(uid => new
            {
                uid = uid.ToString(), entries = em.GetComponent<KiasRecorderComponent>(uid).Entries.ToArray()
            }).ToArray();
            var result = new
            {
                mapSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(KiasTestArtifacts.RepositoryRoot, "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml")))).ToLowerInvariant(),
                presetSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(KiasTestArtifacts.RepositoryRoot, "Resources/Prototypes/_Forge/KIAS/controller_presets.yml")))).ToLowerInvariant(),
                actualCardProgramSha256 = em.TryGetComponent<KiasControllerCardComponent>(card, out var storedCard)
                    ? StateHash(JsonSerializer.Serialize(storedCard.Program, new JsonSerializerOptions { IncludeFields = true })) : null,
                preset = presetId, status, error, scope = "Isolated physical card on a clean Briar clone; not all cross-tests",
                temporaryCard = false, clearClock, stimulusTick, inputConfirmed, inputEvidence, arrivalDistance,
                tickRate = timing.TickRate,
                worldStateBeforeSha256 = worldBefore == null ? null : StateHash(worldBefore),
                worldStateAfterSha256 = StateHash(worldAfter), worldStateEvidence = stateFile,
                worldStateScope = "Relevant grid entities: position, damage, mob state, lights, speaker tones, recorder entries, core availability and alert; not a full map serialization.",
                causalIdScope = "Test correlation assigned at each observed native detector signal, propagated to card commands; boot uses its lifecycle id. Not an engine-generated event GUID.",
                detectorSeen, detectedTick, graphTick, completedTick = timing.CurTick.Value,
                runtimeBeforeStimulus, runtimeAfter = RuntimeSnapshot(), startupSignals, signals, commands, records, crossChecks, speakerToneObserved = speakerToneObserved.Select(uid => uid.ToString()).ToArray(),
                lightTargets = lightTargets.Select(uid => uid.ToString()).ToArray(),
                lightStates = lightTargets.Where(uid => em.HasComponent<PoweredLightComponent>(uid))
                    .Select(uid => new { uid = uid.ToString(), on = em.GetComponent<PoweredLightComponent>(uid).On }).ToArray(),
                defenceStates = OnGrid<KiasDefenceComponent>().Select(uid => new { uid = uid.ToString(),
                    automatic = em.GetComponent<KiasDefenceComponent>(uid).PdcEnabled }).ToArray(),
                alert = core.Valid && em.TryGetComponent<KiasProtocolComponent>(core, out var protocol) ? protocol.Alert.ToString() : null,
                simulationTimeSeconds = timing.CurTime.TotalSeconds,
                crewUnavailable = grid.Valid && em.System<KiasCrewSystem>().CrewUnavailable(grid),
                protocolDiagnostics = core.Valid && em.TryGetComponent<KiasProtocolComponent>(core, out var diagnostics)
                    ? new { distressUntil = diagnostics.CrewDistressUntil.TotalSeconds, unavailableSince = diagnostics.CrewUnavailableSince.TotalSeconds,
                        medicalAfter = diagnostics.MedicalAfter.TotalSeconds, recentDamage = diagnostics.RecentDamage,
                        damageUntil = diagnostics.DamageEvidenceUntil.TotalSeconds, maydayReason = diagnostics.MaydayReason } : null,
                coreActive = em.System<KiasSystem>().ActiveGrids.Contains(grid),
                runtimeStatus = em.System<KiasControllerRuntimeSystem>().Status(card),
                runtimeFault = em.System<KiasControllerRuntimeSystem>().Fault(card),
                graphValues = new[] { (1, "Message"), (1, "Value"), (1, "PowerLost"), (1, "EventKey"),
                    (2, "Value"), (4, "True"), (4, "False"), (5, "Ready"), (7, "$OnlineCount"), (8, "$OnlineCount") }
                    .Select(endpoint => { var value = em.System<KiasControllerRuntimeSystem>().LastValue(card, endpoint.Item1, endpoint.Item2);
                        return new { node = endpoint.Item1, port = endpoint.Item2, type = value.Type.ToString(),
                            value.Number, value.Bool, value.Text }; }).ToArray(),
                topologyPending = em.System<KiasSystem>().TopologyPending(grid)
            };
            File.WriteAllText(Path.Combine(output, $"physical-{presetId}{(clearClock ? "-clear" : string.Empty)}.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }

        io.Emitted += Emission;
        io.CommandDispatched += Dispatch;
        try
        {
            await server.WaitAssertion(() =>
            {
                maps.CreateMap(out mapId, runMapInit: false);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                    new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
                grid = loaded!.Value.Owner;
                core = OnGrid<KiasCoreComponent>().Single();
                console = OnGrid<KiasManagementComponent>().Single();
                foreach (var uid in OnGrid<KiasControllerCardComponent>())
                {
                    var component = em.GetComponent<KiasControllerCardComponent>(uid);
                    component.Enabled = component.Program.Name == presetId;
                    if (component.Enabled) card = uid;
                }
                Assert.That(card.Valid, Is.True, "Each of the 27 programs must exist on a physical card in the saved Briar.");
                var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
                em.EnsureComponent<ShipOwnershipComponent>(grid).OwnerUserId = session.UserId;
                actor = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(-4.5f, 0.5f)));
                var minds = em.System<SharedMindSystem>();
                minds.TransferTo(ownerMind = minds.CreateMind(session.UserId), actor);
                if (presetId is "medical-assistance" or "vessel-critical")
                {
                    em.EnsureComponent<ActiveRadioComponent>(actor).Channels.Add(presetId == "medical-assistance" ? "Medical" : "Traffic");
                    em.EnsureComponent<IntrinsicRadioReceiverComponent>(actor);
                    inputEvidence.Add("Connected client actor has a native tuned radio receiver; no station telecom fixture added.");
                }
                if (presetId is "battle-flash" or "flash-unknown")
                {
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                        new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var foreign), Is.True);
                    externalGrid = foreign!.Value.Owner;
                    var query = em.AllEntityQueryEnumerator<TransformComponent>();
                    while (query.MoveNext(out var uid, out var transform))
                    {
                        if (transform.GridUid != externalGrid) continue;
                        if (em.TryGetComponent<KiasCoreComponent>(uid, out var foreignCore)) foreignCore.Enabled = false;
                        if (em.HasComponent<Content.Server._Mono.SpaceArtillery.Components.SpaceArtilleryComponent>(uid)
                            && em.HasComponent<GunComponent>(uid)
                            && em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WeaponTurretAK570"
                            && !externalGun.Valid) externalGun = uid;
                    }
                    Assert.That(externalGun.Valid, Is.True);
                    var detector = OnGrid<KiasWeaponFlashComponent>().First();
                    var transforms = em.System<SharedTransformSystem>();
                    var position = transforms.GetMapCoordinates(detector).Position
                        + transforms.GetWorldRotation(detector).ToWorldVec() * 100
                        - em.GetComponent<TransformComponent>(externalGun).LocalPosition;
                    transforms.SetWorldPosition(externalGrid, position);
                    if (presetId == "battle-flash")
                    {
                        em.EnsureComponent<ShuttleFactionComponent>(grid).Faction = "NanoTrasen";
                        em.EnsureComponent<ShuttleFactionComponent>(externalGrid).Faction = "Syndicate";
                        em.EnsureComponent<IFFComponent>(externalGrid);
                    }
                    else
                        em.RemoveComponent<IFFComponent>(externalGrid);
                }
                worldBefore = CaptureWorld();
                maps.InitializeMap(mapId);
                stimulusTick = timing.CurTick.Value;
                var chargedBatteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
                while (chargedBatteries.MoveNext(out var uid, out var battery, out var transform))
                {
                    if (transform.GridUid != grid && transform.GridUid != externalGrid) continue;
                    em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge, battery);
                }
            });
            if (presetId == "boot")
            {
                for (var step = 0; step < 600; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
            }
            else await pair.RunTicksSync(600);
            await pair.RunTicksSync(60);
            var safeCommands = commands.Count;
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(commands.Count, Is.EqualTo(safeCommands), "Stable native safe state must not repeatedly actuate the physical card.");
                crossChecks["stableNativeSafeStateDoesNotActuate"] = true;
            });
            if (presetId == "battle-impact")
            {
                await server.WaitAssertion(() =>
                {
                    foreach (var emitter in OnGrid<ShipShieldEmitterComponent>())
                        em.System<SharedPowerReceiverSystem>().SetPowerDisabled(emitter, true);
                    inputEvidence.Add("Native shield generator switched off through its power receiver so the shell can physically reach the hull.");
                });
                await pair.RunTicksSync(60);
            }
            if (presetId == "anomaly")
            {
                await server.WaitAssertion(() =>
                {
                    lifecycleTarget = em.SpawnEntity("AnomalyFlesh", ScannerInterior(KiasScannerModules.Spectral));
                    var anomaly = em.GetComponent<AnomalyComponent>(lifecycleTarget);
                    em.System<SharedAnomalySystem>().ChangeAnomalyStability(lifecycleTarget, -anomaly.Stability);
                });
                await pair.RunTicksSync((int) timing.TickRate * 12);
            }
            await server.WaitAssertion(() =>
            {
                ObserveSound();
                Assert.That(em.System<KiasSystem>().IsOnline(core), Is.True);
                Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
                if (presetId == "boot")
                {
                    inputConfirmed = true;
                    detectorSeen = true;
                    inputEvidence.Add("Real battery charge supplied the original power nets; core online; physical card booted.");
                    return;
                }
                stimulusTick = timing.CurTick.Value;
                startupSignals = signals.ToArray();
                runtimeBeforeStimulus = RuntimeSnapshot();
                signals.Clear(); commands.Clear(); programmedRecorders.Clear(); nativeEventSnapshot = null; commandedSpeakers.Clear();
                lightTargets.Clear(); speakerToneObserved.Clear(); graphTick = null; detectedTick = null; detectorSeen = false;
                foreach (var recorder in OnGrid<KiasRecorderComponent>()) em.GetComponent<KiasRecorderComponent>(recorder).Entries.Clear();
                worldBefore = CaptureWorld();
                if (presetId == "battle-impact")
                {
                    impactWall = OnGrid<KiasHullStructureComponent>().Where(uid => em.TryGetComponent<DamageableComponent>(uid, out var damageable)
                        && damageable.Damage.DamageDict.ContainsKey("Structural") && em.TryGetComponent<PhysicsComponent>(uid, out var body) && body.CanCollide
                        && em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WallPlastitanium")
                        .MaxBy(uid => em.GetComponent<TransformComponent>(uid).LocalPosition.X);
                    impactBefore = (float) em.GetComponent<DamageableComponent>(impactWall).TotalDamage;
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(impactWall).Position;
                    var projectile = em.SpawnEntity("90mmBulletAP", new EntityCoordinates(maps.GetMap(mapId), position + new Vector2(1, 0)));
                    impactProjectile = projectile;
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(maps.GetMap(mapId), new Vector2(3000, 3000)));
                    em.System<SharedGunSystem>().ShootProjectile(projectile, new Vector2(-1, 0), Vector2.Zero, weapon, speed: 40);
                    inputConfirmed = em.GetComponent<PhysicsComponent>(projectile).LinearVelocity.X < 0;
                    inputEvidence.Add($"Native projectile {projectile} physically travels towards real hull {impactWall}; initial damage {impactBefore}.");
                    inputEvidence.Add($"Hull prototype {em.GetComponent<MetaDataComponent>(impactWall).EntityPrototype?.ID}, map position {position}, projectile position {em.System<SharedTransformSystem>().GetMapCoordinates(projectile).Position}.");
                    var fixture = em.GetComponent<FixturesComponent>(projectile).Fixtures["projectile"];
                    var hits = em.System<SharedPhysicsSystem>().IntersectRay(mapId,
                        new CollisionRay(position + new Vector2(1, 0), new Vector2(-1, 0), fixture.CollisionMask), 3, projectile, false);
                    foreach (var hit in hits)
                        inputEvidence.Add($"Native projectile ray candidate {hit.HitEntity}, prototype {em.GetComponent<MetaDataComponent>(hit.HitEntity).EntityPrototype?.ID}, distance {hit.Distance}.");
                }
                else if (presetId is "battle-flash" or "flash-unknown")
                {
                    var disposition = em.System<KiasNavigationSystem>().Classify(grid, externalGrid);
                    Assert.That(disposition, Is.EqualTo(presetId == "battle-flash" ? KiasContactDisposition.Hostile : KiasContactDisposition.Unknown));
                    var gun = em.GetComponent<GunComponent>(externalGun);
                    var before = gun.LastFire;
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(externalGun).Position;
                    em.System<SharedGunSystem>().AttemptShoot(externalGun, externalGun, gun,
                        new EntityCoordinates(maps.GetMap(mapId), position + new Vector2(200, 200)));
                    inputConfirmed = gun.LastFire > before;
                    inputEvidence.Add($"Powered foreign Briar weapon {externalGun} actually fired; native IFF classification {disposition}.");
                }
                else if (presetId == "collision")
                {
                    var bounds = em.GetComponent<MapGridComponent>(grid).LocalAABB;
                    CreateForeignGrid(new Vector2(bounds.Right + 4, 0));
                    em.System<SharedPhysicsSystem>().SetLinearVelocity(externalGrid, new Vector2(-20, 0));
                    inputConfirmed = em.GetComponent<PhysicsComponent>(externalGrid).LinearVelocity.X < 0;
                    inputEvidence.Add($"Physical shuttle fixture {externalGrid} moving at 20 m/s towards Briar; no collision event is injected.");
                }
                else if (presetId == "proximity")
                {
                    var sensor = OnGrid<KiasProximityComponent>().First();
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(sensor).Position;
                    var range = em.GetComponent<KiasProximityComponent>(sensor).Range;
                    CreateForeignGrid(position + new Vector2(range + 5, 0));
                    em.System<SharedPhysicsSystem>().SetLinearVelocity(externalGrid, new Vector2(-20, 0));
                    inputConfirmed = em.GetComponent<PhysicsComponent>(externalGrid).LinearVelocity.X < 0;
                    inputEvidence.Add($"Native moving shuttle fixture {externalGrid} crosses sensor {sensor} range {range}.");
                }
                else if (presetId == "contact")
                {
                    CreateForeignGrid(new Vector2(3000, 3000));
                    em.System<ShuttleSystem>().FTLToCoordinates(externalGrid, em.GetComponent<ShuttleComponent>(externalGrid),
                        new EntityCoordinates(maps.GetMap(mapId), new Vector2(100, 100)), Angle.Zero, startupTime: 0.5f, hyperspaceTime: 0.5f);
                    inputConfirmed = em.HasComponent<FTLComponent>(externalGrid);
                    inputEvidence.Add($"Native FTLToCoordinates started for shuttle {externalGrid}; actual hyperspace transit must complete in Horizon coverage.");
                }
                else if (presetId == "arrival")
                {
                    var query = em.AllEntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                    EntityUid navigator = default;
                    while (query.MoveNext(out var uid, out _, out var transform))
                        if (transform.GridUid == grid) { navigator = uid; break; }
                    Assert.That(navigator.Valid, Is.True);
                    arrivalTarget = em.System<SharedTransformSystem>().GetMapCoordinates(grid).Position + new Vector2(0, 8);
                    steeringBoard = new NPCBlackboard();
                    steeringBoard.SetValue(NPCBlackboard.Owner, navigator);
                    steeringBoard.SetValue("ShipTargetCoordinates", new EntityCoordinates(maps.GetMap(mapId), arrivalTarget));
                    autopilot = new ShipMoveToOperator { Range = 1, RangeTolerance = 0.5f, InRangeMaxSpeed = 0.5f, AvoidCollisions = false, FinishOnCollide = false };
                    autopilot.Initialize(em.EntitySysManager);
                    autopilot.Startup(steeringBoard);
                    inputConfirmed = em.HasComponent<Content.Server._Mono.NPC.HTN.ShipSteererComponent>(navigator);
                    inputEvidence.Add($"Native ShipMoveToOperator on powered shuttle console {navigator} targets actual map position {arrivalTarget}.");
                }
                else if (presetId == "power-deficit")
                {
                    var tile = maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-5, 0)).Tile;
                    maps.SetTile((grid, em.GetComponent<MapGridComponent>(grid)), new Vector2i(40, 0), tile);
                    var coordinates = new EntityCoordinates(grid, new Vector2(40.5f, 0.5f));
                    em.SpawnEntity("CableHV", coordinates);
                    var consumer = lifecycleTarget = em.SpawnEntity("DebugConsumer", coordinates);
                    inputConfirmed = em.GetComponent<Content.Server.Power.Components.PowerConsumerComponent>(consumer).DrawRate > 0;
                    inputEvidence.Add($"Native electrical load fixture {consumer}: real PowerConsumer on an unsupplied isolated HV network, while main Briar APCs remain powered.");
                }
                else if (presetId == "hull-damage")
                {
                    var wall = OnGrid<KiasHullStructureComponent>().First(uid => em.TryGetComponent<DamageableComponent>(uid, out var d) && d.Damage.DamageDict.ContainsKey("Structural"));
                    damagedWall = wall;
                    var before = em.GetComponent<DamageableComponent>(wall).TotalDamage;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Structural", 50);
                    em.System<DamageableSystem>().TryChangeDamage(wall, damage);
                    inputConfirmed = em.GetComponent<DamageableComponent>(wall).TotalDamage > before;
                    inputEvidence.Add($"DamageableSystem changed real structure {wall} from {before} to {em.GetComponent<DamageableComponent>(wall).TotalDamage}.");
                }
                else if (presetId == "power-lost")
                {
                    var device = powerTarget = OnGrid<KiasRoomScannerComponent>().First(uid => em.System<KiasSystem>().IsOnline(uid));
                    Assert.That(em.System<SharedPowerReceiverSystem>().TogglePower(device, playSwitchSound: false, user: actor), Is.False);
                    inputConfirmed = em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(device).PowerDisabled;
                    inputEvidence.Add($"Native power button disabled online scanner {device}; waiting for power network update.");
                }
                else if (presetId == "local-threat")
                {
                    em.System<SharedTransformSystem>().SetCoordinates(actor, ScannerInterior(KiasScannerModules.Threat));
                    Assert.That(em.System<KiasCrewSystem>().HasCoverage(grid, actor, KiasScannerModules.Threat), Is.True);
                    var gun = lifecycleTarget = em.SpawnEntity("WeaponPistolMk58", em.GetComponent<TransformComponent>(actor).Coordinates);
                    inputConfirmed = em.System<SharedHandsSystem>().TryPickup(actor, gun);
                    inputEvidence.Add($"Tracked player actually picked up GunComponent entity {gun} inside Threat coverage.");
                }
                else if (presetId == "radiation")
                {
                    var scanner = OnGrid<KiasRoomScannerComponent>().First(uid => em.HasComponent<RadiationReceiverComponent>(uid));
                    var source = lifecycleTarget = em.SpawnEntity(null, em.GetComponent<TransformComponent>(scanner).Coordinates);
                    em.AddComponent<RadiationSourceComponent>(source).Intensity = 10;
                    inputConfirmed = em.GetComponent<RadiationSourceComponent>(source).Enabled;
                    inputEvidence.Add($"Real radiation source fixture {source}, intensity 10, colocated with receiver {scanner}; receiver is updated by native RadiationSystem.");
                }
                else if (presetId == "greeting")
                {
                    Assert.That(em.GetComponent<KiasProtocolComponent>(core).CaptainGreeted, Is.False);
                    var id = em.SpawnEntity("PassengerIDCard", em.GetComponent<TransformComponent>(actor).Coordinates);
                    Assert.That(em.System<SharedHandsSystem>().TryPickup(actor, id), Is.True);
                    Assert.That(em.System<KiasAccessSystem>().Claim(grid, actor, id), Is.True);
                    inputConfirmed = em.System<KiasAccessSystem>().CanConfigure(grid, actor, security: true);
                    inputEvidence.Add("Player claimed the unowned lab grid with a real held ID; captain recognition uses the actual access policy.");
                }
                else if (presetId == "anomaly")
                {
                    var anomaly = lifecycleTarget;
                    var excluded = new[] { "ElectricityAnomalyComponent", "ElectrifiedComponent", "EmpOnTriggerComponent", "GravityAnomalyComponent", "GravityWellComponent", "RadiationSourceComponent", "RandomWalkComponent" };
                    Assert.That(em.GetComponents(anomaly).Select(value => value.GetType().Name).Intersect(excluded), Is.Empty);
                    var component = em.GetComponent<AnomalyComponent>(anomaly);
                    Assert.That(em.System<KiasCrewSystem>().HasCoverage(grid, anomaly, KiasScannerModules.Spectral), Is.True);
                    em.System<SharedAnomalySystem>().ChangeAnomalyStability(anomaly, -component.Stability);
                    em.System<SharedAnomalySystem>().ChangeAnomalyStability(anomaly, component.GrowthThreshold + 0.1f);
                    inputConfirmed = component.Stability > component.GrowthThreshold;
                    inputEvidence.Add($"Native AnomalyFlesh without electricity, EMP, gravity, radiation source or random walk crossed GrowthThreshold on spectral fixture {anomaly}, stability {component.Stability}.");
                }
                else if (presetId is "crew-critical" or "crew-dead" or "boarding" or "medical-assistance" or "vessel-critical")
                {
                    Assert.That(em.System<KiasCrewSystem>().HasCoverage(grid, actor, KiasScannerModules.Biometric), Is.True);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Alive));
                    if (presetId is "boarding" or "medical-assistance" or "vessel-critical")
                    {
                        var crewServer = OnGrid<KiasCrewServerComponent>().First(uid => em.System<KiasSystem>().IsOnline(uid));
                        if (presetId == "boarding")
                        {
                            var id = em.SpawnEntity("PassengerIDCard", em.GetComponent<TransformComponent>(actor).Coordinates);
                            Assert.That(em.System<SharedHandsSystem>().TryPickup(actor, id), Is.True);
                            Assert.That(em.System<KiasAccessSystem>().Claim(grid, actor, id), Is.True);
                        }
                        var token = em.SpawnEntity("KiasCrewTransponder", em.GetComponent<TransformComponent>(actor).Coordinates);
                        Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(actor, token), Is.True);
                        em.EventBus.RaiseLocalEvent(crewServer, new InteractUsingEvent(actor, token, crewServer, em.GetComponent<TransformComponent>(crewServer).Coordinates));
                        var backpack = em.SpawnEntity("ClothingBackpack", em.GetComponent<TransformComponent>(actor).Coordinates);
                        Assert.That(em.System<InventorySystem>().TryEquip(actor, backpack, "back"), Is.True);
                        Assert.That(em.System<SharedStorageSystem>().Insert(backpack, token, out _, user: actor, playSound: false), Is.True);
                        Assert.That(em.System<KiasCrewSystem>().IsRegisteredPerson(grid, actor), Is.True);
                        Assert.That(em.System<KiasCrewSystem>().CrewUnavailable(grid), Is.False);
                        inputEvidence.Add("Owner registered a real held transponder using crew-server interaction, then stored it in an equipped backpack.");
                        if (presetId == "boarding")
                        {
                            var stranger = lifecycleTarget = em.SpawnEntity("BorgChassisGeneric", em.GetComponent<TransformComponent>(actor).Coordinates);
                            Assert.That(em.System<KiasCrewSystem>().IsKiasTrackedEntity(stranger), Is.True);
                            Assert.That(em.System<KiasCrewSystem>().HasUnknownAlongside(grid, actor), Is.True);
                            inputEvidence.Add($"Unknown living tracked person {stranger} alongside the registered crew member.");
                        }
                        if (presetId == "vessel-critical")
                        {
                            foreach (var wall in OnGrid<KiasHullStructureComponent>().Where(uid => em.TryGetComponent<DamageableComponent>(uid, out var d) && d.Damage.DamageDict.ContainsKey("Structural")))
                            {
                                var structural = new DamageSpecifier(); structural.DamageDict.Add("Structural", 50);
                                em.System<DamageableSystem>().TryChangeDamage(wall, structural);
                                var accumulated = em.GetComponent<KiasProtocolComponent>(core);
                                if (accumulated.RecentDamage >= accumulated.CriticalDamageThreshold) break;
                            }
                            var protocol = em.GetComponent<KiasProtocolComponent>(core);
                            Assert.That(protocol.RecentDamage, Is.GreaterThanOrEqualTo(protocol.CriticalDamageThreshold));
                            inputEvidence.Add($"Real structure damage accumulated {protocol.RecentDamage} within the native evidence window.");
                        }
                    }
                    var thresholds = em.GetComponent<MobThresholdsComponent>(actor).Thresholds;
                    var critical = thresholds.First(t => t.Value == MobState.Critical).Key;
                    var dead = thresholds.First(t => t.Value == MobState.Dead).Key;
                    var desired = presetId is "crew-dead" or "medical-assistance" or "vessel-critical" ? dead + 50 : (critical + dead) / 2;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Bloodloss", desired - em.GetComponent<DamageableComponent>(actor).TotalDamage);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    inputConfirmed = em.GetComponent<MobStateComponent>(actor).CurrentState == (presetId is "crew-dead" or "medical-assistance" or "vessel-critical" ? MobState.Dead : MobState.Critical);
                    inputEvidence.Add($"Real DamageableSystem actor state: {em.GetComponent<MobStateComponent>(actor).CurrentState}.");
                }
                else if (presetId == "shutdown")
                {
                    var key = lifecycleTarget = em.SpawnEntity("KiasMasterKey", em.GetComponent<TransformComponent>(core).Coordinates);
                    em.EventBus.RaiseLocalEvent(core, new InteractUsingEvent(actor, key, core, em.GetComponent<TransformComponent>(core).Coordinates));
                    inputConfirmed = !em.GetComponent<KiasCoreComponent>(core).Enabled;
                    inputEvidence.Add("Owner used real KiasMasterKey interaction; core disabled.");
                }
                else if (presetId is "battle-manual" or "quiet")
                {
                    em.System<SharedTransformSystem>().SetCoordinates(actor, em.GetComponent<TransformComponent>(console).Coordinates);
                    Assert.That(em.System<UserInterfaceSystem>().TryOpenUi(console, KiasUiKey.Key, actor), Is.True);
                    foreach (var uid in OnGrid<KiasLightFixtureComponent>())
                        if (em.HasComponent<PoweredLightComponent>(uid))
                            em.System<SharedPoweredLightSystem>().SetState(uid, presetId == "quiet");
                    if (presetId == "battle-manual")
                    {
                        em.System<KiasProtocolSystem>().ResetAlert(grid);
                        foreach (var uid in OnGrid<KiasDefenceComponent>()) em.System<KiasDefenceSystem>().SetAutomatic(uid, false);
                    }
                    inputConfirmed = true;
                    inputEvidence.Add("Owner opened actual management BUI; client will send native control message.");
                }
                else
                {
                    var monitor = OnGrid<AtmosMonitorComponent>().First(uid =>
                        em.GetComponent<AtmosMonitorComponent>(uid).TileGas != null
                        && em.GetComponent<TransformComponent>(uid).LocalPosition == new Vector2(-4.5f, 0.5f));
                    var tile = maps.TileIndicesFor((grid, em.GetComponent<MapGridComponent>(grid)), em.GetComponent<TransformComponent>(monitor).Coordinates);
                    var atmos = em.System<AtmosphereSystem>();
                    var gas = atmos.GetTileMixture((grid, null, null), null, tile, true)!;
                    gas.Clear(); gas.SetMoles(Gas.Oxygen, 20); gas.SetMoles(Gas.Nitrogen, 79);
                    gas.Temperature = 1000;
                    if (presetId is "fire" or "fire-clear")
                    {
                        gas.SetMoles(Gas.Plasma, 5);
                        atmos.HotspotExpose((grid, null), tile, 1000, 100);
                        inputConfirmed = atmos.IsHotspotActive(grid, tile);
                    }
                    else inputConfirmed = gas.Temperature >= 1000;
                    inputEvidence.Add($"Native atmosphere stimulus on monitor {monitor}, tile {tile}, temperature {gas.Temperature}, hotspot {atmos.IsHotspotActive(grid, tile)}.");
                }
            });
            if (presetId is "battle-manual" or "quiet")
            {
                var consoleNet = em.GetNetEntity(console);
                await pair.RunTicksSync(10);
                await pair.Client.WaitAssertion(() =>
                {
                    var client = pair.Client.ResolveDependency<IEntityManager>();
                    client.System<Robust.Client.GameObjects.UserInterfaceSystem>().ClientSendUiMessage(
                        client.GetEntity(consoleNet), KiasUiKey.Key, new KiasControlMessage { Quiet = presetId == "quiet" });
                });
            }
            // Берём реальные выходы динамиков после каждого тика, затем проверяем состояние устройств.
            for (var step = 0; step < (presetId is "medical-assistance" or "vessel-critical" ? 2100 : presetId is "arrival" or "contact" ? 1800 : presetId == "power-deficit" ? 600 : presetId == "radiation" ? 120 : 240); step++)
            {
                await pair.RunTicksSync(1);
                await server.WaitAssertion(() =>
                {
                    ObserveSound();
                    if (presetId is "medical-assistance" or "vessel-critical" && timing.CurTick.Value - stimulusTick < timing.TickRate * 29)
                        Assert.That(commands, Is.Empty, "MedicalHelp/Mayday must wait for actual crew unavailability; no early broadcast.");
                    if (presetId == "battle-impact" && step is 0 or 4 or 10 or 60)
                        inputEvidence.Add(em.EntityExists(impactProjectile)
                            ? $"Projectile tick +{step + 1}: position {em.System<SharedTransformSystem>().GetMapCoordinates(impactProjectile).Position}, velocity {em.GetComponent<PhysicsComponent>(impactProjectile).LinearVelocity}, collision {em.GetComponent<PhysicsComponent>(impactProjectile).CanCollide}."
                            : $"Projectile deleted by tick +{step + 1}.");
                    if (autopilot != null && !autopilotFinished && autopilot.Update(steeringBoard!, (float)timing.TickPeriod.TotalSeconds) == HTNOperatorStatus.Finished)
                    {
                        autopilotFinished = true;
                        arrivalDistance = Vector2.Distance(em.System<SharedTransformSystem>().GetMapCoordinates(grid).Position, arrivalTarget);
                        autopilot.PlanShutdown(steeringBoard!);
                    }
                });
                if (presetId == "local-threat" && commands.Count > 0 && commandedSpeakers.Count > 0
                    && speakerToneObserved.SetEquals(commandedSpeakers))
                    break;
            }
            if (presetId is "fire-clear" or "atmos-clear")
            {
                await server.WaitAssertion(() =>
                {
                    var atmos = em.System<AtmosphereSystem>();
                    foreach (var (tile, state) in em.GetComponent<GridAtmosphereComponent>(grid).Tiles.ToArray())
                    {
                        if (state.Space) continue;
                        var gas = atmos.GetTileMixture((grid, null, null), null, tile, false);
                        if (gas == null) continue;
                        gas.Clear(); gas.SetMoles(Gas.Oxygen, 21); gas.SetMoles(Gas.Nitrogen, 79); gas.Temperature = 293.15f;
                        atmos.HotspotExtinguish(grid, tile);
                    }
                    foreach (var uid in OnGrid<AtmosAlarmableComponent>())
                        em.System<AtmosAlarmableSystem>().ResetAllOnNetwork(uid);
                    inputEvidence.Add("Native hotspot extinguished and mixture restored; vanilla network reset after the real danger.");
                });
                for (var step = 0; step < 60; step++)
                {
                    await pair.RunTicksSync(10);
                    await server.WaitAssertion(ObserveSound);
                }
            }
            if (presetId is "medical-assistance" or "vessel-critical")
                await pair.Client.WaitAssertion(() =>
                {
                    Assert.That(receivedRadio.Count(message => message.Channel == ChatChannel.Radio), Is.EqualTo(1), "The actual client must receive exactly one native distress broadcast.");
                    crossChecks["nativeRadioBroadcastDeliveredToConnectedClient"] = true;
                });
            await server.WaitAssertion(() =>
            {
                var recorded = programmedRecorders.Any(uid => em.GetComponent<KiasRecorderComponent>(uid).Entries.Any(entry => entry.Contains("[P]")));
                var lighting = lightTargets.Count > 0 && lightTargets.All(uid =>
                    !em.HasComponent<PoweredLightComponent>(uid) || em.GetComponent<PoweredLightComponent>(uid).On == (presetId != "quiet"));
                var output = presetId == "quiet" ? lighting : recorded && (presetId is "shutdown" or "medical-assistance" ||
                    commandedSpeakers.Count > 0 && speakerToneObserved.SetEquals(commandedSpeakers));
                if (presetId is "battle-manual" or "battle-impact" or "battle-flash") output &= lighting
                    && em.GetComponent<KiasProtocolComponent>(core).Alert >= KiasAlert.Battle
                    && OnGrid<KiasDefenceComponent>().Any(uid => em.GetComponent<KiasDefenceComponent>(uid).PdcEnabled);
                if (presetId == "battle-impact")
                    inputConfirmed &= !em.EntityExists(impactWall) || (float) em.GetComponent<DamageableComponent>(impactWall).TotalDamage > impactBefore;
                if (presetId == "arrival") inputConfirmed &= autopilotFinished && arrivalDistance <= 1.5f;
                if (presetId == "contact") inputConfirmed &= em.GetComponent<TransformComponent>(externalGrid).MapID == mapId
                    && Vector2.Distance(em.System<SharedTransformSystem>().GetMapCoordinates(externalGrid).Position, new Vector2(100, 100)) < 100;
                if (presetId == "power-deficit") inputConfirmed &= em.System<KiasPowerSystem>().HasDeficit(grid);
                if (presetId == "radiation")
                    inputConfirmed &= OnGrid<RadiationReceiverComponent>().Any(uid => em.GetComponent<RadiationReceiverComponent>(uid).CurrentRadiation >= 1);
                if (presetId == "greeting") output &= em.GetComponent<KiasProtocolComponent>(core).CaptainGreeted;
                if (presetId == "medical-assistance") output &= em.GetComponent<KiasProtocolComponent>(core).MedicalAfter > timing.CurTime;
                if (presetId == "vessel-critical") output &= !string.IsNullOrWhiteSpace(em.GetComponent<KiasProtocolComponent>(core).MaydayReason);
                if (presetId == "shutdown") output &= !em.System<KiasControllerRuntimeSystem>().Running(card)
                    && !em.System<KiasSystem>().ActiveGrids.Contains(grid);
                var status = inputConfirmed && detectorSeen && commands.Count > 0 && output ? "PASS" : "FAIL";
                SaveResult(status);
                Assert.Multiple(() =>
                {
                    Assert.That(inputConfirmed, Is.True, "Real gameplay input must be observed.");
                    Assert.That(detectorSeen, Is.True, "Native detector path must emit the expected Automation port.");
                    Assert.That(commands, Is.Not.Empty, "The tested physical card must dispatch an actuator command.");
                    Assert.That(output, Is.True, "World state must change: recorder plus tone, or actual light state.");
                });
            });
            if (presetId == "collision")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void Approach(float speed)
                {
                    var transforms = em.System<SharedTransformSystem>();
                    var physics = em.System<SharedPhysicsSystem>();
                    physics.SetLinearVelocity(grid, Vector2.Zero);
                    physics.SetAngularVelocity(grid, 0);
                    physics.SetLinearVelocity(externalGrid, Vector2.Zero);
                    physics.SetAngularVelocity(externalGrid, 0);
                    transforms.SetWorldRotation(externalGrid, transforms.GetWorldRotation(grid));
                    var matrix = transforms.GetWorldMatrix(grid);
                    var bounds = em.GetComponent<MapGridComponent>(grid).LocalAABB;
                    transforms.SetWorldPosition(externalGrid, Vector2.Transform(new Vector2(bounds.Right + .1f, 0), matrix));
                    physics.SetLinearVelocity(externalGrid, Vector2.TransformNormal(new Vector2(-speed, 0), matrix));
                }
                await server.WaitAssertion(() => Approach(20));
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeSameGridCollisionInsideCooldownSuppressed"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => { speakerToneObserved.Clear(); Approach(20); });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeSameGridCollisionAfterCooldownRearmsWithSound"] = true;
                });
                await pair.RunTicksSync(660);
                var commandsBeforeSlowContact = commands.Count;
                var signalsBeforeSlowContact = signals.Count;
                var actualSlowContact = false;
                await server.WaitAssertion(() => Approach(.5f));
                for (var step = 0; step < (int) timing.TickRate * 2; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(() => actualSlowContact |= em.System<SharedPhysicsSystem>().GetContactingEntities(externalGrid).Contains(grid));
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(actualSlowContact, Is.True, "The below-threshold control must physically touch Briar.");
                    Assert.That(signals.Count, Is.EqualTo(signalsBeforeSlowContact));
                    Assert.That(commands.Count, Is.EqualTo(commandsBeforeSlowContact));
                    crossChecks["nativeSlowPhysicalCollisionBelowThresholdDoesNotActuate"] = true;
                });
            }
            if (presetId == "contact")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                async Task Jump(Vector2 destination)
                {
                    for (var second = 0; second < 40; second++)
                    {
                        var cooling = false;
                        await server.WaitAssertion(() => cooling = em.HasComponent<FTLComponent>(externalGrid));
                        if (!cooling) break;
                        await pair.RunTicksSync((int) timing.TickRate);
                    }
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.HasComponent<FTLComponent>(externalGrid), Is.False, "The previous native FTL cooldown must actually finish.");
                        em.System<ShuttleSystem>().FTLToCoordinates(externalGrid, em.GetComponent<ShuttleComponent>(externalGrid),
                            new EntityCoordinates(maps.GetMap(mapId), destination), Angle.Zero, startupTime: .5f, hyperspaceTime: .5f);
                        Assert.That(em.HasComponent<FTLComponent>(externalGrid), Is.True);
                    });
                    for (var step = 0; step < (int) timing.TickRate * 15; step++)
                    {
                        await pair.RunTicksSync(1);
                        await server.WaitAssertion(ObserveSound);
                    }
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.GetComponent<TransformComponent>(externalGrid).MapID, Is.EqualTo(mapId));
                        Assert.That(Vector2.Distance(em.System<SharedTransformSystem>().GetMapCoordinates(externalGrid).Position, destination), Is.LessThan(10));
                    });
                }
                var farPosition = new Vector2(10000, 10000);
                await Jump(farPosition);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeFtlOutsideHorizonCoverageDoesNotActuate"] = true;
                    speakerToneObserved.Clear();
                });
                await Jump(new Vector2(100, -100));
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeSameShuttleFtlReturnRearmsWithSound"] = true;
                });
                foreach (var disposition in new[] { KiasContactDisposition.Friendly, KiasContactDisposition.Neutral, KiasContactDisposition.Hostile })
                {
                    var beforeIffCommands = commands.Count;
                    var beforeIffSignals = signals.Count;
                    await server.WaitAssertion(() =>
                    {
                        em.EnsureComponent<IFFComponent>(externalGrid);
                        em.EnsureComponent<CompanyComponent>(grid).CompanyName = "None";
                        em.EnsureComponent<CompanyComponent>(externalGrid).CompanyName = "None";
                        em.EnsureComponent<ShuttleFactionComponent>(grid).Faction = "NanoTrasen";
                        em.EnsureComponent<ShuttleFactionComponent>(externalGrid).Faction = disposition switch
                        {
                            KiasContactDisposition.Friendly => "NanoTrasen",
                            KiasContactDisposition.Hostile => "Syndicate",
                            _ => "None"
                        };
                        Assert.That(em.System<KiasNavigationSystem>().Classify(grid, externalGrid), Is.EqualTo(disposition));
                        speakerToneObserved.Clear();
                    });
                    await Jump(new Vector2(100, 100));
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.System<KiasNavigationSystem>().Classify(grid, externalGrid), Is.EqualTo(disposition));
                        Assert.That(signals.Count, Is.GreaterThan(beforeIffSignals));
                        Assert.That(commands.Count, Is.GreaterThan(beforeIffCommands), "The saved contact graph reports all IFF classes; it has no disposition filter.");
                        Assert.That(speakerToneObserved, Is.Not.Empty);
                        crossChecks[$"nativeFtl{disposition}ContactMatchesSavedGraph"] = true;
                    });
                }
            }
            if (presetId == "arrival")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                await pair.RunTicksSync((int) timing.TickRate * 12);
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "An autopilot remaining at its destination must not repeatedly announce arrival.");
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    EntityUid navigator = default;
                    var query = em.AllEntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                    while (query.MoveNext(out var uid, out _, out var transform))
                        if (transform.GridUid == grid) { navigator = uid; break; }
                    Assert.That(navigator.Valid, Is.True);
                    arrivalTarget = em.System<SharedTransformSystem>().GetMapCoordinates(grid).Position + new Vector2(0, 8);
                    steeringBoard = new NPCBlackboard();
                    steeringBoard.SetValue(NPCBlackboard.Owner, navigator);
                    steeringBoard.SetValue("ShipTargetCoordinates", new EntityCoordinates(maps.GetMap(mapId), arrivalTarget));
                    autopilot = new ShipMoveToOperator { Range = 1, RangeTolerance = 0.5f, InRangeMaxSpeed = 0.5f, AvoidCollisions = false, FinishOnCollide = false };
                    autopilot.Initialize(em.EntitySysManager);
                    autopilot.Startup(steeringBoard);
                    autopilotFinished = false;
                    speakerToneObserved.Clear();
                });
                for (var step = 0; step < (int) timing.TickRate * 60; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(() =>
                    {
                        ObserveSound();
                        if (!autopilotFinished && autopilot!.Update(steeringBoard!, (float) timing.TickPeriod.TotalSeconds) == HTNOperatorStatus.Finished)
                        {
                            autopilotFinished = true;
                            arrivalDistance = Vector2.Distance(em.System<SharedTransformSystem>().GetMapCoordinates(grid).Position, arrivalTarget);
                            autopilot.PlanShutdown(steeringBoard!);
                        }
                    });
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(autopilotFinished, Is.True);
                    Assert.That(arrivalDistance, Is.LessThanOrEqualTo(1.5f));
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeAutopilotRestDoesNotRepeatAndNewDestinationRearms"] = true;
                });
            }
            if (presetId == "proximity")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void MoveContact(bool inside, Vector2 direction = default)
                {
                    if (direction == Vector2.Zero) direction = Vector2.UnitX;
                    var sensor = OnGrid<KiasProximityComponent>().First();
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(sensor).Position;
                    var range = em.GetComponent<KiasProximityComponent>(sensor).Range;
                    em.System<SharedPhysicsSystem>().SetLinearVelocity(externalGrid, Vector2.Zero);
                    em.System<SharedTransformSystem>().SetWorldPosition(externalGrid, position + direction * (range + (inside ? -20 : 50)));
                }
                async Task ObserveContact(bool inside)
                {
                    for (var step = 0; step < (int) timing.TickRate * 2; step++)
                    {
                        await pair.RunTicksSync(1);
                        var observed = false;
                        await server.WaitAssertion(() => observed = inside
                            ? OnGrid<KiasProximityComponent>().Any(uid => em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid))
                            : OnGrid<KiasProximityComponent>().All(uid => !em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid)));
                        if (observed) return;
                    }
                    Assert.Fail($"Native proximity did not observe {(inside ? "entry" : "departure")} within two scan intervals.");
                }
                await server.WaitAssertion(() => MoveContact(false));
                await ObserveContact(false);
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<KiasProximityComponent>().All(uid => !em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid)), Is.True);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    MoveContact(true);
                });
                await ObserveContact(true);
                await pair.RunTicksSync(2);
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<KiasProximityComponent>().Any(uid => em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid)), Is.True);
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeProximityExitAndReturnInsideCooldownSuppressed"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => MoveContact(false));
                await pair.RunTicksSync((int) timing.TickRate * 2);
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<KiasProximityComponent>().All(uid => !em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid)), Is.True,
                        "The native scanner must actually observe departure before a return can rearm.");
                    speakerToneObserved.Clear(); MoveContact(true);
                });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeProximityReturnAfterCooldownRearmsWithSound"] = true;
                });
                foreach (var disposition in new[] { KiasContactDisposition.Friendly, KiasContactDisposition.Neutral, KiasContactDisposition.Hostile })
                {
                    await pair.RunTicksSync(660);
                    var beforeIffCommands = commands.Count;
                    var beforeIffSignals = signals.Count;
                    var direction = disposition switch
                    {
                        KiasContactDisposition.Friendly => Vector2.UnitY,
                        KiasContactDisposition.Neutral => -Vector2.UnitX,
                        _ => -Vector2.UnitY
                    };
                    await server.WaitAssertion(() =>
                    {
                        em.EnsureComponent<IFFComponent>(externalGrid);
                        em.EnsureComponent<CompanyComponent>(grid).CompanyName = "None";
                        em.EnsureComponent<CompanyComponent>(externalGrid).CompanyName = "None";
                        em.EnsureComponent<ShuttleFactionComponent>(grid).Faction = "NanoTrasen";
                        em.EnsureComponent<ShuttleFactionComponent>(externalGrid).Faction = disposition switch
                        {
                            KiasContactDisposition.Friendly => "NanoTrasen",
                            KiasContactDisposition.Hostile => "Syndicate",
                            _ => "None"
                        };
                        em.System<SharedTransformSystem>().SetWorldRotation(grid, Angle.FromDegrees(90));
                        MoveContact(false, direction);
                        foreach (var uid in OnGrid<BatteryComponent>())
                            em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                        Assert.That(em.System<KiasNavigationSystem>().Classify(grid, externalGrid), Is.EqualTo(disposition));
                        speakerToneObserved.Clear();
                    });
                    await pair.RunTicksSync((int) timing.TickRate * 2);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(OnGrid<KiasProximityComponent>().All(uid => !em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(externalGrid)), Is.True);
                        MoveContact(true, direction);
                    });
                    for (var step = 0; step < 120; step++)
                    {
                        await pair.RunTicksSync(1);
                        await server.WaitAssertion(ObserveSound);
                    }
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(signals.Count, Is.GreaterThan(beforeIffSignals));
                        Assert.That(commands.Count, Is.GreaterThan(beforeIffCommands), "The saved proximity graph reports all IFF classes.");
                        Assert.That(speakerToneObserved, Is.Not.Empty);
                        Assert.That(OnGrid<KiasDeviceComponent>().All(em.System<KiasSystem>().IsOnline), Is.True);
                        crossChecks[$"nativeRotatedProximity{disposition}FromDifferentDirection"] = true;
                    });
                }
            }
            if (presetId == "battle-impact")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                EntityUid repeatWall = default;
                float repeatDamage = 0;
                void ShootHull(bool miss = false)
                {
                    repeatWall = OnGrid<KiasHullStructureComponent>().Where(uid => em.TryGetComponent<DamageableComponent>(uid, out var damageable)
                        && damageable.Damage.DamageDict.ContainsKey("Structural") && em.TryGetComponent<PhysicsComponent>(uid, out var body) && body.CanCollide
                        && em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WallPlastitanium")
                        .MaxBy(uid => em.GetComponent<TransformComponent>(uid).LocalPosition.X);
                    repeatDamage = (float) em.GetComponent<DamageableComponent>(repeatWall).TotalDamage;
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(repeatWall).Position;
                    var projectile = em.SpawnEntity("90mmBulletAP", new EntityCoordinates(maps.GetMap(mapId), position + new Vector2(miss ? 10 : 1, 0)));
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(maps.GetMap(mapId), new Vector2(3000, 3000)));
                    em.System<SharedGunSystem>().ShootProjectile(projectile, new Vector2(miss ? 1 : -1, 0), Vector2.Zero, weapon, speed: 40);
                    Assert.That(em.GetComponent<PhysicsComponent>(projectile).LinearVelocity.X, miss ? Is.GreaterThan(0) : Is.LessThan(0));
                }
                await server.WaitAssertion(() => ShootHull());
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(!em.EntityExists(repeatWall) || (float) em.GetComponent<DamageableComponent>(repeatWall).TotalDamage > repeatDamage, Is.True);
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeHullImpactRepeatInsideCooldownSuppressed"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => { speakerToneObserved.Clear(); ShootHull(); });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(!em.EntityExists(repeatWall) || (float) em.GetComponent<DamageableComponent>(repeatWall).TotalDamage > repeatDamage, Is.True);
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeHullImpactRearmsWithNewSound"] = true;
                });
                await pair.RunTicksSync(660);
                var beforeMiss = commands.Count;
                var signalsBeforeMiss = signals.Count;
                await server.WaitAssertion(() => ShootHull(miss: true));
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(signalsBeforeMiss));
                    Assert.That(commands.Count, Is.EqualTo(beforeMiss));
                    crossChecks["nativeProjectileMissDoesNotActuateHullImpact"] = true;
                });
            }
            if (presetId == "hull-damage")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                await server.WaitAssertion(() =>
                {
                    var heal = new DamageSpecifier(); heal.DamageDict.Add("Structural", -5);
                    em.System<DamageableSystem>().TryChangeDamage(damagedWall, heal);
                });
                await pair.RunTicksSync(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Healing must not emit a damage alarm.");
                    crossChecks["healingDoesNotAlarm"] = true;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Structural", 50);
                    em.System<DamageableSystem>().TryChangeDamage(damagedWall, damage);
                });
                await pair.RunTicksSync(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals), "Repeat damage must reach the real detector.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "Same structure within cooldown must not actuate again.");
                    crossChecks["repeatInsideCooldownSuppressed"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() =>
                {
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Structural", 50);
                    em.System<DamageableSystem>().TryChangeDamage(damagedWall, damage);
                });
                for (var step = 0; step < 60; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands), "After cooldown, real damage must actuate again.");
                    crossChecks["repeatAfterCooldownRearmed"] = true;
                });
            }
            if (presetId == "power-lost")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                await server.WaitAssertion(() => em.System<SharedPowerReceiverSystem>().SetPowerDisabled(powerTarget, false));
                await pair.RunTicksSync(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasSystem>().IsOnline(powerTarget), Is.True);
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Restoring native power must not emit a loss alarm.");
                    em.System<SharedPowerReceiverSystem>().SetPowerDisabled(powerTarget, true);
                });
                await pair.RunTicksSync(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "Native repeated power loss within cooldown must not actuate.");
                    em.System<SharedPowerReceiverSystem>().SetPowerDisabled(powerTarget, false);
                    crossChecks["nativePowerRestoreAndRepeatInsideCooldown"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasSystem>().IsOnline(powerTarget), Is.True);
                    em.System<SharedPowerReceiverSystem>().SetPowerDisabled(powerTarget, true);
                });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands), "Native repeated power loss must rearm after cooldown.");
                    crossChecks["nativePowerLossRearmedAfterCooldown"] = true;
                });
            }
            if (presetId == "crew-critical")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void RecoverCrew()
                {
                    var heal = new DamageSpecifier();
                    foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict)
                        heal.DamageDict.Add(type, -amount);
                    em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Alive));
                }
                void InjureCrew()
                {
                    var thresholds = em.GetComponent<MobThresholdsComponent>(actor).Thresholds;
                    var critical = thresholds.First(t => t.Value == MobState.Critical).Key;
                    var dead = thresholds.First(t => t.Value == MobState.Dead).Key;
                    var damage = new DamageSpecifier();
                    damage.DamageDict.Add("Bloodloss", (critical + dead) / 2 - em.GetComponent<DamageableComponent>(actor).TotalDamage);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Critical));
                }
                await server.WaitAssertion(() =>
                {
                    RecoverCrew();
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Native recovery must not emit a critical alarm.");
                    InjureCrew();
                });
                await pair.RunTicksSync(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals), "Native repeat critical transition must reach the detector.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "Repeat critical transition inside cooldown must not actuate.");
                    RecoverCrew();
                    crossChecks["nativeCrewRecoveryAndRepeatInsideCooldown"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(InjureCrew);
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands), "Native critical transition after cooldown must actuate.");
                    crossChecks["nativeCrewCriticalRearmedAfterCooldown"] = true;
                });
            }
            if (presetId is "battle-manual" or "quiet")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                var consoleNet = em.GetNetEntity(console);
                async Task Control(bool reset = false)
                {
                    await pair.Client.WaitAssertion(() =>
                    {
                        var client = pair.Client.ResolveDependency<IEntityManager>();
                        client.System<Robust.Client.GameObjects.UserInterfaceSystem>().ClientSendUiMessage(
                            client.GetEntity(consoleNet), KiasUiKey.Key, new KiasControlMessage { Quiet = presetId == "quiet", Reset = reset });
                    });
                    await pair.RunTicksSync((int) timing.TickRate);
                }
                await Control();
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeManagementRepeatInsideCooldownSuppressed"] = true;
                });
                if (presetId == "battle-manual") await Control(reset: true);
                await server.WaitAssertion(() =>
                {
                    if (presetId == "battle-manual") Assert.That(em.GetComponent<KiasProtocolComponent>(core).Alert, Is.EqualTo(KiasAlert.Normal));
                    foreach (var uid in OnGrid<KiasLightFixtureComponent>())
                        if (em.HasComponent<PoweredLightComponent>(uid)) em.System<SharedPoweredLightSystem>().SetState(uid, presetId == "quiet");
                    if (presetId == "battle-manual")
                        foreach (var uid in OnGrid<KiasDefenceComponent>()) em.System<KiasDefenceSystem>().SetAutomatic(uid, false);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => speakerToneObserved.Clear());
                await Control();
                for (var step = 0; step < 60; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(lightTargets.All(uid => em.GetComponent<PoweredLightComponent>(uid).On == (presetId != "quiet")), Is.True);
                    if (presetId == "battle-manual")
                    {
                        Assert.That(speakerToneObserved, Is.Not.Empty);
                        Assert.That(em.GetComponent<KiasProtocolComponent>(core).Alert, Is.GreaterThanOrEqualTo(KiasAlert.Battle));
                        Assert.That(OnGrid<KiasDefenceComponent>().Any(uid => em.GetComponent<KiasDefenceComponent>(uid).PdcEnabled), Is.True);
                    }
                    crossChecks["nativeManagementRestoreAndRearmWorldOutputs"] = true;
                });
            }
            if (presetId == "shutdown")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                EntityUid mutedSpeaker = default;
                await server.WaitAssertion(() =>
                {
                    var wall = OnGrid<KiasHullStructureComponent>().First(uid => em.TryGetComponent<DamageableComponent>(uid, out var component) && component.Damage.DamageDict.ContainsKey("Structural"));
                    var before = em.GetComponent<DamageableComponent>(wall).TotalDamage;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Structural", 50);
                    em.System<DamageableSystem>().TryChangeDamage(wall, damage);
                    Assert.That(em.GetComponent<DamageableComponent>(wall).TotalDamage, Is.GreaterThan(before));
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.False);
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "An offline core must not dispatch world effects.");
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    em.EventBus.RaiseLocalEvent(core, new InteractUsingEvent(actor, lifecycleTarget, core, em.GetComponent<TransformComponent>(core).Coordinates));
                });
                await pair.RunTicksSync((int) timing.TickRate * 3);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "Boot must not be mistaken for Shutdown.");
                    speakerToneObserved.Clear();
                    em.EventBus.RaiseLocalEvent(core, new InteractUsingEvent(actor, lifecycleTarget, core, em.GetComponent<TransformComponent>(core).Coordinates));
                    ObserveSound();
                    mutedSpeaker = commandedSpeakers.First(uid => em.GetComponent<KiasSpeakerComponent>(uid).Tone is { } tone && em.EntityExists(tone));
                    em.System<SharedPowerReceiverSystem>().SetPowerDisabled(mutedSpeaker, true);
                });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.False);
                    Assert.That(em.System<SharedPowerReceiverSystem>().IsPowered(mutedSpeaker), Is.False);
                    Assert.That(em.GetComponent<KiasSpeakerComponent>(mutedSpeaker).Tone is { } tone && em.EntityExists(tone), Is.False, "The final tone must stop if its actual speaker loses power.");
                    crossChecks["nativeShutdownRejectsOfflineDamageAndRearmsOnNewLifecycle"] = true;
                    crossChecks["nativeFinalShutdownToneStillRequiresSpeakerPower"] = true;
                });
            }
            if (presetId is "fire" or "atmosphere" or "fire-clear" or "atmos-clear")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void RestoreAtmosphere()
                {
                    var atmos = em.System<AtmosphereSystem>();
                    foreach (var (tile, state) in em.GetComponent<GridAtmosphereComponent>(grid).Tiles.ToArray())
                    {
                        if (state.Space) continue;
                        var gas = atmos.GetTileMixture((grid, null, null), null, tile, false);
                        if (gas == null) continue;
                        gas.Clear(); gas.SetMoles(Gas.Oxygen, 21); gas.SetMoles(Gas.Nitrogen, 79); gas.Temperature = 293.15f;
                        atmos.HotspotExtinguish(grid, tile);
                    }
                    foreach (var uid in OnGrid<AtmosAlarmableComponent>()) em.System<AtmosAlarmableSystem>().ResetAllOnNetwork(uid);
                }
                void RaiseHazard()
                {
                    var monitor = OnGrid<AtmosMonitorComponent>().First(uid => em.GetComponent<TransformComponent>(uid).LocalPosition == new Vector2(-4.5f, 0.5f));
                    var tile = maps.TileIndicesFor((grid, em.GetComponent<MapGridComponent>(grid)), em.GetComponent<TransformComponent>(monitor).Coordinates);
                    var atmos = em.System<AtmosphereSystem>();
                    var gas = atmos.GetTileMixture((grid, null, null), null, tile, true)!;
                    gas.Clear(); gas.SetMoles(Gas.Oxygen, 20); gas.SetMoles(Gas.Nitrogen, 79); gas.Temperature = 1000;
                    if (presetId is "fire" or "fire-clear")
                    {
                        gas.SetMoles(Gas.Plasma, 5);
                        atmos.HotspotExpose((grid, null), tile, 1000, 100);
                        Assert.That(atmos.IsHotspotActive(grid, tile), Is.True);
                    }
                }
                await server.WaitAssertion(RestoreAtmosphere);
                await pair.RunTicksSync((int) timing.TickRate * 6);
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<FireAlarmComponent>().All(uid => em.GetComponent<AtmosAlarmableComponent>(uid).LastAlarmState != Content.Shared.Atmos.Monitor.AtmosAlarmType.Danger), Is.True);
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Normal/reset state must not repeat the card's alarm or clear signal.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    inputEvidence.Add("Restored fire alarms: " + string.Join("; ", OnGrid<FireAlarmComponent>().Select(uid => $"{uid}: {em.GetComponent<AtmosAlarmableComponent>(uid).LastAlarmState}, online={em.System<KiasSystem>().IsOnline(uid)}")));
                    speakerToneObserved.Clear();
                    RaiseHazard();
                });
                for (var step = 0; step < timing.TickRate * 6; step++)
                {
                    await pair.RunTicksSync(1);
                    if (step > 0 && step % timing.TickRate == 0) await server.WaitAssertion(RaiseHazard);
                    await server.WaitAssertion(ObserveSound);
                }
                if (presetId is "fire-clear" or "atmos-clear")
                {
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(commands.Count, Is.EqualTo(firstCommands), "Danger must not be mistaken for a clear transition.");
                        RestoreAtmosphere();
                    });
                    for (var step = 0; step < timing.TickRate * 6; step++)
                    {
                        await pair.RunTicksSync(1);
                        await server.WaitAssertion(ObserveSound);
                    }
                }
                await server.WaitAssertion(() =>
                {
                    inputEvidence.Add("Repeated fire alarms: " + string.Join("; ", OnGrid<FireAlarmComponent>().Select(uid => $"{uid}: {em.GetComponent<AtmosAlarmableComponent>(uid).LastAlarmState}, online={em.System<KiasSystem>().IsOnline(uid)}")));
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty, "The repeated real hazard/clear must produce a new actual speaker tone.");
                    crossChecks["nativeEnvironmentalRestoreAndRearmWithNewSound"] = true;
                });
            }
            if (presetId == "boarding")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void Recover()
                {
                    var heal = new DamageSpecifier();
                    foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict) heal.DamageDict.Add(type, -amount);
                    em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Alive));
                }
                void Injure()
                {
                    var thresholds = em.GetComponent<MobThresholdsComponent>(actor).Thresholds;
                    var critical = thresholds.First(threshold => threshold.Value == MobState.Critical).Key;
                    var dead = thresholds.First(threshold => threshold.Value == MobState.Dead).Key;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Bloodloss", (critical + dead) / 2);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Critical));
                }
                await server.WaitAssertion(() =>
                {
                    em.System<SharedTransformSystem>().SetCoordinates(lifecycleTarget, new EntityCoordinates(grid, new Vector2(40.5f, 0.5f)));
                    Assert.That(em.System<KiasCrewSystem>().HasUnknownAlongside(grid, actor), Is.False);
                    Recover(); Injure();
                });
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Critical registered crew without an intruder must not emit Boarding.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    Recover();
                    em.System<SharedTransformSystem>().SetCoordinates(lifecycleTarget, em.GetComponent<TransformComponent>(actor).Coordinates);
                    Assert.That(em.System<KiasCrewSystem>().HasUnknownAlongside(grid, actor), Is.True);
                    Injure();
                });
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    Recover();
                    crossChecks["nativeIntruderExitAndRepeatInsideCooldown"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(Injure);
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    crossChecks["nativeBoardingRearmedAfterCooldown"] = true;
                });
            }
            if (presetId == "power-deficit")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                var coordinates = default(EntityCoordinates);
                await server.WaitAssertion(() =>
                {
                    coordinates = em.GetComponent<TransformComponent>(lifecycleTarget).Coordinates;
                    em.DeleteEntity(lifecycleTarget);
                });
                await pair.RunTicksSync((int) timing.TickRate * 6);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasPowerSystem>().HasDeficit(grid), Is.False, "Removing the actual load must restore the network.");
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    lifecycleTarget = em.SpawnEntity("DebugConsumer", coordinates);
                });
                for (var step = 0; step < timing.TickRate * 6; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasPowerSystem>().HasDeficit(grid), Is.True);
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    crossChecks["nativeElectricalNetworkRecoveryAndRearm"] = true;
                });
            }
            if (presetId == "boot")
            {
                var firstCommands = commands.Count;
                await server.WaitAssertion(() => em.System<SharedPowerReceiverSystem>().SetPowerDisabled(core, true));
                await pair.RunTicksSync((int) timing.TickRate * 2);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.False);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    em.System<SharedPowerReceiverSystem>().SetPowerDisabled(core, false);
                });
                for (var step = 0; step < timing.TickRate * 6; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands * 2), "A real new core power cycle must boot once.");
                });
                await pair.RunTicksSync((int) timing.TickRate * 12);
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.EqualTo(firstCommands * 2), "A powered core must not endlessly repeat Boot after cooldown.");
                    crossChecks["nativeCorePowerCycleBootsOnceAndStableDoesNotRepeat"] = true;
                });
            }
            if (presetId == "crew-dead")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals, Has.Count.EqualTo(1), "A real death transition must emit once.");
                    var damage = new DamageSpecifier();
                    damage.DamageDict.Add("Bloodloss", 10);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Dead));
                });
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "Further damage to the same corpse must not repeat a death transition.");
                    crossChecks["nativeCorpseDamageDoesNotRepeatDeath"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() =>
                {
                    var heal = new DamageSpecifier();
                    foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict) heal.DamageDict.Add(type, -amount);
                    em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Dead), "Healing a corpse alone must not revive it.");
                    var rescuer = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(actor).Coordinates);
                    var defib = em.SpawnEntity("Defibrillator", em.GetComponent<TransformComponent>(rescuer).Coordinates);
                    Assert.That(em.System<SharedHandsSystem>().TryPickup(rescuer, defib), Is.True);
                    Assert.That(em.System<ItemToggleSystem>().TryActivate(defib, rescuer), Is.True);
                    Assert.That(em.System<DefibrillatorSystem>().TryStartZap(defib, actor, rescuer), Is.True);
                    inputEvidence.Add("A real rescuer used a powered Defibrillator through its native timed interaction; no direct MobState change.");
                });
                await pair.RunTicksSync((int) timing.TickRate * 5);
                await server.WaitAssertion(()
                    => Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.Not.EqualTo(MobState.Dead), "The native defibrillator must actually revive the corpse."));
                await server.WaitAssertion(() =>
                {
                    var heal = new DamageSpecifier();
                    foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict) heal.DamageDict.Add(type, -amount);
                    em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Alive));
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    var dead = em.GetComponent<MobThresholdsComponent>(actor).Thresholds.First(threshold => threshold.Value == MobState.Dead).Key;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Bloodloss", dead + 50);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Dead));
                });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals + 1));
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    crossChecks["nativeDefibrillatorRevivalAndSecondDeathRearms"] = true;
                });
            }
            if (presetId is "anomaly" or "local-threat" or "radiation")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                var samplingSeconds = presetId == "anomaly" ? 1 : 2;
                var samplingTicks = (int) Math.Ceiling(samplingSeconds / timing.TickPeriod.TotalSeconds) + 1;
                void SetHazard(bool active)
                {
                    if (presetId == "anomaly")
                    {
                        var component = em.GetComponent<AnomalyComponent>(lifecycleTarget);
                        var desired = active ? component.GrowthThreshold + 0.1f : 0;
                        em.System<SharedAnomalySystem>().ChangeAnomalyStability(lifecycleTarget, desired - component.Stability);
                    }
                    else if (presetId == "local-threat")
                    {
                        Assert.That(active ? em.System<SharedHandsSystem>().TryPickup(actor, lifecycleTarget)
                            : em.System<SharedHandsSystem>().TryDrop(actor, lifecycleTarget), Is.True);
                    }
                    else em.System<RadiationSystem>().SetSourceEnabled(lifecycleTarget, active);
                }
                await server.WaitAssertion(() => SetHazard(false));
                await pair.RunTicksSync(samplingTicks);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.EqualTo(firstSignals), "Clearing the actual hazard must not raise a new alarm.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    if (presetId == "radiation")
                        Assert.That(OnGrid<RadiationReceiverComponent>().All(uid => em.GetComponent<RadiationReceiverComponent>(uid).CurrentRadiation < 1), Is.True);
                    SetHazard(true);
                });
                await pair.RunTicksSync(samplingTicks);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals), "The repeated real hazard must reach its detector.");
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "The same hazard inside cooldown must not actuate twice.");
                    SetHazard(false);
                    crossChecks["nativeHazardClearAndRepeatInsideCooldown"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => SetHazard(true));
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands), "The same native hazard must rearm after cooldown.");
                    crossChecks["nativeHazardRearmedAfterCooldown"] = true;
                });
                if (presetId is "anomaly" or "radiation")
                {
                    var beforeOutsideCommands = commands.Count;
                    var beforeOutsideSignals = signals.Count;
                    await server.WaitAssertion(() =>
                    {
                        em.System<SharedTransformSystem>().SetCoordinates(lifecycleTarget,
                            new EntityCoordinates(maps.GetMap(mapId), new Vector2(10000, 10000)));
                        Assert.That(em.GetComponent<TransformComponent>(lifecycleTarget).GridUid, Is.Not.EqualTo(grid));
                        SetHazard(true);
                    });
                    await pair.RunTicksSync((int) timing.TickRate * 4);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(signals.Count, Is.EqualTo(beforeOutsideSignals), "An active native source outside scanner coverage must not emit a local hazard signal.");
                        Assert.That(commands.Count, Is.EqualTo(beforeOutsideCommands));
                        if (presetId == "radiation")
                            Assert.That(OnGrid<RadiationReceiverComponent>().All(uid => em.GetComponent<RadiationReceiverComponent>(uid).CurrentRadiation < 1), Is.True);
                        crossChecks["nativeActiveHazardOutsideCoverageDoesNotActuate"] = true;
                    });
                }
            }
            if (presetId == "greeting")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                var originalPosition = default(EntityCoordinates);
                await server.WaitAssertion(() =>
                {
                    originalPosition = em.GetComponent<TransformComponent>(actor).Coordinates;
                    em.System<SharedTransformSystem>().SetCoordinates(actor, new EntityCoordinates(grid, new Vector2(40.5f, 0.5f)));
                    Assert.That(em.System<KiasCrewSystem>().HasCoverage(grid, actor, KiasScannerModules.Motion), Is.False);
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => em.System<SharedTransformSystem>().SetCoordinates(actor, originalPosition));
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.GetComponent<KiasProtocolComponent>(core).CaptainGreeted, Is.True);
                    Assert.That(em.System<KiasCrewSystem>().HasCoverage(grid, actor, KiasScannerModules.Motion), Is.True);
                    Assert.That(signals.Count, Is.EqualTo(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands), "The same captain returning after cooldown must not be greeted twice.");
                    crossChecks["nativeCaptainReturnDoesNotRepeatGreeting"] = true;
                });
            }
            if (presetId is "battle-flash" or "flash-unknown")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                void FireAgain()
                {
                    var gun = em.GetComponent<GunComponent>(externalGun);
                    var before = gun.LastFire;
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(externalGun).Position;
                    em.System<SharedGunSystem>().AttemptShoot(externalGun, externalGun, gun,
                        new EntityCoordinates(maps.GetMap(mapId), position + new Vector2(200, 200)));
                    Assert.That(gun.LastFire, Is.GreaterThan(before), "The repeated foreign shot must actually fire.");
                }
                await server.WaitAssertion(FireAgain);
                await pair.RunTicksSync((int) timing.TickRate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeForeignShotRepeatInsideCooldownSuppressed"] = true;
                });
                await pair.RunTicksSync(660);
                await server.WaitAssertion(() => { speakerToneObserved.Clear(); FireAgain(); });
                for (var step = 0; step < 120; step++)
                {
                    await pair.RunTicksSync(1);
                    await server.WaitAssertion(ObserveSound);
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(commands.Count, Is.GreaterThan(firstCommands));
                    Assert.That(speakerToneObserved, Is.Not.Empty);
                    crossChecks["nativeForeignShotRearmedWithNewSound"] = true;
                });
                if (presetId == "flash-unknown")
                {
                    var beforeFriendly = commands.Count;
                    await server.WaitAssertion(() =>
                    {
                        em.EnsureComponent<ShuttleFactionComponent>(grid).Faction = "NanoTrasen";
                        em.EnsureComponent<ShuttleFactionComponent>(externalGrid).Faction = "NanoTrasen";
                        em.EnsureComponent<IFFComponent>(externalGrid);
                        Assert.That(em.System<KiasNavigationSystem>().Classify(grid, externalGrid), Is.EqualTo(KiasContactDisposition.Friendly));
                        FireAgain();
                    });
                    await pair.RunTicksSync((int) timing.TickRate * 12);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(commands.Count, Is.EqualTo(beforeFriendly));
                        crossChecks["nativeFriendlyShotDoesNotActuateUnknownFlash"] = true;
                    });
                }
            }
            if (presetId == "battle-flash")
            {
                var detectors = Array.Empty<EntityUid>();
                await server.WaitAssertion(() => { detectors = OnGrid<KiasWeaponFlashComponent>(); Assert.That(detectors, Has.Length.EqualTo(6)); });
                foreach (var detector in detectors)
                foreach (var placement in new[] { "front", "left", "right", "back", "inside-edge", "outside-edge", "out-of-range" })
                {
                    await pair.RunTicksSync(120);
                    await server.WaitAssertion(() =>
                    {
                        flashDetectorsSeen.Clear();
                        var transforms = em.System<SharedTransformSystem>();
                        var forward = transforms.GetWorldRotation(detector).ToWorldVec();
                        var distance = placement == "out-of-range" ? em.GetComponent<KiasWeaponFlashComponent>(detector).Range + 50 : 100;
                        var angle = (placement == "inside-edge" ? 44 : 46) * MathF.PI / 180;
                        var edge = new Vector2(forward.X * MathF.Cos(angle) - forward.Y * MathF.Sin(angle), forward.X * MathF.Sin(angle) + forward.Y * MathF.Cos(angle));
                        var direction = placement switch { "left" => new Vector2(-forward.Y, forward.X), "right" => new Vector2(forward.Y, -forward.X), "back" => -forward, "inside-edge" or "outside-edge" => edge, _ => forward };
                        var position = transforms.GetMapCoordinates(detector).Position + direction * distance;
                        var gunOffset = transforms.GetMapCoordinates(externalGun).Position - transforms.GetWorldPosition(externalGrid);
                        transforms.SetWorldPosition(externalGrid, position - gunOffset);
                        Assert.That(Vector2.Distance(transforms.GetMapCoordinates(externalGun).Position, position), Is.LessThan(.01f));
                        foreach (var uid in OnGrid<BatteryComponent>()) em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                        var batteryQuery = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
                        while (batteryQuery.MoveNext(out var uid, out var battery, out var transform))
                            if (transform.GridUid == externalGrid) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
                        var gun = em.GetComponent<GunComponent>(externalGun);
                        var before = gun.LastFire;
                        em.System<SharedGunSystem>().AttemptShoot(externalGun, externalGun, gun,
                            new EntityCoordinates(maps.GetMap(mapId), position + direction * 200));
                        Assert.That(gun.LastFire, Is.GreaterThan(before), "Sector check requires an actual shot.");
                        Assert.That(flashDetectorsSeen.Contains(detector), Is.EqualTo(placement is "front" or "inside-edge"));
                    });
                }
                await server.WaitAssertion(() => crossChecks["sixNativeDirectedDetectorsFrontSideAndRange"] = true);
                await pair.RunTicksSync(660);
                var beforeFriendly = commands.Count;
                await server.WaitAssertion(() =>
                {
                    em.GetComponent<ShuttleFactionComponent>(externalGrid).Faction = "NanoTrasen";
                    Assert.That(em.System<KiasNavigationSystem>().Classify(grid, externalGrid), Is.EqualTo(KiasContactDisposition.Friendly));
                    var transforms = em.System<SharedTransformSystem>();
                    var detector = detectors[0];
                    var direction = transforms.GetWorldRotation(detector).ToWorldVec();
                    var position = transforms.GetMapCoordinates(detector).Position + direction * 100;
                    transforms.SetWorldPosition(externalGrid, position - em.GetComponent<TransformComponent>(externalGun).LocalPosition);
                    var gun = em.GetComponent<GunComponent>(externalGun);
                    var before = gun.LastFire;
                    em.System<SharedGunSystem>().AttemptShoot(externalGun, externalGun, gun,
                        new EntityCoordinates(maps.GetMap(mapId), position + direction * 200));
                    Assert.That(gun.LastFire, Is.GreaterThan(before));
                });
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() => { Assert.That(commands.Count, Is.EqualTo(beforeFriendly)); crossChecks["friendlyNativeShotDoesNotActuateBattle"] = true; });
                await server.WaitAssertion(() =>
                {
                    flashDetectorsSeen.Clear();
                    var ownGun = OnGrid<Content.Server._Mono.SpaceArtillery.Components.SpaceArtilleryComponent>().First(uid => em.HasComponent<GunComponent>(uid));
                    foreach (var uid in OnGrid<BatteryComponent>()) em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                    var gun = em.GetComponent<GunComponent>(ownGun);
                    var before = gun.LastFire;
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(ownGun).Position;
                    em.System<SharedGunSystem>().AttemptShoot(ownGun, ownGun, gun, new EntityCoordinates(maps.GetMap(mapId), position + new Vector2(0, -500)));
                    Assert.That(gun.LastFire, Is.GreaterThan(before));
                    Assert.That(flashDetectorsSeen, Is.Empty, "Native own-grid shots must be excluded by every flash detector.");
                });
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() => { Assert.That(commands.Count, Is.EqualTo(beforeFriendly)); crossChecks["ownNativeShotExcludedByAllDetectors"] = true; });
            }
            await server.WaitAssertion(() =>
            {
                var program = em.GetComponent<KiasControllerCardComponent>(card).Program;
                var compiled = KiasGraphCompiler.Compile(program, io.Schema, io.SnapshotSchema);
                Assert.That(compiled.Errors, Is.Empty);
                double clock = 0;
                var actuations = 0;
                var scheduled = new List<(int Node, double Due, uint Token)>();
                var machine = new KiasGraphMachine(compiled.Graph!, (node, port, value) =>
                {
                    if (io.Schema(node.Profile)?.Any(p => p.Id == port && p.Type == KiasPortType.Signal) == true) actuations++;
                }, (node, seconds, token) => scheduled.Add((node, clock + seconds, token)), () => clock);
                machine.Start();
                if (presetId != "boot")
                {
                    Assert.That(nativeEventSnapshot, Is.Not.Null, "Graph replay requires the context recorded from the real native stimulus.");
                    var source = program.Nodes.Single(node => node.Profile == "Automation" && node.Kind == KiasNodeKind.Any);
                    void Replay()
                    {
                        foreach (var (port, value) in nativeEventSnapshot!) machine.EmitExternal(source.Id, port, value, core);
                        machine.EmitExternal(source.Id, triggerPort, KiasGraphValue.Pulse, core);
                    }
                    Replay();
                    Assert.That(actuations, Is.GreaterThan(0), "Recorded real input must reach the same preset's terminal actions.");
                    var first = actuations;
                    clock = 0.1;
                    Replay();
                    Assert.That(actuations, Is.EqualTo(first), "Identical recorded input inside cooldown must not repeat terminal effects.");
                    clock = program.Nodes.Where(node => node.Kind == KiasNodeKind.Cooldown).Max(node => node.Config.Seconds) + 1;
                    Replay();
                    Assert.That(actuations, Is.EqualTo(first * 2), "The recorded input must rearm after the preset's own cooldown.");
                    crossChecks["graphReplayOfRecordedNativeContextCooldownAndRearm"] = true;
                }
                else Assert.That(actuations, Is.GreaterThan(0));
                machine.Stop();
                var stopped = actuations;
                foreach (var source in program.Nodes.Where(node => node.Profile == "Automation"))
                    machine.EmitExternal(source.Id, triggerPort, KiasGraphValue.Pulse, core);
                foreach (var timer in scheduled) machine.TimerElapsed(timer.Node, timer.Token);
                Assert.That(actuations, Is.EqualTo(stopped));
                crossChecks["graphUnitStoppedMachineRejectsInput"] = true;
            });
            if (presetId == "medical-assistance")
            {
                var firstCommands = commands.Count;
                var firstSignals = signals.Count;
                async Task AdvanceMedical(int seconds)
                {
                    for (var second = 0; second < seconds; second++)
                    {
                        await server.WaitAssertion(() =>
                        {
                            foreach (var uid in OnGrid<BatteryComponent>()) em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                            var atmos = em.System<AtmosphereSystem>();
                            foreach (var (tile, state) in em.GetComponent<GridAtmosphereComponent>(grid).Tiles.ToArray())
                            {
                                if (state.Space) continue;
                                var gas = atmos.GetTileMixture((grid, null, null), null, tile, false);
                                if (gas == null) continue;
                                gas.Clear(); gas.SetMoles(Gas.Oxygen, 21); gas.SetMoles(Gas.Nitrogen, 79); gas.Temperature = 293.15f;
                            }
                        });
                        await pair.RunTicksSync((int) timing.TickRate);
                    }
                }
                async Task ReviveMedicalCrew()
                {
                    await AdvanceMedical(1);
                    await server.WaitAssertion(() =>
                    {
                        var heal = new DamageSpecifier();
                        foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict) heal.DamageDict.Add(type, -amount);
                        em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                        Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Dead));
                        var rescuer = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(actor).Coordinates);
                        var defib = em.SpawnEntity("Defibrillator", em.GetComponent<TransformComponent>(rescuer).Coordinates);
                        Assert.That(em.System<SharedHandsSystem>().TryPickup(rescuer, defib), Is.True);
                        Assert.That(em.System<ItemToggleSystem>().TryActivate(defib, rescuer), Is.True);
                        Assert.That(em.System<DefibrillatorSystem>().TryStartZap(defib, actor, rescuer), Is.True);
                    });
                    await AdvanceMedical(5);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.Not.EqualTo(MobState.Dead));
                        var heal = new DamageSpecifier();
                        foreach (var (type, amount) in em.GetComponent<DamageableComponent>(actor).Damage.DamageDict) heal.DamageDict.Add(type, -amount);
                        em.System<DamageableSystem>().TryChangeDamage(actor, heal);
                        Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Alive));
                    });
                    await AdvanceMedical(3);
                }
                void KillMedicalCrew()
                {
                    var dead = em.GetComponent<MobThresholdsComponent>(actor).Thresholds.First(threshold => threshold.Value == MobState.Dead).Key;
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Bloodloss", dead + 50);
                    em.System<DamageableSystem>().TryChangeDamage(actor, damage);
                    Assert.That(em.GetComponent<MobStateComponent>(actor).CurrentState, Is.EqualTo(MobState.Dead));
                }
                await ReviveMedicalCrew();
                await server.WaitAssertion(KillMedicalCrew);
                await AdvanceMedical(35);
                await server.WaitAssertion(() =>
                {
                    Assert.That(signals.Count, Is.GreaterThan(firstSignals));
                    Assert.That(commands.Count, Is.EqualTo(firstCommands));
                    crossChecks["nativeMedicalRecoveryAndRepeatInsideCooldownSuppressed"] = true;
                });
                await pair.Client.WaitAssertion(() => Assert.That(receivedRadio.Count(message => message.Channel == ChatChannel.Radio), Is.EqualTo(1)));
                await ReviveMedicalCrew();
                var remainingSeconds = 0;
                await server.WaitAssertion(() => remainingSeconds = Math.Max(0, (int) Math.Ceiling((em.GetComponent<KiasProtocolComponent>(core).MedicalAfter - timing.CurTime).TotalSeconds)) + 1);
                await AdvanceMedical(remainingSeconds);
                await server.WaitAssertion(KillMedicalCrew);
                await AdvanceMedical(35);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasSystem>().IsOnline(core), Is.True);
                    Assert.That(commands.Count, Is.EqualTo(firstCommands * 2));
                    crossChecks["nativeMedicalRecoveryAndRepeatAfterCooldownRebroadcasts"] = true;
                });
                await pair.Client.WaitAssertion(() => Assert.That(receivedRadio.Count(message => message.Channel == ChatChannel.Radio), Is.EqualTo(2)));
                inputEvidence.Add("MedicalHelp recovery uses real timed defibrillation and real repeat death; native battery charging and normal cabin gas sustain the long cooldown check.");
            }
            if (presetId == "vessel-critical")
            {
                async Task AdvancePowered(int ticks)
                {
                    for (var remaining = ticks; remaining > 0; remaining -= 300)
                    {
                        await server.WaitAssertion(() =>
                        {
                            foreach (var uid in OnGrid<BatteryComponent>())
                                em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                        });
                        await pair.RunTicksSync(Math.Min(remaining, 300));
                    }
                }
                var initialCommands = commands.Count;
                await AdvancePowered(5600);
                await server.WaitAssertion(() => Assert.That(commands.Count, Is.GreaterThan(initialCommands), "The native armed clock must issue another command after 180 seconds."));
                await pair.Client.WaitAssertion(() =>
                {
                    Assert.That(receivedRadio.Count(message => message.Channel == ChatChannel.Radio), Is.EqualTo(2), "The native clock must deliver the second actual MAYDAY.");
                    crossChecks["nativeClockRetransmitsMaydayAfter180Seconds"] = true;
                });
                var beforeStop = commands.Count;
                if (clearClock)
                {
                    await server.WaitAssertion(() =>
                    {
                        actor = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(console).Coordinates);
                        em.System<SharedMindSystem>().TransferTo(ownerMind, actor);
                        em.EnsureComponent<ActiveRadioComponent>(actor).Channels.Add("Traffic");
                        em.EnsureComponent<IntrinsicRadioReceiverComponent>(actor);
                        Assert.That(em.System<UserInterfaceSystem>().TryOpenUi(console, KiasUiKey.Key, actor), Is.True);
                    });
                    await pair.RunTicksSync(10);
                    var consoleNet = em.GetNetEntity(console);
                    await pair.Client.WaitAssertion(() =>
                    {
                        var client = pair.Client.ResolveDependency<IEntityManager>();
                        client.System<Robust.Client.GameObjects.UserInterfaceSystem>().ClientSendUiMessage(
                            client.GetEntity(consoleNet), KiasUiKey.Key, new KiasControlMessage { Reset = true });
                    });
                    await pair.RunTicksSync(30);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.GetComponent<KiasProtocolComponent>(core).Alert, Is.EqualTo(KiasAlert.Normal));
                        Assert.That(em.GetComponent<KiasProtocolComponent>(core).MaydayReason, Is.Empty);
                    });
                }
                else
                    await server.WaitAssertion(() =>
                    {
                        var rack = OnGrid<KiasControllerRackComponent>().Single(uid => Enumerable.Range(0, KiasControllerRackComponent.SlotCount)
                            .Any(slot => em.System<SharedContainerSystem>().TryGetContainer(uid, KiasControllerRackComponent.SlotId(slot), out var container)
                                && container.ContainedEntities.Contains(card)));
                        Assert.That(em.System<SharedContainerSystem>().RemoveEntity(rack, card), Is.True);
                    });
                await AdvancePowered(5600);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasSystem>().IsOnline(core), Is.True, "Timer cleanup must be checked with the core still powered.");
                    Assert.That(OnGrid<KiasControllerRackComponent>().All(uid => em.System<KiasSystem>().IsOnline(uid)), Is.True);
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.EqualTo(clearClock));
                    Assert.That(commands.Count, Is.EqualTo(beforeStop), "Cleared or removed physical card must stop its native 180-second clock.");
                    crossChecks[clearClock ? "nativeManagementAlertResetStopsClock" : "nativeRemovedCardHasNoOrphanClockAfter180Seconds"] = true;
                });
                await pair.Client.WaitAssertion(() => Assert.That(receivedRadio.Count(message => message.Channel == ChatChannel.Radio), Is.EqualTo(2)));
            }
            await server.WaitAssertion(() => { completed = true; SaveResult("PASS"); });
        }
        catch (Exception e)
        {
            if (!completed)
                await server.WaitPost(() => SaveResult("FAIL", e.Message));
            throw;
        }
        finally
        {
            await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded -= receivedRadio.Add);
            await server.WaitPost(() => { io.Emitted -= Emission; io.CommandDispatched -= Dispatch; });
        }
        await pair.CleanReturnAsync();
    }
}
