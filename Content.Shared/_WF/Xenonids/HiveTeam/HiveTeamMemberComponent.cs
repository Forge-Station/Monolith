using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Xenonids.HiveTeam;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveTeamMemberComponent : Component
{
    [DataField, AutoNetworkedField]
    public int TeamNumber;

    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi Icon = new(new ResPath("/Textures/_WF/Xenonids/Interface/fireteam.rsi"), "hudsquad_ft1");

    [DataField, AutoNetworkedField]
    public Color IconColor = Color.FromHex("#7B2FBE");
}
