using System.Linq;
using Content.Shared._WF.Remade.Limbs;

namespace Content.Server._WF.Remade.Limbs;

/// <summary>Growing limbs back: the hunter and the Undying regenerate.</summary>
public sealed partial class WFRemadeLimbsSystem
{
    /// <summary>The severed limbs in the order they grow back: the head first, then the rest as listed.</summary>
    public List<string> GrowOrder(WFRemadeLimbsComponent comp)
    {
        return comp.Limbs.FindAll(limb => comp.Severed.Contains(limb.Id)).Select(limb => limb.Id).OrderBy(id => id == "head" ? 0 : 1).ToList();
    }

    /// <summary>Grows back up to this many severed limbs, the head first. Returns how many came back.</summary>
    public int Regrow(EntityUid uid, int count, WFRemadeLimbsComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false) || comp.Severed.Count == 0 || count <= 0)
            return 0;

        var grown = 0;
        foreach (var id in GrowOrder(comp))
        {
            if (grown >= count)
                break;

            comp.Severed.Remove(id);
            comp.Damage.Remove(id);
            comp.Growing.Remove(id);
            grown++;
        }

        Regrown(uid, comp, grown);
        return grown;
    }

    /// <summary>Grows one named limb back. Returns whether it was severed.</summary>
    public bool Regrow(EntityUid uid, string id, WFRemadeLimbsComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false) || !comp.Severed.Remove(id))
            return false;

        comp.Damage.Remove(id);
        comp.Growing.Remove(id);
        Regrown(uid, comp, 1);
        return true;
    }

    /// <summary>Marks severed limbs as growing back, which the client draws with their regrowth art.</summary>
    public void SetGrowing(EntityUid uid, IEnumerable<string> ids, WFRemadeLimbsComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        var growing = ids.Where(comp.Severed.Contains).ToHashSet();
        if (growing.SetEquals(comp.Growing))
            return;

        comp.Growing = growing;
        Dirty(uid, comp);
    }

    private void Regrown(EntityUid uid, WFRemadeLimbsComponent comp, int grown)
    {
        if (grown == 0)
            return;

        Dirty(uid, comp);
        _movement.RefreshMovementSpeedModifiers(uid);
        RefreshVision((uid, comp));
    }
}
