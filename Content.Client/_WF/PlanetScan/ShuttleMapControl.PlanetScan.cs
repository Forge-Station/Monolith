using System.Numerics;
using Content.Client._WF.PlanetScan;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    /// <summary>The smallest radius a world is drawn at on the shuttle map, in virtual pixels.</summary>
    private const float WfScopeMinRadius = 10f;

    private WFPlanetScopeSprites? _wfScopeSprites;

    /// <summary>Lays the planet scan over the shuttle map.</summary>
    private void WfAddPlanetScanner()
    {
        var scanner = new WFPlanetScanner(this,
            () => ViewingMap == MapId.Nullspace || InFtl ? null : (WfWorldToView(), ViewingMap),
            WfScopeMinRadius)
        {
            Name = "PlanetScan",
        };
        AddChild(scanner);
        LayoutContainer.SetAnchorPreset(scanner, LayoutContainer.LayoutPreset.Wide);
    }

    /// <summary>World to this control's pixels, as <see cref="ScalePosition"/> places things.</summary>
    private Matrix3x2 WfWorldToView()
    {
        return Matrix3Helpers.CreateInverseTransform(Offset, Angle.Zero)
               * Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale))
               * Matrix3x2.CreateTranslation(MidPointVector);
    }

    /// <summary>Draws the star system's worlds as themselves, at a size that reads as a world; false leaves them to upstream.</summary>
    private bool WfDrawScopePlanets(DrawingHandleScreen handle, PlanetarySystem system, EntityUid? mapUid)
    {
        if (!EntManager.TryGetComponent<TransformComponent>(mapUid, out var xform))
            return false;

        _wfScopeSprites ??= new WFPlanetScopeSprites(EntManager, IoCManager.Resolve<IPrototypeManager>());
        _wfScopeSprites.Draw(handle, xform.MapID, system, WfWorldToView(), WfScopeMinRadius * UIScale,
            new UIBox2(Vector2.Zero, PixelSize));
        return true;
    }
}
