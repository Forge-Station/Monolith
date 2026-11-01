namespace Content.Server._WF.Companies;

/// <summary>Something that came aboard a company hangar ship at launch; it sells for nothing.</summary>
[RegisterComponent]
public sealed partial class WFCompanyPropertyComponent : Component
{
    [DataField]
    public int CompanyId;
}
