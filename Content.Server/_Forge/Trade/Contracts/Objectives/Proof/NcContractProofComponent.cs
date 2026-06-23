namespace Content.Server._Forge.Trade;

[RegisterComponent]
public sealed partial class NcContractProofComponent : Component
{
    [DataField]
    public string ContractId = string.Empty;

    [DataField]
    public string ProofToken = string.Empty;

    public EntityUid Store;
}
