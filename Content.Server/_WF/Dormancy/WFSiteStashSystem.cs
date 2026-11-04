using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Power.Components;
using System.Linq;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Dormancy;

/// <summary>
/// Pauses a world's sites, wrecks, camps and city blocks where they stand while nobody is near, and wakes them before
/// anyone can see them. A paused entity keeps everything about it, its NPCs' memories included, and costs nothing a
/// tick; waking it is cheap, so a site is never seen half there.
/// </summary>
public sealed partial class WFSiteStashSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;

    /// <summary>SharedBiomeSystem.ChunkSize, which is protected.</summary>
    private const int BiomeChunk = 8;

    /// <summary>Entities paused or woken between two looks at the clock.</summary>
    private const int Slice = 32;

    private static readonly TimeSpan EvaluateEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(60);

    private readonly List<WFStashRegion> _regions = new();
    private readonly List<WFStashRegion> _work = new();
    private readonly HashSet<EntityUid> _found = new();
    private readonly List<EntityUid> _tree = new();
    private readonly System.Diagnostics.Stopwatch _watch = new();
    private TimeSpan _nextEvaluate;

    private bool _enabled;
    private float _idle;
    private float _restoreRange;
    private float _stashRange;
    private float _budget;
    private float _pvsRange;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<BiomeComponent> _biomeQuery;

    /// <summary>Every region being watched, for tests and the admin.</summary>
    public IReadOnlyList<WFStashRegion> Regions => _regions;

    /// <summary>The worst time one tick spent pausing and waking, since it was last reset.</summary>
    public double WorstTickMs;

    /// <summary>What the worst tick was doing: registering a world, waking, pausing or sweeping.</summary>
    public string WorstTickWork = string.Empty;

    private string _tickWork = string.Empty;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _biomeQuery = GetEntityQuery<BiomeComponent>();

        Subs.CVar(_cfg, PlanetCVars.SiteStash, OnEnabled, true);
        Subs.CVar(_cfg, PlanetCVars.SiteStashIdle, value => _idle = value, true);
        Subs.CVar(_cfg, PlanetCVars.SiteRestoreRange, value => _restoreRange = value, true);
        Subs.CVar(_cfg, PlanetCVars.SiteStashRange, value => _stashRange = value, true);
        Subs.CVar(_cfg, PlanetCVars.SiteStashBudget, value => _budget = value, true);
        Subs.CVar(_cfg, CVars.NetMaxUpdateRange, value => _pvsRange = value, true);

        InitializeRegions();
    }

    /// <summary>Turned off, everything paused wakes at once and nothing is watched any more.</summary>
    private void OnEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
            return;

        foreach (var region in _regions)
        {
            foreach (var uid in region.Paused)
            {
                Wake(uid);
            }
        }

        _regions.Clear();
        _registered.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        _watch.Restart();
        _tickWork = string.Empty;
        if (_timing.CurTime >= _nextEvaluate)
        {
            _nextEvaluate = _timing.CurTime + EvaluateEvery;
            RegisterPreloaded();
            Evaluate();
        }

        DoWork();
        if (_watch.Elapsed.TotalMilliseconds > WorstTickMs)
        {
            WorstTickMs = _watch.Elapsed.TotalMilliseconds;
            WorstTickWork = _tickWork;
        }
    }

    /// <summary>Decides, from where everyone is, which regions to wake and which have been left alone long enough to pause.</summary>
    private void Evaluate()
    {
        var now = _timing.CurTime;
        for (var i = _regions.Count - 1; i >= 0; i--)
        {
            var region = _regions[i];
            if (TerminatingOrDeleted(region.Ground))
            {
                _regions.RemoveAt(i);
                continue;
            }

            region.Distance = Nearest(region);
            switch (region.State)
            {
                case WFStashState.Awake:
                    if (region.Protected || region.Distance <= _stashRange)
                    {
                        region.IdleSince = null;
                        region.StashNow = false;
                    }
                    else if (region.StashNow || region.IdleSince is { } since && (now - since).TotalSeconds >= _idle)
                    {
                        region.State = WFStashState.Stashing;
                        region.Pending.Clear();
                        region.Paused.Clear();
                        region.Cursor = 0;
                        region.Gathered = false;
                    }
                    else
                    {
                        region.IdleSince ??= now;
                    }
                    break;
                case WFStashState.Stashing:
                case WFStashState.Stashed:
                    if (region.Distance <= _restoreRange)
                    {
                        region.State = WFStashState.Waking;
                        region.Pending.Clear();
                        region.Cursor = 0;
                    }
                    break;
            }
        }
    }

    /// <summary>Wakes regions nearest first, then pauses and sweeps, within the tick's budget.</summary>
    private void DoWork()
    {
        _work.Clear();
        foreach (var region in _regions)
        {
            if (region.State == WFStashState.Waking)
                _work.Add(region);
        }

        _work.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        // Someone about to see a region gets all of it this tick, whatever it costs.
        var urgent = _pvsRange + BiomeChunk;
        foreach (var region in _work)
        {
            _tickWork += $" wake {region.Label};";
            var now = region.Distance <= urgent;
            while (region.Cursor < region.Paused.Count)
            {
                if (!now && OverBudget())
                    return;

                var end = Math.Min(region.Paused.Count, region.Cursor + Slice);
                for (; region.Cursor < end; region.Cursor++)
                {
                    Wake(region.Paused[region.Cursor]);
                }
            }

            region.State = WFStashState.Awake;
            region.Paused.Clear();
            region.Cursor = 0;
            region.IdleSince = null;
            region.StashNow = false;
        }

        foreach (var region in _regions)
        {
            if (region.State != WFStashState.Stashing)
                continue;

            if (OverBudget())
                return;

            _tickWork += $" pause {region.Label};";
            if (!region.Gathered)
            {
                Gather(region);
                region.Gathered = true;
            }

            while (region.Cursor < region.Pending.Count)
            {
                if (OverBudget())
                    return;

                var end = Math.Min(region.Pending.Count, region.Cursor + Slice);
                for (; region.Cursor < end; region.Cursor++)
                {
                    Pause(region, region.Pending[region.Cursor]);
                }
            }

            region.State = WFStashState.Stashed;
            region.Pending.Clear();
            region.Cursor = 0;
            region.NextSweep = _timing.CurTime + SweepEvery;
        }

        // Whatever has turned up in a paused region since, such as an event's spawn, is paused with it.
        foreach (var region in _regions)
        {
            if (region.State != WFStashState.Stashed || _timing.CurTime < region.NextSweep)
                continue;

            if (OverBudget())
                return;

            region.NextSweep = _timing.CurTime + SweepEvery;
            _tickWork += $" sweep {region.Label};";
            Gather(region);
            foreach (var uid in region.Pending)
            {
                Pause(region, uid);
            }

            region.Pending.Clear();
        }
    }

    private bool OverBudget()
    {
        return _watch.Elapsed.TotalMilliseconds >= _budget;
    }

    /// <summary>Pauses one entity and remembers it; one paused already, by its map or anyone else, is left to them.</summary>
    private void Pause(WFStashRegion region, EntityUid uid)
    {
        if (!_metaQuery.TryComp(uid, out var meta) || meta.EntityLifeStage >= EntityLifeStage.Terminating || meta.EntityPaused)
            return;

        _meta.SetEntityPaused(uid, true, meta);
        region.Paused.Add(uid);
    }

    private void Wake(EntityUid uid)
    {
        if (_metaQuery.TryComp(uid, out var meta) && meta.EntityLifeStage < EntityLifeStage.Terminating)
            _meta.SetEntityPaused(uid, false, meta);
    }

    /// <summary>
    /// Lists what a region pauses: everything standing in its box on its layers and grids, and its own hulls whole,
    /// with what each holds; mobs last, so they wake last. Walls and cables, which do nothing a tick, stay as they are.
    /// </summary>
    private void Gather(WFStashRegion region)
    {
        region.Pending.Clear();
        var mobs = new List<EntityUid>();
        var tops = new List<EntityUid>();

        if (region.MobsOnly)
        {
            var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (!xform.Anchored
                    && region.Parents.Contains(xform.ParentUid)
                    && region.Box.Contains(xform.LocalPosition)
                    && !region.Claimed.Any(box => box.Contains(xform.LocalPosition)))
                    mobs.Add(uid);
            }

            foreach (var mob in mobs)
            {
                AddTree(region, mob);
            }

            return;
        }

        // Every parent sits at its map's origin unturned, a layer or a city level, so the box is its own coordinates.
        foreach (var parent in region.Parents)
        {
            if (TerminatingOrDeleted(parent))
                continue;

            _found.Clear();
            _lookup.GetLocalEntitiesIntersecting(parent, region.Box, _found, LookupFlags.Uncontained | LookupFlags.Approximate);
            _biomeQuery.TryComp(parent, out var biome);
            foreach (var uid in _found)
            {
                if (!_xformQuery.TryComp(uid, out var xform)
                    || xform.ParentUid != parent
                    || !region.Box.Contains(xform.LocalPosition)
                    || HasComp<MapGridComponent>(uid)
                    || IsInert(uid, xform)
                    || biome != null && IsBiomeGrown(biome, uid, xform))
                    continue;

                (_mobQuery.HasComp(uid) ? mobs : tops).Add(uid);
            }
        }

        foreach (var hull in region.Hulls)
        {
            if (!TerminatingOrDeleted(hull))
                tops.Add(hull);
        }

        foreach (var top in tops)
        {
            AddTree(region, top);
        }

        foreach (var mob in mobs)
        {
            AddTree(region, mob);
        }
    }

    /// <summary>
    /// Adds an entity and all it holds, unless someone is in it: a player, a mind, or a vehicle with a cabin, whose
    /// crew is elsewhere and drives it, stays awake.
    /// </summary>
    private void AddTree(WFStashRegion region, EntityUid root)
    {
        _tree.Clear();
        _tree.Add(root);
        for (var i = 0; i < _tree.Count; i++)
        {
            var uid = _tree[i];
            if (Blocks(uid))
                return;

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _tree.Add(child);
            }
        }

        foreach (var uid in _tree)
        {
            if (uid == root || !IsInert(uid, Transform(uid)))
                region.Pending.Add(uid);
        }
    }

    /// <summary>Whether an entity keeps the whole thing it is part of awake.</summary>
    private bool Blocks(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid)
               || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind
               || HasComp<VehicleInteriorComponent>(uid)
               || HasComp<VehicleEnterComponent>(uid);
    }

    /// <summary>An anchored wall, window, door or cable: nothing to pause, as it does nothing a tick.</summary>
    private bool IsInert(EntityUid uid, TransformComponent xform)
    {
        return xform.Anchored && (HasComp<AirtightComponent>(uid) || HasComp<CableComponent>(uid));
    }

    /// <summary>Whether the biome keeps this entity as one of a chunk's own, which the terrain stash looks after.</summary>
    private static bool IsBiomeGrown(BiomeComponent biome, EntityUid uid, TransformComponent xform)
    {
        var tile = xform.LocalPosition.Floored();
        var chunk = new Vector2i(
            (int) MathF.Floor(tile.X / (float) BiomeChunk) * BiomeChunk,
            (int) MathF.Floor(tile.Y / (float) BiomeChunk) * BiomeChunk);
        var all = biome.LoadedEntities;
        return all.TryGetValue(chunk, out var loaded) && loaded.ContainsKey(uid);
    }

    /// <summary>Metres from a region's box to the nearest viewer on its planet.</summary>
    private float Nearest(WFStashRegion region)
    {
        var nearest = float.MaxValue;
        foreach (var viewer in _dormancy.Viewers(region.Network))
        {
            var dx = MathF.Max(0f, MathF.Max(region.Box.Left - viewer.X, viewer.X - region.Box.Right));
            var dy = MathF.Max(0f, MathF.Max(region.Box.Bottom - viewer.Y, viewer.Y - region.Box.Top));
            nearest = MathF.Min(nearest, MathF.Sqrt(dx * dx + dy * dy));
        }

        return nearest;
    }
}

/// <summary>Where a stash region is between awake and paused.</summary>
public enum WFStashState : byte
{
    Awake,
    Stashing,
    Stashed,
    Waking,
}

/// <summary>
/// One part of a world that sleeps as a whole: a site, a wreck, a camp or a city block on every level. It holds what
/// stands in its box on its layers and grids, and its own hulls whole.
/// </summary>
public sealed class WFStashRegion
{
    public EntityUid Network;
    public EntityUid Ground;
    public string Label = string.Empty;

    /// <summary>The box, in the coordinates every layer of the planet shares.</summary>
    public Box2 Box;

    /// <summary>Layers and grids whose own entities in the box belong to the region.</summary>
    public List<EntityUid> Parents = new();

    /// <summary>Grids that belong to the region whole, such as a wreck's or a camp's ship.</summary>
    public List<EntityUid> Hulls = new();

    /// <summary>Never paused: players spawn in or near it.</summary>
    public bool Protected;

    /// <summary>Holds only the mobs in its box, and none inside <see cref="Claimed"/>: a square of open world.</summary>
    public bool MobsOnly;

    /// <summary>Boxes other regions look after, which a <see cref="MobsOnly"/> region leaves alone.</summary>
    public List<Box2> Claimed = new();

    public WFStashState State;

    /// <summary>Since when nobody has been within stash range; null while someone is.</summary>
    public TimeSpan? IdleSince;

    /// <summary>Paused at the next look if nobody is near, without waiting out the idle time; set when the world forms.</summary>
    public bool StashNow;

    /// <summary>Metres to the nearest viewer at the last look.</summary>
    public float Distance;

    /// <summary>What this region paused, in the order it went: mobs last.</summary>
    public readonly List<EntityUid> Paused = new();

    public readonly List<EntityUid> Pending = new();
    public int Cursor;
    public bool Gathered;
    public TimeSpan NextSweep;
}
