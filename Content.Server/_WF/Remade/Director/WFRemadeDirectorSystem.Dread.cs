using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Dread;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server.Ghost;
using Content.Shared._WF.Remade;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Psychic tricks, from DS13's Marker abilities, played by the Director on one crewman at a time and paid for out of its
/// Will: flicker, whisper, false sounds, scrawls, runes, drag marks, ritual circles, meddling with doors, blowouts and a
/// biohazard lockdown. How often they come grows with the stage, and they stop while the crewman is shaken, until Rapture, when
/// nothing lets up. Whispers, glimpses and a warped screen are the dread system's, for every player the stone reaches.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private WFDreadAlarmSystem _alarm = default!;
    [Dependency] private WFDreadSystem _dread = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPoweredLightSystem _lights = default!;
    [Dependency] private TurfSystem _turf = default!;

    private const int ScrawlLines = 30;
    private const float DreadRange = 9f;
    private const float BlowoutRange = 12f;
    private const float MeddleRange = 10f;
    public const float LockdownRange = 9f;
    private const float ScrawlSpacing = 2f;
    private const float LockdownSeconds = 40f;
    private static readonly SoundSpecifier DefaultAlarm = new SoundPathSpecifier("/Audio/_WF/Remade/monolith/alarm.ogg");
    private const float LockdownCooldown = 300f;
    private const string ScrawlProto = "WFRemadeScrawl";
    private const string RuneProto = "WFRemadeRune";
    private const string SmearProto = "WFRemadeSmear";
    private const string SplatterProto = "WFRemadeSplatter";
    private const int CircleRunes = 6;
    private const string SparksProto = "EffectSparks";

    private enum Trick : byte
    {
        Flicker,
        Whisper,
        FalseSound,
        Scrawl,
        Rune,
        Trail,
        Circle,
        Meddle,
        Blowout,
        Lockdown,
        Knock,
        Crawl,
        Camera,
    }

    private readonly record struct TrickInfo(Trick Trick, WFMonolithStage Min, float Cost, float Weight);

    private static readonly TrickInfo[] Tricks =
    {
        new(Trick.Flicker, WFMonolithStage.Quiet, 0f, 4f),
        new(Trick.Whisper, WFMonolithStage.Hymn, 0f, 6f),
        new(Trick.FalseSound, WFMonolithStage.Hymn, 5f, 3f),
        new(Trick.Scrawl, WFMonolithStage.Hymn, 8f, 3f),
        new(Trick.Rune, WFMonolithStage.Growth, 8f, 3f),
        new(Trick.Trail, WFMonolithStage.Growth, 12f, 1.5f),
        new(Trick.Circle, WFMonolithStage.Harvest, 25f, 1f),
        new(Trick.Meddle, WFMonolithStage.Growth, 10f, 2f),
        new(Trick.Blowout, WFMonolithStage.Growth, 30f, 1.5f),
        new(Trick.Lockdown, WFMonolithStage.Harvest, 120f, 0.6f),
        new(Trick.Knock, WFMonolithStage.Growth, 6f, 2.5f),
        new(Trick.Crawl, WFMonolithStage.Growth, 8f, 2f),
        new(Trick.Camera, WFMonolithStage.Growth, 8f, 1.5f),
    };

    private readonly HashSet<Entity<PoweredLightComponent>> _lightsNear = new();
    private readonly Dictionary<WFMonolithStage, List<SoundSpecifier>> _voices = new();

    private void InitializeDread()
    {
        SubscribeLocalEvent<WFRemadeScrawlComponent, ExaminedEvent>(OnScrawlExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _voices.Clear());
    }

    private void OnScrawlExamined(Entity<WFRemadeScrawlComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-remade-scrawl-read", ("text", Loc.GetString(ent.Comp.Text))));
    }

    private static float WillIncome(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Hymn => 0.5f,
            WFMonolithStage.Growth => 1f,
            WFMonolithStage.Harvest => 1.5f,
            WFMonolithStage.Rapture => 2.5f,
            WFMonolithStage.Crescendo => 4f,
            _ => 0.2f,
        };
    }

    private TimeSpan DreadDelay(WFMonolithStage stage)
    {
        var (min, max) = stage switch
        {
            WFMonolithStage.Quiet => (60f, 150f),
            WFMonolithStage.Hymn => (30f, 70f),
            WFMonolithStage.Growth => (20f, 40f),
            WFMonolithStage.Harvest => (14f, 28f),
            WFMonolithStage.Rapture => (8f, 18f),
            _ => (5f, 10f),
        };
        return TimeSpan.FromSeconds(_random.NextFloat(min, max));
    }

    /// <summary>The crewman the Director is working on. It keeps to them for a minute or two, favouring the ones on their own.</summary>
    private EntityUid? PickFocus(WFRemadeDirectorComponent director, TimeSpan now)
    {
        if (director.Focus is { } current && now < director.FocusUntil && _crew.Contains(current))
            return current;

        if (_crew.Count == 0)
            return null;

        var weights = _crew.Select(crewman => IsAlone(crewman) ? 2f : 1f).ToList();
        var roll = _random.NextFloat(0f, weights.Sum());
        var focus = _crew[^1];
        for (var i = 0; i < _crew.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f)
            {
                focus = _crew[i];
                break;
            }
        }

        director.Focus = focus;
        director.FocusUntil = now + TimeSpan.FromSeconds(_random.NextFloat(60f, 120f));
        return focus;
    }

    /// <summary>Works on the focus's nerves, if they aren't already frayed. A shaken crewman is left alone.</summary>
    private void Dread(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        director.NextDread = now + TimeSpan.FromSeconds(5);

        if (PickFocus(director, now) is not { } focus)
            return;

        // A shaken crewman gets a rest, until Rapture, when the stone gives none.
        var stress = StressOf(focus);
        if (stress >= director.StressHigh && stage < WFMonolithStage.Rapture)
            return;

        var options = Tricks
            .Where(info => stage >= info.Min && director.Will >= info.Cost
                && (info.Trick != Trick.Lockdown || now >= director.NextLockdown))
            .ToList();
        if (options.Count > 0)
        {
            var roll = _random.NextFloat(0f, options.Sum(info => info.Weight));
            var pick = options[^1];
            foreach (var info in options)
            {
                roll -= info.Weight;
                if (roll <= 0f)
                {
                    pick = info;
                    break;
                }
            }

            if (DoTrick(ent, focus, pick.Trick, now))
                director.Will -= pick.Cost;
            else
                FlickerNear(focus);
        }

        var delay = DreadDelay(stage);
        director.NextDread = now + (stress < director.StressCalm ? delay * 0.6 : delay);
    }

    private bool DoTrick(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid focus, Trick trick, TimeSpan now)
    {
        var director = ent.Comp1;
        switch (trick)
        {
            case Trick.Flicker:
                FlickerNear(focus);
                return true;
            case Trick.Whisper:
                _dread.Whisper(focus, ent.Comp2.Stage);
                _dread.Murmur(focus, ent.Comp2.Stage);
                return true;
            case Trick.FalseSound:
                var offset = _random.NextAngle().ToVec() * _random.NextFloat(6f, 10f);
                var stage = ent.Comp2.Stage;
                _audio.PlayStatic(_random.Prob(WhisperShare(stage)) ? _dread.MurmurSound() : FalseSoundFor(director, stage),
                    focus, Transform(focus).Coordinates.Offset(offset), AudioParams.Default.WithVolume(-4f));
                return true;
            case Trick.Scrawl:
                return Scrawl(focus);
            case Trick.Rune:
                return Rune(focus);
            case Trick.Trail:
                return Trail(focus);
            case Trick.Circle:
                return Circle(focus);
            case Trick.Meddle:
                return Meddle(focus);
            case Trick.Blowout:
                return Blowout(focus, ent.Comp2.Stage switch
                {
                    WFMonolithStage.Crescendo => 5,
                    WFMonolithStage.Rapture => 3,
                    WFMonolithStage.Harvest => 2,
                    _ => 1,
                });
            case Trick.Lockdown:
                director.NextLockdown = now + TimeSpan.FromSeconds(LockdownCooldown);
                return Lockdown((ent.Owner, director), focus, LockdownSeconds) > 0;
            case Trick.Knock:
                return Knock(ent, focus, now);
            case Trick.Crawl:
                return Crawl(focus, now);
            case Trick.Camera:
                return CameraLie(ent);
        }

        return false;
    }

    private void FlickerNear(EntityUid crewman)
    {
        _lightsNear.Clear();
        _lookup.GetEntitiesInRange(Transform(crewman).Coordinates, DreadRange, _lightsNear);
        foreach (var light in _lightsNear)
        {
            _ghost.DoGhostBooEvent(light);
        }
    }

    private void BlowLightsNear(EntityUid crewman, int count)
    {
        _lightsNear.Clear();
        _lookup.GetEntitiesInRange(Transform(crewman).Coordinates, DreadRange, _lightsNear);
        foreach (var light in _lightsNear.OrderBy(_ => _random.Next()).Take(count))
        {
            _lights.TryDestroyBulb(light, light.Comp);
        }
    }

    /// <summary>How often a false sound is a whisper and not a Remade's voice, by stage: mostly whispers while the stone is young.</summary>
    private static float WhisperShare(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Quiet or WFMonolithStage.Hymn => 0.7f,
            WFMonolithStage.Growth => 0.5f,
            WFMonolithStage.Harvest => 0.35f,
            WFMonolithStage.Rapture => 0.25f,
            _ => 0.2f,
        };
    }

    /// <summary>Every voice a false sound can play at a stage. Tests read it.</summary>
    public IReadOnlyList<SoundSpecifier> FalseSoundPool(WFMonolithStage stage)
    {
        return VoicePool(stage);
    }

    /// <summary>A false sound: any voice a Remade could have at this stage, as if one were near.</summary>
    private SoundSpecifier FalseSoundFor(WFRemadeDirectorComponent director, WFMonolithStage stage)
    {
        var pool = VoicePool(stage);
        return pool.Count == 0 ? director.FalseSound : _random.Pick(pool);
    }

    private List<SoundSpecifier> VoicePool(WFMonolithStage stage)
    {
        if (!_voices.TryGetValue(stage, out var pool))
        {
            pool = new List<SoundSpecifier>();
            foreach (var mob in _proto.EnumeratePrototypes<WFRemadeMobPrototype>())
            {
                if (mob.MinStage > stage
                    || !_proto.TryIndex(mob.Entity, out var entity)
                    || !entity.TryGetComponent<WFRemadeVoiceComponent>(out var voice, _factory))
                {
                    continue;
                }

                if (voice.Speech != null)
                    pool.Add(voice.Speech);
                if (voice.Shout != null)
                    pool.Add(voice.Shout);
                if (voice.Pain != null)
                    pool.Add(voice.Pain);
            }

            _voices[stage] = pool;
        }

        return pool;
    }

    /// <summary>A clear floor tile a little way from a crewman, to put something on.</summary>
    private EntityCoordinates? FloorSpot(EntityUid crewman, float min, float max)
    {
        var xform = Transform(crewman);
        if (xform.GridUid is not { } grid || xform.ParentUid != grid)
            return null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var coordinates = xform.Coordinates.Offset(_random.NextAngle().ToVec() * _random.NextFloat(min, max));
            if (ClearFloor(coordinates, grid) is { } spot)
                return spot;
        }

        return null;
    }

    /// <summary>The middle of the clear floor tile at a place on a grid, if there is one.</summary>
    private EntityCoordinates? ClearFloor(EntityCoordinates coordinates, EntityUid grid)
    {
        if (!_turf.TryGetTileRef(coordinates, out var tile)
            || _turf.IsSpace(tile.Value)
            || _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
        {
            return null;
        }

        return new EntityCoordinates(grid, new Vector2(tile.Value.GridIndices.X + 0.5f, tile.Value.GridIndices.Y + 0.5f));
    }

    /// <summary>The highest scrawl the stone nearest a crewman has the nerve for.</summary>
    private int ScrawlCap(EntityUid focus)
    {
        var stage = NearestMonolith(focus) is { } monolith && TryComp<WFMonolithComponent>(monolith, out var comp)
            ? comp.Stage
            : WFMonolithStage.Hymn;
        return stage switch
        {
            WFMonolithStage.Growth => 16,
            WFMonolithStage.Harvest => 24,
            WFMonolithStage.Rapture or WFMonolithStage.Crescendo => ScrawlLines,
            _ => 10,
        };
    }

    public bool Scrawl(EntityUid focus)
    {
        if (FloorSpot(focus, 3f, 9f) is not { } spot)
            return false;

        var near = new HashSet<Entity<WFRemadeScrawlComponent>>();
        _lookup.GetEntitiesInRange(spot, ScrawlSpacing, near);
        if (near.Count > 0)
            return false;

        var scrawl = Spawn(ScrawlProto, spot);
        EnsureComp<WFRemadeScrawlComponent>(scrawl).Text = $"wf-remade-scrawl-{_random.Next(1, ScrawlCap(focus) + 1)}";
        return true;
    }

    /// <summary>A trail of drag marks across the floor, ending in a splash of blood.</summary>
    public bool Trail(EntityUid focus)
    {
        if (FloorSpot(focus, 3f, 8f) is not { } start || Transform(focus).GridUid is not { } grid)
            return false;

        var direction = _random.NextAngle().ToVec();
        var angle = direction.ToWorldAngle();
        var length = _random.Next(4, 8);
        var placed = 0;
        for (var i = 0; i < length; i++)
        {
            if (ClearFloor(start.Offset(direction * i), grid) is not { } at)
                break;

            _transform.SetLocalRotation(Spawn(SmearProto, at), angle);
            placed++;
        }

        if (placed == 0)
            return false;

        if (ClearFloor(start.Offset(direction * placed), grid) is { } end)
            Spawn(SplatterProto, end);

        return true;
    }

    /// <summary>A ring of runes with blood in the middle.</summary>
    public bool Circle(EntityUid focus)
    {
        if (FloorSpot(focus, 4f, 9f) is not { } centre || Transform(focus).GridUid is not { } grid)
            return false;

        var placed = 0;
        for (var i = 0; i < CircleRunes; i++)
        {
            var at = centre.Offset(new Angle(MathF.Tau * i / CircleRunes).ToVec() * 1.5f);
            if (ClearFloor(at, grid) is not { } spot)
                continue;

            Spawn(RuneProto, spot);
            placed++;
        }

        if (placed < 3)
            return false;

        Spawn(SplatterProto, centre);
        return true;
    }

    public bool Rune(EntityUid focus)
    {
        if (FloorSpot(focus, 3f, 9f) is not { } spot)
            return false;

        Spawn(RuneProto, spot);
        return true;
    }

    /// <summary>Opens a shut door or shuts an open one near the crewman, with nobody touching it.</summary>
    public bool Meddle(EntityUid focus)
    {
        var doors = new HashSet<Entity<DoorComponent>>();
        _lookup.GetEntitiesInRange(Transform(focus).Coordinates, MeddleRange, doors);
        foreach (var door in doors.OrderBy(_ => _random.Next()))
        {
            if (HasComp<WFRemadeLockdownDoorComponent>(door) || TryComp<DoorBoltComponent>(door, out var bolt) && bolt.BoltsDown)
                continue;

            if (door.Comp.State == DoorState.Open && _doors.TryClose(door, door.Comp)
                || door.Comp.State == DoorState.Closed && _doors.TryOpen(door, door.Comp))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Lights burst in showers of sparks: one, or as many as <paramref name="count"/> if there are that many near.</summary>
    public bool Blowout(EntityUid focus, int count = 1)
    {
        _lightsNear.Clear();
        _lookup.GetEntitiesInRange(Transform(focus).Coordinates, BlowoutRange, _lightsNear);
        var blown = 0;
        foreach (var light in _lightsNear.OrderBy(_ => _random.Next()))
        {
            if (!_lights.TryDestroyBulb(light, light.Comp))
                continue;

            var at = Transform(light).Coordinates;
            Spawn(SparksProto, at);
            _audio.PlayPvs(new SoundCollectionSpecifier("sparks"), at);
            if (++blown >= count)
                break;
        }

        return blown > 0;
    }

    /// <summary>
    /// Bolts the powered doors round a crewman for a while, with the alarm and a message for whoever is near. The doors let
    /// go by themselves. Returns how many it bolted.
    /// </summary>
    public int Lockdown(Entity<WFRemadeDirectorComponent> director, EntityUid around, float seconds)
    {
        return Lockdown(around, seconds, LockdownRange, director.Comp.AlarmSound);
    }

    /// <summary>A lockdown with no stone behind it, as an admin calls one. Returns how many doors it bolted.</summary>
    public int Lockdown(EntityUid around, float seconds, float range, SoundSpecifier? alarm = null)
    {
        var now = _timing.CurTime;
        var bolted = 0;
        var doors = new HashSet<Entity<DoorBoltComponent>>();
        _lookup.GetEntitiesInRange(Transform(around).Coordinates, range, doors);
        foreach (var door in doors)
        {
            if (door.Comp.BoltsDown)
                continue;

            if (TryComp<DoorComponent>(door, out var component) && component.State == DoorState.Open)
                _doors.TryClose(door, component);

            if (!_doors.TrySetBoltDown(door, true))
                continue;

            EnsureComp<WFRemadeLockdownDoorComponent>(door).ReleaseAt = now + TimeSpan.FromSeconds(seconds);
            bolted++;
        }

        if (bolted == 0)
            return 0;

        _alarm.Start(around, alarm ?? DefaultAlarm);
        var crew = new HashSet<Entity<ActorComponent>>();
        _lookup.GetEntitiesInRange(Transform(around).Coordinates, range + 4f, crew);
        foreach (var crewman in crew)
        {
            _popup.PopupEntity(Loc.GetString("wf-remade-lockdown"), crewman, crewman, PopupType.LargeCaution);
        }

        return bolted;
    }

    private TimeSpan _nextRelease;

    private void UpdateLockdowns(TimeSpan now)
    {
        if (now < _nextRelease)
            return;

        _nextRelease = now + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<WFRemadeLockdownDoorComponent, DoorBoltComponent>();
        List<(EntityUid, DoorBoltComponent)>? release = null;
        while (query.MoveNext(out var uid, out var lockdown, out var bolt))
        {
            if (now >= lockdown.ReleaseAt)
                (release ??= new()).Add((uid, bolt));
        }

        if (release == null)
            return;

        foreach (var (uid, bolt) in release)
        {
            // An unpowered door can't unbolt; keep trying until it can.
            if (!bolt.BoltsDown || _doors.TrySetBoltDown((uid, bolt), false))
                RemComp<WFRemadeLockdownDoorComponent>(uid);
        }
    }
}
