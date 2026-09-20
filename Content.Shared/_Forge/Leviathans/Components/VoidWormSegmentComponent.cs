using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Leviathans.Components;

/// <summary>
/// Visual body segment owned by a void worm leviathan.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoidWormSegmentComponent : Component
{
    [DataField]
    public EntityUid OwnerWorm;
}
