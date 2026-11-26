// WOLFGATE: HOOK 23 body. Port of Onyx's HealthAnalyzerSystem diagnostic builders (P4-4a), kept out of the
// upstream file so it carries only the one-line call site. The builders are public so a headless test can
// call them (P4-D26) - UpdateScannedUser itself ends in a network send with no capture point.

using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Server._WF.Wolfmed.Wounds; // WOLFGATE (W5)
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE (W1)
using Content.Shared._WF.Wolfmed.Reagents; // WOLFGATE (CONSC)
using Robust.Shared.Timing; // WOLFGATE (CONSC)
using Content.Shared.Body.Components;
using Content.Shared.Body.Part; // WOLFGATE (EVISC): BodyPartComponent, for the empty organ slot count.
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WoundSystem _wounds = default!; // WOLFGATE: HOOK 23
    [Dependency] private PainSystem _pain = default!; // WOLFGATE: HOOK 23
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!; // WOLFGATE: HOOK 23
    [Dependency] private MobThresholdSystem _mobThreshold = default!; // WOLFGATE: HOOK 23
    [Dependency] private IPrototypeManager _prototypes = default!; // WOLFGATE: HOOK 23
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!; // WOLFGATE (W1)
    [Dependency] private WolfmedInfectionSystem _infection = default!; // WOLFGATE (W5)
    [Dependency] private WolfmedNecrosisSystem _necrosis = default!; // WOLFGATE (W5)
    [Dependency] private WolfmedWoundTraitSystem _traits = default!; // WOLFGATE (W6)
    [Dependency] private WolfmedOverheatingSystem _overheating = default!; // WOLFGATE (W6)
    [Dependency] private Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem _life = default!; // WOLFGATE (BRAIN)
    [Dependency] private Content.Server._WF.Wolfmed.Life.WolfmedShutdownSystem _shutdown = default!; // WOLFGATE (BRAIN)

    /// <summary>Per-part wound findings for a wound host, or null for anything else.</summary>
    public HealthAnalyzerWoundDiagnostics? BuildWoundDiagnostics(EntityUid body)
    {
        // WOLFGATE (D2): Onyx gates on SurgeryTargetComponent; a Shitmed surgery target without WoundHost
        // (a borg, a synthetic species) has no WoundableComponent anywhere and would report "no findings"
        // instead of "unavailable".
        if (!HasComp<WoundHostComponent>(body))
            return null;

        var result = new Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic>();
        foreach (var (part, bodyPart) in _bodySystem.GetBodyChildren(body))
        {
            // WOLFGATE: SharedTargetingSystem.TryConvert does not exist in Wolfgate; Shitmed's
            // SharedBodySystem.GetTargetBodyPart is the equivalent conversion.
            if (!TryComp(part, out WoundableComponent? woundable) ||
                _bodySystem.GetTargetBodyPart(bodyPart.PartType, bodyPart.Symmetry) is not { } target)
                continue;

            var fracture = FractureGrade.None;
            var fractureTreatment = FractureTreatment.None;
            var bleedingRate = 0f;
            var bleedingTreatment = BleedingTreatment.None;
            var highestBleedingRate = 0f;
            ushort scarCount = 0;
            // WOLFGATE (UI2): the category joins the grouping key so two wounds that share a name but not a
            // category could never collapse into one row with the wrong icon.
            // WOLFGATE (UI3): the prototype id joins the key too, so the row can name the treatment advice.
            var visibleWounds =
                new Dictionary<(LocId Name, LocId? StageName, WolfmedWoundCategory Category, string Prototype), int>();
            var clottingPhases = new HashSet<HealthAnalyzerClottingPhase>();
            var treatments = WolfmedPartTreatments.None; // WOLFGATE (UI4)
            var internalBleedingRate = 0f;
            var pain = TryComp(part, out PainComponent? painComponent)
                ? _pain.GetPain((part, painComponent))
                : FixedPoint2.Zero;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (TryComp(wound, out WoundFractureComponent? foundFracture) &&
                    foundFracture.Treatment != FractureTreatment.Mended &&
                    foundFracture.Grade > fracture)
                {
                    fracture = foundFracture.Grade;
                    fractureTreatment = foundFracture.Treatment;
                }

                if (TryComp(wound, out WoundBleedingComponent? foundBleeding) && foundBleeding.CurrentRate > 0f)
                {
                    bleedingRate += foundBleeding.CurrentRate;
                    if (foundBleeding.CurrentRate > highestBleedingRate)
                    {
                        highestBleedingRate = foundBleeding.CurrentRate;
                        bleedingTreatment = foundBleeding.Treatment;
                    }
                }

                if (HasComp<WoundScarComponent>(wound))
                    scarCount++;

                // WOLFGATE (UI4): the treatments already on the part, whatever the wound's current rate.
                if (TryComp(wound, out WoundBleedingComponent? treated))
                    treatments |= WolfmedStepChecks.Flag(treated.Treatment);

                if (TryComp(wound, out WolfmedInfectionComponent? contamination) && contamination.Cleaned)
                    treatments |= WolfmedPartTreatments.Cleaned;

                if (TryComp(wound, out WoundInternalBleedingComponent? internalBleeding) &&
                    wound.Comp.State == WoundState.Open && internalBleeding.Severity > FixedPoint2.Zero)
                    internalBleedingRate += internalBleeding.Rate * internalBleeding.Severity.Float();

                if (TryComp(wound, out WoundBleedingComponent? clotting) && wound.Comp.State == WoundState.Open)
                {
                    clottingPhases.Add(clotting.AutomaticClottingAt != null
                        ? HealthAnalyzerClottingPhase.InProgress
                        : clotting.NaturalClotting > 0f && clotting.CurrentRate <= 0f
                            ? HealthAnalyzerClottingPhase.Complete
                            : HealthAnalyzerClottingPhase.None);
                }

                if (!_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                    prototype.Visibility != WoundVisibility.Visible ||
                    wound.Comp.State is WoundState.Healed or WoundState.Scarred)
                    continue;

                var stageName = prototype.GetStageDefinition(wound.Comp.Severity)?.Name;
                // WOLFGATE (UI2 category, UI3 prototype id).
                var key = (prototype.Name, stageName, WolfmedWoundCategories.Resolve(prototype), prototype.ID);
                visibleWounds[key] = visibleWounds.GetValueOrDefault(key) + 1;
            }

            var wounds = visibleWounds
                .OrderBy(entry => entry.Key.Category) // WOLFGATE (UI2): the panel renders one block per category.
                .ThenBy(entry => entry.Key.Name)
                .ThenBy(entry => entry.Key.StageName)
                .Select(entry => new HealthAnalyzerVisibleWound(
                    entry.Key.Name, entry.Key.StageName, entry.Value, entry.Key.Category, entry.Key.Prototype))
                .ToList();
            var clottingPhase = clottingPhases.Count switch
            {
                0 => HealthAnalyzerClottingPhase.NotApplicable,
                1 => clottingPhases.Single(),
                _ => HealthAnalyzerClottingPhase.Mixed,
            };

            var diagnostic = new HealthAnalyzerWoundDiagnostic(
                fracture,
                fractureTreatment,
                bleedingRate,
                bleedingTreatment,
                scarCount,
                pain,
                wounds,
                _functionality.GetState((part, woundable)),
                internalBleedingRate,
                clottingPhase,
                (ushort) Math.Clamp(_embedded.GetPartCount((part, woundable)), 0, ushort.MaxValue), // WOLFGATE (W1)
                _infection.GetWorstWoundStage((part, woundable)), // WOLFGATE (W5)
                _necrosis.IsNecrotic(part), // WOLFGATE (W5)
                _necrosis.IsAtRisk(part), // WOLFGATE (W5)
                _traits.IsMechanical((part, woundable)), // WOLFGATE (W6): picks the chassis wording
                _overheating.IsOverheating(part), // WOLFGATE (W6)
                treatments, // WOLFGATE (UI4): what the procedure window ticks off
                MissingOrgans(part, bodyPart), // WOLFGATE (EVISC)
                // INFECTION: shown from Local; a part under that is still clearing and says nothing yet.
                _infection.GetPartStage(part) is var partStage && partStage >= WolfmedInfectionStage.Local
                    ? partStage
                    : WolfmedInfectionStage.None);

            if (diagnostic.HasFindings)
                result[target] = diagnostic;
        }

        // WOLFGATE (CONSC): what is masking the patient's pain, and how sedated it has left them.
        var relief = CompOrNull<WolfmedPainReliefComponent>(body);
        var reliefLeft = relief?.Ends is { } ends
            ? MathF.Max(0f, (float) (ends - _timing.CurTime).TotalSeconds)
            : 0f;

        // WOLFGATE (BRAIN): the two vitals that actually decide whether this patient lives.
        var brain = _life.GetBrain(body);
        var brainOrgan = _life.GetBrainOrgan(body);

        // WOLFGATE (M1a): the remaining problem after a shock, in units (plan §7.1).
        var postShock = _life.GetPostShockAdvice(body);

        return new HealthAnalyzerWoundDiagnostics(
            result,
            _infection.GetSepsis(body), // WOLFGATE (W5)
            relief?.Tier ?? WolfmedPainReliefTier.None, // WOLFGATE (CONSC)
            reliefLeft, // WOLFGATE (CONSC)
            relief?.Sedation ?? 0f, // WOLFGATE (CONSC)
            _life.InArrest(body), // WOLFGATE (BRAIN)
            brainOrgan is { } dead && dead.Comp.Health <= FixedPoint2.Zero, // WOLFGATE (BRAIN)
            brainOrgan == null ? -1f : _life.GetBrainActivity(body), // WOLFGATE (BRAIN)
            brain == null ? -1f : brain.Value.Comp.Oxygenation, // WOLFGATE (BRAIN)
            _life.GetBrainDeathSeconds(body) ?? -1f, // WOLFGATE (BRAIN)
            _shutdown.IsShutDown(body), // WOLFGATE (BRAIN)
            postShock?.Units ?? -1f, // WOLFGATE (M1a)
            postShock?.SafeUnits ?? 0f, // WOLFGATE (M1a)
            postShock?.GraceSeconds ?? 0f, // WOLFGATE (M1a)
            postShock?.SafeLine ?? 0f, // WOLFGATE (M1a)
            BuildVitals(body), // WOLFGATE (M1a): the vitals block, HealthAnalyzerSystem.Vitals.cs
            BrainTraumaSeconds(body)); // Playtest 5: the repaired brain's trauma, so its stutter and blur are named
    }

    /// <summary>Playtest 5: seconds left on a repaired brain's trauma, or -1 when the patient carries none.</summary>
    private float BrainTraumaSeconds(EntityUid body) =>
        TryComp(body, out Content.Shared._WF.Wolfmed.Life.WolfmedBrainTraumaComponent? trauma) && trauma.Ends > _timing.CurTime
            ? (float) (trauma.Ends - _timing.CurTime).TotalSeconds
            : -1f;

    /// <summary>
    /// WOLFGATE (EVISC): organ slots on this part with nothing in them. The slots are the ones the body
    /// prototype created, so a part whose organs are all present reports zero and an eviscerated torso
    /// reports what is still on the floor.
    /// </summary>
    private ushort MissingOrgans(EntityUid part, BodyPartComponent bodyPart)
    {
        var present = 0;
        foreach (var _ in _bodySystem.GetPartOrgans(part, bodyPart))
            present++;

        return (ushort) Math.Clamp(bodyPart.Organs.Count - present, 0, ushort.MaxValue);
    }

    /// <summary>Organ health rows in medical reading order, or null for a non-wound-host.</summary>
    public List<HealthAnalyzerOrganInfo>? BuildOrganInfo(EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body))
            return null;

        var result = new List<HealthAnalyzerOrganInfo>();
        foreach (var (organ, component) in _bodySystem.GetBodyOrgans(body))
        {
            // WOLFGATE (D8): health lives on WolfmedOrganComponent, and Onyx's OrganCategoryPrototype is Nubody -
            // Shitmed's SlotId already carries the same ids (brain, eyes, lungs, heart, stomach, liver, kidneys).
            if (!TryComp(organ, out WolfmedOrganComponent? health))
                continue;

            result.Add(new HealthAnalyzerOrganInfo(
                GetNetEntity(organ),
                health.Health,
                health.MaxHealth,
                OrganOrder(component.SlotId)));
        }

        result.Sort((left, right) => left.Order.CompareTo(right.Order));
        return result;
    }

    private static int OrganOrder(string? slotId) => slotId?.ToLowerInvariant() switch
    {
        "brain" => 0,
        "eyes" => 1,
        "ears" => 2,
        "tongue" => 3,
        "lungs" => 4,
        "heart" => 5,
        "liver" => 6,
        "stomach" => 7,
        "appendix" => 8,
        "kidneys" => 9,
        _ => 10,
    };

    /// <summary>Reagent contents of the bloodstream, chemical, stomach and lung solutions.</summary>
    public List<HealthAnalyzerChemicalInfo>? BuildChemicalInfo(EntityUid body, BloodstreamComponent? bloodstream)
    {
        var result = new List<HealthAnalyzerChemicalInfo>();
        if (bloodstream != null)
        {
            if (_solutionContainerSystem.TryGetSolution(body, bloodstream.BloodSolutionName, out _, out var blood))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Bloodstream, blood);

            // WOLFGATE: Wolfgate's bloodstream names its reagent solution ChemicalSolutionName; the wire value
            // stays Metabolites so the payload type does not churn, and the client localises it as "Chemicals".
            if (_solutionContainerSystem.TryGetSolution(body, bloodstream.ChemicalSolutionName, out _, out var chemicals))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Metabolites, chemicals);
        }

        foreach (var (organ, _) in _bodySystem.GetBodyOrgans(body))
        {
            if (HasComp<StomachComponent>(organ) &&
                _solutionContainerSystem.TryGetSolution(organ, StomachSystem.DefaultSolutionName, out _, out var stomach))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Stomach, stomach);

            if (TryComp(organ, out LungComponent? lung) &&
                _solutionContainerSystem.TryGetSolution(organ, lung.SolutionName, out _, out var lungs))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Lung, lungs);
        }

        return result;
    }

    private static void AddChemicalInfo(
        List<HealthAnalyzerChemicalInfo> result,
        HealthAnalyzerSolutionType type,
        Solution solution)
    {
        var reagents = new List<HealthAnalyzerReagentInfo>();
        foreach (var reagent in solution.Contents)
            reagents.Add(new HealthAnalyzerReagentInfo(reagent.Reagent.Prototype, reagent.Quantity));

        result.Add(new HealthAnalyzerChemicalInfo(type, reagents));
    }

    /// <summary>The damage figure that actually decides crit and death on a wound host.</summary>
    public FixedPoint2? BuildVitalDamage(EntityUid body) =>
        HasComp<WoundHostComponent>(body) && TryComp(body, out DamageableComponent? damageable)
            ? _mobThreshold.CheckVitalDamage(body, damageable)
            : null;
}
