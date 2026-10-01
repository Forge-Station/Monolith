#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Administration.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Rejuvenate;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 1 (2026-09-23): the defibrillator's "No response", message spam in vacuum, an IPC crawling on low
/// power, a rejuvenated IPC's missing cell, and the revolver casings that filled the server log.
/// </summary>
[TestFixture]
public sealed class WolfmedPlaytestOneTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibChance, 0.85f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibOxygenationFloor, 0.15f);
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) => SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    /// <summary>
    /// Item 3. Arrested from blood, transfused to 45% with the bleeding stopped: the paddles and the analyzer
    /// agree the shock is indicated, and every shock says either that it worked or that it did not take and to
    /// charge again, never a bare "No response". Shocks keep coming while the heart is stopped. A body that has
    /// died of the brain meanwhile is told to have the brain repaired, and a body the paddles cannot read says so.
    /// </summary>
    [Test]
    public async Task DefibAfterTransfusionTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(body, 0.29f);
            s.Life.Tick(body, 1f);
            Assert.That(s.Life.InArrest(body), Is.True, "29% blood kept a pulse.");

            // Two minutes on the arrest clock: the brain's oxygen, and with it the paddles' odds, is low.
            s.Advance(body, 110);
            s.Transfuse(body, 0.16f * s.Pool(body));
            Assert.That(s.Life.GetBleedRate(body), Is.EqualTo(0f), "the fixture is bleeding.");
            Assert.That(s.Life.InArrest(body), Is.True, "the transfusion restarted the heart.");

            var refusal = s.Revival.GetRefusal(body);
            var chance = s.Revival.GetChance(body);
            TestContext.Out.WriteLine($"blood {s.Blood(body):P0}, oxygenation {s.Life.GetOxygenation(body):F2}: " +
                                      $"refusal {refusal ?? "none"}, odds {chance:P1}; analyzer: {s.Analyzer(body)}");
            Assert.That(refusal, Is.Null, "a transfused patient in arrest was refused.");
            Assert.That(s.AnalyzerLines(body), Does.Contain("Defib: shock indicated"), "the analyzer and the paddles disagree.");

            // Five failed rolls in a row: each one is the roll's own line, and nothing stops the next.
            s.Revival.ForcedRoll = 1f;
            try
            {
                for (var shock = 0; shock < 5; shock++)
                {
                    Assert.That(s.Revival.TryDefibrillate(body, out var line), Is.False);
                    Assert.That(line, Is.EqualTo(WolfmedRevivalSystem.NoResponse));
                    Assert.That(s.Revival.LocalizeLine(body, line), Is.EqualTo("No response. Charge again."));
                    Assert.That(s.Life.InArrest(body), Is.True);
                }
            }
            finally
            {
                s.Revival.ForcedRoll = null;
            }

            // The real roll, until it takes: never anything but success or the roll's line.
            var tries = 0;
            string last;
            while (!s.Revival.TryDefibrillate(body, out last) && tries < 200)
            {
                tries++;
                Assert.That(last, Is.EqualTo(WolfmedRevivalSystem.NoResponse), $"shock {tries} said {last}.");
            }

            TestContext.Out.WriteLine($"the heart restarted after {tries} failed shocks at {chance:P1} odds.");
            Assert.That(last, Is.EqualTo(WolfmedRevivalSystem.Success));
            Assert.That(s.Life.InArrest(body), Is.False);

            // Not a body the paddles read: says so rather than "No response".
            var stock = SEntMan.SpawnEntity("WolfmedPlaytestStockMob", map.GridCoords);
            Assert.That(s.Revival.TryDefibrillate(stock, out var stockLine), Is.False);
            Assert.That(stockLine, Is.EqualTo(WolfmedRevivalSystem.NotMonitored));
        });

        // Meanwhile dead of the brain. Playtest 5: the paddles take it anyway, and the patient comes back with the
        // brain at the revive floor. The organ system applies the death on its own tick, so real time has to pass
        // after the clock.
        EntityUid dead = default;
        await Server.WaitAssertion(() =>
        {
            dead = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(dead, 0.29f);
            s.Life.Tick(dead, 1f);
            s.Advance(dead, 900);
        });
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(dead), Is.True, "fifteen minutes of arrest did not kill.");
            s.SetBlood(dead, 0.6f);
            Assert.That(s.Analyzer(dead), Does.Not.Contain("repair surgery first"), "the analyzer still asks for brain repair before a shock.");
            Assert.That(s.Shock(dead, out var deadLine), Is.True, $"the paddles refused a destroyed brain: {deadLine}");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(dead), Is.False, "the shock did not bring the brain-dead body back.");
            Assert.That(s.Life.GetBrainActivity(dead), Is.GreaterThan(0f).And.LessThan(0.5f), "the revived brain is not at the damage floor.");
        });
    }

    /// <summary>
    /// Item 4. A human in vacuum for a minute (barotrauma, cold, no air) is told each change of state once. A body
    /// that keeps bleeding and keeps taking a little Heat hears "You feel your wounds painfully close!" once per
    /// cooldown instead of on every hit. Measured unlimited first.
    /// </summary>
    [Test]
    public async Task VacuumMessageSpamTest()
    {
        await Pin();
        await OverrideCVar(Side.Server, Content.Shared.CCVar.CCVars.WoundsBleedingAutoStopEnabled, false);
        await OverrideCVar(Side.Server, WolfmedCVars.CauteryPopupSeconds, 0f);
        var map = await Pair.CreateTestMap();
        EntityUid body = default, bleeder = default;
        var lines = new List<string>();
        var states = new List<string>();

        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bleeder = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<DamageableSystem>().TryChangeDamage(bleeder, WolfmedScenario.Spec("Slash", 21), origin: null,
                targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm);
        });

        var count = 0;
        var lastState = string.Empty;
        var unlimited = 0;
        for (var tick = 1; tick <= 120; tick++)
        {
            await RunSeconds(0.5f);
            var now = tick / 2f;
            if (tick == 40)
            {
                // Twenty seconds unlimited, the way it shipped; then the shipped cooldown.
                await Server.WaitPost(() =>
                {
                    var announce = SEntMan.EnsureComponent<Content.Server._WF.Wolfmed.Wounds.WolfmedCauteryAnnounceComponent>(bleeder);
                    unlimited = announce.Count;
                    announce.Count = 0;
                });
                await OverrideCVar(Side.Server, WolfmedCVars.CauteryPopupSeconds, CauteryCooldown);
            }

            await Server.WaitPost(() =>
            {
                // A fight somewhere hot: a fresh cut and a little Heat on the same arm every second. Heat alone
                // seals a cut in a few hits; the new cuts keep one open for it to find.
                if (tick % 2 == 0)
                {
                    var damage = SEntMan.System<DamageableSystem>();
                    var arm = Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm;
                    damage.TryChangeDamage(bleeder, WolfmedScenario.Spec("Slash", 3), origin: null, targetPart: arm);
                    damage.TryChangeDamage(bleeder, WolfmedScenario.Spec("Heat", 0.3f), origin: null, targetPart: arm);
                }

                var comp = Consc(body);
                if (comp.ConditionLineCount != count)
                {
                    lines.Add($"{now:F1}s x{comp.ConditionLineCount - count}: {comp.LastConditionLine}");
                    count = comp.ConditionLineCount;
                }

                var state = $"{comp.State}/{comp.Cause}";
                if (state != lastState)
                {
                    states.Add($"{now:F1}s {state}");
                    lastState = state;
                }
            });
        }

        await Server.WaitAssertion(() =>
        {
            var closing = SEntMan.GetComponent<Content.Server._WF.Wolfmed.Wounds.WolfmedCauteryAnnounceComponent>(bleeder).Count;
            var damage = SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict
                .Where(pair => pair.Value > FixedPoint2.Zero)
                .Select(pair => $"{pair.Key} {pair.Value}");
            TestContext.Out.WriteLine($"wounds closing: {unlimited} in 20 s unlimited, {closing} in the next 40 s at a {CauteryCooldown} s cooldown.");
            TestContext.Out.WriteLine($"60 s in vacuum: {count} condition lines.");
            TestContext.Out.WriteLine($"states: {string.Join(" | ", states)}");
            TestContext.Out.WriteLine($"lines: {string.Join(" | ", lines)}");
            TestContext.Out.WriteLine($"damage: {string.Join(", ", damage)}");
            Assert.Multiple(() =>
            {
                Assert.That(unlimited, Is.GreaterThan(10), "the fixture never reproduced the wounds-closing spam.");
                Assert.That(closing, Is.LessThanOrEqualTo(40f / CauteryCooldown + 1f), "the wounds-closing line is spamming.");
                Assert.That(count, Is.LessThanOrEqualTo(MaxVacuumLines), "the patient's condition lines are spamming.");
            });
        });
    }

    private const float CauteryCooldown = 10f;

    /// <summary>
    /// A minute of vacuum is a handful of changes: measured 2 (down at about 55 s, adrenaline). Over three
    /// minutes it is 7, every one a real change: down, adrenaline and its end, blood loss, a faint, out, arrest.
    /// </summary>
    private const int MaxVacuumLines = 4;

    /// <summary>
    /// Item 6. An IPC on low charge and Downed by frame pain can still crawl: it can move and its speed is
    /// above zero, with the low-power slowdown and the crawl on top of each other.
    /// </summary>
    [Test]
    public async Task IpcLowPowerCrawlTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid ipc = default;

        await Server.WaitPost(() =>
        {
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var charge = SEntMan.System<SiliconChargeSystem>();
            Assert.That(charge.TryGetSiliconBattery(ipc, out var battery), Is.True);
            SEntMan.System<BatterySystem>().SetCharge(battery!.Owner, battery.MaxCharge * 0.12f);
            SetPain(ipc, BodyPartType.Torso, 129);
        });

        // The charge loop re-reads the cell; the fall's stun passes.
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var silicon = SEntMan.GetComponent<Content.Shared._EinsteinEngines.Silicon.Components.SiliconComponent>(ipc);
            var move = SEntMan.GetComponent<MovementSpeedModifierComponent>(ipc);
            TestContext.Out.WriteLine($"IPC charge state {silicon.ChargeState}, {Consc(ipc).State}/{Consc(ipc).Cause}: " +
                                      $"walk {move.CurrentWalkSpeed:F2}, sprint {move.CurrentSprintSpeed:F2}.");
            Assert.Multiple(() =>
            {
                Assert.That(silicon.ChargeState, Is.InRange(1, 2), "the fixture is not on low power.");
                Assert.That(Consc(ipc).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(ipc), Is.True, "a Downed IPC on low power cannot move.");
                Assert.That(move.CurrentWalkSpeed, Is.GreaterThan(0.2f), "a Downed IPC on low power crawls at nothing.");
                Assert.That(move.CurrentSprintSpeed, Is.GreaterThan(0.2f));
            });
        });
    }

    /// <summary>Item 7. Rejuvenating an IPC with its cell pulled puts its own starting cell back and starts it up.</summary>
    [Test]
    public async Task RejuvenateRestoresMissingCellTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid ipc = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            var cell = slot!.Item!.Value;
            Assert.That(SEntMan.System<SharedContainerSystem>().Remove(cell, slot.ContainerSlot!), Is.True);
            SEntMan.DeleteEntity(cell);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Consc(ipc).Cause, Is.EqualTo(WolfmedCause.Shutdown), "pulling the cell did not shut the chassis down.");
            SEntMan.System<RejuvenateSystem>().PerformRejuvenate(ipc);

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(slot!.Item, Is.Not.Null, "the rejuvenate left the cell slot empty.");
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(slot.Item!.Value).EntityPrototype?.ID,
                    Is.EqualTo(slot.StartingItem), "the cell is not the chassis's own starting cell.");
                Assert.That(SEntMan.HasComponent<WolfmedShutdownComponent>(ipc), Is.False, "the chassis is still shut down.");
                Assert.That(Consc(ipc).State, Is.EqualTo(WolfmedConsciousness.Up));
            });
        });

        // And it stays up once the charge loop has had its say.
        await RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(Consc(ipc).State, Is.EqualTo(WolfmedConsciousness.Up)));
    }

    /// <summary>
    /// Item 8. The ~150 "Can't resolve MetaDataComponent" errors in the playtest log were a revolver sending its
    /// cylinder: Mono's casing despawn deleted a spent casing still sitting in its slot. A fired casing stays
    /// in the cylinder, alive, past the despawn time, and gets its timer once it is emptied onto the floor.
    /// </summary>
    [Test]
    public async Task RevolverCasingKeepsItsSlotTest()
    {
        var map = await Pair.CreateTestMap();
        var guns = SEntMan.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>();
        EntityUid revolver = default, round = default;

        // A real round loaded a few ticks before the shot, the way a player's is: a round the gun conjures in the
        // same tick it fires is a state the connected client has never seen.
        await Server.WaitPost(() =>
        {
            revolver = SEntMan.SpawnEntity("WeaponRevolverPython", map.GridCoords);
            var comp = SEntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            round = SEntMan.SpawnEntity(comp.FillPrototype, map.GridCoords);
        });
        await RunTicksSync(5);
        await Server.WaitPost(() =>
        {
            var comp = SEntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            guns.EmptyRevolver(revolver, comp);
        });
        await RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            Assert.That(guns.TryRevolverInsert(revolver, comp, round, null), Is.True, "the round would not load.");
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
            SEntMan.EventBus.RaiseLocalEvent(revolver, new TakeAmmoEvent(1, ammo, map.GridCoords, null, willBeFired: true));
            Assert.That(ammo, Has.Count.EqualTo(1), "the revolver did not fire.");
            foreach (var (entity, _) in ammo)
            {
                if (entity is { } bullet)
                    SEntMan.DeleteEntity(bullet);
            }
        });

        await RunSeconds(35);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            Assert.That(comp.AmmoSlots, Does.Contain((EntityUid?) round), "the fired casing is not in the cylinder.");
            Assert.That(SEntMan.EntityExists(round), Is.True, "the casing was deleted in its slot.");

            guns.EmptyRevolver(revolver, comp);
            Assert.That(SEntMan.HasComponent<Robust.Shared.Spawners.TimedDespawnComponent>(round), Is.True,
                "an emptied casing never despawns.");
        });
    }

    [TestPrototypes]
    private const string Prototypes = @"
# A mob with a body and a mob state and no wound host: the paddles do not read it.
- type: entity
  id: WolfmedPlaytestStockMob
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Damageable
";
}
