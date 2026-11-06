using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Worldgen.Components;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Planets;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Lattice;

/// <summary>
/// Holds the Lattice field on every sector map, builds Drift on the ships and people out in it, and tells crews when
/// they cross into another band. A ship carries everyone aboard; a person off a ship carries themselves; anyone on a
/// planet takes the band of the world's place in the sector.
/// </summary>
public sealed partial class WFLatticeSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFChoirSystem _choir = default!;
    [Dependency] private WFDriftEffectsSystem _effects = default!;

    /// <summary>Seconds between field checks.</summary>
    public const float Interval = 1f;

    private const float ChipSeconds = 12f;

    private float _accumulator;
    private int _primaries;
    private readonly Dictionary<NetUserId, int> _seen = new();
    private readonly List<(Vector2 Position, float Strength)> _anchors = new();
    private readonly Dictionary<EntityUid, (Vector2 Position, List<(ICommonSession Session, EntityUid Mob)> Aboard)> _carriers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _seen.Clear();
            _ovals.Clear();
            _nextLostShip.Clear();
        });
        InitializeStations();
        InitializeDefence();
        InitializeFtl();
        InitializeRelayFuel();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateDefence(frameTime);
        HoldBarriers();

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;

        _accumulator -= Interval;
        UpdateRelays(Interval);
        MarkShields();

        var sectors = EntityQueryEnumerator<BiomeSelectionComponent>();
        while (sectors.MoveNext(out var uid, out _))
        {
            if (!HasComp<WFLatticeFieldComponent>(uid))
                Setup(uid);
        }

        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        while (fields.MoveNext(out var uid, out var field))
        {
            Tick((uid, field), Interval);
        }
    }

    /// <summary>Puts the Lattice on a sector map, spawning its primary anchors if it has none.</summary>
    public void Setup(EntityUid map, string profileId = "WFDefault")
    {
        if (!_proto.TryIndex<WFLatticeProfilePrototype>(profileId, out var profile))
            return;

        var field = EnsureComp<WFLatticeFieldComponent>(map);
        field.Profile = profileId;
        Dirty(map, field);

        var corners = WFLatticeMath.Corners(Vector2.Zero, profile.AnchorCount, profile.AnchorRadius, profile.AnchorAngle);
        GatherAnchors(map);
        if (_anchors.Count == 0)
        {
            for (var i = 0; i < corners.Count; i++)
            {
                if (profile.AnchorStations)
                    BuildStation(map, profile, corners[i], i);
                else
                    Spawn(profile.AnchorPrototype, new MapCoordinates(corners[i], Transform(map).MapID));
            }
        }

        // Thresholds come from the ideal polygon, so lost anchors pull edges in rather than resetting them.
        var ideal = new List<(Vector2, float)>();
        foreach (var corner in corners)
        {
            ideal.Add((corner, 1f));
        }

        var edges = new List<float>();
        foreach (var band in profile.Bands)
        {
            edges.Add(band.Edge);
        }

        // The drone rings mark where each tier of drones begins, so the bands follow them when they line up.
        if (profile.DroneRingEdges
            && TryComp<WFDroneRingsComponent>(map, out var rings)
            && rings.Radii.Count == edges.Count - 1)
        {
            for (var i = 0; i < rings.Radii.Count; i++)
            {
                edges[i + 1] = rings.Radii[i];
            }
        }

        field.Edges = edges;
        field.Thresholds = WFLatticeMath.Thresholds(edges, ideal, profile.Reach, Vector2.Zero);
        GatherAnchors(map);
        SyncAnchors((map, field));
    }

    /// <summary>The band index and depth into it at a point on a sector map; band 0 if it has no Lattice.</summary>
    public (int Band, float Depth) Locate(EntityUid map, Vector2 position)
    {
        if (!TryComp<WFLatticeFieldComponent>(map, out var field))
            return (0, 0f);

        var strength = FieldAt(map, position);
        var band = WFLatticeMath.Band(strength, field.Thresholds);
        return (band, WFLatticeMath.Depth(strength, band, field.Thresholds));
    }

    /// <summary>Garbles text by the depth at a point, if the band there garbles; unchanged otherwise.</summary>
    public string GarbleAt(string text, EntityUid map, Vector2 position, int seed, float scale = 1f)
    {
        if (!TryComp<WFLatticeFieldComponent>(map, out var field) || !_proto.TryIndex(field.Profile, out var profile))
            return text;

        var (band, depth) = Locate(map, position);
        if (band >= profile.Bands.Count || !profile.Bands[band].Garbled)
            return text;

        return WFLatticeMath.Garble(text, WFLatticeMath.GarbleStrength(depth) * scale, seed);
    }

    /// <summary>The band a point falls in, or null with no Lattice there.</summary>
    public WFLatticeBand? BandOf(MapCoordinates point, out WFLatticeProfilePrototype? profile)
    {
        profile = null;
        if (!_maps.TryGetMap(point.MapId, out var map)
            || !TryComp<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out profile)
            || profile.Bands.Count == 0)
            return null;

        return profile.Bands[Math.Min(Locate(map.Value, point.Position).Band, profile.Bands.Count - 1)];
    }

    /// <summary>Asteroid ore at a point, relative to anchored space.</summary>
    public float YieldAt(MapCoordinates point)
    {
        return BandOf(point, out _)?.Yield ?? 1f;
    }

    /// <summary>
    /// Drops the exotic matter a rock of this many veins holds, if the band where it broke has any: at the rock, or at
    /// <paramref name="dropAt"/> when the loot goes straight to a ship drill.
    /// </summary>
    public void SpawnExotic(MapCoordinates point, float veins, float radius, MapCoordinates? dropAt = null)
    {
        if (BandOf(point, out var profile) is not { Exotic: > 0f } band || profile == null)
            return;

        var expected = veins * band.Exotic;
        var count = (int) expected + (_random.Prob(expected - MathF.Floor(expected)) ? 1 : 0);
        for (var i = 0; i < count; i++)
        {
            var offset = _random.NextAngle().ToVec() * radius * 0.8f * MathF.Sqrt(_random.NextFloat());
            var at = dropAt ?? point;
            Spawn(profile.ExoticPrototype, new MapCoordinates(at.Position + offset, at.MapId));
        }
    }

    /// <summary>Adds the centre of every sector map with a Lattice.</summary>
    public void GetCentres(List<MapCoordinates> into)
    {
        var fields = EntityQueryEnumerator<WFLatticeFieldComponent, TransformComponent>();
        while (fields.MoveNext(out _, out _, out var xform))
        {
            into.Add(new MapCoordinates(Vector2.Zero, xform.MapID));
        }
    }

    /// <summary>Copies the gathered anchors onto the field for clients, if they changed.</summary>
    private void SyncAnchors(Entity<WFLatticeFieldComponent> map)
    {
        var same = map.Comp.Anchors.Count == _anchors.Count;
        for (var i = 0; same && i < _anchors.Count; i++)
        {
            same = Vector2.DistanceSquared(map.Comp.Anchors[i], _anchors[i].Position) < 1f
                   && MathHelper.CloseTo(map.Comp.Strengths[i], _anchors[i].Strength);
        }

        var integrity = _proto.TryIndex(map.Comp.Profile, out var profile) && profile.AnchorCount > 0
            ? Math.Clamp(_primaries / (float) profile.AnchorCount, 0f, 1f)
            : 1f;
        if (same && MathHelper.CloseTo(map.Comp.Integrity, integrity))
            return;

        map.Comp.Integrity = integrity;
        map.Comp.Anchors.Clear();
        map.Comp.Strengths.Clear();
        foreach (var (position, strength) in _anchors)
        {
            map.Comp.Anchors.Add(position);
            map.Comp.Strengths.Add(strength);
        }

        Dirty(map);
    }

    /// <summary>The field strength at a point on a sector map.</summary>
    public float FieldAt(EntityUid map, Vector2 position)
    {
        if (!TryComp<WFLatticeFieldComponent>(map, out var field) || !_proto.TryIndex(field.Profile, out var profile))
            return 0f;

        GatherAnchors(map);
        return WFLatticeMath.Field(position, _anchors, profile.Reach);
    }

    /// <summary>The band index at a point on a sector map, 0 if it has no Lattice.</summary>
    public int BandAt(EntityUid map, Vector2 position)
    {
        if (!TryComp<WFLatticeFieldComponent>(map, out var field))
            return 0;

        return WFLatticeMath.Band(FieldAt(map, position), field.Thresholds);
    }

    /// <summary>The band's name as shown, garbled with depth if the band is.</summary>
    public string BandName(WFLatticeBand band, float depth, int seed)
    {
        var name = Loc.GetString(band.Name);
        return band.Garbled ? WFLatticeMath.Garble(name, WFLatticeMath.GarbleStrength(depth), seed) : name;
    }

    private void Tick(Entity<WFLatticeFieldComponent> map, float seconds)
    {
        if (!_proto.TryIndex(map.Comp.Profile, out var profile) || profile.Bands.Count == 0)
            return;

        GatherAnchors(map);
        SyncAnchors(map);
        RollLostShip(map, profile);
        _carriers.Clear();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob)
                || !TryGetCarrier(mob, map, out var carrier, out var position))
                continue;

            if (!_carriers.TryGetValue(carrier, out var entry))
            {
                entry = (position, new List<(ICommonSession, EntityUid)>());
                _carriers[carrier] = entry;
            }

            entry.Aboard.Add((session, mob));
        }

        foreach (var (carrier, (position, aboard)) in _carriers)
        {
            var strength = WFLatticeMath.Field(position, _anchors, profile.Reach);
            var index = Math.Min(WFLatticeMath.Band(strength, map.Comp.Thresholds), profile.Bands.Count - 1);
            var depth = WFLatticeMath.Depth(strength, index, map.Comp.Thresholds);
            var band = profile.Bands[index];

            var drift = EnsureComp<WFDriftComponent>(carrier);
            var rate = WFLatticeMath.DriftRate(band, depth);
            if (rate > 0f && TryComp<WFLatticeShelterComponent>(carrier, out var shelter))
                rate *= 1f - Math.Clamp(shelter.Shelter, 0f, 1f);

            drift.Drift = Math.Clamp(drift.Drift + rate * seconds, 0f, profile.DriftMax);
            var level = (int) (drift.Drift / WFDriftComponent.LevelStep);
            if (drift.Band != index || drift.Level != level || MathF.Abs(drift.Depth - depth) >= 0.05f || drift.NoFtl == band.Ftl)
            {
                drift.Band = index;
                drift.NoFtl = !band.Ftl;
                drift.Level = level;
                drift.Depth = depth;
                Dirty(carrier, drift);
            }

            // Ships only: not a person in open space, nor a planet's ground.
            if (carrier != aboard[0].Mob && HasComp<MapGridComponent>(carrier) && !HasComp<MapComponent>(carrier))
                _effects.RollTake(carrier, index, depth, profile, seconds * TakeScale(profile));

            foreach (var (session, mob) in aboard)
            {
                var seen = _seen.TryGetValue(session.UserId, out var last);
                if (seen ? last == index : index == 0)
                {
                    _seen[session.UserId] = index;
                    continue;
                }

                _seen[session.UserId] = index;
                Notify(session, mob, band, depth);
            }

            foreach (var (session, mob) in aboard)
            {
                var own = drift.Drift;
                // A person's own charm shelters them on a ship that has none.
                if (carrier != mob && TryComp<WFLatticeShelterComponent>(mob, out var charm))
                    own *= 1f - Math.Clamp(charm.Shelter, 0f, 1f);

                // What they carry in their own head counts as much as where they are.
                if (carrier != mob && TryComp<WFDriftComponent>(mob, out var head))
                    own = Math.Max(own, head.Mind);

                // The Choir are at home out here: nothing they see is wrong to them.
                if (HasComp<WFChoirMemberComponent>(mob))
                    continue;

                _effects.Roll(session, mob, own, profile);
                _choir.Consider(session, mob, own);
            }
        }
    }

    /// <summary>
    /// What carries a person's Drift and where it sits on the sector map: the grid under them, or themselves in open
    /// space; on a planet, their ground or grid at the world's place.
    /// </summary>
    private bool TryGetCarrier(EntityUid mob, EntityUid sector, out EntityUid carrier, out Vector2 position)
    {
        var xform = Transform(mob);
        carrier = xform.GridUid ?? mob;
        position = default;
        if (xform.MapUid is not { } map)
            return false;

        if (map == sector)
        {
            position = _transform.GetWorldPosition(carrier);
            return true;
        }

        if (!TryGetPlanetBody(map, out var body) || Transform(body).MapUid != sector)
            return false;

        position = _transform.GetWorldPosition(body);
        return true;
    }

    /// <summary>The planet body on the sector map that a planet layer or orbit belongs to.</summary>
    private bool TryGetPlanetBody(EntityUid map, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (TryComp<WFOrbitLayerComponent>(map, out var orbit)
            && orbit.Planet is { } orbitPlanet
            && TryGetEntity(orbitPlanet, out var orbitBody))
        {
            body = orbitBody.Value;
            return !TerminatingOrDeleted(body);
        }

        if (!TryComp<WFPlanetLayerComponent>(map, out var layer)
            || layer.Network is not { } netNetwork
            || !TryGetEntity(netNetwork, out var network)
            || !TryComp<WFPlanetNetworkComponent>(network, out var networkComp)
            || networkComp.Planet is not { } planet)
            return false;

        body = planet;
        return !TerminatingOrDeleted(body);
    }

    private void GatherAnchors(EntityUid map)
    {
        _anchors.Clear();
        var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        _primaries = 0;
        while (query.MoveNext(out _, out var anchor, out var xform))
        {
            if (xform.MapUid != map || !anchor.Active)
                continue;

            _anchors.Add((_transform.GetWorldPosition(xform), anchor.Strength));
            if (anchor.Primary)
                _primaries++;
        }
    }

    private void Notify(ICommonSession session, EntityUid mob, WFLatticeBand band, float depth)
    {
        var seed = session.UserId.UserId.GetHashCode();
        // Nothing out here reads wrong to the Choir.
        var choir = HasComp<WFChoirMemberComponent>(mob);
        var name = choir ? Loc.GetString(band.Name) : BandName(band, depth, seed);
        var text = Loc.GetString(band.Message, ("band", name));
        if (band.Garbled && !choir)
            text = WFLatticeMath.Garble(text, 0.8f * depth, seed + 1);

        _popup.PopupEntity(text, mob, session, PopupType.LargeCaution);
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = band.Chip,
            Accent = band.Color,
            Title = name,
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = ChipSeconds,
            Flash = band.Flash,
        }, session);
    }
}

/// <summary>Raised once when a primary anchor is lost and the sector is told.</summary>
[ByRefEvent]
public readonly record struct WFLatticeBreachEvent(EntityUid Anchor);
