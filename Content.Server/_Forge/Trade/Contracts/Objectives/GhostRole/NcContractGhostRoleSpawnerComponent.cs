using Content.Shared.Roles;

namespace Content.Server._Forge.Trade;

[RegisterComponent]
public sealed partial class NcContractGhostRoleSpawnerComponent : Component
{
    public bool Claimed;

    public List<JobRequirement> Requirements = new();

    [DataField("prototype", required: true)]
    public string TargetPrototype = string.Empty;
}

public sealed class GhostRoleGetRequirementsEvent : EntityEventArgs
{
    public GhostRoleGetRequirementsEvent(List<JobRequirement>? requirements)
    {
        Requirements = requirements;
    }

    public List<JobRequirement>? Requirements { get; set; }
}
