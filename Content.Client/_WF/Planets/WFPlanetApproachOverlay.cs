using System.Numerics;
using Content.Client._FarHorizons.StarSystem;
using Content.Client.Parallax;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Planets;

/// <summary>Replaces the FTL tunnel on an orbit hop with the sector's planet swelling to fill the sky, reversed on leaving.</summary>
public sealed class WFPlanetApproachOverlay : Overlay
{
    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly IGameTiming _timing;
    private readonly SharedStarSystemMapSystem _starSystems;
    private readonly PlanetOverlay _planets;

    /// <summary>The sector parallax factor the planet shaders run under.</summary>
    private const float SectorParallax = 0.1f;

    /// <summary>How many viewport diagonals the body's radius reaches at full approach, so its limb is well off-screen.</summary>
    private const float FullApproachDiagonals = 4f;

    private static readonly Color Sky = Color.FromHex("#020308");

    private ShaderInstance? _shader;
    private Planet? _planet;
    private (ProtoId<Content.Shared._FarHorizons.StarSystem.Prototypes.StarSystemPrototype> System, Vector2 Position)? _built;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public WFPlanetApproachOverlay(IEntityManager entMan, IPrototypeManager protoMan, IPlayerManager player, IGameTiming timing)
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _entMan = entMan;
        _player = player;
        _timing = timing;
        _starSystems = entMan.System<SharedStarSystemMapSystem>();
        _planets = new PlanetOverlay(entMan, protoMan);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Only the tunnel is replaced: the spool-up and the arrival are the sector and the orbit layer themselves.
        // No time window: the hull leaves the FTL map a few ticks after the end time, so the last frame holds until then.
        if (!_entMan.HasComponent<FTLMapComponent>(args.MapUid) || !TryGetApproach(out var approach))
            return false;

        return EnsureShader(approach);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_shader == null || _planet == null || !TryGetApproach(out var approach))
            return;

        var handle = args.WorldHandle;
        var bounds = args.WorldAABB;

        var length = (approach.End - approach.Start).TotalSeconds;
        var t = length <= 0 ? 1f : (float) Math.Clamp((_timing.CurTime - approach.Start).TotalSeconds / length, 0, 1);

        if (!approach.Arriving)
            t = 1f - t;

        // Constant closing speed reads as exponential growth, which is what an approach looks like.
        var full = MathF.Max(_planet.Radius, bounds.Size.Length() * FullApproachDiagonals);
        var radius = _planet.Radius * MathF.Pow(full / _planet.Radius, t);

        // Where the sector drew it, sliding to dead ahead as it closes.
        var offset = (approach.PlanetPosition - approach.ShipPosition) * SectorParallax * (1f - t) * (1f - t);

        handle.DrawRect(bounds, Sky);

        // The shader places the body in world space, and this is the FTL map: hand it a viewport in the sector's frame.
        _shader.SetParameter("planetRadius", radius);
        _shader.SetParameter("parallaxCenter", _planet.Position);
        _shader.SetParameter("viewportMin", _planet.Position - offset - bounds.Size / 2f);
        _shader.SetParameter("viewportSize", bounds.Size);

        handle.UseShader(_shader);
        handle.DrawRect(bounds, Color.White);
        handle.UseShader(null);
    }

    /// <summary>The approach riding the hull the local player stands on.</summary>
    private bool TryGetApproach(out WFPlanetApproachComponent approach)
    {
        approach = default!;

        if (_player.LocalEntity is not { } player
            || !_entMan.TryGetComponent<TransformComponent>(player, out var xform)
            || xform.GridUid is not { } grid
            || !_entMan.TryGetComponent<WFPlanetApproachComponent>(grid, out var comp))
        {
            return false;
        }

        approach = comp;
        return true;
    }

    /// <summary>Builds the sector's shader for the nearest planet, once per destination.</summary>
    private bool EnsureShader(WFPlanetApproachComponent approach)
    {
        if (_built == (approach.System, approach.PlanetPosition))
            return _shader != null;

        _built = (approach.System, approach.PlanetPosition);
        _shader = null;
        _planet = null;

        if (_starSystems.BuildPlanetarySystem(approach.System) is not { } system)
            return false;

        var best = float.MaxValue;

        foreach (var planet in system.Planets)
        {
            var distance = (planet.Position - approach.PlanetPosition).LengthSquared();

            if (distance >= best)
                continue;

            best = distance;
            _planet = planet;
        }

        if (_planet == null)
            return false;

        _shader = _planets.SetupPlanetShader(_planet, system.Star);
        return _shader != null;
    }
}
