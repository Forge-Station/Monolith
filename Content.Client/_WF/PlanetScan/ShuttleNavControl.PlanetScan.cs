using System.Numerics;
using Content.Client._WF.PlanetScan;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The smallest radius a world is drawn at on the radar, in virtual pixels.</summary>
    private const float WfScopeMinRadius = 12f;

    private WFPlanetScopeSprites? _wfScopeSprites;

    /// <summary>Lays the planet scan over the radar.</summary>
    private void WfAddPlanetScanner()
    {
        var scanner = new WFPlanetScanner(this,
            () => WfCockpitTryGetWorldToView(out var worldToView, out var map) ? (worldToView, map) : null,
            WfScopeMinRadius)
        {
            Name = "PlanetScan",
        };
        AddChild(scanner);
        LayoutContainer.SetAnchorPreset(scanner, LayoutContainer.LayoutPreset.Wide);
    }

    /// <summary>Draws the star system's worlds as themselves, at a size that reads as a world; false leaves them to upstream.</summary>
    private bool WfDrawScopePlanets(DrawingHandleScreen handle, PlanetarySystem system, Matrix3x2 worldToView, EntityUid? mapUid)
    {
        if (!EntManager.TryGetComponent<TransformComponent>(mapUid, out var xform))
            return false;

        _wfScopeSprites ??= new WFPlanetScopeSprites(EntManager, IoCManager.Resolve<IPrototypeManager>());
        _wfScopeSprites.Draw(handle, xform.MapID, system, worldToView, WfScopeMinRadius * UIScale,
            new UIBox2(Vector2.Zero, PixelSize));
        return true;
    }

    /// <summary>
    /// Whether a radar beacon is a world on the scope, whose sprite and hover scan stand in for its label. Records where
    /// it is for a click to open its signal list.
    /// </summary>
    private bool WfIsScopePlanet(EntityUid uid, Vector2 uiPosition)
    {
        if (WfPlanetRadius(uid) <= 0f)
            return false;

        _wfPlanetHits.Add((uid, uiPosition * UIScale, MathF.Max(WfScopeMinRadius * UIScale, WfPlanetRadius(uid)), default));
        return true;
    }
}
