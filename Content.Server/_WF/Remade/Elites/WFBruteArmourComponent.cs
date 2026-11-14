namespace Content.Server._WF.Remade.Elites;

/// <summary>Armour that depends on which way the blow comes from: heavy in front, thin behind. The brute has it.</summary>
[RegisterComponent]
public sealed partial class WFBruteArmourComponent : Component
{
    /// <summary>The share of damage it takes from the front.</summary>
    [DataField]
    public float Front = 0.35f;

    /// <summary>The share of damage it takes from the side, or from something with no direction such as a blast.</summary>
    [DataField]
    public float Side = 0.9f;

    /// <summary>The share of damage it takes from behind.</summary>
    [DataField]
    public float Back = 1.4f;

    /// <summary>Attackers within this many degrees of straight ahead count as the front.</summary>
    [DataField]
    public float FrontArc = 60f;

    /// <summary>Attackers further than this many degrees from straight ahead count as behind.</summary>
    [DataField]
    public float BackArc = 120f;
}
