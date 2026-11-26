// WOLFGATE: W0. Topicals are restricted by the damage type of the wound they treat: a bruise pack works on
// Blunt, gauze and sutures on Slash/Piercing plus bleeding, ointment on the burn group. The restriction is
// one data field, HealingComponent.TreatedDamageTypes, and this is the single place an item's healing spec
// is narrowed before any wound-host path reads it.

using System.Linq;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Medical.Components;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Damage;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundHealingSystem
{
    [Dependency] private WolfmedInfectionSystem _wfInfection = default!;

    /// <summary>
    /// The part of an item's healing spec it is allowed to apply to a wound host. Types outside
    /// <see cref="HealingComponent.TreatedDamageTypes"/> are dropped, so an item with nothing left to give
    /// resolves no part and the caller reports that it cannot treat the injury.
    /// </summary>
    public DamageSpecifier GetTreatableDamage(HealingComponent healing)
    {
        if (healing.TreatedDamageTypes is not { Count: > 0 } allowed)
            return healing.Damage;

        var result = new DamageSpecifier();
        foreach (var (type, amount) in healing.Damage.DamageDict)
        {
            if (allowed.Contains(type))
                result.DamageDict[type] = amount;
        }

        return result;
    }

    /// <summary>
    /// Playtest 5: a dirty item (<see cref="WolfmedDirtyTreatmentComponent"/>, the makeshift suture) leaves the open
    /// wounds it just treated on the part contaminated. Called by <see cref="TryApplyHealing"/> once it has done something.
    /// </summary>
    private void ContaminateIfDirty(Entity<HealingComponent> healing, EntityUid part)
    {
        if (HasComp<WolfmedDirtyTreatmentComponent>(healing))
            _wfInfection.ContaminateTreated(part, healing.Comp.TreatedDamageTypes?.Select(type => type.Id).ToList());
    }
}
