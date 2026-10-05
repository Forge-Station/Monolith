using Content.Server.Fluids.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._WF.Chimera;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Events;

namespace Content.Server._WF.Chimera;

/// <summary>Bursts flesh pustules into ticks on contact and handles planting them.</summary>
public sealed partial class WFFleshPustuleSystem : EntitySystem
{
    public const int TicksPerBurst = 3;
    public const int MaxTicksPerGround = 24;
    public const int MaxTrackedTicks = 96;
    // Sensor radius plus the approaching humanoid's own collision radius.
    public const float TriggerRange = 1.25f;
    public const float PlantRange = 2f;

    private static readonly EntProtoId TickPrototype = "WFMobFleshTick";
    private static readonly EntProtoId BiomassPrototype = "ChimeraFleshKudzu";
    private static readonly EntProtoId PustulePrototype = "WFFleshPustule";
    private static readonly ProtoId<TagPrototype> ChimeraTag = "Chimera";
    private static readonly ProtoId<TagPrototype> ChimeraFloorTag = "ChimeraFloor";
    private static readonly SoundSpecifier BurstSound = new SoundCollectionSpecifier("WFFleshPustuleBurst");

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, EntityUid> _ticks = new();
    private readonly List<EntityUid> _staleTicks = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WFFleshPustuleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFFleshPustuleComponent, StartCollideEvent>(OnContact);
        SubscribeLocalEvent<WFFleshPustuleSpawnedTickComponent, EntityTerminatingEvent>(OnTickTerminating);
        SubscribeLocalEvent<WFPlantFleshPustuleActionEvent>(OnPlantAction);
    }

    private void OnMapInit(Entity<WFFleshPustuleComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.Bursted)
            return;

        _appearance.SetData(ent, WFFleshPustuleVisuals.Bursted, true);
    }

    // Event-driven: only a body actually touching this nest's sensor can trigger the check.
    private void OnContact(Entity<WFFleshPustuleComponent> ent, ref StartCollideEvent args)
    {
        if (!ent.Comp.Bursted && args.OtherFixture.Hard && IsValidTrigger(ent, args.OtherEntity))
        {
            TryBurst(ent);
        }
    }

    private void OnTickTerminating(Entity<WFFleshPustuleSpawnedTickComponent> ent, ref EntityTerminatingEvent args)
    {
        _ticks.Remove(ent);
    }

    private void OnPlantAction(WFPlantFleshPustuleActionEvent args)
    {
        if (!args.Handled && TryPlant(args.Performer, args.Target, args.Entity))
            args.Handled = true;
    }

    private bool IsValidTrigger(EntityUid pustule, EntityUid target)
    {
        if (!HasComp<ActorComponent>(target) ||
            !TryComp<MobStateComponent>(target, out var mob) ||
            mob.CurrentState != MobState.Alive ||
            _tags.HasTag(target, ChimeraTag))
        {
            return false;
        }

        var origin = _transform.GetMapCoordinates(pustule);
        var other = _transform.GetMapCoordinates(target);
        return _interaction.InRangeUnobstructed(
            origin,
            other,
            TriggerRange,
            CollisionGroup.Opaque | CollisionGroup.Impassable,
            uid => uid == pustule || uid == target);
    }

    /// <summary>Burst once and create exactly three ticks, or wait intact while the population guard is full.</summary>
    public bool TryBurst(Entity<WFFleshPustuleComponent> ent, bool forceImpact = false)
    {
        if (ent.Comp.Bursted)
            return false;

        var ground = PopulationRoot(ent);
        var hasCapacity = HasTickCapacity(ground);
        if (!hasCapacity && !forceImpact)
            return false;

        ent.Comp.Bursted = true;
        _appearance.SetData(ent, WFFleshPustuleVisuals.Bursted, true);
        PinPoppedBiomeTile(ent);

        _audio.PlayPvs(BurstSound, ent);
        _puddle.TrySpillAt(
            ent,
            new Solution("NaturalLetoferol", FixedPoint2.New(ent.Comp.SpillQuantity)),
            out _);

        var coordinates = Transform(ent).Coordinates;
        for (var i = 0; hasCapacity && i < TicksPerBurst; i++)
        {
            var tick = Spawn(TickPrototype, coordinates);
            EnsureComp<WFFleshPustuleSpawnedTickComponent>(tick).Ground = ground;
            _ticks[tick] = ground;
        }

        return true;
    }

    /// <summary>Plant only on the exact Chimera biomass tile, within reach and unobstructed.</summary>
    public bool TryPlant(EntityUid performer, EntityCoordinates target, EntProtoId? pustulePrototype = null)
    {
        if (!_tags.HasTag(performer, ChimeraTag) ||
            !TryComp<MobStateComponent>(performer, out var mob) ||
            mob.CurrentState != MobState.Alive ||
            _transform.GetGrid(target) is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            return false;
        }

        var tile = _map.TileIndicesFor(grid, mapGrid, target);
        var center = _map.GridTileToLocal(grid, mapGrid, tile);
        EntityUid? biomass = null;
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, mapGrid, tile);
        while (anchored.MoveNext(out var maybeCandidate))
        {
            if (maybeCandidate is not { } candidate)
                continue;
            if (HasComp<WFFleshPustuleComponent>(candidate))
                return false;
            if (_tags.HasTag(candidate, ChimeraFloorTag) &&
                MetaData(candidate).EntityPrototype?.ID == BiomassPrototype.Id)
            {
                biomass = candidate;
            }
        }

        if (biomass == null ||
            !_interaction.InRangeUnobstructed(
                performer,
                center,
                PlantRange,
                CollisionGroup.Opaque | CollisionGroup.Impassable,
                uid => uid == biomass.Value))
        {
            return false;
        }

        Spawn(pustulePrototype ?? PustulePrototype, center);
        return true;
    }

    private EntityUid PopulationRoot(EntityUid uid)
    {
        var xform = Transform(uid);
        return xform.MapUid ?? xform.GridUid ?? xform.ParentUid;
    }

    private bool HasTickCapacity(EntityUid ground)
    {
        _staleTicks.Clear();
        var local = 0;
        foreach (var (tick, origin) in _ticks)
        {
            if (TerminatingOrDeleted(tick) ||
                TryComp<MobStateComponent>(tick, out var mob) && mob.CurrentState == MobState.Dead)
            {
                _staleTicks.Add(tick);
                continue;
            }

            if (origin == ground)
                local++;
        }

        foreach (var stale in _staleTicks)
            _ticks.Remove(stale);

        return local + TicksPerBurst <= MaxTicksPerGround &&
               _ticks.Count + TicksPerBurst <= MaxTrackedTicks;
    }

    private void PinPoppedBiomeTile(EntityUid pustule)
    {
        var xform = Transform(pustule);
        if (xform.MapUid is not { } ground || xform.GridUid != ground ||
            !TryComp<BiomeComponent>(ground, out var biome) ||
            !TryComp<MapGridComponent>(ground, out var grid))
        {
            return;
        }

        var tile = _map.TileIndicesFor(ground, grid, _transform.WithEntityId(xform.Coordinates, ground));
        _biomes.WfPinTiles((ground, biome), new[] { tile });
    }
}
