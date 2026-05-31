using Robust.Shared.Containers;

namespace Content.Server._Mono.FireControl;

// Forge-Change-full
[RegisterComponent]
public sealed partial class GunneryServerUpgradeConstructionComponent : Component
{
    public const string PartsContainerId = "upgrade_parts";

    [DataField]
    public GunneryServerTier TargetTier;

    [DataField]
    public Dictionary<string, int> RequiredParts = new();

    [ViewVariables]
    public Dictionary<string, int> InsertedParts = new();

    [ViewVariables]
    public Container PartsContainer = default!;
}
