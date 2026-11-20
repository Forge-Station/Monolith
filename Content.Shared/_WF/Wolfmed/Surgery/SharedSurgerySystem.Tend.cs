// WOLFGATE (playtest 3 SAM): the bodies of the tend hooks in SharedSurgerySystem.cs (listing) and .Steps.cs (the step).

using Content.Shared._Onyx.Wounds;

namespace Content.Shared._Shitmed.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    /// <summary>
    /// The tend surgeries list only while the body or the part carries some damage (or the part is open). On a wound
    /// host HOOK 24 already asks the part for wounds a tend could close, and the damage can be gone while they stay:
    /// a patient healed with packs kept every wound and the pod planned nothing for them.
    /// </summary>
    private bool WolfmedJudgedByWounds(EntityUid body) => HasComp<WoundHostComponent>(body);

    /// <summary>Tends a wound host's wounds with no group damage left; true when this answered the step.</summary>
    // The tend step returns before HOOK 27 unless there is damage of its group, but on a wound host chems, packs and
    // the pod's damage sync take that off while the wounds stay, and the completion check (HOOK 26) reads the wounds.
    private bool WolfmedTendUndamaged(EntityUid body, EntityUid part, string mainGroup, string[] types)
    {
        if (!HasComp<WoundHostComponent>(body) ||
            HasDamageGroup(body, types, out _) ||
            HasDamageGroup(part, types, out _))
            return false;

        WolfmedTendWounds(body, part, mainGroup);
        return true;
    }
}
