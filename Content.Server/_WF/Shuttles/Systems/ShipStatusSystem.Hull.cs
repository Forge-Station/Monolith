using Content.Server.Atmos.Components;
using Content.Server.Construction;
using Content.Server.Construction.Completions;
using Content.Server.Destructible;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Shuttles.Systems;

public sealed partial class ShipStatusSystem
{
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, float>> _hullBaselines = new();
    private readonly HashSet<EntityUid> _sharedHullBaselines = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, (float Capacity, float Remaining)>> _hullStructures = new();
    private readonly Dictionary<EntityUid, float> _hullIntegrity = new();
    private readonly Dictionary<EntityUid, HullSurvey> _surveyedHull = new();
    private readonly Dictionary<EntityUid, ConstructionBeforeDeleteEvent?> _hullConstructionRemovals = new();

    private readonly record struct HullSurvey(EntityUid Grid, Vector2i Index, float Capacity);

    private void InitializeHullConstruction()
    {
        SubscribeLocalEvent<DestructibleComponent, ConstructionBeforeDeleteEvent>(OnHullConstructionDelete);
        SubscribeLocalEvent<DestructibleComponent, ConstructionChangeEntityEvent>(OnHullConstructionChange);
        SubscribeLocalEvent<DestructibleComponent, DestructionEventArgs>(OnHullDestroyed);
        SubscribeLocalEvent<DestructibleComponent, WFHullDeconstructedEvent>(OnHullRcdDelete);
        SubscribeLocalEvent<DestructibleComponent, EntityTerminatingEvent>(OnHullTerminating);
        SubscribeLocalEvent<MapGridComponent, WFHullTileDeconstructedEvent>(OnHullTileDeconstructed);
    }

    private void ObserveHullStructure(EntityUid uid, EntityUid grid, Vector2i index, float capacity) =>
        _surveyedHull[uid] = new HullSurvey(grid, index, capacity);

    /// <summary>Walls, windows and airtight doors are hull; diagonal variants replace the Wall and Window tags.</summary>
    private bool IsHullStructure(EntityUid uid) =>
        _tags.HasTag(uid, "Wall") || _tags.HasTag(uid, "Window")
        || (HasComp<AirtightComponent>(uid) && (HasComp<DoorComponent>(uid) || _tags.HasTag(uid, "Diagonal")));

    private void OnHullConstructionDelete(EntityUid uid, DestructibleComponent component, ConstructionBeforeDeleteEvent args)
    {
        if (_surveyedHull.ContainsKey(uid) && !HullDestroyedByDamage(uid, component))
            _hullConstructionRemovals[uid] = args;
    }

    private void OnHullConstructionChange(EntityUid uid, DestructibleComponent component, ConstructionChangeEntityEvent args)
    {
        if (args.Old == uid && _surveyedHull.ContainsKey(uid) && !HullDestroyedByDamage(uid, component))
            _hullConstructionRemovals[uid] = null;
    }

    /// <summary>Destruction behaviors can also replace construction nodes and must retain their damage.</summary>
    private bool HullDestroyedByDamage(EntityUid uid, DestructibleComponent component) =>
        TryComp<DamageableComponent>(uid, out var damage) && damage.TotalDamage >= _destructible.DestroyedAt(uid, component);

    /// <summary>Graph actions such as DestroyEntity delete without a construction event, but never past their damage threshold.</summary>
    private void OnHullDestroyed(EntityUid uid, DestructibleComponent component, DestructionEventArgs args)
    {
        if (_surveyedHull.ContainsKey(uid) && !HullDestroyedByDamage(uid, component))
            _hullConstructionRemovals[uid] = null;
    }

    private void OnHullRcdDelete(EntityUid uid, DestructibleComponent component, ref WFHullDeconstructedEvent args)
    {
        if (_surveyedHull.ContainsKey(uid))
            _hullConstructionRemovals[uid] = null;
    }

    /// <summary>A deliberately removed floor stops counting as a missing location; damage and explosions never raise this.</summary>
    private void OnHullTileDeconstructed(EntityUid uid, MapGridComponent component, ref WFHullTileDeconstructedEvent args)
    {
        if (!_hullBaselines.TryGetValue(uid, out var baseline))
            return;
        if (_sharedHullBaselines.Remove(uid))
            _hullBaselines[uid] = baseline = new Dictionary<Vector2i, float>(baseline);
        baseline.Remove(args.Index);
    }

    /// <summary>Only completed deconstruction changes the design; combat and cancelled actions retain their losses.</summary>
    private void OnHullTerminating(EntityUid uid, DestructibleComponent component, ref EntityTerminatingEvent args)
    {
        var surveyed = _surveyedHull.Remove(uid, out var structure);
        var constructed = _hullConstructionRemovals.Remove(uid, out var beforeDelete) && beforeDelete?.Cancelled != true;
        if (!surveyed || !constructed || !_hullBaselines.TryGetValue(structure.Grid, out var baseline))
            return;
        if (_sharedHullBaselines.Remove(structure.Grid))
            _hullBaselines[structure.Grid] = baseline = new Dictionary<Vector2i, float>(baseline);
        baseline[structure.Index] = Math.Max(0, baseline.GetValueOrDefault(structure.Index) - structure.Capacity);
    }

    /// <summary>Snapshots the hull before piloting can damage it, even if its telemetry page has never opened.</summary>
    public void ObserveHull(EntityUid console)
    {
        if (GetConsoleGrid(console) is { } grid && !_hullBaselines.ContainsKey(grid))
            GatherTiles(new HashSet<EntityUid> { grid }, new HashSet<EntityUid>(), fuel: false);
    }

    /// <summary>Retains lost locations and measures each surveyed tile against its own structural capacity.</summary>
    private void GatherHullIntegrity(HashSet<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            if (!TryComp<MapGridComponent>(grid, out var gridComp))
                continue;
            if (!_hullBaselines.TryGetValue(grid, out var baseline))
                _hullBaselines[grid] = baseline = new Dictionary<Vector2i, float>();
            else if (_sharedHullBaselines.Remove(grid))
                _hullBaselines[grid] = baseline = new Dictionary<Vector2i, float>(baseline);
            var structures = _hullStructures[grid];
            foreach (var (index, structure) in structures)
                baseline[index] = Math.Max(baseline.GetValueOrDefault(index), structure.Capacity);

            // Every floor joins the survey; a surveyed location that has lost its floor scores nothing.
            var intact = 0f;
            foreach (var tile in _maps.GetAllTiles(grid, gridComp))
            {
                var index = tile.GridIndices;
                if (!baseline.TryGetValue(index, out var capacity))
                    baseline[index] = capacity = 0f;
                intact += capacity > 0f
                    ? Math.Clamp(structures.GetValueOrDefault(index).Remaining / capacity, 0f, 1f)
                    : 1f;
            }
            _hullIntegrity[grid] = baseline.Count > 0 ? intact / baseline.Count : 1f;
        }
    }

    /// <summary>Split grids retain their original coordinate frame and inherit the whole surveyed hull.</summary>
    private void OnHullSplit(ref GridSplitEvent args)
    {
        if (!_hullBaselines.TryGetValue(args.Grid, out var baseline))
            return;
        // Debris shares the snapshot until surveyed; the original must also copy before changing it.
        _sharedHullBaselines.Add(args.Grid);
        foreach (var grid in args.NewGrids)
        {
            _hullBaselines[grid] = baseline;
            _sharedHullBaselines.Add(grid);

            // Only the fragment's own children can have moved, so the whole survey is never walked.
            var children = Transform(grid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (_surveyedHull.TryGetValue(child, out var structure) && structure.Grid == args.Grid)
                    _surveyedHull[child] = structure with { Grid = grid };
            }
        }
    }

    private void ForgetDeletedHulls()
    {
        HashSet<EntityUid>? deleted = null;
        foreach (var grid in _hullBaselines.Keys)
        {
            if (TerminatingOrDeleted(grid))
                (deleted ??= new HashSet<EntityUid>()).Add(grid);
        }
        if (deleted == null)
            return;

        foreach (var grid in deleted)
        {
            _hullBaselines.Remove(grid);
            _sharedHullBaselines.Remove(grid);
        }

        // Structures delete themselves with their grid; this only catches ones that had already left it.
        List<EntityUid>? stale = null;
        foreach (var (uid, structure) in _surveyedHull)
        {
            if (deleted.Contains(structure.Grid))
                (stale ??= new List<EntityUid>()).Add(uid);
        }
        if (stale == null)
            return;

        foreach (var uid in stale)
        {
            _surveyedHull.Remove(uid);
            _hullConstructionRemovals.Remove(uid);
        }
    }
}
