using System.Numerics;
using Content.Client._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.Planets;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    /// <summary>How far past the plot's corners the world's disc reaches, as a share of its diagonal.</summary>
    private const float WfPlanetFill = 0.6f;

    private PlanetOverlay? _wfPlanets;
    private ShaderInstance? _wfPlanetShader;
    private Planet? _wfPlanet;
    private EntityUid? _wfPlanetBody;

    /// <summary>
    /// Over a world's layers, the plot's backdrop is the world itself, as the sector draws it, close enough to fill the
    /// plot. False anywhere else, so the space background is drawn instead.
    /// </summary>
    private bool DrawWfPlanetBackground(DrawingHandleScreen handle, EntityUid map)
    {
        if (!WfEnsurePlanetShader(map) || _wfPlanetShader is not { } shader || _wfPlanet is not { } planet)
            return false;

        // The shader places the body in world space: hand it a frame round the body, the plot's shape, with the
        // body's disc just past the corners.
        var size = new Vector2(PixelWidth, PixelHeight) / MathF.Max(PixelWidth, PixelHeight);
        shader.SetParameter("planetRadius", size.Length() * WfPlanetFill);
        shader.SetParameter("parallaxCenter", planet.Position);
        shader.SetParameter("viewportMin", planet.Position - size / 2f);
        shader.SetParameter("viewportSize", size);

        var box = new UIBox2(Vector2.Zero, PixelSize);
        handle.DrawRect(box, Color.Black);
        handle.UseShader(shader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(null);
        return true;
    }

    /// <summary>Finds the sector body a planet layer belongs to and builds its shader, once per body.</summary>
    private bool WfEnsurePlanetShader(EntityUid map)
    {
        var network = EntManager.TryGetComponent<WFOrbitLayerComponent>(map, out var orbit) ? orbit.Network
            : EntManager.TryGetComponent<WFPlanetLayerComponent>(map, out var layer) ? layer.Network
            : null;
        if (network == null)
            return false;

        EntityUid? body = null;
        var query = EntManager.AllEntityQueryEnumerator<WFSectorPlanetComponent>();
        while (query.MoveNext(out var uid, out var sector))
        {
            if (sector.Network != network)
                continue;

            body = uid;
            break;
        }

        if (body == null)
            return false;

        if (body == _wfPlanetBody)
            return _wfPlanetShader != null;

        _wfPlanetBody = body;
        _wfPlanetShader = null;
        _wfPlanet = null;

        var xform = EntManager.GetComponent<TransformComponent>(body.Value);
        if (!EntManager.TryGetComponent<StarSystemMapComponent>(xform.MapUid, out var system) || system.StarSystem is not { } stars)
            return false;

        foreach (var planet in stars.Planets)
        {
            if ((planet.Position - xform.LocalPosition).LengthSquared() < 1f)
                _wfPlanet = planet;
        }

        if (_wfPlanet == null)
            return false;

        _wfPlanets ??= new PlanetOverlay(EntManager, IoCManager.Resolve<IPrototypeManager>());
        _wfPlanetShader = _wfPlanets.SetupPlanetShader(_wfPlanet, stars.Star);
        return _wfPlanetShader != null;
    }
}
