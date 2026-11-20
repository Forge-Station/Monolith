namespace Content.Server._WF.Remade.Abilities;

/// <summary>The abilities a Remade uses by itself while it has a target. They're tried in the order listed.</summary>
[RegisterComponent]
public sealed partial class WFRemadeAbilitiesComponent : Component
{
    [DataField(required: true)]
    public List<WFAbility> Abilities = new();

    /// <summary>The ability that's running.</summary>
    [ViewVariables]
    public WFAbility? Active;

    /// <summary>The target it had last tick, to tell when it first sees a new one.</summary>
    [ViewVariables]
    public EntityUid? LastTarget;
}
