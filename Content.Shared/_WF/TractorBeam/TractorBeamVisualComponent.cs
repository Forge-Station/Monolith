using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.TractorBeam;

/// <summary>
/// A lightweight beam effect replicated independently of the dish's visibility range.
/// Its transform belongs directly to the source grid, avoiding a visibility override on machinery.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TractorBeamVisualComponent : Component
{
    [AutoNetworkedField] public EntityUid? Target;
    [AutoNetworkedField] public Vector2 TargetOffset;
    [AutoNetworkedField] public Box2 TargetBounds;
    [AutoNetworkedField] public float WidthScale = 1f;
    [AutoNetworkedField] public float Strain;
}
