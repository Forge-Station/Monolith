namespace Content.Server._WF.Remade.Elites;

/// <summary>Whoever a melee hit from this mob lands on is flung away from it. The brute has it.</summary>
[RegisterComponent]
public sealed partial class WFFlingOnHitComponent : Component
{
    /// <summary>Tiles the target is thrown.</summary>
    [DataField]
    public float Distance = 3.5f;

    [DataField]
    public float Speed = 10f;

    /// <summary>Seconds the target is knocked down for. 0 means not at all.</summary>
    [DataField]
    public float Knockdown = 1.5f;
}
