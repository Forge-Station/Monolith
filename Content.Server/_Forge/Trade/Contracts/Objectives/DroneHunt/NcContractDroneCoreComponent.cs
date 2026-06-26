namespace Content.Server._Forge.Trade;

[RegisterComponent]
public sealed partial class NcContractDroneCoreComponent : Component
{
    [DataField]
    public string ContractId = string.Empty;

    public EntityUid Store;
}
