using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed partial class ShowHealthIconsSystem
{
    /// <summary>
    /// ARREST: a body whose heart has stopped is MobState.Critical and looks dead to everyone examining it.
    /// A medical HUD is what tells the two apart, so an arrested patient wears a flatline instead of the
    /// ordinary critical icon.
    /// </summary>
    private static readonly ProtoId<HealthIconPrototype> ArrestIcon = "WFHealthIconWolfmedArrest";

    /// <summary>M1a: whether the local player sees health icons, for the Call for help flag.</summary>
    public bool WolfmedHudActive => IsActive;

    private HealthIconPrototype? WolfmedArrestIcon(EntityUid uid)
    {
        return HasComp<WolfmedCardiacArrestComponent>(uid) && _prototypeMan.TryIndex(ArrestIcon, out var icon)
            ? icon
            : null;
    }
}
