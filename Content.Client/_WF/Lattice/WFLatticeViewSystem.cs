using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The local player's view through the Lattice field: names and numbers garble in a garbling band, by the deeper of
/// where the viewer is and where the thing seen is, and flicker as they garble.
/// </summary>
public sealed partial class WFLatticeViewSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFLatticeBeamOverlay());
        _overlays.AddOverlay(new WFLatticeSkyOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFLatticeBeamOverlay>();
        _overlays.RemoveOverlay<WFLatticeSkyOverlay>();
    }

    /// <summary>Seconds between changes in how a garbled text reads.</summary>
    private const double FlickerSeconds = 0.6;

    private readonly List<(Vector2 Position, float Strength)> _anchors = new();

    /// <summary>Garbles text seen by the local player; unchanged in anchored space.</summary>
    /// <param name="map">The map the text belongs to.</param>
    /// <param name="seen">Where the thing named is, if it has a place.</param>
    /// <param name="seed">Keeps each text's garbling its own.</param>
    /// <param name="scale">Share of the usual garbling, for text that should hold up better.</param>
    public string Garble(string text, EntityUid? map, Vector2? seen, int seed, float scale = 1f)
    {
        if (map is not { } mapUid)
            return text;

        if (!TryComp<WFLatticeFieldComponent>(mapUid, out var field) || !_proto.TryIndex(field.Profile, out var profile))
        {
            // Off the sector map, on a planet, the viewer's own text follows the Drift the server sends for them.
            return _player.LocalEntity is { } local && Transform(local).MapUid == mapUid && !HasComp<WFChoirMemberComponent>(local)
                   && CarrierDepth(out _) is var carried and >= 0f
                ? Scramble(text, carried, seed, scale)
                : text;
        }

        // Nothing out here reads wrong to the Choir.
        if (_player.LocalEntity is { } member && HasComp<WFChoirMemberComponent>(member))
            return text;

        var depth = -1f;
        if (_player.LocalEntity is { } viewer && Transform(viewer).MapUid == mapUid)
            depth = Depth(field, profile, _transform.GetWorldPosition(viewer));

        if (seen is { } position)
            depth = MathF.Max(depth, Depth(field, profile, position));

        return depth < 0f ? text : Scramble(text, depth, seed, scale);
    }

    private string Scramble(string text, float depth, int seed, float scale)
    {
        var tick = (int) (_timing.RealTime.TotalSeconds / FlickerSeconds);
        return WFLatticeMath.Garble(text, WFLatticeMath.GarbleStrength(depth) * scale, seed * 7919 + tick);
    }

    /// <summary>
    /// Depth into a garbling band of the local player's carrier (their ship, or themselves) as the server sent it, or -1;
    /// also gives the sector's field. Used where the viewer isn't on the sector map.
    /// </summary>
    private float CarrierDepth(out WFLatticeFieldComponent? field)
    {
        field = null;
        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        if (!fields.MoveNext(out _, out var sector) || !_proto.TryIndex(sector.Profile, out var profile))
            return -1f;

        field = sector;
        if (_player.LocalEntity is not { } mob)
            return -1f;

        WFDriftComponent? drift = null;
        if (Transform(mob).GridUid is not { } grid || !TryComp(grid, out drift))
            TryComp(mob, out drift);

        if (drift == null || drift.Band >= profile.Bands.Count || !profile.Bands[drift.Band].Garbled)
            return -1f;

        return drift.Depth;
    }

    /// <summary>
    /// How wrong Kyphrus looks to the local player, 0 to 1: the share of primary anchors lost, or more out in a garbling
    /// band, where it always looks wrong.
    /// </summary>
    public float Unmooring()
    {
        if (_player.LocalEntity is not { } viewer || Transform(viewer).MapUid is not { } map)
            return 0f;

        if (!TryComp<WFLatticeFieldComponent>(map, out var field) || !_proto.TryIndex(field.Profile, out var profile))
        {
            // On a planet: the sector's integrity and the Drift the server sends.
            var carried = CarrierDepth(out var sector);
            if (sector == null)
                return 0f;

            var lostThere = 1f - sector.Integrity;
            return Math.Clamp(carried >= 0f ? MathF.Max(lostThere, 0.6f + 0.4f * carried) : lostThere, 0f, 1f);
        }

        var lost = 1f - field.Integrity;
        var depth = Depth(field, profile, _transform.GetWorldPosition(viewer));
        return Math.Clamp(depth >= 0f ? MathF.Max(lost, 0.6f + 0.4f * depth) : lost, 0f, 1f);
    }

    /// <summary>Whether the local player's ship, or they themselves, are out where the Coda's hazards reach.</summary>
    public bool InHazardBand()
    {
        if (_player.LocalEntity is not { } mob || HasComp<WFChoirMemberComponent>(mob))
            return false;

        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        if (!fields.MoveNext(out _, out var field) || !_proto.TryIndex(field.Profile, out var profile))
            return false;

        WFDriftComponent? drift = null;
        if (Transform(mob).GridUid is not { } grid || !TryComp(grid, out drift))
            TryComp(mob, out drift);

        return drift != null && drift.Band >= profile.HazardBand;
    }

    /// <summary>Garbles text about where the local player is, such as their own coordinates.</summary>
    public string GarbleForViewer(string text, int seed, float scale = 1f)
    {
        return _player.LocalEntity is { } viewer ? Garble(text, Transform(viewer).MapUid, null, seed, scale) : text;
    }

    /// <summary>Depth into the band at a point, or -1 where the band does not garble.</summary>
    private float Depth(WFLatticeFieldComponent field, WFLatticeProfilePrototype profile, Vector2 position)
    {
        _anchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var strength = WFLatticeMath.Field(position, _anchors, profile.Reach);
        var band = WFLatticeMath.Band(strength, field.Thresholds);
        if (band >= profile.Bands.Count || !profile.Bands[band].Garbled)
            return -1f;

        return WFLatticeMath.Depth(strength, band, field.Thresholds);
    }
}
