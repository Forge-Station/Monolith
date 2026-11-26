using System.Linq;
using Content.IntegrationTests.Fixtures;
// WOLFGATE(Wolfmed): D13 moves WoundBleedingSystem to Content.Server but keeps its Onyx namespace, so no extra using.
using Content.Server.Body.Components; // WOLFGATE(Wolfmed): BloodstreamComponent is server-only here.
using Content.Server.Body.Systems; // WOLFGATE(Wolfmed): BloodstreamSystem is server-only here.
using Content.Shared._Onyx.Medical.Tourniquet; // WOLFGATE(Wolfmed): P4-D10 relocates TourniquetSystem to Content.Server but keeps this namespace.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): §2.7 TryDetachPart.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

[TestFixture]
[TestOf(typeof(WoundBleedingSystem))]
public sealed class WoundBleedingTest : GameTest
{
    // WOLFGATE(Wolfmed): Shitmed body graph instead of Onyx's Nubody `InitialBody`; `Injurable` dropped (D19); Chest → Torso (D9).
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WoundBleedingBodyGraph
  name: ""wound bleeding body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
    head:
      part: HeadHuman

- type: entity
  id: WoundBleedingBody
  parent: [InventoryBase, MobBloodstream]
  components:
  - type: Body
    prototype: WoundBleedingBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost
";

    [Test]
    public async Task ProjectsTreatsAndTracksAttachmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // The literals below are Onyx's unscaled rates; wolfmed.bleed_rate slows every bleed in normal play.
        var bleedRateBefore = server.CfgMan.GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate);
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, 1f));
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var wounds = entityManager.System<WoundSystem>();
            var bleeding = entityManager.System<WoundBleedingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var bloodstream = entityManager.GetComponent<BloodstreamComponent>(body);

            Assert.That(wounds.CreateOrMergeWound(head, "SlashWound", 15), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(torso, "PiercingWound", 10), Is.Not.Null);
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(3f).Within(0.001f));
            Assert.That(entityManager.System<BloodstreamSystem>().TryModifyBleedAmount(body, 5f), Is.False);
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(3f).Within(0.001f));

            var headWound = wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head))).Single();
            Assert.That(bleeding.SetTreatment(headWound.Owner, BleedingTreatment.Bandaged));
            Assert.That(entityManager.GetComponent<WoundBleedingComponent>(headWound).CurrentRate,
                Is.EqualTo(0.375f).Within(0.001f));
            Assert.That(bleeding.SetTreatment(headWound.Owner, BleedingTreatment.Bandaged));
            Assert.That(entityManager.GetComponent<WoundBleedingComponent>(headWound).CurrentRate,
                Is.EqualTo(0.375f).Within(0.001f));
            Assert.That(bleeding.GetPartRate(torso), Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(1.875f).Within(0.001f));

            Assert.That(wfBody.TryDetachPart(head)); // WOLFGATE(Wolfmed)
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(graph.AttachPart(torso, "head", head)); // WOLFGATE(Wolfmed)
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(1.875f).Within(0.001f));

            Assert.That(bleeding.ModifyBodyBleeding(body, -20f));
            Assert.That(bleeding.GetPartRate(head), Is.Zero);
            Assert.That(bleeding.GetPartRate(torso), Is.Zero);
            Assert.That(bloodstream.BleedAmount, Is.Zero);

            entityManager.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());
            Assert.That(bloodstream.BleedAmount, Is.Zero);
            Assert.That(wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head))), Is.Empty);
            Assert.That(bleeding.ModifyBodyBleeding(body, 1f));
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(graph.GetBodyChildren(body).SelectMany(part =>
                    wounds.GetWounds((part.Id, entityManager.GetComponent<WoundableComponent>(part.Id))))
                .Single().Comp.Prototype, Is.EqualTo(new ProtoId<WoundPrototype>("SystemicBleedingWound")));
        });
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, bleedRateBefore));
    }

    [Test]
    public async Task BandageReducesBleedingAndDamageReopensWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
            var parts = entityManager.System<SharedBodySystem>().GetBodyChildren(body).ToList();
            var part = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var wounds = entityManager.System<WoundSystem>();
            var bleeding = entityManager.System<WoundBleedingSystem>();
            var wound = wounds.CreateOrMergeWound(part, "SlashWound", 30)!.Value;

            Assert.That(bleeding.ReduceBleeding(wound, 10));
            Assert.That(entityManager.GetComponent<WoundBleedingComponent>(wound).BleedingSeverity,
                Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(wounds.GetWounds((part, entityManager.GetComponent<WoundableComponent>(part))).Count(), Is.EqualTo(1));

            Assert.That(bleeding.ReduceBleeding(wound, 20));
            Assert.That(bleeding.GetPartRate(part), Is.Zero);
            Assert.That(entityManager.HasComponent<WoundBleedingComponent>(wound), Is.False);
            Assert.That(wounds.CloseWound(wound));
            Assert.That(wounds.CreateOrMergeWound(part, "SlashWound", 5), Is.EqualTo(wound));
            Assert.That(entityManager.GetComponent<WoundComponent>(wound).State, Is.EqualTo(WoundState.Open));
            Assert.That(entityManager.HasComponent<WoundBleedingComponent>(wound), Is.True);
            // WOLFGATE(Wolfmed): Onyx's literal 5 is stale against its own code. WoundSystem.SetWoundState -> and
            // WoundSystem.cs (both byte-identical to Onyx) re-add WoundBleedingComponent with
            // `BleedingSeverity = wound.Comp.Severity` the moment CloseWound re-syncs, so a fully bandaged
            // severity-30 wound reopening with +5 lands at 35, not 5. Same stale-literal class as the
            // fracture-grade thresholds in WoundFractureTest. Contract kept: reopening restores bleeding.
            Assert.That(entityManager.GetComponent<WoundBleedingComponent>(wound).BleedingSeverity,
                Is.EqualTo(FixedPoint2.New(35)));
            Assert.That(bleeding.GetPartRate(part), Is.GreaterThan(0f));
        });
    }

    /// <summary>
    /// Onyx's TourniquetStopsOnlySelectedPartTest, restored in WP12-9 now that WP12-3 has vendored
    /// TourniquetSystem and swapped the shipped `Tourniquet` entity's Healing block for a real one (PROTO D).
    /// The phase-1 skip note this replaces was correct only while neither existed.
    /// </summary>
    /// <remarks>PLAN4 §6.2 T-TOURNIQUET.</remarks>
    [Test]
    public async Task TourniquetStopsOnlySelectedPartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wounds = entityManager.System<WoundSystem>();
            var bleeding = entityManager.System<WoundBleedingSystem>();
            var tourniquet = entityManager.System<TourniquetSystem>(); // WOLFGATE(Wolfmed): P4-D10 puts it in Content.Server.
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            // WOLFGATE(Wolfmed): D9, Onyx targets BodyPartType.Chest here.
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            // SlashWound's bleeding behaviour has `minimumSeverity: 9`, so 10 is the smallest severity that
            // actually bleeds on both parts - the same correction AutomaticClottingDeadlineTest carries.
            Assert.That(wounds.CreateOrMergeWound(head, "SlashWound", 10), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(torso, "SlashWound", 10), Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(head), Is.GreaterThan(0f));
                Assert.That(bleeding.GetPartRate(torso), Is.GreaterThan(0f));
            });

            // WOLFGATE(Wolfmed): Apply is called directly (PLAN4 §6.1 trap 9). The do-after and the
            // TargetingComponent round trip TryStart goes through are UI layers, not the mechanic.
            Assert.That(tourniquet.Apply(body, head), Is.True);

            Assert.Multiple(() =>
            {
                // BleedingTreatment.Clamped is a 0f multiplier (WoundBleedingSystem.cs:469), applied to every
                // bleeding wound on the part.
                Assert.That(bleeding.GetPartRate(head), Is.Zero);
                Assert.That(bleeding.GetPartRate(torso), Is.GreaterThan(0f),
                    "a tourniquet must clamp only the part it was applied to.");
            });

            // WOLFGATE(Wolfmed): a check Onyx's own test never made. CanApply requires GetPartRate(part) > 0, so a
            // second application to an already-clamped limb is refused rather than silently re-clamping.
            Assert.That(tourniquet.Apply(body, head), Is.False);
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-AMP-THRESHOLD. Restored in WP11-5 now that WP11-1 has vendored AmputationSystem (D26
    /// lifted); the phase-1 skip note this replaces was correct only while nothing could set Severable.
    /// </summary>
    [Test]
    public async Task TraumaticAmputationCreatesSevereStumpBleedingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wounds = entityManager.System<WoundSystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            // WOLFGATE(Wolfmed): D9, Onyx targets BodyPartType.Chest here.
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var bloodstream = entityManager.GetComponent<BloodstreamComponent>(body);

            // WOLFGATE(Wolfmed): HeadHuman inherits WFWolfmedBaseHead, so its Slash amputation threshold is 200
            // (_WF/Wolfmed/Body/parts.yml). progress = 200/200 = 1.0 -> Severable, and the threshold hit
            // itself never detaches (AmputationSystem.HandlePartDamageApplied's first branch returns).
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 200)));
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.True,
                    "reaching the threshold only arms the limb; it must not come off on that hit.");
                Assert.That(entityManager.GetComponent<WoundableComponent>(head).Severable, Is.True);
            });

            // WOLFGATE(Wolfmed): post-hit 215 -> progress 1.075 >= SeverableResetRatio 0.8; damageBeforeHit is
            // 215 - 15 = 200 so ReachedThreshold is true; IsFinishingHit reads Slash 15 against
            // WoundHostComponent.DefaultDismembermentFinishingDamage["Slash"] = 15 (parts.yml's per-part
            // dict lists only Piercing/Heat, so Slash still falls back to Onyx's host default) -> detach.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 15)));

            var torsoWounds = wounds.GetWounds((torso, entityManager.GetComponent<WoundableComponent>(torso)))
                .ToList();
            var dismemberment = torsoWounds
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("DismembermentWound"))
                .ToList();
            var consequence = torsoWounds
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("AmputationConsequenceWound"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.False);
                // WOLFGATE(Wolfmed): the severed head is a live entity that left the body's container tree, not a
                // deleted one - Shitmed's DropPart re-parents it to the grid/map.
                Assert.That(entityManager.Deleted(head), Is.False);
                Assert.That(entityManager.GetComponent<TransformComponent>(head).ParentUid,
                    Is.Not.EqualTo(body));

                // WOLFGATE(Wolfmed): WoundHostComponent.DismembermentSeverities[Head] = 200.
                Assert.That(dismemberment, Has.Count.EqualTo(1));
                Assert.That(dismemberment[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(200)));
                // WOLFGATE(Wolfmed): WolfmedBodyPartComponent.AmputationConsequenceSeverity defaults to 35 and
                // no prototype overrides it on BaseTorso; the severity is read off the PARENT stump.
                Assert.That(consequence, Has.Count.EqualTo(1));
                Assert.That(consequence[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(35)));

                // WOLFGATE(Wolfmed): P3-D14: Onyx asserts Is.GreaterThanOrEqualTo(40f). That literal is stale for
                // Wolfgate: DismembermentWound's raw rate is 0.2 * 200 * awakeMultiplier 1.5 = 60, but
                // BloodstreamSystem clamps BleedAmount to BloodstreamComponent.MaxBleedAmount, which is
                // 10 here (WoundBleedingBody inherits MobBloodstream and never raises it). The contract
                // "a traumatic amputation bleeds as hard as this body can bleed" is what is asserted.
                Assert.That(bloodstream.BleedAmount,
                    Is.EqualTo(bloodstream.MaxBleedAmount).Within(0.001f));
            });
        });
    }

    [Test]
    public async Task AutomaticClottingDeadlineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid light = default;
        EntityUid heavy = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopEnabled, true);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopSecondsPerSeverity, 0.05f);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopMinSeconds, 0f);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopMaxSeconds, 10f);

                body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
                var parts = entityManager.System<SharedBodySystem>().GetBodyChildren(body).ToList();
                light = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9
                heavy = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
                var wounds = entityManager.System<WoundSystem>();
                // WOLFGATE(Wolfmed): Onyx's severities (1 and 3) are below SlashWound's own `minimumSeverity: 9`
                // bleeding threshold in its pinned wounds.yml, so neither wound ever bleeds and the test
                // measures nothing. 10 and 30 keep the same shape (a short deadline and a long one) above it.
                wounds.CreateOrMergeWound(light, new ProtoId<WoundPrototype>("SlashWound"), 10);
                wounds.CreateOrMergeWound(heavy, new ProtoId<WoundPrototype>("SlashWound"), 30);
            });

            await RunSeconds(0.8f);
            await server.WaitAssertion(() =>
            {
                var bleeding = entityManager.System<WoundBleedingSystem>();
                Assert.That(bleeding.GetPartRate(light), Is.Zero);
                Assert.That(bleeding.GetPartRate(heavy), Is.GreaterThan(0f));
            });

            await server.WaitAssertion(() =>
            {
                var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
                Assert.That(wfBody.TryDetachPart(heavy));
            });
            await RunSeconds(1.2f);
            await server.WaitAssertion(() =>
            {
                var graph = entityManager.System<SharedBodySystem>();
                var bleeding = entityManager.System<WoundBleedingSystem>();
                Assert.That(bleeding.GetPartRate(heavy), Is.Zero);
                Assert.That(graph.AttachPart(light, "head", heavy)); // WOLFGATE(Wolfmed)
                Assert.That(bleeding.GetPartRate(heavy), Is.Zero);

                var wound = entityManager.System<WoundSystem>()
                    .GetWounds((heavy, entityManager.GetComponent<WoundableComponent>(heavy))).Single();
                Assert.That(bleeding.GetPartRate(heavy), Is.Zero);

                Assert.That(entityManager.System<WoundSystem>().ChangeSeverity(wound.Owner, 1));
                Assert.That(bleeding.GetPartRate(heavy), Is.GreaterThan(0f));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopEnabled,
                    CCVars.WoundsBleedingAutoStopEnabled.DefaultValue);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopSecondsPerSeverity,
                    CCVars.WoundsBleedingAutoStopSecondsPerSeverity.DefaultValue);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopMinSeconds,
                    CCVars.WoundsBleedingAutoStopMinSeconds.DefaultValue);
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopMaxSeconds,
                    CCVars.WoundsBleedingAutoStopMaxSeconds.DefaultValue);
            });
        }
    }

    [Test]
    public async Task AutomaticClottingDisabledTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();
        EntityUid part = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                configuration.SetCVar(CCVars.WoundsBleedingAutoStopEnabled, false);
                var body = entityManager.SpawnEntity("WoundBleedingBody", map.GridCoords);
                part = entityManager.System<SharedBodySystem>().GetBodyChildren(body).First().Id;
                // WOLFGATE(Wolfmed): as in AutomaticClottingDeadlineTest, Onyx's severity 1 is below SlashWound's
                // `minimumSeverity: 9`, so the wound never bleeds and "still bleeding after a second" is vacuous.
                entityManager.System<WoundSystem>()
                    .CreateOrMergeWound(part, new ProtoId<WoundPrototype>("SlashWound"), 10);
            });
            await RunSeconds(1f);
            await server.WaitAssertion(() =>
                Assert.That(entityManager.System<WoundBleedingSystem>().GetPartRate(part), Is.GreaterThan(0f)));
        }
        finally
        {
            await server.WaitPost(() => configuration.SetCVar(CCVars.WoundsBleedingAutoStopEnabled,
                CCVars.WoundsBleedingAutoStopEnabled.DefaultValue));
        }
    }

    private static DamageSpecifier Spec(string type, int amount)
    {
        return new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
        };
    }
}
