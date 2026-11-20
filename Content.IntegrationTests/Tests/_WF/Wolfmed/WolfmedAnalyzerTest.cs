#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Wolfmed.Medical;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.MedicalScanner;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Body.Components; // WOLFGATE: BloodstreamComponent is server-only here.
using Content.Server.Medical; // WOLFGATE: HOOK 23's builders are a _WF partial of this server system.
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE (UI2): the analyzer wound categories.
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.ContentPack; // WOLFGATE (UI2): reads the analyzer icon RSI off disk.
using Robust.Shared.GameObjects;
using Robust.Shared.Localization; // WOLFGATE (T-P5-18): resolves the mechanical wound LocIds for real.
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility; // WOLFGATE (UI2): ResPath.

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The four analyzer payload builders HOOK 23 appends to the scanned-user message.
/// </summary>
/// <remarks>
/// PLAN4 §6.2 T-AN-GATE / -FINDINGS / -CLEARS / -CLOT / -PAIN / -ORGANS / -CHEM / -VITAL (WP12-9). P4-D26
/// makes all four builders public precisely so they can be called here: UpdateScannedUser itself ends in
/// ServerSendUiMessage, which has no headless capture point.
/// </remarks>
[TestFixture]
[TestOf(typeof(HealthAnalyzerSystem))]
public sealed class WolfmedAnalyzerTest : GameTest
{
    // WOLFGATE: WolfmedAnalyzerPlainTarget is the D2 control - Damageable and nothing else. The
    // SurgeryTarget-without-WoundHost case (a borg, a Protogen) is the shape §8.5 risk 20 is about: Onyx gates
    // its builder on SurgeryTargetComponent, which would report "no findings" instead of "unavailable".
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedAnalyzerPlainTarget
  name: ""wolfmed analyzer plain target""
  components:
  - type: Damageable
    damageContainer: Biological

- type: entity
  id: WolfmedAnalyzerSurgeryTarget
  parent: WolfmedSurgeryControlBody
  components:
  - type: SurgeryTarget
";

    /// <summary>PLAN4 §6.2 T-AN-GATE.</summary>
    [Test]
    public async Task AnalyzerBuildersReturnNullForNonHostsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var plain = entities.SpawnEntity("WolfmedAnalyzerPlainTarget", map.GridCoords);
            var surgical = entities.SpawnEntity("WolfmedAnalyzerSurgeryTarget", map.GridCoords);

            Assert.That(entities.HasComponent<WoundHostComponent>(surgical), Is.False);

            Assert.Multiple(() =>
            {
                // null means "diagnostics unavailable", which is what the client renders as a hidden panel.
                Assert.That(analyzer.BuildWoundDiagnostics(plain), Is.Null);
                Assert.That(analyzer.BuildOrganInfo(plain), Is.Null);
                Assert.That(analyzer.BuildVitalDamage(plain), Is.Null);
                Assert.That(analyzer.BuildWoundDiagnostics(surgical), Is.Null,
                    "a Shitmed surgery target that is not a wound host must read as unavailable, not empty (§8.5 risk 20).");
                Assert.That(analyzer.BuildOrganInfo(surgical), Is.Null);

                // BuildChemicalInfo is deliberately not wound-gated: it returns an empty list, never null.
                var chemicals = analyzer.BuildChemicalInfo(plain, null);
                Assert.That(chemicals, Is.Not.Null);
                Assert.That(chemicals, Is.Empty);
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-FINDINGS.</summary>
    [Test]
    public async Task WoundDiagnosticsReportsInjuredPartsOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            // SlashWound's bleeding behaviour is `minimumSeverity: 9`, so 10 is the smallest severity that
            // actually bleeds.
            Assert.That(wounds.CreateOrMergeWound(head, "SlashWound", 10), Is.Not.Null);
            // P2-D23: 75 Blunt is the only deterministic fracture (Comminuted threshold 60, creationChance 1).
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, arm, Spec("Blunt", 75)));
            // A scar is an ordinary MedicalScarWound carrying WoundScarComponent, which is what ScarCount counts.
            var torsoWound = wounds.CreateOrMergeWound(torso, "BluntWound", 30)!.Value;
            Assert.That(entities.System<WoundScarSystem>().CreateScar(torsoWound), Is.Not.Null);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(diagnostics!.Parts[TargetBodyPart.Head].BleedingRate, Is.GreaterThan(0f));
                Assert.That(diagnostics.Parts[TargetBodyPart.LeftArm].Fracture,
                    Is.EqualTo(FractureGrade.Comminuted));
                Assert.That(diagnostics.Parts[TargetBodyPart.LeftArm].FractureTreatment,
                    Is.EqualTo(FractureTreatment.None));
                Assert.That(diagnostics.Parts[TargetBodyPart.Torso].ScarCount, Is.EqualTo(1));

                // HasFindings gates the dictionary: a clean part is absent, so the client must never assume
                // ten keys.
                Assert.That(diagnostics.Parts.ContainsKey(TargetBodyPart.RightFoot), Is.False);

                // D9: SharedTargetingSystem.GetValidParts() has Groin commented out and WG's body graph has no
                // Groin part, so GetTargetBodyPart can never produce this key.
                Assert.That(diagnostics.Parts.ContainsKey(TargetBodyPart.Groin), Is.False,
                    "Wolfgate must never emit a Groin diagnostic row (D9).");
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-AN-CLEARS. The acceptance criterion for WP12-5's surgeries: what the surgeon fixed stops
    /// being reported.
    /// </summary>
    [Test]
    public async Task TreatmentDisappearsFromTheReadoutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var graph = entities.System<SharedBodySystem>();
            var fractures = entities.System<WoundFractureSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var arm = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(entities.System<WoundSystem>().CreateOrMergeWound(head, "SlashWound", 10), Is.Not.Null);
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, arm, Spec("Blunt", 75)));

            var before = analyzer.BuildWoundDiagnostics(body)!;
            Assert.Multiple(() =>
            {
                Assert.That(before.Parts[TargetBodyPart.LeftArm].Fracture, Is.EqualTo(FractureGrade.Comminuted));
                Assert.That(before.Parts.ContainsKey(TargetBodyPart.Head), Is.True);
            });

            // `removeWoundWhenMended: true` on OrganicFractureProfile deletes the wound outright.
            Assert.That(fractures.TryMend(fractures.GetFracture(arm)!.Value.Owner));

            var mended = analyzer.BuildWoundDiagnostics(body)!;
            // WOLFGATE: PLAN4 predicts the LeftArm row disappears. It does not, and should not: the same
            // 75-Blunt hit that broke the bone also left a BluntWound and part pain, both of which are real
            // findings. What must disappear is the FRACTURE, which is the thing the surgery treated.
            Assert.That(mended.Parts[TargetBodyPart.LeftArm].Fracture, Is.EqualTo(FractureGrade.None),
                "a mended fracture must stop being reported.");

            // A part that leaves the body leaves the readout with it - GetBodyChildren is the builder's source.
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(head));
            var detached = analyzer.BuildWoundDiagnostics(body)!;
            Assert.That(detached.Parts.ContainsKey(TargetBodyPart.Head), Is.False,
                "a detached part must vanish from the readout entirely.");
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-CLOT.</summary>
    [Test]
    public async Task ClottingPhaseIsClassifiedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var parts = graph.GetBodyChildren(body).ToList();

            EntityUid Part(BodyPartType type, BodyPartSymmetry? symmetry = null) => parts.Single(part =>
                part.Component.PartType == type &&
                (symmetry == null || part.Component.Symmetry == symmetry)).Id;

            var inProgress = Part(BodyPartType.Head);
            var complete = Part(BodyPartType.Torso);
            var none = Part(BodyPartType.Arm, BodyPartSymmetry.Left);
            var mixed = Part(BodyPartType.Arm, BodyPartSymmetry.Right);
            var notApplicable = Part(BodyPartType.Leg, BodyPartSymmetry.Left);

            // Every wound is created FIRST and only then tweaked: WoundSystem.CreateOrMergeWound ends in
            // WoundBleedingSystem.RefreshBody, which recomputes CurrentRate for every bleeding wound on the
            // body, so interleaving creation and mutation would silently undo the earlier rows.
            var inProgressWound = Bleed(entities, wounds, inProgress, "SlashWound");
            var completeWound = Bleed(entities, wounds, complete, "SlashWound");
            var noneWound = Bleed(entities, wounds, none, "SlashWound");
            var mixedInProgress = Bleed(entities, wounds, mixed, "SlashWound");
            var mixedComplete = Bleed(entities, wounds, mixed, "PiercingWound");

            // A part with findings but no bleeding wound at all. AmputationConsequenceWound has no bleeding
            // behaviour and no damage types, so it is a finding (a visible wound) with nothing to clot.
            Assert.That(wounds.CreateOrMergeWound(notApplicable, "AmputationConsequenceWound", 20), Is.Not.Null);

            // The three phases are pure functions of two WoundBleedingComponent fields the builder reads, so the
            // fields are set directly - driving the auto-clotting CVars to reach each state would take real time
            // and could not produce Mixed on one part at all.
            // `none` needs its AutomaticClottingAt cleared explicitly, not merely left alone:
            // wounds.bleeding_auto_stop_enabled defaults to true, so WoundBleedingSystem schedules a clotting
            // deadline on every fresh bleeder and an untouched wound reads as InProgress.
            SetPhase(entities, noneWound);
            SetPhase(entities, inProgressWound, clottingAt: timing.CurTime + TimeSpan.FromSeconds(5));
            SetPhase(entities, completeWound, naturalClotting: 1f, currentRate: 0f);
            SetPhase(entities, mixedInProgress, clottingAt: timing.CurTime + TimeSpan.FromSeconds(5));
            SetPhase(entities, mixedComplete, naturalClotting: 1f, currentRate: 0f);

            var diagnostics = analyzer.BuildWoundDiagnostics(body)!;

            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.Parts[TargetBodyPart.Head].ClottingPhase,
                    Is.EqualTo(HealthAnalyzerClottingPhase.InProgress));
                Assert.That(diagnostics.Parts[TargetBodyPart.Torso].ClottingPhase,
                    Is.EqualTo(HealthAnalyzerClottingPhase.Complete));
                Assert.That(diagnostics.Parts[TargetBodyPart.LeftArm].ClottingPhase,
                    Is.EqualTo(HealthAnalyzerClottingPhase.None));
                Assert.That(diagnostics.Parts[TargetBodyPart.RightArm].ClottingPhase,
                    Is.EqualTo(HealthAnalyzerClottingPhase.Mixed),
                    "two bleeders in different phases on one part collapse to Mixed.");
                Assert.That(diagnostics.Parts[TargetBodyPart.LeftLeg].ClottingPhase,
                    Is.EqualTo(HealthAnalyzerClottingPhase.NotApplicable));
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-PAIN.</summary>
    [Test]
    public async Task WoundDiagnosticsCarriesPartPainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var pain = entities.System<PainSystem>();
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(body).Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(pain.ChangePain(arm, FixedPoint2.New(20)));

            var painComp = entities.GetComponent<PainComponent>(arm);
            var diagnostics = analyzer.BuildWoundDiagnostics(body)!;

            // The readout is the EFFECTIVE pain (PainSystem.GetPain), the same number the phase-2 HUD draws -
            // not the raw value, so a painkillered patient reads as comfortable.
            Assert.That(diagnostics.Parts[TargetBodyPart.LeftArm].Pain,
                Is.EqualTo(pain.GetPain((arm, painComp))));
            Assert.That(diagnostics.Parts[TargetBodyPart.LeftArm].Pain, Is.GreaterThan(FixedPoint2.Zero));
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-ORGANS.</summary>
    [Test]
    public async Task OrganRowsAreOrderedAndScopedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var rows = analyzer.BuildOrganInfo(body);

            Assert.That(rows, Is.Not.Null);
            // Seven, not Onyx's ten: WG's human body graph has no `ears`, `tongue` or `appendix` slot, so
            // OrganOrder's 2, 3 and 8 are simply never produced.
            Assert.That(rows, Has.Count.EqualTo(7));
            Assert.That(rows!.Select(row => row.Order), Is.EqualTo(new[] { 0, 1, 4, 5, 6, 7, 9 }),
                "brain(0) eyes(1) lungs(4) heart(5) liver(6) stomach(7) kidneys(9), ascending.");
            Assert.That(rows.Select(row => row.MaxHealth),
                Is.EqualTo(new[] { 15, 15, 25, 25, 25, 25, 25 }.Select(FixedPoint2.New)), "brain and eyes at the component default, torso organs at 25 (playtest 4).");

            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var heart = graph.GetPartOrgans(torso)
                .Single(organ => entities.GetComponent<OrganComponent>(organ.Id).SlotId == "heart").Id;

            // No tick: OrganHealthSystem.Update would destroy the organ on the next one, and the row with it.
            entities.System<OrganHealthSystem>()
                .SetHealth((heart, entities.GetComponent<WolfmedOrganComponent>(heart)), FixedPoint2.Zero);

            var damaged = analyzer.BuildOrganInfo(body)!;
            Assert.That(damaged.Single(row => row.Order == 5).Health, Is.EqualTo(FixedPoint2.Zero));

            // A wound host whose body graph declares no organs at all: an EMPTY list, not null. Null is
            // reserved for "not a wound host" and the client renders the two differently.
            var organless = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(organless), Is.True);
            var none = analyzer.BuildOrganInfo(organless);
            Assert.Multiple(() =>
            {
                Assert.That(none, Is.Not.Null);
                Assert.That(none, Is.Empty);
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-CHEM.</summary>
    [Test]
    public async Task ChemicalInfoListsEverySolutionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);

            // WolfmedMedicalPatchTest's inert test chem - nothing metabolises it, so it is still there when the
            // builder runs.
            Assert.That(solutions.TryGetSolution(body, bloodstream.ChemicalSolutionName, out var chemSolution, out _));
            Assert.That(solutions.TryAddReagent(chemSolution!.Value, "WolfmedPatchTestChem", FixedPoint2.New(5)));

            var rows = analyzer.BuildChemicalInfo(body, bloodstream);
            Assert.That(rows, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(rows!.Any(row => row.Type == HealthAnalyzerSolutionType.Bloodstream), Is.True);
                // WOLFGATE: the wire value stays Metabolites while the underlying solution is Wolfgate's
                // `chemicals` one (BloodstreamComponent.ChemicalSolutionName) - WP12-6's deliberate mapping.
                var metabolites = rows.Single(row => row.Type == HealthAnalyzerSolutionType.Metabolites);
                Assert.That(metabolites.Reagents.Single(r => r.Prototype == "WolfmedPatchTestChem").Quantity,
                    Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(rows.Any(row => row.Type == HealthAnalyzerSolutionType.Stomach), Is.True,
                    "a human's stomach solution must be listed.");
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-AN-VITAL.</summary>
    [Test]
    public async Task VitalDamageDiffersFromProjectedTotalTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var thresholds = entities.System<MobThresholdSystem>();
            var damageable = entities.GetComponent<DamageableComponent>(body);
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(body).Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(analyzer.BuildVitalDamage(body), Is.EqualTo(FixedPoint2.Zero));

            // An arm is not a vital part: CheckVitalDamage sums Head + Torso plus SystemicDamageComponent only.
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, arm, Spec("Blunt", 40)));

            var vital = analyzer.BuildVitalDamage(body);
            Assert.Multiple(() =>
            {
                Assert.That(vital, Is.EqualTo(thresholds.CheckVitalDamage(body, damageable)),
                    "the readout must be the same number that decides crit and death.");
                Assert.That(vital, Is.EqualTo(FixedPoint2.Zero),
                    "damage on a non-vital limb must not count toward crit.");
                Assert.That(entities.System<WolfmedDamageableSystem>().GetTotalDamage((body, damageable)),
                    Is.GreaterThan(FixedPoint2.Zero),
                    "while the body's projected total does move - which is exactly the divergence this row exists to show.");
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-18 (WP13-6). A mechanical wound must reach the analyzer panel as real, resolvable
    /// English, not as a raw LocId placeholder. This is the whole of P5-5 that is assertable headlessly: the
    /// per-wound name and stage-name path, which WP13-0 shipped complete.
    /// </summary>
    /// <remarks>
    /// WOLFGATE (WP13-6): PLAN5's own wording for this test ends with "<c>Mechanical == true</c> on the
    /// payload". That member does not exist. WP13-5 shipped only the locale half of §2.6 — the four new
    /// <c>-mechanical</c>/<c>-frame</c> keys — and explicitly deferred the C# half (the appended
    /// <c>bool Mechanical</c> on <see cref="HealthAnalyzerWoundDiagnostic"/> and the three consumer branches),
    /// so there is nothing to assert it against yet. When that lands, add the flag assertion here; the rest of
    /// this test is unaffected, exactly as WP13-5's handoff predicted.
    /// </remarks>
    [Test]
    public async Task MechanicalWoundDiagnosticTextResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            // Severity 30 sits in the Moderate band: IpcMechanicalDamageWound's stages are
            // Minor 0 / Moderate 25 / Severe 50 / Critical 80 (_Onyx/Wounds/wounds.yml), so
            // WoundPrototype.GetStageDefinition returns the Moderate one and no other.
            Assert.That(entities.System<WoundSystem>()
                .CreateOrMergeWound(arm, "IpcMechanicalDamageWound", 30), Is.Not.Null);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            var visible = diagnostics!.Parts[TargetBodyPart.LeftArm].VisibleWounds;
            Assert.That(visible, Has.Count.EqualTo(1),
                "one chassis wound, one row - the builder groups by (name, stage) and counts.");

            Assert.Multiple(() =>
            {
                Assert.That(visible[0].Name.ToString(), Is.EqualTo("wound-name-ipc-mechanical-damage"));
                Assert.That(visible[0].StageName?.ToString(), Is.EqualTo("wound-stage-mechanical-moderate"));
                Assert.That(visible[0].Count, Is.EqualTo(1));

                // The point of the test: both keys must actually exist in
                // Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl, or a medic scanning an IPC reads
                // the raw key. Fluent returns the key itself when it cannot resolve one.
                Assert.That(locale.GetString(visible[0].Name), Is.EqualTo("chassis damage"));
                Assert.That(locale.GetString(visible[0].StageName!.Value), Is.EqualTo("moderate"));
            });
        });
    }

    /// <summary>
    /// UI2. Every visible wound reaches the panel carrying the category its card files it under. All three
    /// resolution paths are covered: an explicit field on a Wolfmed wound, an explicit field on a vendored
    /// Onyx wound, and the damage-type derivation for a wound that declares nothing.
    /// </summary>
    [Test]
    public async Task VisibleWoundsCarryTheirCategoryTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var wounds = entities.System<WoundSystem>();
            var graph = entities.System<SharedBodySystem>();

            EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry? symmetry = null) =>
                graph.GetBodyChildren(body).Single(part =>
                    part.Component.PartType == type &&
                    (symmetry == null || part.Component.Symmetry == symmetry)).Id;

            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            // `ruleOnly` gates only the default per-damage-type pass; a direct create is still allowed,
            // which is what lets this assert the ballistic wound without firing a gun.
            Assert.That(wounds.CreateOrMergeWound(
                Part(human, BodyPartType.Arm, BodyPartSymmetry.Left), "WFWolfmedGunshotWound", 20), Is.Not.Null);
            // BurnWound declares no analyzerCategory at all: Heat in its damageTypes is what makes it a burn.
            Assert.That(wounds.CreateOrMergeWound(Part(human, BodyPartType.Head), "BurnWound", 20), Is.Not.Null);

            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            Assert.That(wounds.CreateOrMergeWound(
                Part(ipc, BodyPartType.Arm, BodyPartSymmetry.Left), "IpcMechanicalDamageWound", 30), Is.Not.Null);

            var organic = analyzer.BuildWoundDiagnostics(human)!;
            var chassis = analyzer.BuildWoundDiagnostics(ipc)!;

            Assert.Multiple(() =>
            {
                Assert.That(Category(organic, TargetBodyPart.LeftArm, "wolfmed-wound-name-gunshot"),
                    Is.EqualTo(WolfmedWoundCategory.Ballistic), "an explicit analyzerCategory wins.");
                Assert.That(Category(organic, TargetBodyPart.Head, "wound-name-burn"),
                    Is.EqualTo(WolfmedWoundCategory.Burn), "Heat damage derives Burn with no field set.");
                Assert.That(Category(chassis, TargetBodyPart.LeftArm, "wound-name-ipc-mechanical-damage"),
                    Is.EqualTo(WolfmedWoundCategory.Mechanical),
                    "a chassis wound must never derive Blunt from the first damage type it lists.");
            });
        });
    }

    /// <summary>
    /// UI2. Nothing the panel draws may come up empty: every wound prototype resolves to a category, and
    /// every category and condition the panel can draw has a name and a pictogram.
    /// </summary>
    [Test]
    public async Task EveryWoundCategoryIsNamedAndDrawableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var meta = resources.ContentFileReadAllText(IconRsi / "meta.json");

            Assert.Multiple(() =>
            {
                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>().OrderBy(wound => wound.ID))
                {
                    Assert.That(WolfmedWoundCategories.All,
                        Does.Contain(WolfmedWoundCategories.Resolve(wound)),
                        $"{wound.ID} resolves to a category outside the enum.");
                }

                foreach (var category in WolfmedWoundCategories.All)
                {
                    Assert.That(locale.HasString(WolfmedWoundCategories.NameKey(category)), Is.True,
                        $"category {category} has no name string.");
                    AssertIcon(resources, meta, WolfmedWoundCategories.IconState(category));
                }

                // The part-level condition glyphs. The list is the second half of
                // Tools/_WF/Wolfmed/gen_analyzer_icons.py's CONDITION_STATES; the RSI is generated, so the
                // failure this catches is a state renamed in the script and not in the panel.
                foreach (var state in ConditionIcons)
                    AssertIcon(resources, meta, state);

                // UI4: the procedure window's glyphs, for the steps with no item of their own.
                foreach (var state in ProcedureIcons)
                    AssertIcon(resources, meta, state);
            });
        });
    }

    private static readonly ResPath IconRsi = new("/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi");

    private static readonly string[] ConditionIcons =
    [
        "fracture", "bleeding", "internal_bleeding", "embedded", "necrosis", "overheating",
        "scar", "pain", "impaired", "clotting", "sepsis", "blood_low",
    ];

    /// <summary>UI4: gen_analyzer_icons.py's PROCEDURE_STATES, used by the treatment window.</summary>
    private static readonly string[] ProcedureIcons = ["surgery", "reagent", "warning", "done", "step"];

    private static void AssertIcon(IResourceManager resources, string meta, string state)
    {
        Assert.That(resources.ContentFileExists(IconRsi / (state + ".png")), Is.True,
            $"analyzer_icons.rsi has no {state}.png.");
        Assert.That(meta, Does.Contain($"\"name\": \"{state}\""),
            $"analyzer_icons.rsi meta.json does not list {state}.");
    }

    private static WolfmedWoundCategory Category(
        HealthAnalyzerWoundDiagnostics diagnostics,
        TargetBodyPart part,
        string name) =>
        diagnostics.Parts[part].VisibleWounds.Single(wound => wound.Name.Id == name).Category;

    /// <summary>
    /// M1a (plan §5.5): the vitals block heads the panel. The diagnostics carry it for a wound host, a chassis
    /// gets the machine words, and the client's panel draws the state-and-cause line as its first row, the
    /// same text <see cref="WolfmedVitalsText"/> gives the tests.
    /// </summary>
    [Test]
    public async Task VitalsBlockHeadsThePanelTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        var server = Pair.Server;
        var client = Pair.Client;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        HealthAnalyzerScannedUserMessage? message = null;
        var expected = string.Empty;

        await server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(entities);
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            s.SetBlood(body, 0.45f);
            s.Consciousness.Refresh(body);
            s.Life.UpdateVitalSigns(body);

            var vitals = analyzer.BuildWoundDiagnostics(body)!.Vitals;
            Assert.That(vitals, Is.Not.Null, "a wound host's diagnostics carry no vitals block.");
            Assert.Multiple(() =>
            {
                Assert.That(vitals!.State, Is.EqualTo(WolfmedVitalsState.Downed));
                Assert.That(vitals.Cause, Is.EqualTo(WolfmedCause.Blood));
                Assert.That(vitals.Mechanical, Is.False);
                Assert.That(analyzer.BuildWoundDiagnostics(ipc)!.Vitals!.Mechanical, Is.True);
                Assert.That(WolfmedVitalsText.StateLine(analyzer.BuildWoundDiagnostics(ipc)!.Vitals!),
                    Is.EqualTo("ONLINE"));
            });

            message = analyzer.WolfmedBuildScanMessage(body);
            expected = WolfmedVitalsText.StateLine(vitals!);
        });

        await client.WaitAssertion(() =>
        {
            var panel = new WolfmedDiagnosticPanel();
            panel.Populate(message!);
            var texts = Descendants(panel).OfType<RichTextLabel>()
                .Select(label => FormattedMessage.RemoveMarkupPermissive(label.GetMessage() ?? string.Empty))
                .ToList();

            Assert.That(texts, Is.Not.Empty, "the panel drew no banner rows.");
            Assert.That(texts[0], Does.StartWith(expected), "the vitals block is not the panel's first row.");
            // Playtest 3: the compact block. Normal breathing is not listed; the transfusion is on "Do first".
            Assert.That(texts[0], Does.Contain("Pulse weak, rapid").And.Contain("Do first: transfuse ≈")
                .And.Not.Contain("reathing"));
        });
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static EntityUid Bleed(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        var wound = wounds.CreateOrMergeWound(part, prototype, 20);
        Assert.That(wound, Is.Not.Null);
        Assert.That(entities.HasComponent<WoundBleedingComponent>(wound!.Value), Is.True,
            $"{prototype} at severity 20 must bleed for the clotting classification to mean anything.");
        return wound.Value;
    }

    private static void SetPhase(
        IEntityManager entities,
        EntityUid wound,
        TimeSpan? clottingAt = null,
        float? naturalClotting = null,
        float? currentRate = null)
    {
        var bleeding = entities.GetComponent<WoundBleedingComponent>(wound);
        bleeding.AutomaticClottingAt = clottingAt;
        if (naturalClotting is { } clotting)
            bleeding.NaturalClotting = clotting;
        if (currentRate is { } rate)
            bleeding.CurrentRate = rate;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
