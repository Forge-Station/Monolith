#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Traits.Assorted;
using Robust.Shared.GameObjects;
using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// CONSC: a wound host's mob state is decided by consciousness, not by a damage total. Pain, blood, legs and
/// airloss each own a threshold, painkillers discount pain by tier, and Downed restricts a player to
/// themselves.
/// </summary>
/// <remarks>
/// The fixture carries no <c>PainShockTarget</c>, so no pain shock stuns it mid-test. M1a: pain goes on the
/// parts, because the body's own pain is the sum of its parts (P13), and pain past the unconscious line is a
/// pain faint of fixed length, not a held unconsciousness.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedConsciousnessTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedConscBodyGraph
  name: ""wolfmed consciousness body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
      - left leg
      - right leg
    head:
      part: HeadHuman
    left leg:
      part: LeftLegHuman
    right leg:
      part: RightLegHuman

- type: entity
  id: WolfmedConscBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedConscBodyGraph
    requiredLegs: 2
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Alerts
  - type: MovementSpeedModifier
  - type: StandingState
  - type: WoundHost

- type: entity
  id: WolfmedConscPlainMob
  parent: InventoryBase
  components:
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      50: Critical
      100: Dead
";

    /// <summary>
    /// The premise of the whole package: on a wound host a damage total decides nothing. A mob without
    /// WoundHost is the control and still crosses its thresholds exactly as it always did.
    /// </summary>
    [Test]
    public async Task DamageTotalsNeverCritAWoundHostTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var mobState = entities.System<MobStateSystem>();
            var damage = entities.System<DamageableSystem>();

            // Numb, so pain contributes nothing and damage is the only thing left that could do it.
            entities.EnsureComponent<PainNumbnessComponent>(body);

            // M3: on the torso. Routed to a random part, a hit this size on the head is a knockout (plan §3.6),
            // which is a consequence of the blow, not of a damage total.
            var torso = Content.Shared._Shitmed.Targeting.TargetBodyPart.Torso;
            damage.TryChangeDamage(body, Spec("Blunt", 250), ignoreResistances: true, targetPart: torso);
            Assert.That(mobState.IsAlive(body), Is.True,
                "250 Blunt crit a wound host: the MobThresholdSystem gate is not holding.");
            Assert.That(mobState.IsCritical(body), Is.False);
            Assert.That(mobState.IsDead(body), Is.False);

            // 600 in all, well past the old dead threshold. M1b: no body-wide cap on the living any more; the
            // per-part ceiling applies to damage nobody dealt, and none of it decides the state either.
            damage.TryChangeDamage(body, Spec("Blunt", 350), ignoreResistances: true, targetPart: torso);
            Assert.That(mobState.IsAlive(body), Is.True);
            Assert.That(mobState.IsDead(body), Is.False);

            // The control: no WoundHost, so nothing changed for it.
            var plain = entities.SpawnEntity("WolfmedConscPlainMob", map.GridCoords);
            damage.TryChangeDamage(plain, Spec("Blunt", 60), ignoreResistances: true);
            Assert.That(entities.System<MobStateSystem>().IsCritical(plain), Is.True,
                "a mob without WoundHost stopped crossing its own thresholds.");

            damage.TryChangeDamage(plain, Spec("Blunt", 60), ignoreResistances: true);
            Assert.That(mobState.IsDead(plain), Is.True);
        });
    }

    /// <summary>
    /// Pain past 0.70 of the soft cap puts a body on the floor, and the hysteresis keeps it there until the
    /// pain has dropped a tenth below the value that downed it.
    /// </summary>
    [Test]
    public async Task PainDownsAndThenReleasesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var pain = entities.System<PainSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var torso = Torso(entities, body);

            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));

            // 0.95 x the 135 soft cap is 128.25. 129 is the first whole number past it. M1a (P13): the body's
            // pain is the sum of its parts, so the torso carries it.
            pain.SetPain(torso, FixedPoint2.New(129));
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True);
            Assert.That(entities.GetComponent<MobStateComponent>(body).CurrentState,
                Is.EqualTo(MobState.Alive), "Downed is conscious: it is not Critical.");
            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Pain), "Downed by pain did not name pain.");

            // Hysteresis: 0.9 x 128.25 = 115.4, so 116 is still on the floor.
            pain.SetPain(torso, FixedPoint2.New(116));
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed),
                "the body stood up inside the hysteresis band.");

            // 0.80 of the cap is 108, which is 0.84 of the Downed threshold: under the band.
            pain.SetPain(torso, FixedPoint2.New(108));
            consciousness.Refresh(body);

            // AUTODOC5: getting up also takes the two-second dwell, which is what stops a body on the
            // threshold flickering (and playing the body-fall sound) several times a second.
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed),
                "the body got up inside the dwell.");
        });

        await Pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            entities.System<WolfmedConsciousnessSystem>().Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.False);
        });
    }

    /// <summary>
    /// What Downed costs: everything past the body's own reach. M1a (P16, OD7 (b)): a loose item lying within
    /// reach is inside it now; a door is not.
    /// </summary>
    [Test]
    public async Task DownedReachesOnlyItselfTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var pen = entities.SpawnEntity("EmergencyMedipen", map.GridCoords);
            var airlock = entities.SpawnEntity("Airlock", map.GridCoords);
            var blocker = entities.System<ActionBlockerSystem>();
            var downed = entities.System<WolfmedDownedSystem>();

            Assert.That(blocker.CanInteract(body, airlock), Is.True);
            Assert.That(blocker.CanAttack(body), Is.True);

            entities.System<PainSystem>().SetPain(Torso(entities, body), FixedPoint2.New(129));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True);

            // Themselves and what they are carrying, and nothing else.
            Assert.That(blocker.CanInteract(body, body), Is.True, "a Downed body cannot reach itself.");
            Assert.That(blocker.CanInteract(body, null), Is.True);
            Assert.That(blocker.CanUseHeldEntity(body, pen), Is.True,
                "a Downed body cannot use what is already in its hands.");
            Assert.That(downed.IsSelfOrCarried(body, body), Is.True);
            Assert.That(downed.IsSelfOrCarried(body, pen), Is.False);
            Assert.That(blocker.CanInteract(body, pen), Is.True, "a Downed body cannot reach a pen at its side.");
            Assert.That(downed.IsWithinReach(body, airlock), Is.False, "a door counted as a loose item.");

            Assert.That(blocker.CanInteract(body, airlock), Is.False, "a Downed body opened a door.");
            Assert.That(blocker.CanAttack(body), Is.False, "a Downed body swung at something.");
            Assert.That(blocker.CanThrow(body, pen), Is.False);
        });
    }

    /// <summary>
    /// Past the unconscious point the body faints (M1a: a pain faint of fixed length, Critical), a weak
    /// painkiller never ends it, and a strong one does because pain was the only thing holding it there.
    /// </summary>
    [Test]
    public async Task PainCritIsLiftedByStrongPainkillersOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var pain = entities.System<PainSystem>();
            var relief = entities.System<WolfmedPainReliefSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // The unconscious test reads pain BEFORE the soft clamp, which is the sum of the parts: the
            // body's own value is clamped to 135 and could never say more than 1.0 of the cap.
            pain.SetPain(torso, FixedPoint2.New(100));
            pain.SetPain(head, FixedPoint2.New(100));
            Assert.That(consciousness.GetUncappedPain(body), Is.EqualTo(200f).Within(0.01f));
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint), "200 summed pain did not faint.");
            Assert.That(entities.System<MobStateSystem>().IsCritical(body), Is.True);

            // Weak: the faint line is 1.4 x 135 = 189. Weak relief never ends a faint however much it
            // discounts, because it is not counted against this line at all.
            relief.AddDose(body, "weak", WolfmedPainReliefTier.Weak, 22f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                "a weak painkiller lifted unconsciousness.");

            // Strong: a strong painkiller ends the faint at once (plan §3.1), and the effective pain of
            // 135 - 92 is under the Downed threshold too, so the patient is back on their feet.
            relief.AddDose(body, "strong", WolfmedPainReliefTier.Strong, 70f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious),
                "a strong painkiller could not lift a crit that was pain and nothing else.");
            Assert.That(entities.System<MobStateSystem>().IsCritical(body), Is.False);
        });
    }

    /// <summary>
    /// GAMEPLAY: relief does not add up dose by dose. A cocktail counts as its strongest single dose plus a
    /// share of the rest, so drinking one of everything cannot discount pain away for free.
    /// </summary>
    [Test]
    public async Task StackedPainkillersHitTheReliefCapTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var relief = entities.System<WolfmedPainReliefSystem>();
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var window = TimeSpan.FromSeconds(60);

            // The three weak reagents in the game, at their own strengths.
            relief.AddDose(body, "ibuprofen", WolfmedPainReliefTier.Weak, 14f, window, 0f);
            Assert.That(relief.GetRelief(body), Is.EqualTo(14f).Within(0.01f),
                "one painkiller was worth anything but its own strength.");

            relief.AddDose(body, "ketorolac", WolfmedPainReliefTier.Weak, 26f, window, 0f);
            relief.AddDose(body, "analgesic", WolfmedPainReliefTier.Weak, 22f, window, 0f);

            var comp = entities.GetComponent<WolfmedPainReliefComponent>(body);
            var expected = 26f + comp.StackShare * (14f + 22f);

            Assert.Multiple(() =>
            {
                Assert.That(relief.GetRelief(body), Is.EqualTo(expected).Within(0.01f));
                Assert.That(relief.GetRelief(body), Is.LessThan(62f),
                    "three weak painkillers still added up to the sum of their strengths.");
                Assert.That(relief.GetStrongRelief(body), Is.EqualTo(0f),
                    "a weak painkiller counted against the unconscious threshold.");
            });

            // The strong tier caps on its own terms, off the strongest strong dose.
            relief.AddDose(body, "tramadol", WolfmedPainReliefTier.Strong, 45f, window, 0f);
            relief.AddDose(body, "oxycodone", WolfmedPainReliefTier.Strong, 80f, window, 0f);

            Assert.That(relief.GetStrongRelief(body),
                Is.EqualTo(80f + comp.StackShare * 45f).Within(0.01f),
                "two strong painkillers stacked their strengths against unconsciousness.");
        });
    }

    /// <summary>
    /// Blood is its own input: it downs and crits a body with no pain at all, and no painkiller of any tier
    /// touches it. That is the whole of the owner's "up until you have no blood" rule.
    /// </summary>
    [Test]
    public async Task BloodLossDownsCritsAndIgnoresPainkillersTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = entities.System<BloodstreamSystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var relief = entities.System<WolfmedPainReliefSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);

            void SetBlood(float fraction)
            {
                var current = bloodstream.GetBloodLevelPercentage(body);
                bloodstream.TryModifyBloodLevel(body, FixedPoint2.New((fraction - current) * 300f));
                consciousness.OnBloodLevelChanged(body, bloodstream.GetBloodLevelPercentage(body));
            }

            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));

            // 0.45 is past the 0.50 Downed threshold and 0.78 of the way to the 0.35 one.
            SetBlood(0.45f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            // 0.30 is past 0.35.
            SetBlood(0.3f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));

            // Not one tier of painkiller touches blood.
            relief.AddDose(body, "strong", WolfmedPainReliefTier.Strong, 200f, TimeSpan.FromSeconds(30), 0f);
            relief.AddDose(body, "pen", WolfmedPainReliefTier.Emergency, 0f, TimeSpan.FromSeconds(30), 0f);
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                "a painkiller lifted a body that had run out of blood.");

            // 0.52 is 0.96 of the way to the Downed threshold: inside the hysteresis band, still down.
            SetBlood(0.52f);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            // AUTODOC5: past the band the body is up as soon as its two-second dwell on the floor is over.
            SetBlood(0.7f);
            comp.DownedUntil = TimeSpan.Zero;
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
        });
    }

    /// <summary>Both legs out of action puts a body on the floor, and one working leg picks it back up.</summary>
    [Test]
    public async Task DisabledLegsDownTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var legs = graph.GetBodyChildren(body)
                .Where(part => part.Component.PartType == BodyPartType.Leg)
                .Select(part => part.Id)
                .ToList();

            Assert.That(legs, Has.Count.EqualTo(2));

            foreach (var leg in legs)
                entities.EnsureComponent<BodyPartFunctionalityComponent>(leg).State =
                    BodyPartFunctionalityState.Disabled;

            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));

            // AUTODOC5: one working leg is enough to stand on, once the two-second dwell is over.
            entities.GetComponent<BodyPartFunctionalityComponent>(legs[0]).State =
                BodyPartFunctionalityState.Functional;
            comp.DownedUntil = TimeSpan.Zero;
            consciousness.Refresh(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
        });
    }

    /// <summary>
    /// The emergency pen buys a window on your feet however bad the pain is, and then charges for it: pain
    /// up by the crash multiplier, and Downed for the crash's duration.
    /// </summary>
    [Test]
    public async Task EmergencyPenLiftsThenCrashesTest()
    {
        // Playtest 4: the pain here carries no wound floor, so the loose rate would fade it under the pen's arithmetic.
        await OverrideCVar(Side.Server, WolfmedCVars.PainLooseRecovery, 0f);
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var pain = entities.System<PainSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            pain.SetPain(torso, FixedPoint2.New(100));
            pain.SetPain(head, FixedPoint2.New(100));
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Unconscious));
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).Cause,
                Is.EqualTo(WolfmedCause.PainFaint), "M1a: pain past the unconscious line is a faint.");

            // The reagent's own window is 30 s; this is the same mechanism on a timer a test can wait out.
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "pen", WolfmedPainReliefTier.Emergency, 0f, TimeSpan.FromSeconds(1), 0f);
            entities.System<WolfmedConsciousnessSystem>().Refresh(body);
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Up),
                "the emergency pen did not lift a body that pain alone had put out.");
        });

        // Past the window and into the crash, but not past the crash's own 10 s.
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            var relief = entities.GetComponent<WolfmedPainReliefComponent>(body);
            Assert.That(relief.EmergencyEnds, Is.Null);
            Assert.That(relief.CrashEnds, Is.Not.Null, "the pen's window ended without a crash.");
            Assert.That(comp.State, Is.Not.EqualTo(WolfmedConsciousness.Up),
                "the crash left the body on its feet.");

            // Pain x1.3 on every part that had any: 100 each becomes 130 each, less whatever the natural
            // recovery of 1/9 a second has taken off since the crash landed.
            var pain = entities.System<PainSystem>();
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            Assert.That(pain.GetRawPain(torso).Float(), Is.EqualTo(130f).Within(1f));
        });
    }

    /// <summary>
    /// Too much of a sedating painkiller depresses breathing. BRAIN took the Asphyxiation stand-in out: the
    /// overdose is now a pressure of its own and an input to the brain's oxygenation clock.
    /// </summary>
    [Test]
    public async Task SedationOverdoseTakesAirTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            // M2 (plan §3.4): a dose now asks for a target, and sedation climbs toward it at wolfmed.sedation_rise.
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "opiate", WolfmedPainReliefTier.Strong, 10f, TimeSpan.FromSeconds(60), 1.5f);
        });

        // 0.05 a second toward full sedation: past the 0.6 threshold at 12 s.
        await RunSeconds(16);

        await server.WaitAssertion(() =>
        {
            var relief = entities.GetComponent<WolfmedPainReliefComponent>(body);
            Assert.That(relief.Sedation, Is.GreaterThan(relief.SedationAirlossThreshold));

            var reliefSystem = entities.System<WolfmedPainReliefSystem>();
            Assert.That(reliefSystem.GetRespiratoryDepression(body), Is.GreaterThan(0f),
                "a sedation overdose stopped depressing breathing.");
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).Pressures
                    .ContainsKey(WolfmedPainReliefSystem.SedationPressure), Is.True);

            var damage = entities.GetComponent<DamageableComponent>(body);
            Assert.That(damage.DamagePerGroup.TryGetValue("Airloss", out var airloss) &&
                        airloss > FixedPoint2.Zero, Is.False,
                "the sedation stand-in still deals Asphyxiation damage.");

        });
    }

    /// <summary>Rejuvenate is the admin's undo, and it has to undo all of this too.</summary>
    [Test]
    public async Task RejuvenateClearsConsciousnessTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedConscBody", map.GridCoords);
            // A full external pressure is the worst input there is, and BRAIN pushes arrest and hypoxia
            // through this same seam. The fixture carries no organs, so the seam is driven directly.
            entities.System<WolfmedConsciousnessSystem>().SetExternalPressure(body, "test", 1f);
            entities.System<WolfmedPainReliefSystem>()
                .AddDose(body, "opiate", WolfmedPainReliefTier.Strong, 40f, TimeSpan.FromSeconds(60), 0.2f);

            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Unconscious));

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());

            var comp = entities.GetComponent<WolfmedConsciousnessComponent>(body);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(comp.Pressures, Is.Empty);
            Assert.That(entities.System<WolfmedLifeSystem>().InArrest(body), Is.False);
            Assert.That(comp.Oxygenation, Is.EqualTo(1f));
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.False);
            Assert.That(entities.HasComponent<WolfmedPainReliefComponent>(body), Is.False);
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.True);
        });

        // The re-evaluation is deferred by a tick so every other rejuvenate handler has run first.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.True);
        });
    }

    private static EntityUid Torso(IEntityManager entities, EntityUid body) =>
        entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == BodyPartType.Torso).Id;

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
