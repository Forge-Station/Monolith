using Content.Server.Disposal.Tube;
using Content.Server.Disposal.Unit;
using Content.Server.Materials;
using Content.Server.Stack;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Disposal.Components;
using Content.Shared.Disposal.Tube;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.OrePipe;

/// <summary>
/// Ship drills buffer mined ore, then inject it into a same-tile <see cref="DisposalEntryComponent"/> (trunk).
/// Ore travels with normal disposal holder rules (arrows / junctions / routers).
/// Ore holds and processors must also have a disposal trunk on their tile to receive exiting ore.
/// </summary>
public sealed partial class OrePipeSystem : EntitySystem
{
    public const float FlushIntervalSeconds = 5f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private OreHoldSystem _oreHold = default!;
    [Dependency] private MaterialStorageSystem _materialStorage = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private DisposableSystem _disposable = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    private TimeSpan _nextFlush;

    public override void Initialize()
    {
        base.Initialize();
        _nextFlush = _timing.CurTime + TimeSpan.FromSeconds(FlushIntervalSeconds);

        SubscribeLocalEvent<OrePipeInletComponent, ExaminedEvent>(OnInletExamined);
        SubscribeLocalEvent<OrePipeBufferComponent, ExaminedEvent>(OnBufferExamined);
        SubscribeLocalEvent<OrePipeOutletComponent, ExaminedEvent>(OnOutletExamined);
    }

    private void OnInletExamined(EntityUid uid, OrePipeInletComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(TryGetTrunkOnTile(uid, out _)
            ? "ore-pipe-examine-connected"
            : "ore-pipe-examine-disconnected"));
    }

    private void OnBufferExamined(EntityUid uid, OrePipeBufferComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("ore-pipe-examine-buffer",
            ("count", component.TotalCount),
            ("max", component.MaxTotalCount)));
    }

    private void OnOutletExamined(EntityUid uid, OrePipeOutletComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || HasComp<OreHoldComponent>(uid))
            return;

        if (!TryGetTrunkOnTile(uid, out _))
            args.PushMarkup(Loc.GetString("ore-pipe-examine-hold-no-trunk"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextFlush)
            return;

        _nextFlush = _timing.CurTime + TimeSpan.FromSeconds(FlushIntervalSeconds);
        FlushAllBuffersIntoTrunks();
    }

    /// <summary>
    /// Drill may run when it has a same-tile disposal trunk and free buffer space.
    /// Pipe direction is handled by real disposal travel after flush.
    /// </summary>
    public bool CanDrillOperate(EntityUid drill)
    {
        if (!HasComp<OrePipeInletComponent>(drill))
            return false;

        if (!TryGetTrunkOnTile(drill, out _))
            return false;

        if (!TryComp<OrePipeBufferComponent>(drill, out var buffer))
            return false;

        return buffer.TotalCount < buffer.MaxTotalCount;
    }

    public bool TryDepositOre(EntityUid uid, EntProtoId oreProto, int count, OrePipeBufferComponent? buffer = null)
    {
        if (count <= 0 || !Resolve(uid, ref buffer))
            return false;

        if (!_proto.HasIndex(oreProto))
            return false;

        var free = buffer.MaxTotalCount - buffer.TotalCount;
        if (free <= 0)
            return false;

        var toAdd = Math.Min(count, free);
        buffer.Contents.TryGetValue(oreProto, out var existing);
        buffer.Contents[oreProto] = existing + toAdd;
        Dirty(uid, buffer);
        return toAdd > 0;
    }

    /// <summary>
    /// Modules on the same tile as the hold, or sharing any disposal tube face with it.
    /// </summary>
    public int GetLinkedModuleExtraCapacity(EntityUid holdUid)
    {
        if (!TryGetGridTile(holdUid, out var gridUid, out var grid, out var tile))
            return 0;

        var extra = 0;
        var seen = new HashSet<EntityUid>();
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            foreach (var ent in _map.GetAnchoredEntities(gridUid, grid, tile + new Vector2i(dx, dy)))
            {
                if (!seen.Add(ent))
                    continue;

                if (TryComp<OreHoldModuleComponent>(ent, out var module))
                    extra += module.ExtraCapacity;
            }
        }

        return extra;
    }

    /// <summary>
    /// When a disposal holder arrives at a <see cref="DisposalEntryComponent"/> (trunk) that shares
    /// a tile with an ore hold / ore processor, deposit cargo straight into storage and delete the
    /// holder — no ExitDisposals eject/throw.
    /// </summary>
    /// <returns>True if the holder was fully handled (deleted).</returns>
    public bool TryDepositHolderAtTube(EntityUid tubeUid, EntityUid holderUid, DisposalHolderComponent holder)
    {
        if (!HasComp<DisposalEntryComponent>(tubeUid))
            return false;

        if (!TryGetGridTile(tubeUid, out var gridUid, out var grid, out var tile))
            return false;

        if (!TryFindTrunkAndReceiver(gridUid, grid, tile, out _, out var receiver))
            return false;

        var cargo = new List<EntityUid>(holder.Container.ContainedEntities);
        foreach (var ent in cargo)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            RemComp<BeingDisposedComponent>(ent);
            TryDepositEntity(receiver, ent);
        }

        // Still something left (hold full / not ore) — let ExitDisposals handle leftovers.
        if (holder.Container.ContainedEntities.Count > 0)
            return false;

        holder.IsExitingDisposals = true;

        if (TryComp(tubeUid, out DisposalTubeComponent? tube))
            _container.Remove(holderUid, tube.Contents, reparent: false, force: true);

        QueueDel(holderUid);
        return true;
    }

    /// <summary>
    /// Fallback for non-trunk exits: deposit into a same-tile hold/processor with a trunk.
    /// Successfully deposited entities are removed from <paramref name="entities"/>.
    /// </summary>
    public void TryAbsorbExitingEntities(EntityCoordinates coords, List<EntityUid> entities)
    {
        if (entities.Count == 0)
            return;

        if (coords.GetGridUid(EntityManager) is not { } gridUid
            || !TryComp(gridUid, out MapGridComponent? grid))
        {
            return;
        }

        var tile = _map.TileIndicesFor(gridUid, grid, coords);
        if (!TryFindTrunkAndReceiver(gridUid, grid, tile, out _, out var receiver))
            return;

        for (var i = entities.Count - 1; i >= 0; i--)
        {
            var ent = entities[i];
            if (TerminatingOrDeleted(ent))
            {
                entities.RemoveAt(i);
                continue;
            }

            if (TryDepositEntity(receiver, ent))
                entities.RemoveAt(i);
        }
    }

    private void FlushAllBuffersIntoTrunks()
    {
        var bufferQuery = EntityQueryEnumerator<OrePipeBufferComponent, OrePipeInletComponent>();
        while (bufferQuery.MoveNext(out var uid, out var buffer, out _))
        {
            if (buffer.Contents.Count == 0)
                continue;

            if (!TryGetTrunkOnTile(uid, out var trunk))
                continue;

            var budget = 40;
            if (TryComp<OrePipeOutletComponent>(uid, out var selfOutlet))
                budget = selfOutlet.MaxInsertPerFlush;

            FlushBufferIntoTrunk(uid, buffer, trunk, budget);
        }
    }

    private void FlushBufferIntoTrunk(
        EntityUid bufferUid,
        OrePipeBufferComponent buffer,
        EntityUid trunk,
        int budget)
    {
        if (budget <= 0 || !TryComp<DisposalEntryComponent>(trunk, out var entry))
            return;

        var keys = new List<EntProtoId>(buffer.Contents.Keys);
        var spawned = 0;
        var anyInjected = false;

        // One holder per ore prototype so ore filters can sort cleanly.
        foreach (var protoId in keys)
        {
            if (spawned >= budget)
                break;

            if (!buffer.Contents.TryGetValue(protoId, out var available) || available <= 0)
                continue;

            if (!_proto.TryIndex(protoId, out EntityPrototype? entProto))
                continue;

            var bound = 1;
            if (entProto.TryGetComponent(out StackComponent? stackTemplate, _factory))
                bound = _stack.GetMaxCount(stackTemplate);

            var toInsert = new List<EntityUid>();
            while (available > 0 && spawned < budget)
            {
                var chunk = Math.Min(bound, Math.Min(available, budget - spawned));
                var ent = Spawn(protoId, _xform.GetMoverCoordinates(trunk));
                if (TryComp<StackComponent>(ent, out var stack))
                    _stack.SetCount(ent, chunk, stack);

                toInsert.Add(ent);
                available -= chunk;
                spawned += chunk;
            }

            if (available > 0)
                buffer.Contents[protoId] = available;
            else
                buffer.Contents.Remove(protoId);

            if (toInsert.Count == 0)
                continue;

            if (TryInsertEntitiesIntoTrunk(trunk, entry, toInsert))
            {
                anyInjected = true;
                continue;
            }

            foreach (var ent in toInsert)
            {
                if (!TerminatingOrDeleted(ent))
                    _xform.AttachToGridOrMap(ent);
            }
        }

        if (anyInjected || buffer.Contents.Count != keys.Count)
            Dirty(bufferUid, buffer);
    }

    private bool TryInsertEntitiesIntoTrunk(
        EntityUid trunk,
        DisposalEntryComponent entry,
        List<EntityUid> entities)
    {
        var xform = Transform(trunk);
        var holder = Spawn(entry.HolderPrototypeId, _xform.GetMapCoordinates(trunk, xform: xform));
        var holderComp = Comp<DisposalHolderComponent>(holder);

        var inserted = 0;
        foreach (var ent in entities)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            if (_disposable.TryInsert(holder, ent, holderComp))
                inserted++;
        }

        if (inserted == 0)
        {
            QueueDel(holder);
            return false;
        }

        return _disposable.EnterTube(holder, trunk, holderComp);
    }

    private bool TryDepositEntity(EntityUid receiver, EntityUid ore)
    {
        if (TryComp<OreHoldComponent>(receiver, out var hold))
        {
            var proto = MetaData(ore).EntityPrototype?.ID;
            if (proto == null || !_proto.TryIndex<EntityPrototype>(proto, out _))
                return false;

            var count = 1;
            if (TryComp<StackComponent>(ore, out var stack))
                count = stack.Count;

            if (!_oreHold.TryDeposit(receiver, proto, count, hold))
                return false;

            QueueDel(ore);
            return true;
        }

        if (!TryComp<MaterialStorageComponent>(receiver, out var storage)
            || !HasComp<OrePipeOutletComponent>(receiver))
        {
            return false;
        }

        return TryAbsorbIntoMaterialStorage(receiver, storage, ore);
    }

    private bool TryAbsorbIntoMaterialStorage(EntityUid storageUid, MaterialStorageComponent storage, EntityUid ore)
    {
        var protoId = MetaData(ore).EntityPrototype?.ID;
        if (protoId == null || !_proto.TryIndex(protoId, out EntityPrototype? entProto))
            return false;

        if (!entProto.TryGetComponent(out PhysicalCompositionComponent? composition, _factory))
            return false;

        if (!PrototypePassesStorageWhitelist(storage.Whitelist, entProto))
            return false;

        _materialStorage.UpdateMaterialWhitelist(storageUid, storage);
        if (!CompositionAcceptedByStorage(storageUid, storage, composition))
            return false;

        var units = 1;
        if (TryComp<StackComponent>(ore, out var stack))
            units = Math.Max(1, stack.Count);

        foreach (var (mat, vol) in composition.MaterialComposition)
        {
            if (!_materialStorage.CanChangeMaterialAmount(storageUid, mat, vol * units, storage))
                return false;
        }

        foreach (var (mat, vol) in composition.MaterialComposition)
            _materialStorage.TryChangeMaterialAmount(storageUid, mat, vol * units, storage);

        QueueDel(ore);
        return true;
    }

    private bool CompositionAcceptedByStorage(
        EntityUid storageUid,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition)
    {
        if (storage.MaterialWhiteList is { Count: 0 })
            return false;

        foreach (var mat in composition.MaterialComposition.Keys)
        {
            if (!_materialStorage.IsMaterialWhitelisted((storageUid, storage), mat))
                return false;
        }

        return composition.MaterialComposition.Count > 0;
    }

    private bool PrototypePassesStorageWhitelist(EntityWhitelist? whitelist, EntityPrototype proto)
    {
        if (whitelist?.Tags is not { Count: > 0 })
            return true;

        if (!proto.TryGetComponent(out TagComponent? tags, _factory))
            return false;

        return whitelist.RequireAll
            ? _tag.HasAllTags(tags, whitelist.Tags)
            : _tag.HasAnyTag(tags, whitelist.Tags);
    }

    private bool TryFindTrunkAndReceiver(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        out EntityUid trunk,
        out EntityUid receiver)
    {
        trunk = default;
        receiver = default;
        EntityUid? foundTrunk = null;
        EntityUid? foundReceiver = null;

        foreach (var ent in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<DisposalEntryComponent>(ent))
                foundTrunk = ent;

            if (HasComp<OreHoldComponent>(ent)
                || (HasComp<OrePipeOutletComponent>(ent) && HasComp<MaterialStorageComponent>(ent)))
            {
                foundReceiver = ent;
            }
        }

        if (foundTrunk is not { } t || foundReceiver is not { } r)
            return false;

        trunk = t;
        receiver = r;
        return true;
    }

    public bool TryGetTrunkOnTile(EntityUid machine, out EntityUid trunk)
    {
        trunk = default;
        if (!TryGetGridTile(machine, out var gridUid, out var grid, out var tile))
            return false;

        foreach (var ent in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (!HasComp<DisposalEntryComponent>(ent))
                continue;

            trunk = ent;
            return true;
        }

        return false;
    }

    private bool TryGetGridTile(
        EntityUid uid,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tile)
    {
        gridUid = default;
        grid = default!;
        tile = default;

        var xform = Transform(uid);
        if (xform.GridUid is not { } gid || !TryComp(gid, out MapGridComponent? mapGrid) || !xform.Anchored)
            return false;

        gridUid = gid;
        grid = mapGrid;
        tile = _map.TileIndicesFor(gid, mapGrid, xform.Coordinates);
        return true;
    }
}
