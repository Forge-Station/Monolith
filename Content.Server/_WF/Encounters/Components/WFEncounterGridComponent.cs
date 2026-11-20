namespace Content.Server._WF.Encounters.Components;

/// <summary>Marks a ship as belonging to an encounter.</summary>
[RegisterComponent]
public sealed partial class WFEncounterGridComponent : Component
{
    [DataField]
    public EntityUid Encounter;

    [DataField]
    public string Key = string.Empty;
}
