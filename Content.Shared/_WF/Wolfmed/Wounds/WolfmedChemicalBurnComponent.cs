using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// On a body part still carrying corrosive residue. Its presence is the whole state: while it is there the
/// chemical burn keeps eating the part, and washing the patient takes it off.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedChemicalBurnComponent : Component
{
    /// <summary>Seconds since the residue last bit.</summary>
    [ViewVariables]
    public float Accumulator;

    /// <summary>
    /// Seconds between bites, cached off the worst <see cref="WolfmedCausticResidueBehavior"/> on the part
    /// so the per-frame pass can skip the part without walking its wounds.
    /// </summary>
    [ViewVariables]
    public float Interval = 4f;
}
