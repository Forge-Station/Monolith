namespace Content.Server._WF.Remade.Elites;

/// <summary>An Infector that turns corpses faster than the plain kind: its conversions take this share of the time.</summary>
[RegisterComponent]
public sealed partial class WFReanimateHasteComponent : Component
{
    [DataField]
    public float Share = 0.55f;
}

/// <summary>A conversion that has already been sped up.</summary>
[RegisterComponent]
public sealed partial class WFReanimateHastenedComponent : Component;
