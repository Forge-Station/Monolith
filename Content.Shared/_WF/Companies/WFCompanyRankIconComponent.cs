using Robust.Shared.GameStates;

namespace Content.Shared._WF.Companies;

/// <summary>An ID card whose job icon is its holder's company rank icon.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCompanyRankIconComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RankId;
}

/// <summary>Marks a player's body so the HUD can swap in a company rank icon from whatever ID it carries.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFCompanyRankHudComponent : Component;
