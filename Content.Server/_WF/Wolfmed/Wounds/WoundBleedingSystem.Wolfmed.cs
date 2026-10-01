// WOLFGATE: W2. The bleeding rules an arterial bleed plays by, kept out of the vendored bleeding system,
// which carries only the three call sites that route through here.

using System.Linq;
using Content.Server._WF.Wolfmed.Stasis;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Robust.Shared.GameObjects;
using Content.Shared.FixedPoint;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundBleedingSystem
{
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <summary>
    /// The rate multiplier a treatment earns on this wound. An arterial bleed may override the shared
    /// table, which is how a dressing slows it instead of nearly stopping it.
    /// </summary>
    private float GetTreatmentMultiplier(EntityUid wound, BleedingTreatment treatment)
    {
        // Avali stasis holds every bleed on the body for as long as it lasts (WolfmedStasisSystem).
        if (TryComp(wound, out WoundComponent? core) && TryGetBody(core.HoldingPart, out var body) &&
            HasComp<WolfmedStasisHoldComponent>(body))
            return 0f;

        if (_traits.TryGetBehavior(wound, out WolfmedArterialBleedBehavior behavior) &&
            behavior.TreatmentMultipliers.TryGetValue(treatment, out var multiplier))
            return multiplier;

        return TreatmentMultiplier(treatment);
    }

    /// <summary>
    /// A reagent's bleed change on a wound host (the <c>ModifyBleedAmount</c> effect), where the bloodstream's own
    /// figure is only the wounds' projection. Negative is a coagulant: it takes that much off the body's bleed rate,
    /// scaled by <see cref="WolfmedCVars.BleedRate"/> like every wound's rate, worst bleed first, and never touches a
    /// dressing, clamp or tourniquet. An arterial bleed only clots down to its
    /// <see cref="WolfmedArterialBleedBehavior.CoagulantFloor"/>, so it slows and keeps pumping. Positive opens or
    /// deepens a systemic bleed, as Onyx's <c>ModifyBleed</c> did.
    /// </summary>
    public bool ApplyReagentBleeding(EntityUid body, float amount)
    {
        if (!_net.IsServer || amount == 0f || !HasComp<WoundHostComponent>(body))
            return false;

        if (amount > 0f)
            return ModifyBodyBleeding(body, amount);

        var remaining = -amount * _configuration.GetCVar(WolfmedCVars.BleedRate);
        var modified = false;
        var wounds = GetAttachedBleedingWounds(body)
            .Where(wound => wound.Comp2.CurrentRate > 0f)
            .OrderByDescending(wound => wound.Comp2.CurrentRate)
            .ToArray();
        foreach (var (uid, core, bleeding) in wounds)
        {
            // The rate is linear in the bleeding severity, so a share of the rate is the same share of the severity.
            var flow = bleeding.CurrentRate + bleeding.NaturalClotting;
            var wanted = remaining >= bleeding.CurrentRate
                ? bleeding.BleedingSeverity
                : FixedPoint2.New(bleeding.BleedingSeverity.Float() * remaining / flow);
            if (_traits.TryGetBehavior(uid, out WolfmedArterialBleedBehavior artery))
            {
                var floor = FixedPoint2.New(core.Severity.Float() * Math.Clamp(artery.CoagulantFloor, 0f, 1f));
                wanted = FixedPoint2.Min(wanted, FixedPoint2.Max(FixedPoint2.Zero, bleeding.BleedingSeverity - floor));
            }

            if (wanted <= FixedPoint2.Zero)
                continue;

            var before = bleeding.CurrentRate;
            bleeding.BleedingSeverity = FixedPoint2.Max(FixedPoint2.Zero, bleeding.BleedingSeverity - wanted);
            modified = true;

            // Stopped with nothing on it, the bleed goes, as clotting leaves it; a dressed one keeps its dressing.
            if (bleeding.BleedingSeverity == FixedPoint2.Zero && bleeding.Treatment == BleedingTreatment.None)
            {
                RemComp<WoundBleedingComponent>(uid);
                RefreshBodyForPart(core.HoldingPart);
                remaining -= before;
            }
            else
            {
                RefreshWound((uid, bleeding));
                remaining -= Math.Max(0f, before - bleeding.CurrentRate);
            }

            if (remaining <= 0f)
                break;
        }

        return modified;
    }

    /// <summary>Whether a topical's bloodloss modifier is allowed to eat this wound's bleeding severity.</summary>
    private bool AllowsTopicalBleedReduction(Entity<WoundComponent> wound) =>
        !_traits.TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior behavior) ||
        behavior.TopicalsReduceBleeding;

    /// <summary>
    /// Records a dressing on every untreated arterial bleed in the part. The dressing cannot close the
    /// wound or take its severity, so this is the only thing gauze achieves there: a slower rate.
    /// </summary>
    public bool BandageArterialBleeds(Entity<WoundableComponent?> part)
    {
        var bandaged = false;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State != WoundState.Open ||
                !_traits.TryGetBehavior(wound.Owner, out WolfmedArterialBleedBehavior _) ||
                !TryComp(wound, out WoundBleedingComponent? bleeding) ||
                bleeding.Treatment != BleedingTreatment.None ||
                bleeding.BleedingSeverity <= FixedPoint2.Zero)
                continue;

            bandaged |= SetTreatment(wound.Owner, BleedingTreatment.Bandaged);
        }

        return bandaged;
    }

    /// <summary>
    /// Whether a dressing can still do anything for this part. An arterial bleed that is already dressed keeps
    /// a rate above zero that no topical can lower, and asking "is it bleeding" there made gauze apply itself
    /// until the stack ran out.
    /// </summary>
    public bool CanDressBleeding(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (!TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.CurrentRate <= 0f)
                continue;

            if (AllowsTopicalBleedReduction(wound) || bleeding.Treatment == BleedingTreatment.None)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Playtest 4: whether a tourniquet is on this part or on one it hangs from (a strap on the leg ties off the foot).
    /// The part carries <see cref="WolfmedTourniquetComponent"/> from the strap going on until it comes off.
    /// </summary>
    public bool IsTiedOff(EntityUid part)
    {
        EntityUid? current = part;
        while (current is { } uid && !TerminatingOrDeleted(uid))
        {
            if (HasComp<WolfmedTourniquetComponent>(uid))
                return true;
            current = _body.GetParentPartOrNull(uid);
        }

        return false;
    }

    /// <summary>Clamps a bleed that begins on a wound whose part is already under a tourniquet.</summary>
    private void TieOffIfStrapped(Entity<WoundBleedingComponent> wound)
    {
        if (TryComp(wound, out WoundComponent? core) && core.HoldingPart.IsValid() && IsTiedOff(core.HoldingPart) &&
            _traits.CanTourniquet(wound.Owner, core.HoldingPart))
            wound.Comp.Treatment = BleedingTreatment.Clamped;
    }

    /// <summary>Whether the part still has a bleed that only a tourniquet or surgery will stop.</summary>
    public bool HasUndressableBleed(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f &&
                !AllowsTopicalBleedReduction(wound))
                return true;
        }

        return false;
    }
}
