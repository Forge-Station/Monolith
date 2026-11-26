using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Defcon;
using Content.Server._WF.ShipShields;
using Content.Server.Chat.Systems;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.Announcements;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The primary anchors as small stations: a deck, the anchor core at its centre and a fortress shield. Drones still loyal
/// to the Lattice come out when one is attacked, and the loss of one is a breach announced to the whole sector. Relay
/// anchors that players build take hold once powered and parked.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;

    /// <summary>Furthest a station is pushed out along its bearing to find clear space, in metres.</summary>
    private const float StationNudge = 600f;

    private const float StationNudgeStep = 50f;

    /// <summary>Clear space a loaded station needs round its point, in metres.</summary>
    private const float TemplateClearance = 48f;

    private static readonly SoundSpecifier BreachSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>The alarm a breach sounds across the sector.</summary>
    private static readonly SoundSpecifier BreachAlarm = new SoundPathSpecifier("/Audio/Misc/delta_alt.ogg");

    /// <summary>IFF of a lost anchor's station: dead grey.</summary>
    private static readonly Color DeadAnchorColor = Color.FromHex("#6e6e6e");

    private List<Entity<MapGridComponent>> _stationGrids = new();

    private void InitializeStations()
    {
        SubscribeLocalEvent<WFLatticeAnchorComponent, DestructionEventArgs>(OnAnchorDestroyed);
        SubscribeLocalEvent<WFLatticeAnchorComponent, EntityTerminatingEvent>(OnAnchorTerminating);
        SubscribeLocalEvent<WFLatticeAnchorComponent, ComponentShutdown>(OnAnchorShutdown);
        SubscribeLocalEvent<WFLatticeAnchorComponent, AnchorStateChangedEvent>(OnAnchorUnbolted);
        SubscribeLocalEvent<WFLatticeAnchorComponent, DamageChangedEvent>(OnAnchorDamaged);
        SubscribeLocalEvent<WFLatticeAnchorShieldComponent, MapInitEvent>(OnShieldMapInit);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldAttacked);
        SubscribeLocalEvent<WFDefconGatherEvent>(OnDefconGather);
    }

    /// <summary>Each lost primary anchor raises the sector's DEFCON score.</summary>
    private void OnDefconGather(WFDefconGatherEvent ev)
    {
        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        while (fields.MoveNext(out _, out var field))
        {
            if (!_proto.TryIndex(field.Profile, out var profile))
                continue;

            var lost = (int) MathF.Round((1f - field.Integrity) * profile.AnchorCount);
            if (lost <= 0)
                continue;

            ev.Add(lost * profile.BreachDefcon, Loc.GetString("wf-lattice-defcon-reason", ("count", lost)));
            // Each lost anchor also holds DEFCON at least a step worse: one lost is 4, four or more is 1.
            ev.Floor = Math.Min(ev.Floor, Math.Max(1, 5 - lost));
        }
    }

    /// <summary>
    /// Builds one primary anchor's station at a corner of the hexagon, pushed outward clear of other grids: loaded from
    /// the profile's grid file with its core swapped for this anchor's own, or a plain deck built here if that fails.
    /// </summary>
    private void BuildStation(EntityUid map, WFLatticeProfilePrototype profile, Vector2 corner, int index)
    {
        var mapId = Transform(map).MapID;
        var bearing = corner.LengthSquared() > 0f ? Vector2.Normalize(corner) : Vector2.UnitX;
        var size = profile.StationMap != null ? TemplateClearance : profile.StationRadius * 2f + 4f;
        var position = corner;
        for (var push = 0f; push <= StationNudge; push += StationNudgeStep)
        {
            position = corner + bearing * push;
            _stationGrids.Clear();
            _maps.FindGridsIntersecting(mapId, Box2.CenteredAround(position, new Vector2(size)), ref _stationGrids, true, false);
            if (_stationGrids.Count == 0)
                break;
        }

        if (profile.StationMap is { } file && TryLoadStation(mapId, file, profile, position, index))
            return;

        var grid = _maps.CreateGridEntity(mapId);
        var tile = new Tile(_tiles[profile.StationTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        var radius = profile.StationRadius;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius + radius)
                    tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        _transform.SetWorldPosition(grid.Owner, position);
        _meta.SetEntityName(grid.Owner, Loc.GetString("wf-lattice-station-name", ("anchor", AnchorName(index))));
        EnsureComp<CleanupImmuneComponent>(grid.Owner);

        var core = Spawn(profile.CorePrototypes[index % profile.CorePrototypes.Count], new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
        _meta.SetEntityName(core, AnchorName(index));
        var anchor = EnsureComp<WFLatticeAnchorComponent>(core);
        anchor.Primary = true;

        var shield = Spawn(profile.ShieldPrototype, new EntityCoordinates(grid.Owner, new Vector2(0.5f, -2.5f)));
        if (TryComp<ApcPowerReceiverComponent>(shield, out var power))
            power.NeedsPower = false;

        var station = EnsureComp<WFLatticeAnchorStationComponent>(grid.Owner);
        station.Core = core;
    }

    /// <summary>Loads the station grid so its core sits on the point, and makes the core this anchor's own.</summary>
    private bool TryLoadStation(MapId mapId, string file, WFLatticeProfilePrototype profile, Vector2 position, int index)
    {
        if (!_mapLoader.TryLoadGrid(mapId, new ResPath(file), out var loaded))
            return false;

        var grid = loaded.Value.Owner;
        EntityUid? template = null;
        var cores = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (cores.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (xform.GridUid == grid && anchor.Primary)
                template = uid;
        }

        if (template is not { } old)
        {
            Log.Error($"Lattice station {file} has no primary anchor core.");
            Del(grid);
            return false;
        }

        var local = Transform(old).LocalPosition;
        _transform.SetWorldPosition(grid, position - local);
        _meta.SetEntityName(grid, Loc.GetString("wf-lattice-station-name", ("anchor", AnchorName(index))));
        EnsureComp<CleanupImmuneComponent>(grid);

        // The template's core stands in for whichever anchor this is.
        var coordinates = Transform(old).Coordinates;
        // Swapping the template's core out is no breach.
        Comp<WFLatticeAnchorComponent>(old).Primary = false;
        Del(old);
        var core = Spawn(profile.CorePrototypes[index % profile.CorePrototypes.Count], coordinates);
        _meta.SetEntityName(core, AnchorName(index));
        EnsureComp<WFLatticeAnchorComponent>(core).Primary = true;
        EnsureComp<WFLatticeAnchorStationComponent>(grid).Core = core;
        return true;
    }

    /// <summary>Alpha to Zeta, then numbers.</summary>
    private string AnchorName(int index)
    {
        return Loc.TryGetString($"wf-lattice-anchor-name-{index + 1}", out var name)
            ? name
            : Loc.GetString("wf-lattice-anchor-name-n", ("n", index + 1));
    }

    private void OnAnchorDestroyed(Entity<WFLatticeAnchorComponent> ent, ref DestructionEventArgs args)
    {
        Breach(ent);
    }

    /// <summary>
    /// A primary anchor deleted mid-round, alone or with its grid, is a breach too. This runs while it is still on its
    /// map: by the time its components shut down it has already left it.
    /// </summary>
    private void OnAnchorTerminating(Entity<WFLatticeAnchorComponent> ent, ref EntityTerminatingEvent args)
    {
        if (Transform(ent.Owner).MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        Breach(ent);
    }

    /// <summary>A primary core knocked loose, as when the deck under it is blown out, is lost too.</summary>
    private void OnAnchorUnbolted(Entity<WFLatticeAnchorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored || TerminatingOrDeleted(ent.Owner) || Transform(ent.Owner).MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        Breach(ent);
    }

    /// <summary>The anchor taken off a core that stays is a breach as well.</summary>
    private void OnAnchorShutdown(Entity<WFLatticeAnchorComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner) || Transform(ent.Owner).MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        Breach(ent);
    }

    /// <summary>
    /// The loss of a primary anchor, once: it stops holding at once, and the whole sector hears that reality is coming
    /// loose in its direction.
    /// </summary>
    private void Breach(Entity<WFLatticeAnchorComponent> ent)
    {
        if (!ent.Comp.Primary || !ent.Comp.Active)
            return;

        ent.Comp.Active = false;
        MarkDead(ent.Owner);
        var breach = new WFLatticeBreachEvent(ent.Owner);
        RaiseLocalEvent(ref breach);
        var bearing = _transform.GetWorldPosition(ent.Owner);
        var text = Loc.GetString("wf-lattice-breach", ("anchor", Name(ent.Owner)), ("direction", Compass(bearing)));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachAlarm, Color.FromHex("#d030a0"));
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = Color.FromHex("#d030a0"),
            Title = Loc.GetString("wf-lattice-breach-title"),
            Text = Loc.GetString("wf-lattice-breach-chip", ("anchor", Name(ent.Owner)), ("direction", Compass(bearing))),
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 30f,
            Flash = true,
        });
    }

    /// <summary>Renames a lost anchor's station and greys its IFF, so radar shows at a glance that it is dead.</summary>
    private void MarkDead(EntityUid core)
    {
        // Only an anchor's own station; a core spawned loose on some other grid leaves it alone.
        if (Transform(core).GridUid is not { } grid || TerminatingOrDeleted(grid) || !HasComp<WFLatticeAnchorStationComponent>(grid))
            return;

        _meta.SetEntityName(grid, Loc.GetString("wf-lattice-station-name-dead", ("anchor", Name(core))));
        _shuttle.SetIFFColor(grid, DeadAnchorColor);
        _shuttle.RemoveIFFFlag(grid, IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
    }

    /// <summary>An anchor's shield generator can be blown up, but not unbolted or taken apart.</summary>
    private void OnShieldMapInit(Entity<WFLatticeAnchorShieldComponent> ent, ref MapInitEvent args)
    {
        RemComp<AnchorableComponent>(ent);
        RemComp<MachineComponent>(ent);
    }

    /// <summary>A hit on the core counts like a hit on the shield.</summary>
    private void OnAnchorDamaged(Entity<WFLatticeAnchorComponent> ent, ref DamageChangedEvent args)
    {
        if (!ent.Comp.Primary || !args.DamageIncreased || Transform(ent.Owner).GridUid is not { } grid)
            return;

        var attacker = args.Origin is { } origin && TryComp<TransformComponent>(origin, out var from) ? from.GridUid : null;
        Strike(grid, attacker, 1f);
    }

    /// <summary>A hit on the shield counts towards the limit.</summary>
    private void OnShieldAttacked(ref WFShipShieldAttackedEvent args)
    {
        if (HasComp<WFLatticeAnchorStationComponent>(args.Grid))
            Strike(args.Grid, args.AttackerGrid, 1f);
    }

    /// <summary>Calls out the anchor's drones, between it and whoever attacked it, unless they came out lately.</summary>
    public void Defend(EntityUid station, EntityUid? attacker)
    {
        if (!TryComp<WFLatticeAnchorStationComponent>(station, out var comp)
            || _timing.CurTime < comp.NextDefence
            || Transform(station).MapUid is not { } map
            || !TryComp<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return;

        comp.NextDefence = _timing.CurTime + TimeSpan.FromSeconds(profile.DefenceCooldown);
        var from = _transform.GetWorldPosition(station);
        var towards = attacker is { } enemy && Exists(enemy)
            ? _transform.GetWorldPosition(enemy) - from
            : _random.NextAngle().ToVec();
        if (towards.LengthSquared() < 1f)
            towards = Vector2.UnitX;

        var point = from + Vector2.Normalize(towards) * profile.DefenceDistance;
        Spawn(profile.DefenderPrototype, new MapCoordinates(point, Transform(map).MapID));
    }

    /// <summary>Draws each anchor shield's field as a rooted Ward in the profile's tint; a field comes back new after a collapse.</summary>
    private void MarkShields()
    {
        var shields = EntityQueryEnumerator<WFLatticeAnchorShieldComponent, ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out _, out _, out var emitter, out var xform))
        {
            if (emitter.Shield is not { } field
                || HasComp<WFWardFieldComponent>(field)
                || xform.MapUid is not { } map
                || !TryComp<WFLatticeFieldComponent>(map, out var lattice)
                || !_proto.TryIndex(lattice.Profile, out var profile))
                continue;

            var ward = EnsureComp<WFWardFieldComponent>(field);
            ward.Stage = WFMonolithStage.Crescendo;
            ward.Rooted = true;
            ward.Tint = profile.ShieldTint;
            Dirty(field, ward);
        }
    }

    /// <summary>A relay holds the field only while anchored, powered and on a grid that has sat still for a while.</summary>
    private void UpdateRelays(float seconds)
    {
        var relays = EntityQueryEnumerator<WFLatticeRelayComponent, WFLatticeAnchorComponent, TransformComponent>();
        while (relays.MoveNext(out var uid, out var relay, out var anchor, out var xform))
        {
            var still = xform.Anchored
                        && xform.GridUid is { } grid
                        && (!TryComp<PhysicsComponent>(grid, out var body) || body.LinearVelocity.Length() <= relay.MaxSpeed);
            var powered = !TryComp<ApcPowerReceiverComponent>(uid, out var power) || power.Powered;
            relay.Settled = still ? relay.Settled + seconds : 0f;
            anchor.Strength = relay.Strength;
            var was = anchor.Active;
            anchor.Active = powered && relay.Settled >= relay.SettleSeconds && (!relay.NeedsFuel || relay.Fuel > 0f);
            if (anchor.Active && relay.NeedsFuel)
                relay.Fuel = MathF.Max(0f, relay.Fuel - seconds);

            if (was && relay.NeedsFuel && relay.Fuel <= 0f)
                _popup.PopupEntity(Loc.GetString("wf-lattice-relay-empty"), uid, PopupType.MediumCaution);
        }
    }

    /// <summary>Which way from the centre a point lies, as a compass word.</summary>
    private string Compass(Vector2 offset)
    {
        var degrees = MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI;
        // East is 0 and north 90; eight sectors of 45 degrees each, starting from east.
        var sector = (int) MathF.Round(((degrees % 360f) + 360f) % 360f / 45f) % 8;
        return Loc.GetString($"wf-lattice-compass-{sector}");
    }
}
