using System.Numerics;
using Content.Client._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.PlanetScan;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.PlanetScan;

/// <summary>
/// Draws a star system's bodies on a radar plot as the sector shows them, through each body's own planet shader, at
/// their scope radius. One per plot: the shaders' viewport is set per draw.
/// </summary>
public sealed class WFPlanetScopeSprites
{
    /// <summary>How far past the core the drawn square reaches, in core radii, so the atmosphere's glow fits.</summary>
    private const float AtmosphereReach = 1.35f;

    /// <summary>The same past a ring's outer edge.</summary>
    private const float RingMargin = 0.15f;

    private static readonly Color Fallback = Color.Gray.WithAlpha(0.5f);

    private readonly PlanetOverlay _builder;
    private readonly WFPlanetScanReader _reader;
    private readonly Dictionary<Planet, ShaderInstance?> _shaders = new();
    private readonly Dictionary<Planet, float> _radii = new();
    private PlanetarySystem? _system;

    public WFPlanetScopeSprites(IEntityManager entities, IPrototypeManager prototypes)
    {
        _builder = new PlanetOverlay(entities, prototypes);
        _reader = new WFPlanetScanReader(entities, prototypes);
    }

    /// <summary>
    /// Draws every body of <paramref name="system"/> on <paramref name="map"/>, at least <paramref name="minRadius"/>
    /// pixels across, skipping those wholly outside <paramref name="bounds"/>.
    /// </summary>
    public void Draw(DrawingHandleScreen handle, MapId map, PlanetarySystem system, Matrix3x2 worldToView, float minRadius,
        UIBox2 bounds)
    {
        if (!ReferenceEquals(_system, system))
        {
            _system = system;
            _shaders.Clear();
        }

        _radii.Clear();
        foreach (var (_, planet, radius) in _reader.Bodies(map))
        {
            _radii[planet] = radius;
        }

        var pixelsPerMetre = new Vector2(worldToView.M11, worldToView.M12).Length();
        var star = Vector2.Transform(system.Star.Position, worldToView);
        foreach (var planet in system.Planets)
        {
            var metres = _radii.TryGetValue(planet, out var known) ? known : WFPlanetScanReader.ScopeRadius(planet, null);
            var radius = MathF.Max(metres * pixelsPerMetre, minRadius);
            var centre = Vector2.Transform(planet.Position, worldToView);
            DrawBody(handle, system, planet, centre, radius, star, bounds);
        }

        handle.UseShader(null);
    }

    private void DrawBody(DrawingHandleScreen handle, PlanetarySystem system, Planet planet, Vector2 centre, float radius,
        Vector2 star, UIBox2 bounds)
    {
        var reach = planet.Rings is { } rings ? MathF.Max(AtmosphereReach, rings.RadiusOuter + RingMargin) : AtmosphereReach;
        var half = radius * reach;
        var box = UIBox2.FromDimensions(centre - new Vector2(half), new Vector2(half * 2f));
        if (!box.Intersects(bounds))
            return;

        if (!_shaders.TryGetValue(planet, out var shader))
        {
            shader = _builder.SetupPlanetShader(planet, system.Star);
            _shaders[planet] = shader;
        }

        if (shader == null)
        {
            handle.DrawCircle(centre, radius, Fallback);
            return;
        }

        // The shader places the body in its own units: a core of radius one, framed by the drawn square, with no parallax.
        shader.SetParameter("planetaryRadiusFactor", 1f);
        shader.SetParameter("planetRadius", 1f);
        shader.SetParameter("parallaxCenter", planet.Position);
        shader.SetParameter("viewportMin", planet.Position - new Vector2(reach));
        shader.SetParameter("viewportSize", new Vector2(reach * 2f));

        // Lit from where the star lies on this plot, which turns with the ship; the shader's y runs up the screen.
        var toStar = star - centre;
        var light = toStar.LengthSquared() > 0.01f ? Vector2.Normalize(new Vector2(toStar.X, -toStar.Y)) : Vector2.UnitX;
        shader.SetParameter("starPos", planet.Position + light * 1000f);

        handle.UseShader(shader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(null);
    }
}
