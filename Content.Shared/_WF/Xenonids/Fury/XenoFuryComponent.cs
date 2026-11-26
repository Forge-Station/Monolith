using Content.Shared._WF.RMCCore.Maths;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Fury;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoFuryComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Heal = 15;

    [DataField, AutoNetworkedField]
    public int BoostedHeal = 25;

    [DataField, AutoNetworkedField]
    public EntProtoId Effect = "RMCEffectHeal";

    [DataField, AutoNetworkedField]
    public float Range = RMCMathExtensions.CircleAreaFromSquareAbilityRange(3);
}
