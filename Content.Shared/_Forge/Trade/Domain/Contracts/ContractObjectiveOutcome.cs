using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum ContractObjectiveOutcome : byte
{
    None = 0,
    Success,
    Failed,
    RoleSurvived,
    NotAccepted,
    TargetLost,
    TargetRotten
}
