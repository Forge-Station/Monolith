using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Environment;

/// <summary>
/// Makes a corrupted ship look infested. As a Monolith climbs it seeds plain sprites over its corruption: roots, sacs, organs,
/// bone spurs and gore on the floor, remains fused into walls, veins over doors and vents, glyph screens on consoles, and mist at
/// Rapture. Each corruption tile rolls once per stage, a slice of the tiles a second, and the decor is parented to its tile so it goes with it.
/// It also plays the odd wet noise near the crew and sparks a glyph screen.
/// </summary>
public sealed class WFEnvDecorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;

    private const int Slices = 8;
    private static readonly TimeSpan Rate = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly SoundSpecifier Squelch = new SoundCollectionSpecifier("WFEnvSquelch");
    private static readonly SoundSpecifier Creak = new SoundCollectionSpecifier("WFEnvCreak");
    private static readonly SoundSpecifier SparkSound = new SoundCollectionSpecifier("sparks");
    private static readonly EntProtoId SparkEffect = "EffectSparks";
    private static readonly Vector2i[] Cardinals = { new(0, 1), new(0, -1), new(1, 0), new(-1, 0) };

    /// <summary>What can lie on the floor, from which stage, by weight.</summary>
    private static readonly (EntProtoId Proto, WFMonolithStage Min, float Weight)[] Floor =
    {
        ("WFEnvTendril", WFMonolithStage.Growth, 3f),
        ("WFEnvTendrilB", WFMonolithStage.Growth, 3f),
        ("WFEnvTendrilC", WFMonolithStage.Growth, 3f),
        ("WFEnvGoreSmear", WFMonolithStage.Growth, 2f),
        ("WFEnvSpurLow", WFMonolithStage.Growth, 1f),
        ("WFEnvSacSmall", WFMonolithStage.Growth, 1.5f),
        ("WFEnvSac", WFMonolithStage.Harvest, 2f),
        ("WFEnvOrgans", WFMonolithStage.Harvest, 1.2f),
        ("WFEnvOrgansB", WFMonolithStage.Harvest, 1.2f),
        ("WFEnvOrgansC", WFMonolithStage.Harvest, 1.2f),
        ("WFEnvSpur", WFMonolithStage.Harvest, 1.5f),
        ("WFEnvGoreScraps", WFMonolithStage.Harvest, 1.5f),
        ("WFEnvGoreSpray", WFMonolithStage.Harvest, 1.2f),
        ("WFEnvOoze", WFMonolithStage.Harvest, 1.2f),
        ("WFEnvDrip", WFMonolithStage.Harvest, 1.5f),
        ("WFEnvRemains", WFMonolithStage.Harvest, 0.6f),
        ("WFEnvSacBig", WFMonolithStage.Rapture, 2f),
        ("WFEnvSacBeating", WFMonolithStage.Rapture, 0.8f),
        ("WFEnvSpurTall", WFMonolithStage.Rapture, 1.5f),
        ("WFEnvGore", WFMonolithStage.Rapture, 2f),
        ("WFEnvGoreTrail", WFMonolithStage.Rapture, 1.5f),
    };

    private static readonly EntProtoId[] Hazes = { "WFEnvHaze", "WFEnvHazeB" };
    private static readonly EntProtoId[] Cocoons = { "WFEnvCocoon", "WFEnvCocoonB" };
    private static readonly EntProtoId[] Webs = { "WFEnvWeb", "WFEnvWebB", "WFEnvWebC" };
    private static readonly EntProtoId[] Screens = { "WFEnvScreen", "WFEnvScreenB" };

    private TimeSpan _nextTick;
    private int _slice;
    private readonly HashSet<Entity<WFEnvDecorComponent>> _near = new();
    private readonly List<EntityUid> _crew = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEnvDecorComponent, EntityTerminatingEvent>(OnDecorGone);
    }

    private void OnDecorGone(Entity<WFEnvDecorComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Counted && TryComp<WFEnvStateComponent>(ent.Comp.Stone, out var state))
            state.Decor = Math.Max(0, state.Decor - 1);
    }

    /// <summary>Floor chance per tile for each stage, rolled once when a tile reaches it.</summary>
    public static float FloorChance(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 0.06f,
            WFMonolithStage.Harvest => 0.10f,
            WFMonolithStage.Rapture => 0.16f,
            WFMonolithStage.Crescendo => 0.12f,
            _ => 0f,
        };
    }

    /// <summary>The most decor a Monolith carries, as a share of its corruption tiles.</summary>
    public static float Budget(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 0.06f,
            WFMonolithStage.Harvest => 0.14f,
            WFMonolithStage.Rapture => 0.24f,
            WFMonolithStage.Crescendo => 0.28f,
            _ => 0f,
        };
    }

    /// <summary>Mist is thin and doesn't count against the budget, so it is kept to a few tiles in twenty.</summary>
    private static float HazeChance(WFMonolithStage stage) => stage >= WFMonolithStage.Rapture ? 0.05f : 0f;

    private static float CocoonChance(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Harvest => 0.03f,
            WFMonolithStage.Rapture or WFMonolithStage.Crescendo => 0.05f,
            _ => 0f,
        };
    }

    private static float WebChance(WFMonolithStage stage) => stage >= WFMonolithStage.Growth ? 0.35f : 0f;

    private static float ScreenChance(WFMonolithStage stage) => stage >= WFMonolithStage.Harvest ? 0.5f : 0f;

    /// <summary>Seconds between wet noises near the crew, by stage.</summary>
    private static (float Min, float Max) AmbientEvery(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => (35f, 55f),
            WFMonolithStage.Harvest => (25f, 40f),
            WFMonolithStage.Rapture => (16f, 28f),
            _ => (10f, 18f),
        };
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + Rate;
        _slice = (_slice + 1) % Slices;

        var tiles = EntityQueryEnumerator<WFCorruptionComponent, TransformComponent>();
        while (tiles.MoveNext(out var uid, out var tile, out var xform))
        {
            if (uid.Id % Slices != _slice || tile.DecayAt != null || tile.Source is not { } stone)
                continue;

            if (!TryComp<WFMonolithComponent>(stone, out var monolith) || monolith.Stage < WFMonolithStage.Growth)
                continue;

            var decorated = EnsureComp<WFEnvDecoratedComponent>(uid);
            if (decorated.Level >= (int) monolith.Stage)
                continue;

            decorated.Level = (int) monolith.Stage;
            Decorate((uid, xform), decorated, (stone, monolith), now);
        }

        var stones = EntityQueryEnumerator<WFMonolithComponent, WFEnvStateComponent>();
        while (stones.MoveNext(out var uid, out var monolith, out var state))
        {
            Ambient((uid, monolith), state, now);
        }
    }

    private void Decorate(Entity<TransformComponent> tile, WFEnvDecoratedComponent decorated, Entity<WFMonolithComponent> stone, TimeSpan now)
    {
        var stage = stone.Comp.Stage;
        var state = EnsureComp<WFEnvStateComponent>(stone);
        var limit = (int) MathF.Ceiling(stone.Comp.Corruption.Count * Budget(stage));
        if (state.Decor >= limit)
            return;

        if (_random.Prob(FloorChance(stage)))
            SpawnFloor(tile, stone, state);

        if (_random.Prob(HazeChance(stage)))
            Place(_random.Pick(Hazes), tile, Vector2.Zero, stone, state, false);

        if (_random.Prob(CocoonChance(stage)) && WallBeside(tile) is { } wall)
            Place(_random.Pick(Cocoons), tile, new Vector2(wall.X, wall.Y), stone, state);

        if (!decorated.Covered)
            Cover(tile, decorated, stone, state);
    }

    private void SpawnFloor(Entity<TransformComponent> tile, Entity<WFMonolithComponent> stone, WFEnvStateComponent state)
    {
        var total = 0f;
        foreach (var option in Floor)
        {
            if (stone.Comp.Stage >= option.Min)
                total += option.Weight;
        }

        var roll = _random.NextFloat(0f, total);
        foreach (var option in Floor)
        {
            if (stone.Comp.Stage < option.Min)
                continue;

            roll -= option.Weight;
            if (roll <= 0f)
            {
                var offset = new Vector2(_random.NextFloat(-0.2f, 0.2f), _random.NextFloat(-0.2f, 0.2f));
                Place(option.Proto, tile, offset, stone, state);
                return;
            }
        }
    }

    /// <summary>Veins over a door or vent, a glyph screen over a console, if one stands on this tile.</summary>
    private void Cover(Entity<TransformComponent> tile, WFEnvDecoratedComponent decorated, Entity<WFMonolithComponent> stone, WFEnvStateComponent state)
    {
        if (tile.Comp.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var index = _map.TileIndicesFor(gridUid, grid, tile.Comp.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, index);
        while (anchored.MoveNext(out var found))
        {
            if (found is not { } uid)
                continue;

            if ((HasComp<DoorComponent>(uid) || HasComp<GasVentPumpComponent>(uid) || HasComp<GasVentScrubberComponent>(uid))
                && _random.Prob(WebChance(stone.Comp.Stage)))
            {
                decorated.Covered = true;
                Place(_random.Pick(Webs), tile, Vector2.Zero, stone, state);
                return;
            }

            if ((HasComp<ComputerComponent>(uid) || HasComp<ApcComponent>(uid)) && _random.Prob(ScreenChance(stone.Comp.Stage)))
            {
                decorated.Covered = true;
                Place(_random.Pick(Screens), tile, Vector2.Zero, stone, state);
                return;
            }
        }
    }

    /// <summary>The direction of a wall next to the tile, if there is one.</summary>
    private Vector2i? WallBeside(Entity<TransformComponent> tile)
    {
        if (tile.Comp.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var index = _map.TileIndicesFor(gridUid, grid, tile.Comp.Coordinates);
        var start = _random.Next(Cardinals.Length);
        for (var i = 0; i < Cardinals.Length; i++)
        {
            var step = Cardinals[(start + i) % Cardinals.Length];
            var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, index + step);
            while (anchored.MoveNext(out var found))
            {
                if (found is { } uid && _tag.HasTag(uid, WallTag))
                    return step;
            }
        }

        return null;
    }

    private void Place(EntProtoId proto, Entity<TransformComponent> tile, Vector2 offset, Entity<WFMonolithComponent> stone, WFEnvStateComponent state, bool counted = true)
    {
        var decor = Spawn(proto, new EntityCoordinates(tile, offset));
        var comp = EnsureComp<WFEnvDecorComponent>(decor);
        comp.Stone = stone;
        comp.Counted = counted;
        if (counted)
            state.Decor++;
    }

    /// <summary>Now and then a wet noise or a creak near one of the crew, and a glyph screen sparking.</summary>
    private void Ambient(Entity<WFMonolithComponent> stone, WFEnvStateComponent state, TimeSpan now)
    {
        if (stone.Comp.Stage < WFMonolithStage.Growth || state.Decor == 0)
            return;

        if (state.NextAmbient == TimeSpan.Zero)
            state.NextAmbient = now + TimeSpan.FromSeconds(AmbientEvery(stone.Comp.Stage).Min);

        if (now >= state.NextAmbient)
        {
            var (min, max) = AmbientEvery(stone.Comp.Stage);
            state.NextAmbient = now + TimeSpan.FromSeconds(_random.NextFloat(min, max));
            if (PickDecor(stone, 9f, false) is { } decor)
            {
                _audio.PlayPvs(_random.Prob(0.6f) ? Squelch : Creak, decor, AudioParams.Default.WithVolume(-8f).WithMaxDistance(7f));
                state.Noises++;
            }
        }

        if (stone.Comp.Stage < WFMonolithStage.Harvest)
            return;

        if (state.NextSpark == TimeSpan.Zero)
            state.NextSpark = now + TimeSpan.FromSeconds(_random.NextFloat(10f, 20f));

        if (now >= state.NextSpark)
        {
            state.NextSpark = now + TimeSpan.FromSeconds(_random.NextFloat(14f, 30f));
            if (PickDecor(stone, 14f, true) is { } screen)
            {
                Spawn(SparkEffect, Transform(screen).Coordinates);
                _audio.PlayPvs(SparkSound, screen);
                state.Sparked++;
            }
        }
    }

    /// <summary>A decor entity near one of the crew on the Monolith's map, or a spark-throwing screen if asked. Null if none is near.</summary>
    private EntityUid? PickDecor(Entity<WFMonolithComponent> stone, float range, bool screens)
    {
        _crew.Clear();
        var map = _transform.GetMapId(stone.Owner);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !_mobState.IsDead(uid) && !HasComp<Conversion.WFRemadeComponent>(uid))
                _crew.Add(uid);
        }

        if (_crew.Count == 0)
            return null;

        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(_random.Pick(_crew)).Coordinates, range, _near);
        var options = _near.Where(decor => decor.Comp.Stone == stone.Owner && (!screens || decor.Comp.Sparks)).ToList();
        return options.Count == 0 ? null : _random.Pick(options).Owner;
    }
}
