using Content.Shared._WF.RMCCore.Maths;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Finesse;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoFinesseSystem))]
public sealed partial class XenoFinesseComponent : Component
{
    [DataField]
    public TimeSpan MarkedTime = TimeSpan.FromSeconds(3.5);

    [DataField]
    public TimeSpan CriticalMarkTime = TimeSpan.FromSeconds(7);

    [DataField]
    public float SpreadCriticalMarkRange = RMCMathExtensions.CircleAreaFromSquareAbilityRange(5);

    [DataField]
    public int? MaxCriticalMarkSpread = 5;

    [DataField]
    public TimeSpan CritcalMarkSpreadCooldown = TimeSpan.FromSeconds(7);

    [DataField, AutoNetworkedField]
    public TimeSpan NextCriticalMarkSpreadTime;

    [DataField]
    public TimeSpan CriticalMarkSpreadImmuneDuration = TimeSpan.FromSeconds(20);
}
