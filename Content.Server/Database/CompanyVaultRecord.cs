namespace Content.Server.Database;

public sealed class CompanyVaultRecord
{
    public int Width = Content.Shared._Forge.Company.CompanyOrgLimits.VaultGrid;
    public int Height = Content.Shared._Forge.Company.CompanyOrgLimits.VaultGrid;
    public List<string> Items = new();
}
