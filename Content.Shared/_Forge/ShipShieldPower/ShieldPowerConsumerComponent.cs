namespace Content.Shared._Forge.ShipShieldPower;

/// <summary>
/// Shield emitter draw from a same-grid <see cref="ShieldPowerSourceComponent"/>.
/// Replaces <c>ApcPowerReceiver</c> for ship shields so combat load never hits Pow3r.
/// </summary>
[RegisterComponent]
public sealed partial class ShieldPowerConsumerComponent : Component
{
    /// <summary>
    /// Desired watts this tick (base + damage load). Written by the shield emitter system.
    /// </summary>
    [ViewVariables]
    public float DesiredDraw;

    /// <summary>
    /// Watts allocated by <see cref="Content.Server._Forge.ShipShieldPower.ShieldPowerSystem"/> last tick.
    /// </summary>
    [ViewVariables]
    public float PowerReceived;

    /// <summary>
    /// True when <see cref="PowerReceived"/> meets the linked source powered threshold.
    /// </summary>
    [ViewVariables]
    public bool Powered;

    /// <summary>
    /// When false, this emitter draws nothing and reports unpowered.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Explicit link override. Null = auto-bind to the best source on the same grid.
    /// </summary>
    [ViewVariables]
    public EntityUid? LinkedSource;

    /// <summary>
    /// Set on shield hits; load is recomputed on the next power tick instead of every projectile.
    /// </summary>
    [ViewVariables]
    public bool LoadDirty;
}
