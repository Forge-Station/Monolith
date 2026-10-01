// WOLFGATE: HOOK 24, 25, 26 and 27 bodies. Kept out of the upstream SharedSurgerySystem files so those
// carry only the marked call sites and gain neither a using nor a dependency line.

using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._Shitmed.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    /// <summary>Hook body (playtest 5): a surgeon's step keeps its Wolfmed procedure open until its closing step.</summary>
    private void WolfmedStepDone(EntityUid part, Entity<SurgeryComponent> surgery, EntProtoId step) =>
        _wolfmedConditions.RecordStep(part, surgery, step);

    [Dependency] private WoundSystem _wolfmedWounds = default!; // WOLFGATE: HOOK 25
    [Dependency] private WolfmedSurgeryConditionSystem _wolfmedConditions = default!; // WOLFGATE: HOOK 24
    [Dependency] private IConfigurationManager _wolfmedCfg = default!; // WOLFGATE: HOOK 27
    [Dependency] private WolfmedWoundDamageSyncSystem _wolfmedDamageSync = default!; // WOLFGATE (AUTODOC5): HOOK 27
    [Dependency] private WolfmedWoundTraitSystem _wolfmedTraits = default!; // WOLFGATE (playtest 3 IPC 2): HOOK 24

    /// <summary>Whether a tend surgery should stay off this wound host's part (HOOK 24).</summary>
    // Upstream lists it wherever the body has any damage. On a wound host the part must carry something tending could
    // close, inside the surgery's severity window; non-hosts are unchanged. Never on a machine part, which is welded
    // and rewired instead (WFSurgeryWeldChassis, WFSurgeryRewireChassis).
    private bool WolfmedWoundWindowFails(Entity<SurgeryWoundedConditionComponent> ent, EntityUid body, EntityUid part)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        // Pre-merge review: a tend a surgeon has begun stays listed until its closing step, whatever the wound now reads.
        if (_wolfmedConditions.InProgress(part, ent.Owner))
            return false;

        if (_wolfmedTraits.IsMechanical(part))
            return true;

        var severity = _wolfmedConditions.GetTreatableGroupSeverity(part, ent.Comp.WoundGroup);
        if (severity <= FixedPoint2.Zero)
            return true;

        return ent.Comp.MinWoundSeverity is { } min && severity < min ||
               ent.Comp.MaxWoundSeverity is { } max && severity > max;
    }

    /// <summary>HOOK 25 body: true while a stump still carries an untreated AmputationConsequenceWound.</summary>
    private bool WolfmedStumpBlocksAttachment(EntityUid parent)
    {
        return TryComp(parent, out BodyPartComponent? bodyPart) && bodyPart.Body is { } body &&
               TryComp(body, out WoundHostComponent? host) &&
               _wolfmedWounds.GetWounds(parent).Any(wound => wound.Comp.Prototype == host.AmputationConsequenceWound);
    }

    /// <summary>Whether a tend step has wounds left to close on a host's part; null for a non-host (HOOK 26).</summary>
    // Upstream reads the whole body's damage of the group, so a torso tend never finished while a hand had a scratch.
    // On a wound host the step runs until the open part's wounds are closed, whatever the damage bookkeeping says.
    private bool? WolfmedTendPending(EntityUid body, EntityUid part, string mainGroup, string[] types)
    {
        if (!HasComp<WoundHostComponent>(body))
            return null;

        return _wolfmedConditions.GetTreatableGroupSeverity(part, mainGroup) > FixedPoint2.Zero;
    }

    /// <summary>Closes the part's own wounds at a suture's strength on each tend pass (HOOK 27).</summary>
    // Damage the step removes reaches a wound only through the routing's HealingMultiplier, a tenth of it, which is
    // too slow for the surgical way to close cuts and bruises. Wounds nothing closes and wounds refusing treatment are
    // untouched, because TreatWound raises the same attempt event a dressing does.
    private void WolfmedTendWounds(EntityUid body, EntityUid part, string mainGroup)
    {
        if (!HasComp<WoundHostComponent>(body))
            return;

        var strength = FixedPoint2.New(MathF.Max(0f, _wolfmedCfg.GetCVar(WolfmedCVars.SurgeryTendStrength)));
        if (strength <= FixedPoint2.Zero)
            return;

        var healing = new DamageSpecifier();
        foreach (var type in mainGroup == "Brute" ? BruteDamageTypes : BurnDamageTypes)
            healing.DamageDict[type] = -strength;

        _wolfmedWounds.TryHealWounds(part, healing);

        // AUTODOC5: closing the wound by hand leaves the damage it was made from on the part, and the body
        // totals every part, so a patient tended back to no wounds still read as hurt and only a brute pack
        // could clear it. The part now gives up the damage its wounds no longer account for.
        _wolfmedDamageSync.SyncPart(part);
    }
}
