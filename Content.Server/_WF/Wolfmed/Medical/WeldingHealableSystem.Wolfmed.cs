using System.Linq;
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tools.Components;
using Robust.Server.Audio;

namespace Content.Server._EinsteinEngines.Silicon.WeldingHealable;

public sealed partial class WeldingHealableSystem
{
    private static readonly HashSet<TreatmentCapability> RepairCapabilities = new() { TreatmentCapability.Mechanical };

    [Dependency] private WoundHealingSystem _woundHealing = default!;
    [Dependency] private WoundDamageRoutingSystem _woundRouting = default!;
    [Dependency] private WoundSystem _repairWounds = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _repairEmbedded = default!;
    [Dependency] private ItemToggleSystem _repairToggle = default!;
    [Dependency] private AudioSystem _repairAudio = default!;

    private void InitializeWoundRepair()
    {
        // WOLFGATE (W6): a welder on a damaged chassis repairs it instead of setting it on fire, which is
        // what FlammableSystem would do with the same interaction. Ordered on the WoundHost pair only, so
        // nothing else in the game changes order.
        SubscribeLocalEvent<WoundHostComponent, InteractUsingEvent>(OnWoundRepair,
            before: [typeof(FlammableSystem)]);
        SubscribeLocalEvent<WoundHostComponent, WoundRepairFinishedEvent>(OnWoundRepairFinished);
    }

    private void OnWoundRepair(Entity<WoundHostComponent> body, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out WeldingHealingComponent? healing) ||
            !CanUseRepairTool(body, args.User, args.Used, healing))
            return;

        if (!_woundHealing.TryGetTargetedPart(body, args.User, out var requested))
            return;

        var selected = _woundHealing.ResolveHealingPart(body, requested, healing.Damage,
            null, RepairCapabilities, null, 0f, healWounds: true);

        // Playtest 5: the aiming doll decides only when it points at something to repair. The analyzer says "weld
        // the fluid leak" without saying where, and a doll left on the chest used to make the welder do nothing, and
        // say nothing, about a breach on a leg.
        if (selected is not { } aimed || !CanRepairPart(body, aimed, healing))
            selected = _woundHealing.ResolveHealingPart(body, null, healing.Damage,
                null, RepairCapabilities, null, 0f, healWounds: true);

        if (selected is not { } partUid || !CanRepairPart(body, partUid, healing))
        {
            // Playtest 5: a round lodged in a breach refuses every repair, and the welder used to burn its tank on it
            // without a word. Now it names the obstacle.
            // Either way the click was a repair attempt on a chassis: handled, or FlammableSystem takes the lit welder
            // next and sets the patient on fire. Flesh is left to the welder's other uses.
            if (FindEmbeddedPart(body, selected) != null)
            {
                _popup.PopupEntity(Loc.GetString("wolfmed-repair-embedded", ("target", body.Owner), ("tool", args.Used)),
                    body, args.User);
                args.Handled = true;
            }
            else if (HasRepairableParts(body))
            {
                _popup.PopupEntity(Loc.GetString("wolfmed-repair-nothing", ("target", body.Owner), ("tool", args.Used)),
                    body, args.User);
                args.Handled = true;
            }
            return;
        }

        var delay = healing.DoAfterDelay * (body.Owner == args.User ? healing.SelfHealPenalty : 1f);
        args.Handled = StartWoundRepair(body, partUid, args.User, args.Used, healing, delay);
    }

    // WOLFGATE (W6): a tool with no fuel cost is not a welder. A wrench repairs a dented chassis and has
    // nothing to burn, so the fuel check and the fuel spend below are both skipped for it.
    private bool CanUseRepairTool(EntityUid body, EntityUid user, EntityUid tool, WeldingHealingComponent healing) =>
        (body != user || healing.AllowSelfHeal) &&
        _toolSystem.HasQuality(tool, healing.QualityNeeded) &&
        (healing.FuelCost <= 0 || _toolSystem.GetWelderFuelAndCapacity(tool).fuel >= healing.FuelCost);

    /// <summary>Whether any part of this body is one a repair tool works on at all.</summary>
    private bool HasRepairableParts(EntityUid body)
    {
        foreach (var (part, _) in _bodySystem.GetBodyChildren(body))
        {
            if (_woundHealing.IsCompatiblePart(body, part, null, RepairCapabilities))
                return true;
        }

        return false;
    }

    /// <summary>The aimed part if something is lodged in it, else the first repairable part with something lodged.</summary>
    private EntityUid? FindEmbeddedPart(Entity<WoundHostComponent> body, EntityUid? aimed)
    {
        if (aimed is { } part && _repairEmbedded.GetEmbeddedWound(part) != null)
            return part;

        foreach (var (candidate, _) in _bodySystem.GetBodyChildren(body))
        {
            if (_woundHealing.IsCompatiblePart(body, candidate, null, RepairCapabilities) &&
                _repairEmbedded.GetEmbeddedWound(candidate) != null)
                return candidate;
        }

        return null;
    }

    /// <summary>What a pass can change: the part's damage and the severity of its wounds.</summary>
    private (FixedPoint2 Damage, FixedPoint2 Severity) RepairSignature(EntityUid part)
    {
        var severity = FixedPoint2.Zero;
        foreach (var wound in _repairWounds.GetWounds(part))
            severity += wound.Comp.Severity;

        return (CompOrNull<DamageableComponent>(part)?.TotalDamage ?? FixedPoint2.Zero, severity);
    }

    private bool CanRepairPart(EntityUid body, EntityUid part, WeldingHealingComponent healing)
    {
        // Playtest 5: nothing closes a breach with a round still in it, so a part with one is not repairable.
        if (!_woundHealing.IsCompatiblePart(body, part, null, RepairCapabilities) ||
            !TryComp(part, out DamageableComponent? damageable) ||
            damageable.DamageContainerID is not { } container || !healing.DamageContainers.Contains(container) ||
            _repairEmbedded.GetEmbeddedWound(part) != null)
            return false;

        return _woundHealing.HasTreatableWounds(part, healing.Damage, null) ||
               healing.Damage.DamageDict.Any(entry => entry.Value < FixedPoint2.Zero &&
                   damageable.Damage.DamageDict.GetValueOrDefault(entry.Key) > FixedPoint2.Zero);
    }

    private bool StartWoundRepair(EntityUid body, EntityUid part, EntityUid user, EntityUid tool,
        WeldingHealingComponent healing, float delay) =>
        _toolSystem.UseTool(tool, user, body, delay, healing.QualityNeeded,
            new WoundRepairFinishedEvent { Part = GetNetEntity(part), Delay = delay });

    private void OnWoundRepairFinished(Entity<WoundHostComponent> body, ref WoundRepairFinishedEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } tool ||
            !TryComp(tool, out WeldingHealingComponent? healing) ||
            !_repairToggle.IsActivated(tool) ||
            !CanUseRepairTool(body, args.User, tool, healing) ||
            !TryGetEntity(args.Part, out var part) || part is not { } partUid ||
            !CanRepairPart(body, partUid, healing))
            return;

        // WOLFGATE (W6): only a fuelled tool has to have fuel to spend. A wrench has none.
        var fuelled = healing.FuelCost > 0;
        if (fuelled && (!TryComp(tool, out WelderComponent? welder) ||
                        !_solutionContainer.TryGetSolution(tool, welder.FuelSolutionName, out _)))
            return;

        args.Handled = true;
        var user = args.User;

        // Playtest 5, "the nanite applicator spams 40 messages in one tick": an instant do-after (an admin ghost's)
        // finishes inside StartWoundRepair below, so this is the pass that frame started. That frame applies it and
        // every pass after, and the whole chain is one sound and one line.
        if (_repairChains.Contains(body))
        {
            _repairInstant.Add(body);
            return;
        }

        var progressed = ApplyRepairPass(body, partUid, tool, healing, user);
        var more = progressed && CanRepairPart(body, partUid, healing) && CanUseRepairTool(body, user, tool, healing);
        if (more)
        {
            _repairChains.Add(body);
            try
            {
                more = StartWoundRepair(body, partUid, user, tool, healing, args.Delay);
                if (_repairInstant.Remove(body))
                {
                    while (CanRepairPart(body, partUid, healing) && CanUseRepairTool(body, user, tool, healing) &&
                           ApplyRepairPass(body, partUid, tool, healing, user))
                    {
                    }

                    more = false;
                }
            }
            finally
            {
                _repairChains.Remove(body);
            }
        }

        // WOLFGATE (V124): the tool's own useSound covers the start of the pass; this is the finish.
        if (TryComp(tool, out WolfmedRepairSoundComponent? sound))
            _repairAudio.PlayPvs(sound.EndSound, body);

        // The line waits for the chain's end: repaired, dry, or a pass that closed nothing (playtest 5: the welder used
        // to repeat that one until the tank was empty, and to say "you repair" once a pass, forty times for a bad torso).
        if (more)
            return;

        var line = !progressed ? "wolfmed-repair-no-progress"
            : CanRepairPart(body, partUid, healing) && !CanUseRepairTool(body, user, tool, healing) ? "wolfmed-repair-fuel"
            : "comp-repairable-repair";
        _popup.PopupEntity(Loc.GetString(line, ("target", body.Owner), ("tool", tool)), body, user);
    }

    // Bodies whose repair chain OnWoundRepairFinished is advancing, and those whose next pass finished at once.
    private readonly HashSet<EntityUid> _repairChains = new();
    private readonly HashSet<EntityUid> _repairInstant = new();

    /// <summary>One pass: what the tool removes and its fuel. False, and nothing paid, when the pass changed nothing.</summary>
    private bool ApplyRepairPass(Entity<WoundHostComponent> body, EntityUid part, EntityUid tool,
        WeldingHealingComponent healing, EntityUid user)
    {
        var before = RepairSignature(part);
        _woundRouting.WithTreatmentCapabilities(body, RepairCapabilities, () =>
            _woundRouting.TryApplyPartDamage(body, part, healing.Damage, user,
                ignoreResistances: true, healWounds: true));
        if (RepairSignature(part) == before)
            return false;

        if (healing.FuelCost > 0 && TryComp(tool, out WelderComponent? spender) &&
            _solutionContainer.TryGetSolution(tool, spender.FuelSolutionName, out var solution))
            _solutionContainer.RemoveReagent(solution.Value, spender.FuelReagent, healing.FuelCost);
        return true;
    }
}
