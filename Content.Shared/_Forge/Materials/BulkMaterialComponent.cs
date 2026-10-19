using Robust.Shared.GameObjects;

namespace Content.Shared._Forge.Materials;

/// <summary>
/// Ore and material stacks. Nearby loose stacks merge over time, and one stack
/// takes more bag space as its count grows.
/// </summary>
[RegisterComponent]
public sealed partial class BulkMaterialComponent : Component
{
    /// <summary>
    /// Stacks can grow up to this amount. Existing smaller prototype caps are raised to it.
    /// </summary>
    [DataField]
    public int MaxCount = 1000;

    /// <summary>
    /// Loose stacks of the same type within this range are merged together.
    /// </summary>
    [DataField]
    public float MergeRange = 1.25f;

    /// <summary>
    /// 1..SmallMax occupies a single bag cell.
    /// </summary>
    [DataField]
    public int SmallMax = 10;

    /// <summary>
    /// SmallMax+1..MediumMax occupies 2x2.
    /// </summary>
    [DataField]
    public int MediumMax = 100;

    /// <summary>
    /// MediumMax+1..LargeMax occupies 2x3.
    /// Above this the stack occupies 3x3, needs two hands and slows the carrier.
    /// </summary>
    [DataField]
    public int LargeMax = 300;

    /// <summary>
    /// Walk and sprint multiplier while a stack above <see cref="LargeMax"/> is carried. 0.8 is 20% slower.
    /// </summary>
    [DataField]
    public float HeavySpeedModifier = 0.8f;
}
