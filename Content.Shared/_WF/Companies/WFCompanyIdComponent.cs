using Robust.Shared.GameStates;

namespace Content.Shared._WF.Companies;

/// <summary>An ID card issued to a company member, naming their company and rank on examine; clients read it to draw the card without waiting on the server.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCompanyIdComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CompanyId;

    [DataField, AutoNetworkedField]
    public string Company = string.Empty;

    [DataField, AutoNetworkedField]
    public string Tag = string.Empty;

    [DataField, AutoNetworkedField]
    public string Rank = string.Empty;

    /// <summary>The rank title put on the card at spawn, empty when it kept its job's title.</summary>
    [DataField, AutoNetworkedField]
    public string Title = string.Empty;
}
