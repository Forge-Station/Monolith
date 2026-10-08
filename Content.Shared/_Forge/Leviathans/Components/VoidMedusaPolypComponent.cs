using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Leviathans.Components;

/// <summary>
/// Shock-jelly spawn of a void medusa. Dies into a small EMP bloom.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoidMedusaPolypComponent : Component
{
    [DataField]
    public float EmpRange = 3.5f;

    [DataField]
    public float EmpEnergy = 400000f;

    [DataField]
    public TimeSpan EmpDuration = TimeSpan.FromSeconds(6);
}
