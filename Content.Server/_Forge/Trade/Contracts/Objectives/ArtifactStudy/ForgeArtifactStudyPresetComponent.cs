namespace Content.Server._Forge.Trade;

[RegisterComponent]
public sealed partial class ForgeArtifactStudyPresetComponent : Component
{
    [ViewVariables]
    public bool Applied;

    [DataField]
    public List<string> Effects = new();

    [DataField]
    public List<string> ExcludedEffects = new();

    [DataField]
    public int NodeCount = 5;

    [DataField]
    public int NodeCountMax;

    [DataField]
    public int NodeCountMin;

    [DataField]
    public List<string> Triggers = new();
}
