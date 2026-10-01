using System.Linq;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Hud;

/// <summary>
/// Fills <see cref="WolfmedSyntheticHudComponent"/> for every mechanical wound host: the analyzer payload
/// for one body, pushed instead of pulled, because the client can see neither the wound entities nor the
/// bloodstream. Rebuilt twice a second and dirtied only when something actually moved.
/// </summary>
public sealed class WolfmedSyntheticHudSystem : EntitySystem
{
    /// <summary>Fluid fraction below which the readout calls it out.</summary>
    public const float LowFluid = 0.7f;

    /// <summary>Brain activity below which the core is reported as damaged.</summary>
    public const float BrainWarn = 0.85f;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SiliconChargeSystem _charge = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditions = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private WolfmedSyntheticHudLineSystem _lines = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private Content.Server._HL.Silicons.Synths.Battery.SynthBatterySystem _synthBattery = default!; // M4

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;

        var query = EntityQueryEnumerator<WoundHostComponent, BodyComponent>();
        while (query.MoveNext(out var body, out _, out _))
        {
            if (TerminatingOrDeleted(body))
                continue;

            if (!IsMechanicalBody(body))
            {
                if (HasComp<WolfmedSyntheticHudComponent>(body))
                    RemComp<WolfmedSyntheticHudComponent>(body);

                continue;
            }

            Refresh((body, EnsureComp<WolfmedSyntheticHudComponent>(body)));
        }
    }

    /// <summary>
    /// A body whose chassis is a chassis. The torso decides, because that is the part a body cannot be
    /// without: a human wearing one cybernetic arm is still flesh and still gets the organic view.
    /// </summary>
    public bool IsMechanicalBody(EntityUid body)
    {
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType == BodyPartType.Torso)
                return TryComp(part, out WoundableComponent? woundable) && _traits.IsMechanical((part, woundable));
        }

        return false;
    }

    /// <summary>Rebuilds the readout and dirties the component if any of it changed.</summary>
    public void Refresh(Entity<WolfmedSyntheticHudComponent> hud)
    {
        var found = new List<WolfmedSyntheticFault>();
        var integrity = 0f;
        var weight = 0f;
        var servos = 0f;
        var limbs = 0f;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(hud.Owner))
        {
            if (!TryComp(part, out WoundableComponent? woundable) ||
                _body.GetTargetBodyPart(bodyPart.PartType, bodyPart.Symmetry) is not { } target)
                continue;

            var share = bodyPart.PartType is BodyPartType.Torso or BodyPartType.Head ? 2f : 1f;
            integrity += share * PartIntegrity(part);
            weight += share;

            var state = _functionality.GetState((part, woundable));
            if (bodyPart.PartType is BodyPartType.Arm or BodyPartType.Leg or BodyPartType.Hand or BodyPartType.Foot)
            {
                limbs += 1f;
                servos += state switch
                {
                    BodyPartFunctionalityState.Functional => 1f,
                    BodyPartFunctionalityState.Impaired => 0.6f,
                    _ => 0f,
                };
            }

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                // Only a chassis finding earns the unmapped fallback row; an organic wound on a mechanical
                // body (a grafted limb) belongs to the medic's analyzer, not to the machine's own readout.
                var mechanical = _prototypes.TryIndex(wound.Comp.Prototype, out var proto) &&
                                 proto.AnalyzerCategory == WolfmedWoundCategory.Mechanical;
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !_lines.TryGetWoundLine(wound.Comp.Prototype, wound.Comp.Severity, mechanical, out var line, out var tag))
                    continue;

                Add(found, target, line.Line, tag, line.Advice);
            }

            if (state == BodyPartFunctionalityState.Disabled)
                AddCondition(found, target, WolfmedSyntheticCondition.PartDisabled);
            else if (state == BodyPartFunctionalityState.Unavailable)
                AddCondition(found, target, WolfmedSyntheticCondition.PartMissing);

            // A severed part is not a body child any more, so the gap is read off this part's empty slots.
            foreach (var (slotId, _) in bodyPart.Children)
            {
                if (SlotPart(slotId) is not { } gone ||
                    !_containers.TryGetContainer(part, SharedBodySystem.GetPartSlotContainerId(slotId), out var slot) ||
                    slot.ContainedEntities.Count > 0)
                    continue;

                AddCondition(found, gone, WolfmedSyntheticCondition.PartMissing);
            }

            if (_embedded.GetPartCount((part, woundable)) > 0)
                AddCondition(found, target, WolfmedSyntheticCondition.Embedded);
        }

        var fluid = TryComp(hud.Owner, out BloodstreamComponent? bloodstream)
            ? Math.Clamp(_blood.GetBloodLevelPercentage(hud.Owner, bloodstream), 0f, 1f)
            : CompOrNull<WolfmedConsciousnessComponent>(hud.Owner)?.BloodFraction ?? 1f;

        if (fluid < LowFluid)
            AddCondition(found, TargetBodyPart.Torso, WolfmedSyntheticCondition.LowFluid);

        var down = _shutdown.IsShutDown(hud.Owner);
        if (down)
            AddCondition(found, TargetBodyPart.Torso, WolfmedSyntheticCondition.Shutdown);

        var core = _life.GetBrainOrgan(hud.Owner);
        var offline = core is { } organ && organ.Comp.Health <= FixedPoint2.Zero;
        if (offline)
            AddCondition(found, TargetBodyPart.Head, WolfmedSyntheticCondition.BrainDestroyed);
        else if (core != null && _life.GetBrainActivity(hud.Owner) < BrainWarn)
            AddCondition(found, TargetBodyPart.Head, WolfmedSyntheticCondition.BrainDamage);

        // M2 (OD10): a repaired core shows it for a while, in place of the trauma a brain would carry.
        if (HasComp<Content.Shared._WF.Wolfmed.Life.WolfmedCoreRestoredComponent>(hud.Owner) && !offline)
            AddCondition(found, TargetBodyPart.Torso, WolfmedSyntheticCondition.CoreRestored);

        // M4 (plan §3.11, §5.6): CORE TEMP CRITICAL while the chassis heats; thermal shutdown is the banner's.
        var heat = CompOrNull<Content.Shared._WF.Wolfmed.Life.WolfmedCoreHeatComponent>(hud.Owner);
        if (heat is { Hot: true, ThermalShutdown: false } && !offline)
            AddCondition(found, TargetBodyPart.Torso, WolfmedSyntheticCondition.CoreTempCritical);

        var faults = Order(hud.Comp.Faults, found);
        var advice = faults.Count > 0 ? faults[0].Advice : string.Empty;
        var power = _charge.TryGetSiliconBattery(hud.Owner, out var battery) && battery.MaxCharge > 0f
            ? Math.Clamp(battery.CurrentCharge / battery.MaxCharge, 0f, 1f)
            : SynthPower(hud.Owner); // M4: a synth's cell sits in its battery organ slot

        var newIntegrity = weight > 0f ? integrity / weight : 1f;
        var newServos = limbs > 0f ? servos / limbs : 1f;
        var causeLine = CauseLine(hud.Owner);
        var sensors = TryComp(hud.Owner, out PainComponent? pain) && pain.SoftPainCap > FixedPoint2.Zero
            ? Math.Clamp(pain.Value.Float() / pain.SoftPainCap.Float(), 0f, 1f)
            : 0f;
        // M4 (plan §3.11): the CORE row is the core's own temperature once the core-heat route tracks it.
        var temperature = heat?.CoreTemperature ??
                          (TryComp(hud.Owner, out TemperatureComponent? chassis) ? chassis.CurrentTemperature : -1f);

        if (Same(hud.Comp.Faults, faults) &&
            Near(hud.Comp.Integrity, newIntegrity) &&
            Near(hud.Comp.Fluid, fluid) &&
            Near(hud.Comp.Servos, newServos) &&
            Near(hud.Comp.Power, power) &&
            hud.Comp.Shutdown == down &&
            hud.Comp.CoreOffline == offline &&
            hud.Comp.Advice == advice &&
            hud.Comp.CauseLine == causeLine &&
            Near(hud.Comp.Sensors, sensors) &&
            MathF.Abs(hud.Comp.CoreTemperature - temperature) < 0.5f)
            return;

        hud.Comp.Faults = faults;
        hud.Comp.Integrity = newIntegrity;
        hud.Comp.Fluid = fluid;
        hud.Comp.Servos = newServos;
        hud.Comp.Power = power;
        hud.Comp.Shutdown = down;
        hud.Comp.CoreOffline = offline;
        hud.Comp.Advice = advice;
        hud.Comp.CauseLine = causeLine;
        hud.Comp.Sensors = sensors;
        hud.Comp.CoreTemperature = temperature;
        Dirty(hud);
    }

    /// <summary>M4 (OD16): a synth's charge, 0 to 1, from the cell in its battery organ slot; -1 with none.</summary>
    private float SynthPower(EntityUid body)
    {
        if (!_synthBattery.TryGetBattery(body, out var cell) || cell.Value.Comp.MaxCharge <= 0f)
            return -1f;

        return Math.Clamp(cell.Value.Comp.CurrentCharge / cell.Value.Comp.MaxCharge, 0f, 1f);
    }

    /// <summary>
    /// M1a: the cause's own banner line while the chassis is down or shut down (plan §5.6). A shutdown that
    /// consciousness has not caught up with yet still reads as a shutdown.
    /// </summary>
    private string CauseLine(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) || _life.IsBrainDead(body))
            return string.Empty;

        var cause = consciousness.State == WolfmedConsciousness.Up ? WolfmedCause.None : consciousness.Cause;
        if (cause == WolfmedCause.None && _shutdown.IsShutDown(body))
            cause = WolfmedCause.Shutdown;

        return _conditions.GetCausePrototype(cause)?.SyntheticHudLine?.Id ?? string.Empty;
    }

    /// <summary>
    /// Newest first: a fault the readout did not have last time goes to the top, worst tag leading, and
    /// everything already on screen keeps the order it was in. Then the list is cut to its cap.
    /// </summary>
    private static List<WolfmedSyntheticFault> Order(
        List<WolfmedSyntheticFault> previous,
        List<WolfmedSyntheticFault> found)
    {
        var old = previous.Select(Key).ToHashSet();
        var fresh = found.Where(fault => !old.Contains(Key(fault)))
            .OrderByDescending(fault => fault.Severity)
            .ThenBy(fault => fault.Part)
            .ToList();

        var current = found.ToDictionary(Key, fault => fault);
        var kept = previous.Where(fault => current.ContainsKey(Key(fault)))
            .Select(fault => current[Key(fault)]);

        return fresh.Concat(kept).Take(WolfmedSyntheticHudComponent.MaxFaults).ToList();
    }

    private static (TargetBodyPart, string) Key(WolfmedSyntheticFault fault) => (fault.Part, fault.Line);

    /// <summary>The limb a body slot id names. Shitmed's slot ids are the part names ("left arm").</summary>
    private static TargetBodyPart? SlotPart(string slotId) => slotId switch
    {
        "head" => TargetBodyPart.Head,
        "torso" => TargetBodyPart.Torso,
        "groin" => TargetBodyPart.Groin,
        "left arm" => TargetBodyPart.LeftArm,
        "right arm" => TargetBodyPart.RightArm,
        "left hand" => TargetBodyPart.LeftHand,
        "right hand" => TargetBodyPart.RightHand,
        "left leg" => TargetBodyPart.LeftLeg,
        "right leg" => TargetBodyPart.RightLeg,
        "left foot" => TargetBodyPart.LeftFoot,
        "right foot" => TargetBodyPart.RightFoot,
        _ => null,
    };

    private void AddCondition(List<WolfmedSyntheticFault> into, TargetBodyPart part, WolfmedSyntheticCondition condition)
    {
        if (_lines.TryGetConditionLine(condition, out var line))
            Add(into, part, line.Line, line.Severity, line.Advice);
    }

    private static void Add(
        List<WolfmedSyntheticFault> into,
        TargetBodyPart part,
        string text,
        WolfmedSyntheticSeverity severity,
        string advice)
    {
        foreach (var existing in into)
        {
            if (existing.Part == part && existing.Line == text)
                return;
        }

        into.Add(new WolfmedSyntheticFault(part, text, severity, advice));
    }

    /// <summary>How whole one part is: 1 untouched, 0 at the damage that would take it off.</summary>
    private float PartIntegrity(EntityUid part)
    {
        var thresholds = _wfPart.Get(part).AmputationThresholds;
        if (thresholds.Count == 0 || !TryComp(part, out DamageableComponent? damageable))
            return 1f;

        var progress = 0f;
        foreach (var (type, threshold) in thresholds)
        {
            if (threshold <= FixedPoint2.Zero)
                continue;

            progress += damageable.Damage.DamageDict.GetValueOrDefault(type).Float() / threshold.Float();
        }

        return 1f - Math.Clamp(progress, 0f, 1f);
    }

    private static bool Same(List<WolfmedSyntheticFault> left, List<WolfmedSyntheticFault> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static bool Near(float left, float right) => MathF.Abs(left - right) < 0.005f;
}
