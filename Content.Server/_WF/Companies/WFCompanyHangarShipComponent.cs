namespace Content.Server._WF.Companies;

/// <summary>A ship launched from a company hangar licence; it belongs to the company and can't be sold.</summary>
[RegisterComponent]
public sealed partial class WFCompanyHangarShipComponent : Component
{
    [DataField]
    public int CompanyId;
}
