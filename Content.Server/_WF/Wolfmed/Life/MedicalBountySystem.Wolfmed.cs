using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._NF.Medical.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.Random;

namespace Content.Server._NF.Medical;

public sealed partial class MedicalBountySystem
{
    private const string Asphyxiation = "Asphyxiation";

    /// <summary>A random bounty this body can carry, or any bounty when none fits.</summary>
    private MedicalBountyPrototype PickBountyFor(EntityUid body)
    {
        var fitting = _cachedPrototypes.Where(bounty => Fits(body, bounty)).ToList();
        return _random.Pick(fitting.Count > 0 ? fitting : _cachedPrototypes);
    }

    /// <summary>
    /// Whether the body can take every injury of the bounty. A bounty with a damage type the body has no room for (an
    /// IPC and poison, a synth and suffocation) dealt the rest or nothing, and came up redeemable untreated.
    /// </summary>
    public bool Fits(EntityUid body, MedicalBountyPrototype bounty)
    {
        if (!TryComp(body, out DamageableComponent? damageable))
            return false;

        foreach (var type in bounty.DamageSets.Keys)
        {
            if (!damageable.Damage.DamageDict.ContainsKey(type))
                return false;

            // A body with no respirator holds no suffocation.
            if (type == Asphyxiation && !HasComp<RespiratorComponent>(body))
                return false;
        }

        return true;
    }
}
