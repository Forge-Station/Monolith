using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>Uses the radar's existing pan, zoom and rotation transform for cockpit aiming.</summary>
    public EntityCoordinates WfCockpitAimCoordinates(Vector2 relativePosition) => GetMouseEntityCoordinates(relativePosition);

    /// <summary>The radar's current world-to-pixel transform and the map it plots, for cockpit overlays drawn over it.</summary>
    public bool WfCockpitTryGetWorldToView(out Matrix3x2 worldToView, out MapId map)
    {
        worldToView = default;
        map = MapId.Nullspace;
        if (_coordinates == null || _rotation == null ||
            !EntManager.TryGetComponent<TransformComponent>(_coordinates.Value.EntityId, out var xform) ||
            xform.MapID == MapId.Nullspace)
            return false;

        var centre = _transform.ToMapCoordinates(_coordinates.Value).Offset(_rotation.Value.RotateVec(Offset));
        var worldToShuttle = Matrix3Helpers.CreateTranslation(-centre.Position) * Matrix3Helpers.CreateRotation(-_rotation.Value);
        var shuttleToView = Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) * Matrix3x2.CreateTranslation(MidPointVector);
        worldToView = worldToShuttle * shuttleToView;
        map = xform.MapID;
        return true;
    }

    /// <summary>Keeps the radar's tracking frame consistent with right-mouse panning.</summary>
    protected override void WfCockpitPanMoved()
    {
        if (_coordinates == null)
            return;

        if (!_relativePanning &&
            _coordinates?.EntityId is { } coordEnt &&
            EntManager.TryGetComponent<TransformComponent>(coordEnt, out var coordXform) &&
            coordXform.MapUid != coordEnt)
            _coordinates = _transform.ToCoordinates(_transform.ToMapCoordinates(_coordinates.Value));

        _wasPanned = true;
    }
}
