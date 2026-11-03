using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Turrets;

/// <summary>
/// Marks a turret that obeys a <see cref="TurretCommandServerComponent"/> on its grid.
/// PvE turrets leave this off (or set <see cref="Managed"/> false) and keep attacking everyone.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TurretCommandLinkComponent : Component
{
    /// <summary>
    /// When false, the turret ignores the command server and uses its own AI.
    /// </summary>
    [DataField]
    public bool Managed = true;

    /// <summary>
    /// Binds this turret only to a server of the same faction.
    /// </summary>
    [DataField]
    public string Faction = string.Empty;

    /// <summary>
    /// Binds to the nearest command server on the grid, whatever its faction.
    /// Used by ASPT, so one turret type shows up on every faction console.
    /// </summary>
    [DataField]
    public bool BindAny;

    /// <summary>
    /// Two command turrets closer than four tiles. Neither acquires targets until one is moved away.
    /// </summary>
    [ViewVariables]
    public bool Jammed;

    /// <summary>
    /// When true, the magazine cannot be pulled out. Loading an empty turret still works.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool MagazineLocked;

    /// <summary>
    /// Console stand-down. A stood-down turret does not acquire targets.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// This turret's doctrine. The console can set one turret or every turret at once.
    /// </summary>
    [DataField]
    public TurretDoctrine Doctrine = TurretDoctrine.Neutral;

    /// <summary>
    /// Server this turret is currently bound to. Server-only cache.
    /// </summary>
    [ViewVariables]
    public EntityUid? Server;
}
