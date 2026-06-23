namespace Content.Server._Mono.FireControl;

// Forge-Change-full
[RegisterComponent]
public sealed partial class GunneryServerUpgradeBoardComponent : Component
{
    [DataField(required: true)]
    public GunneryServerTier SourceTier;

    [DataField(required: true)]
    public GunneryServerTier TargetTier;

    [DataField]
    public Dictionary<string, int> RequiredParts = new();
}
