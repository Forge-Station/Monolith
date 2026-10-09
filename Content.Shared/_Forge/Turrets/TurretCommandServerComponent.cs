using Robust.Shared.Network;

namespace Content.Shared._Forge.Turrets;

/// <summary>
/// Shared IFF state for faction turrets on the same grid.
/// PvE turrets never read this.
/// </summary>
[RegisterComponent]
public sealed partial class TurretCommandServerComponent : Component
{
    [DataField]
    public List<TurretIffEntry> Entries = new();

    /// <summary>
    /// Only turrets with the same faction id use this server.
    /// </summary>
    [DataField]
    public string Faction = string.Empty;

    [DataField]
    public TurretDoctrine Doctrine = TurretDoctrine.Neutral;

    /// <summary>
    /// Faction accent used by the console window.
    /// </summary>
    [DataField]
    public Color Accent = Color.FromHex("#c87820");

    /// <summary>
    /// True after the operator presses refresh. The list is not live.
    /// </summary>
    [DataField]
    public bool PlayersScanned;

    /// <summary>
    /// Character names on this grid at the last refresh. Positions are not stored.
    /// </summary>
    [DataField]
    public List<string> Detected = new();
}

[DataDefinition]
public sealed partial class TurretIffEntry
{
    [DataField]
    public NetUserId? UserId;

    [DataField]
    public EntityUid? Entity;

    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// True: never shoot. False: always shoot, even if the faction would ignore them.
    /// </summary>
    [DataField]
    public bool Friendly;
}
