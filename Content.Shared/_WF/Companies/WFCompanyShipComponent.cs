using Robust.Shared.GameStates;

namespace Content.Shared._WF.Companies;

/// <summary>A ship flying under a player company, shown on its IFF label.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCompanyShipComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CompanyId;

    [DataField, AutoNetworkedField]
    public string Name = string.Empty;

    [DataField, AutoNetworkedField]
    public string Tag = string.Empty;

    [DataField, AutoNetworkedField]
    public Color Colour = Color.White;

    /// <summary>The IFF colour before the company's was applied, restored when the ship stops flying for it.</summary>
    [DataField]
    public Color? PreviousIffColour;

    /// <summary>The faction company the ship carried, whose colour would otherwise replace the company's on radar labels.</summary>
    [DataField]
    public string? PreviousFaction;
}
