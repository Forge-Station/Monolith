using System.Numerics;
using Content.Shared._WF.Vehicles.Scanner;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfVehicleScannerColor = Color.FromHex("#3fe05a");

    /// <summary>On a vehicle's scanner: a ring round the vehicle, and its position along the bottom.</summary>
    private void DrawWfVehicleScanner(DrawingHandleScreen handle, Matrix3x2 worldToView)
    {
        if (!EntManager.HasComponent<WFVehicleScannerComponent>(_consoleEntity)
            || _wfRadarOrigin is not { } origin
            || !EntManager.EntityExists(origin.EntityId))
            return;

        var position = _transform.GetWorldPosition(origin.EntityId);
        var point = Vector2.Transform(position, worldToView);
        handle.DrawCircle(point, 6f * UIScale, WfVehicleScannerColor, false);

        var text = Loc.GetString("wf-vehicle-scanner-position", ("x", position.X.ToString("0")), ("y", position.Y.ToString("0")));
        handle.DrawString(Font, new Vector2(10f * UIScale, PixelSize.Y - 24f * UIScale), text, UIScale, WfVehicleScannerColor);
    }
}
