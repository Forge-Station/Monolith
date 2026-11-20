using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Wolfmed pain, as a player actually meets it: the vignette level the client overlay draws, the pain shock
/// that paralyses at 130, the HighPainThreshold trait and the pain-numbness suppression.
/// </summary>
/// <remarks>
/// PLAN2 §6.2 T-PAIN-OVERLAY / T-PAIN-SHOCK / T-HIGH-PAIN / T-PAIN-NUMB (WP10-6b). Phase 2 is the first build
/// in which pain, pain shock and the pain HUD run on a real mob (P2-4), so every literal here was measured on
/// this tree, not copied from Onyx (P2-D16), and each one carries its derivation.
/// </remarks>
[TestFixture]
[TestOf(typeof(PainSystem))]
public sealed class WolfmedPainTest : GameTest
{
    /// <summary>
    /// Bespoke pain fixtures. P2-D24: a pain-shock fixture needs an explicit
    /// <c>StatusEffects allowed: [Stun, KnockedDown, Jitter]</c> (neither Stun nor KnockedDown is
    /// <c>alwaysAllowed</c>, so TryParalyze silently no-ops without it) AND <c>MobState</c> (PainSystem.Update's
    /// shock loop is <c>EntityQueryEnumerator&lt;PainComponent, MobStateComponent, PainShockTargetComponent&gt;</c>,
    /// so an entity without it is never visited). Either omission fails the test for a reason unrelated to pain.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedPainBodyGraph
  name: ""wolfmed pain body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
      - left arm
    head:
      part: HeadHuman
    left arm:
      part: LeftArmHuman

- type: entity
  id: WolfmedPainControlBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedPainBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: WoundHost

- type: entity
  id: WolfmedHighPainThresholdBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedPainBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: HighPainThreshold
  - type: WoundHost

- type: entity
  id: WolfmedPainShockBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedPainBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MovementSpeedModifier
  - type: PainShockTarget
  - type: StatusEffects
    allowed:
    - Stun
    - KnockedDown
    - Jitter
  - type: WoundHost

- type: entity
  id: WolfmedPainNumbBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedPainBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MovementSpeedModifier
  - type: PainShockTarget
  - type: PainNumbness
  - type: StatusEffects
    allowed:
    - Stun
    - KnockedDown
    - Jitter
  - type: WoundHost
";

    /// <summary>
    /// T-PAIN-OVERLAY (PLAN2 §6.2, replacing P2-2's impossible pain alert per P2-D5): the level the client
    /// vignette draws is a pure function of PainSystem.GetPain, so it is assertable headlessly even though the
    /// vignette itself is not.
    /// </summary>
    [Test]
    public async Task PainOverlayLevelTracksPainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var pain = entities.System<PainSystem>();

            // M1a (P13): the body's pain is the sum of its parts, so the pain goes on the torso; a direct set on
            // the body is rederived from the parts.
            var torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            // WoundDamageProjectionSystem.SetupBody ensures PainComponent on every wound host at map-init, so
            // a real mob carries the overlay's input from the moment it spawns.
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True);
            var comp = entities.GetComponent<PainComponent>(body);
            Assert.That(comp.SoftPainCap, Is.EqualTo(FixedPoint2.New(135)));

            Assert.That(Level(pain, body, comp), Is.EqualTo(0f).Within(0.0001f));

            // WOLFGATE (measured): 6 / 135 = 0.044, below DamageOverlay.Wolfmed.cs's 0.05 floor, so the
            // vignette stays fully off. FixedPoint2 division truncates (FixedPoint2.cs:116), 0.0444 -> 0.04.
            Assert.That(pain.SetPain(torso, FixedPoint2.New(6)), Is.True);
            Assert.That(Level(pain, body, comp), Is.EqualTo(0f).Within(0.0001f));

            // 6.75 is exactly 5% of the 135 soft cap: the first pain value that draws anything.
            Assert.That(pain.SetPain(torso, FixedPoint2.New(6.75)), Is.True);
            Assert.That(Level(pain, body, comp), Is.EqualTo(0.05f).Within(0.0001f));

            // Half the cap, half the vignette.
            Assert.That(pain.SetPain(torso, FixedPoint2.New(67.5)), Is.True);
            Assert.That(Level(pain, body, comp), Is.EqualTo(0.5f).Within(0.0001f));

            // SetPain clamps to the soft cap (135), which is over the 130 pain-shock threshold, so the shock
            // fires synchronously from RaisePainChanged. M1a (OD5): its adrenaline no longer takes 30% off every
            // reading, so the vignette stays full as the shock lands.
            Assert.That(pain.SetPain(torso, FixedPoint2.New(200)), Is.True);
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(135)));
            Assert.That(entities.HasComponent<StunnedComponent>(body), Is.True,
                "pain shock did not fire on a real MobHuman: WP10-5's PainShockTarget wiring or the StatusEffects allow-list is missing.");
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(135)));
            Assert.That(Level(pain, body, comp), Is.EqualTo(1f).Within(0.0001f));
        });
    }

    /// <summary>
    /// T-PAIN-SHOCK (PLAN2 §6.2): pain driven by real damage, not by SetPain, crosses 130 and paralyses,
    /// disarms and opens the adrenaline window. First end-to-end exercise of WP9's open item 1.
    /// </summary>
    [Test]
    public async Task PainShockStunsAtThresholdTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f); // playtest 5: this test pins Onyx's figures

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedPainShockBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var pain = entities.System<PainSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = parts.Single(part => part.Component.PartType == BodyPartType.Arm).Id;

            var shockTarget = entities.GetComponent<PainShockTargetComponent>(body);
            Assert.That(shockTarget.Armed, Is.True);
            Assert.That(shockTarget.AdrenalineEnds, Is.Null);
            Assert.That(entities.HasComponent<StunnedComponent>(body), Is.False);

            // WOLFGATE (measured, P2-D16 — do not assume a single hit's number): a 60 Blunt routed hit on a
            // fracture-capable part produces pain twice. Once from the damage itself
            // (PainComponent.DamageMultipliers["Blunt"] = 0.87 -> 52.2), and once from the fracture it creates:
            // 60 is the Comminuted threshold with creationChance 1 (P2-D23), and BoneFractureWound's
            // Comminuted stage carries a `oneTime` WoundPainBehavior at painPerSeverity 0.8 -> 48, applied by
            // WoundStatusEffectSystem's WoundCreatedEvent handler. 52.2 + 48 = 100.2, under the 130 threshold.
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 60)));
            Assert.That(pain.GetRawPain(torso), Is.EqualTo(FixedPoint2.New(100.2)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(100.2)));
            Assert.That(entities.HasComponent<StunnedComponent>(body), Is.False,
                "pain shock fired below its 130 threshold.");
            Assert.That(shockTarget.Armed, Is.True);

            // The same hit on the arm takes the body to 200.4, which SetPain clamps to the 135 soft cap.
            // 135 >= 130, the target is armed, so UpdatePainShock paralyses for 2 s, screams, jitters and
            // opens the adrenaline window - all synchronously, from RaisePainChanged. M3: the arm, not the head,
            // because Blunt 30 or more to the head is a knockout (plan §3.6), and a knocked-out body takes no stun.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Blunt", 60)));
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(135)));
            Assert.That(entities.HasComponent<StunnedComponent>(body), Is.True,
                "no paralysis: P2-D24's `StatusEffects allowed: [Stun, KnockedDown, Jitter]` is what lets StunSystemOnyxCompat.TryUpdateParalyzeDuration succeed.");
            Assert.That(shockTarget.Armed, Is.False);
            Assert.That(shockTarget.AdrenalineEnds, Is.Not.Null);

            // M1a (OD5): adrenaline no longer takes 30% off the reading, so it never stands anyone up. The
            // window is wolfmed.adrenaline_seconds long, and the shock re-arms only under wolfmed.pain_shock_rearm.
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(135)));
            var cfg = server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
            Assert.That((shockTarget.AdrenalineEnds!.Value - timing.CurTime).TotalSeconds,
                Is.EqualTo(cfg.GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.AdrenalineSeconds)).Within(0.1));
            Assert.That(entities.System<Content.Shared._WF.Wolfmed.Consciousness.WolfmedBodyPainSystem>()
                .HasAdrenaline(body), Is.True);
        });
    }

    /// <summary>
    /// T-HIGH-PAIN (PLAN2 §6.2): the HighPainThreshold trait multiplies wound pain gain by 0.75. Also a canary
    /// that ModifyPainGainEvent's multiplier still defaults to 1 after WP9's fix.
    /// </summary>
    [Test]
    public async Task HighPainThresholdReducesWoundPainGainTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f); // playtest 5: this test pins Onyx's figures

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var control = entities.SpawnEntity("WolfmedPainControlBody", map.GridCoords);
            var traited = entities.SpawnEntity("WolfmedHighPainThresholdBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var pain = entities.System<PainSystem>();

            EntityUid Head(EntityUid body) =>
                graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Head).Id;

            var controlHead = Head(control);
            var traitedHead = Head(traited);

            Assert.That(routing.TryApplyPartDamage(control, controlHead, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(traited, traitedHead, Spec("Blunt", 10)));

            // WOLFGATE (measured): 10 Blunt * DamageMultipliers["Blunt"] 0.87 = 8.7, the same baseline
            // WoundDamageFoundationTest.cs:508 pins. 10 is below BoneFractureWound's Hairline threshold (20),
            // so GetGrade returns None and no fracture pain muddies the number (P2-D23).
            Assert.That(pain.GetRawPain(controlHead), Is.EqualTo(FixedPoint2.New(8.7)));

            // WOLFGATE (measured): HighPainThresholdSystem multiplies ModifyPainGainEvent by 0.75 on the BODY
            // (ChangePain raises it on the part's body, PainSystem.cs:232-241). FixedPoint2 truncates rather
            // than rounds (FixedPoint2.cs:101), so 870 * 0.75 = 652.5 -> 6.52, not 6.53.
            Assert.That(pain.GetRawPain(traitedHead), Is.EqualTo(FixedPoint2.New(6.52)));
            Assert.That(pain.GetRawPain(traitedHead), Is.LessThan(pain.GetRawPain(controlHead)));
        });
    }

    /// <summary>
    /// T-PAIN-NUMB (PLAN2 §6.2, gate for P2-D8): Wolfgate's shipped PainNumbness trait grants the legacy
    /// PainNumbnessComponent, which WP10-3 taught PainSystem.IsPainNumb to honour. Without that widening this
    /// test fails, which is the point.
    /// </summary>
    [Test]
    public async Task PainNumbnessSuppressesWoundPainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedPainNumbBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var pain = entities.System<PainSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 60)));
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 60)));

            // The raw value still accumulates exactly as on the shock fixture - IsPainNumb gates what the rest
            // of the game can see (GetPainBeforeAdrenaline returns zero), not what is stored.
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(135)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(pain.GetPain(torso), Is.EqualTo(FixedPoint2.Zero));

            // No visible pain means no pain shock: the same damage that paralyses WolfmedPainShockBody does
            // nothing here, so there is no forced Scream either (UpdatePainShock returns before the emote).
            Assert.That(entities.HasComponent<StunnedComponent>(body), Is.False,
                "pain shock fired on a pain-numb mob: PainSystem.IsPainNumb no longer honours the legacy PainNumbnessComponent (P2-D8).");
            Assert.That(entities.GetComponent<PainShockTargetComponent>(body).Armed, Is.True);
            Assert.That(entities.GetComponent<PainShockTargetComponent>(body).AdrenalineEnds, Is.Null);
        });
    }

    /// <summary>
    /// The exact level Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs writes into
    /// DamageOverlay.BruteLevel, mirrored here because an Overlay cannot be driven from a headless pair.
    /// </summary>
    private static float Level(PainSystem pain, EntityUid body, PainComponent comp)
    {
        var level = FixedPoint2.Min(1f, pain.GetPain((body, comp)) / comp.SoftPainCap).Float();
        return level < 0.05f ? 0f : level;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
