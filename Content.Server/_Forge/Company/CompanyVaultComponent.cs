using Content.Shared._Forge.Company;

namespace Content.Server._Forge.Company;

/// <summary>
/// The company's shared safe. It lives off the map and is brought to whoever opens it.
/// </summary>
[RegisterComponent]
public sealed partial class CompanyVaultComponent : Component
{
    [ViewVariables]
    public string CompanyId = string.Empty;

    [ViewVariables]
    public int Width = CompanyOrgLimits.VaultGrid;

    [ViewVariables]
    public int Height = CompanyOrgLimits.VaultGrid;

    /// <summary>True while saved items are being put back, so that insert does not write the safe halfway.</summary>
    [ViewVariables]
    public bool Restoring;

    /// <summary>Saved items that failed to spawn. They stay in the database on the next write.</summary>
    public List<string> Unloaded = new();

    public EntityUid? OpenedBy;

    /// <summary>
    /// The person who has it open may put items in and take them out.
    /// When false, the same window only shows the contents and lets papers be read.
    /// </summary>
    public bool Writable;
}
