using System.Numerics;
using Content.Client._WF.DroneRings;
using Content.Client._WF.Lattice;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private readonly List<float> _wfLatticeRadii = new();
    private readonly List<string> _wfLatticeLabels = new();
    private readonly List<Color> _wfLatticeColors = new();
    private string? _wfLatticeBuilt;

    /// <summary>Rings at the Lattice's band edges, or where drones start to spawn on a map without one.</summary>
    private void DrawDroneRings(DrawingHandleScreen handle, Matrix3x2 matty, EntityUid map)
    {
        var centre = Vector2.Transform(Vector2.Zero, matty);
        centre = ScalePosition(centre with { Y = -centre.Y });
        if (EntManager.TryGetComponent<WFLatticeFieldComponent>(map, out var field)
            && (WFLatticeRings.IsCurrent(field, _wfLatticeBuilt, _wfLatticeRadii)
                || WFLatticeRings.TryBuild(IoCManager.Resolve<IPrototypeManager>(), field, _wfLatticeRadii, _wfLatticeLabels, _wfLatticeColors)))
        {
            _wfLatticeBuilt = field.Profile.Id;
            WFDroneRingDrawing.Draw(handle, Font, _wfLatticeRadii, centre, MinimapScale, PixelSize, UIScale, _wfLatticeLabels, _wfLatticeColors);
            return;
        }

        if (!EntManager.TryGetComponent<WFDroneRingsComponent>(map, out var rings))
            return;

        WFDroneRingDrawing.Draw(handle, Font, rings.Radii, centre, MinimapScale, PixelSize, UIScale);
    }
}
