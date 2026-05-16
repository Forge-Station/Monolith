using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Forge-Change: replicated shield HUD/radar snapshot stored on the grid entity so consoles and
/// mass scanners can show shield HP and outlines without PVS visibility of the emitter or bubble.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipShieldGridStateComponent : Component
{
    /// <summary>
    /// True when at least one anchored shield emitter exists on this grid.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool HasEmitter;

    [ViewVariables, AutoNetworkedField]
    public float Damage;

    [ViewVariables, AutoNetworkedField]
    public float DamageLimit;

    [ViewVariables, AutoNetworkedField]
    public bool Recharging;

    [ViewVariables, AutoNetworkedField]
    public bool Online;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan? RechargeEndTime;

    [ViewVariables, AutoNetworkedField]
    public Color ShieldColor = Color.White;

    /// <summary>
    /// Extra padding around the grid AABB when drawing the shield outline on radar.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float Padding = 50f;
}
