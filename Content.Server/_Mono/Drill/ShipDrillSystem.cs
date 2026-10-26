using System.Numerics;
using Content.Server._Forge.OrePipe;
using Content.Server.Gatherable;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Drill;

public partial class ShipDrillSystem : EntitySystem
{
    /// <summary>
    /// Cap tile wipes per drill per tick. Clearing asteroid chunks while a shuttle is
    /// physically contacting them floods client broadphase with invalid contacts
    /// (Entity 0 / invalid contact index) → freeze, CPU spike, memory growth from exception spam.
    /// </summary>
    private const int MaxTilesPerUpdate = 4;

    /// <summary>
    /// After worldgen loads a debris chunk, ExtraMassive asteroids dump hundreds of rocks into
    /// client PVS ("unfreeze" / appear on approach). Digging during that flood while the shuttle
    /// is already contacting the grid produces the same Entity 0 physics crash.
    /// ~3s at 30 tickrate.
    /// </summary>
    private const uint DebrisWarmupTicks = 90;

    [Dependency] private EntityLookupSystem _look = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tileDef = default!;
    [Dependency] private GatherableSystem _gather = default!;
    [Dependency] private OrePipeSystem _orePipe = default!; // Forge-Change
    [Dependency] private DamageableSystem _damageable = default!; // Forge-Change: ore crabs/golems
    [Dependency] private IGameTiming _timing = default!;

    private static readonly DamageSpecifier OreMobDrillDamage = new()
    {
        DamageDict = new() { ["Blunt"] = FixedPoint2.New(40) },
    };

    private readonly HashSet<EntityUid> _ents = new();
    private readonly HashSet<EntityUid> _mobs = new();
    private readonly HashSet<TileRef> _nonEmptyTiles = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<(Vector2i GridIndices, Tile Tile)> _tilesToClear = new();

    private float _updateCooldown = 0.25f;
    private float _updateTimer = 0f;

    public override void Update(float frameTime)
    {
        if (_updateTimer < _updateCooldown)
        {
            _updateTimer += frameTime;
            return;
        }
        _updateTimer -= _updateCooldown;

        var eQe = EntityQueryEnumerator<ShipDrillComponent>();

        while (eQe.MoveNext(out var uid, out var comp))
        {
            if (!this.IsPowered(uid, EntityManager))
                continue;

            // Forge-Change: drill runs only with DisposalPipe link to ore storage (not magnets).
            if (!_orePipe.CanDrillOperate(uid))
                continue;

            var coords = _xform.GetMapCoordinates(uid);
            var angle = _xform.GetWorldRotation(uid);
            var dGrid = Transform(uid).GridUid;

            if (!dGrid.HasValue)
                continue;

            var dVec = comp.DrillSize / 2;
            var tVec = new Vector2(0.25f, 0.25f);

            var worldBox = new Box2Rotated(
                new Box2(coords.Offset(-dVec + comp.DrillOffsets).Position, coords.Offset(dVec + comp.DrillOffsets).Position),
                angle,
                coords.Position);

            /// I dont want to do this but RT lookups being so evil is fucking insane.
            /// Apparently tile lookup is bigger than entity lookup for literally 0 reason.
            var tileWorldBox = new Box2Rotated(
                new Box2(coords.Offset(-dVec + tVec + comp.DrillOffsets).Position, coords.Offset(dVec - tVec + comp.DrillOffsets).Position),
                angle,
                coords.Position);

            _grids.Clear();
            // includeMap: false — after IMapManager removal, planet/map-as-grid entities are
            // returned by default and drilling them freezes (full-map tile/entity queries).
            _map.FindGridsIntersecting(_xform.GetMapId(dGrid.Value), worldBox.CalcBoundingBox(), ref _grids, includeMap: false);

            foreach (var grid in _grids)
            {
                if (grid.Owner == dGrid)
                    continue;

                // Belt-and-suspenders: never treat a map entity as a drillable asteroid grid.
                if (HasComp<MapComponent>(grid.Owner))
                    continue;

                // Skip paused / still-spawning debris. Clients "unfreeze" (PVS reattach) rocks on
                // approach — if the drill already chewed tiles before that, broadphase contacts break.
                var gridMeta = MetaData(grid.Owner);
                if (gridMeta.EntityPaused || !gridMeta.EntityInitialized)
                    continue;

                if (_timing.CurTick.Value < gridMeta.CreationTick.Value + DebrisWarmupTicks)
                    continue;

                var tiles = _map.GetTilesIntersecting(grid.Owner, grid.Comp, tileWorldBox);
                _look.GetEntitiesIntersecting(grid.Owner, worldBox, _ents, LookupFlags.Static);
                _look.GetEntitiesIntersecting(grid.Owner, worldBox, _mobs, LookupFlags.Dynamic);

                foreach (var ent in _ents)
                {
                    if (TerminatingOrDeleted(ent) || Paused(ent))
                        continue;

                    comp.DrillType?.Drill(ent, uid, this, EntityManager);
                    var tileRef = _map.GetTileRef(grid.Owner, grid.Comp, Transform(ent).Coordinates);
                    _nonEmptyTiles.Add(tileRef);
                }

                // Forge-Change: only ore crabs/golems — damaging every Dynamic entity every tick
                // spam-dirties damage states and worsens client hitching during mining.
                foreach (var mob in _mobs)
                {
                    if (TerminatingOrDeleted(mob) || Paused(mob))
                        continue;

                    if (!HasComp<OreDrillHarvestTargetComponent>(mob))
                        continue;

                    _damageable.TryChangeDamage(mob, OreMobDrillDamage, ignoreResistances: true, origin: uid);
                }

                // Do NOT QueueDel all anchored entities here.
                // Gas deposits / crystals already have RequiresTile and are removed by RequiresTileSystem
                // when the floor is cleared. Mass-deleting Static fixtures while the shuttle is in contact
                // with ExtraMassive asteroids corrupts client physics contacts (Entity 0 / invalid index).
                _tilesToClear.Clear();
                foreach (var tileRef in tiles)
                {
                    if (_tilesToClear.Count >= MaxTilesPerUpdate)
                        break;

                    if (_nonEmptyTiles.Contains(tileRef))
                        continue;

                    if (tileRef.Tile.IsEmpty)
                        continue;

                    var tileDef = _tileDef[tileRef.Tile.TypeId];
                    if (comp.TileWhitelist != null && !comp.TileWhitelist.Contains(tileDef.ID))
                        continue;

                    _tilesToClear.Add((tileRef.GridIndices, Tile.Empty));
                }

                if (_tilesToClear.Count > 0)
                    _map.SetTiles(grid.Owner, grid.Comp, _tilesToClear);

                _ents.Clear();
                _mobs.Clear();
                _nonEmptyTiles.Clear();
            }
        }
    }
}
