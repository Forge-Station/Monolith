using Content.Client.Administration.Managers;
using Content.Shared._WF.Administration.RadarTeleport;
using Content.Shared.Administration;
using Content.Shared.Ghost;
using Robust.Client.Player;
using Robust.Shared.Map;

namespace Content.Client._WF.Administration.RadarTeleport;

/// <summary>
/// Lets an admin ghost teleport to a spot clicked on the mass scanner.
/// </summary>
public sealed partial class RadarTeleportSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Whether a click on the scanner teleports. Kept here so it survives the window closing.
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// True while the local player is an admin in a ghost.
    /// </summary>
    public bool CanTeleport()
    {
        return _admin.HasFlag(AdminFlags.Admin) && HasComp<GhostComponent>(_player.LocalEntity);
    }

    /// <summary>
    /// Asks the server to move the local ghost to the clicked scanner coordinates.
    /// </summary>
    public void RequestTeleport(EntityCoordinates coordinates)
    {
        if (!Enabled || !CanTeleport() || !coordinates.IsValid(EntityManager))
            return;

        var target = _transform.ToMapCoordinates(coordinates);
        if (target.MapId == MapId.Nullspace)
            return;

        RaiseNetworkEvent(new RadarTeleportRequestEvent(target));
    }
}
