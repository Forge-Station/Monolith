using Robust.Shared.GameStates;

namespace Content.Shared._WF.Tether;

/// <summary>
/// A player paying rope out from an attach point. The rope entity it owns is visual only until
/// the coil is used on a second attach point.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RopeCarrierComponent : Component
{
    [AutoNetworkedField] public NetEntity Rope;

    /// <summary>The attach point the loose end came from.</summary>
    [AutoNetworkedField] public NetEntity Anchor;

    /// <summary>The coil in hand. Dropping or stowing it cancels the carry.</summary>
    [AutoNetworkedField] public NetEntity Coil;

    /// <summary>Metres of rope the coil can still cover. Walking past this cancels the carry.</summary>
    [AutoNetworkedField] public float Reach;
}
