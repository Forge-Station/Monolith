using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Stack;
using Content.Shared._Forge.CCVar;
using Content.Shared.Database;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.EntityTable;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Shuttles.Systems;

/// <summary>
/// Drives <see cref="PoiTreasuryComponent"/>: random rewards from a YAML pool,
/// fixed-position enforcement (snap-back, anti-pull), and access control —
/// anyone may open and view, but only the current capture leader can withdraw,
/// and players never insert directly (deposits come from the system only).
/// </summary>
public sealed class PoiTreasurySystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityTableSystem _entityTable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PoiTreasuryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PoiTreasuryComponent, StorageInteractUsingAttemptEvent>(OnInteractUsingAttempt);
        SubscribeLocalEvent<PoiTreasuryComponent, ContainerIsInsertingAttemptEvent>(OnContainerInsertAttempt);
        SubscribeLocalEvent<PoiTreasuryComponent, StartPullAttemptEvent>(OnStartPull);
        SubscribeLocalEvent<PoiTreasuryComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<GettingPickedUpAttemptEvent>(OnItemPickup);
    }

    private void OnMapInit(EntityUid uid, PoiTreasuryComponent comp, ref MapInitEvent args)
    {
        comp.HomePosition ??= Transform(uid).Coordinates;

        if (comp.NextRewardTime == TimeSpan.Zero)
            comp.NextRewardTime = _timing.CurTime + GetRewardInterval(comp);
    }

    private TimeSpan GetRewardInterval(PoiTreasuryComponent comp)
    {
        var minutes = comp.RewardIntervalMinutes
                      ?? _cfg.GetCVar(ForgeCVars.PoiCaptureRewardIntervalMinutes);
        return TimeSpan.FromMinutes(Math.Max(0.1f, minutes));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PoiTreasuryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var treasury, out var xform))
        {
            EnforceHomePosition(uid, treasury, xform);

            if (xform.GridUid is not { Valid: true } gridUid)
                continue;

            if (!IsGridOwned(gridUid))
                continue;

            if (now < treasury.NextRewardTime)
                continue;

            RollAndDepositReward(uid, treasury);
            treasury.NextRewardTime = now + GetRewardInterval(treasury);
        }
    }

    private bool IsGridOwned(EntityUid gridUid)
    {
        return TryComp<PoiCaptureComponent>(gridUid, out var capture)
               && capture.CurrentOwnerCompanyId != "None";
    }

    private void EnforceHomePosition(EntityUid uid, PoiTreasuryComponent treasury, TransformComponent xform)
    {
        if (treasury.HomePosition is not { } home)
            return;

        if (xform.Coordinates.Equals(home))
            return;

        // Allow movement if the parent (grid) itself moved; only fix relative drift.
        if (xform.ParentUid != home.EntityId)
        {
            _transform.SetCoordinates(uid, home);
            _adminLogger.Add(LogType.Action, LogImpact.High,
                $"POI treasury {ToPrettyString(uid)} snapped back to home (parent changed).");
            return;
        }

        var delta = (xform.Coordinates.Position - home.Position).LengthSquared();
        if (delta > 0.01f)
        {
            _transform.SetCoordinates(uid, home);
        }
    }

    private void OnInteractUsingAttempt(EntityUid uid, PoiTreasuryComponent comp, ref StorageInteractUsingAttemptEvent args)
    {
        // Players cannot click-insert items into the treasury; deposits come from the system.
        args.Cancelled = true;
    }

    private void OnContainerInsertAttempt(EntityUid uid, PoiTreasuryComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (_systemDepositDepth > 0)
            return;

        args.Cancel();
    }

    private void OnStartPull(EntityUid uid, PoiTreasuryComponent comp, StartPullAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnPullAttempt(EntityUid uid, PoiTreasuryComponent comp, PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnItemPickup(GettingPickedUpAttemptEvent args)
    {
        if (!_containers.TryGetContainingContainer(args.Item, out var container))
            return;

        if (!TryComp<PoiTreasuryComponent>(container.Owner, out _))
            return;

        if (IsLeaderOnGrid(container.Owner, args.User))
            return;

        _popup.PopupEntity(Loc.GetString("poi-treasury-withdraw-denied"), args.Item, args.User, PopupType.SmallCaution);
        args.Cancel();
    }

    private bool IsLeaderOnGrid(EntityUid treasury, EntityUid user)
    {
        if (Transform(treasury).GridUid is not { Valid: true } gridUid)
            return false;

        if (!TryComp<PoiCaptureComponent>(gridUid, out var capture))
            return false;

        if (capture.CaptureLeaderUserId is not { } leaderId)
            return false;

        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        return actor.PlayerSession.UserId == leaderId;
    }

    // Re-entrant counter so system-driven inserts pass the ContainerIsInsertingAttemptEvent guard.
    private int _systemDepositDepth;

    /// <summary>
    /// Rolls one random prototype from the treasury's pool and deposits the
    /// requested count. Skips silently if the pool is empty.
    /// </summary>
    public void RollAndDepositReward(EntityUid treasuryUid, PoiTreasuryComponent treasury)
    {
        List<EntProtoId> spawns;

        if (treasury.RewardTable != null)
        {
            spawns = _entityTable.GetSpawns(treasury.RewardTable).ToList();
        }
        else if (treasury.RewardPool.Count > 0)
        {
            var protoId = _random.Pick(treasury.RewardPool);
            if (!_proto.HasIndex<EntityPrototype>(protoId))
            {
                Log.Warning($"PoiTreasury {ToPrettyString(treasuryUid)}: reward proto '{protoId}' not found.");
                return;
            }

            var count = Math.Max(1, treasury.RewardCount);
            spawns = Enumerable.Repeat(protoId, count).ToList();
        }
        else
        {
            return;
        }

        if (spawns.Count == 0)
            return;

        // Table returned a single pick but YAML still uses rewardCount (e.g. mining ×2).
        if (treasury.RewardTable != null && spawns.Count == 1 && treasury.RewardCount > 1)
        {
            DepositSpawnBatch(treasuryUid, spawns[0], treasury.RewardCount);
            return;
        }

        foreach (var group in spawns.GroupBy(s => s))
            DepositSpawnBatch(treasuryUid, group.Key, group.Count());
    }

    private void DepositSpawnBatch(EntityUid treasuryUid, EntProtoId protoId, int count)
    {
        TrySpawnReward(treasuryUid, protoId, count);

        var ownerCompany = "None";
        if (Transform(treasuryUid).GridUid is { Valid: true } gridUid
            && TryComp<PoiCaptureComponent>(gridUid, out var capture))
            ownerCompany = capture.CurrentOwnerCompanyId;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"POI treasury {ToPrettyString(treasuryUid)} (owner {ownerCompany}) rolled reward: {count}x {protoId}");
    }

    /// <summary>
    /// Spawns <paramref name="count"/> entities of <paramref name="protoId"/> at the treasury
    /// and tries to insert them; anything that does not fit lands at the treasury's coordinates.
    /// </summary>
    public void TrySpawnReward(EntityUid treasuryUid, EntProtoId protoId, int count)
    {
        var coords = Transform(treasuryUid).Coordinates;

        if (_proto.TryIndex<EntityPrototype>(protoId, out var entProto)
            && entProto.HasComponent<StackComponent>(EntityManager.ComponentFactory))
        {
            var stacks = _stack.SpawnMultiple(protoId, count, coords);
            foreach (var stack in stacks)
                TryDeposit(treasuryUid, stack);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var item = Spawn(protoId, coords);
            TryDeposit(treasuryUid, item);
        }
    }

    /// <summary>
    /// Inserts an entity into the treasury's storage, bypassing the player insert guard.
    /// Returns false if the storage rejects the item; caller may leave it on the floor.
    /// </summary>
    public bool TryDeposit(EntityUid treasuryUid, EntityUid item)
    {
        if (!HasComp<PoiTreasuryComponent>(treasuryUid))
            return false;

        _systemDepositDepth++;
        try
        {
            return _storage.Insert(treasuryUid, item, out _, playSound: false);
        }
        finally
        {
            _systemDepositDepth--;
        }
    }

    /// <summary>
    /// Resolves the active treasury entity for <paramref name="gridUid"/>, if any.
    /// </summary>
    public bool TryGetTreasuryForGrid(EntityUid gridUid, out EntityUid treasuryUid, out PoiTreasuryComponent? treasury)
    {
        var query = AllEntityQuery<PoiTreasuryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            treasuryUid = uid;
            treasury = comp;
            return true;
        }

        treasuryUid = default;
        treasury = null;
        return false;
    }

    /// <summary>
    /// Convenience: deposit a stack of credits into the treasury for the given grid.
    /// Used by sales-tax hooks. No-op if there is no treasury or the POI is unowned.
    /// </summary>
    public bool TryDepositCash(EntityUid sellerEntity, int amount, string? cashType = null)
    {
        if (amount <= 0)
            return false;

        if (Transform(sellerEntity).GridUid is not { Valid: true } gridUid)
            return false;

        if (!IsGridOwned(gridUid))
            return false;

        if (!TryGetTreasuryForGrid(gridUid, out var treasuryUid, out _))
            return false;

        var stackProto = cashType ?? "Credit";

        if (!_proto.TryIndex<StackPrototype>(stackProto, out var stackPrototype))
            return false;

        var coords = Transform(treasuryUid).Coordinates;
        var stack = _stack.Spawn(amount, stackPrototype, coords);
        if (!TryDeposit(treasuryUid, stack))
            return true; // landed on the floor at the treasury, still counts.

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"POI treasury {ToPrettyString(treasuryUid)} received {amount} {stackProto} sales tax from {ToPrettyString(sellerEntity)}.");
        return true;
    }

    /// <summary>
    /// Hook called by the capture system on ownership changes, in case future visuals
    /// or per-treasury bookkeeping needs to react.
    /// </summary>
    public void OnOwnershipChanged(EntityUid gridUid)
    {
        // Nothing to do today; access checks are runtime.
    }
}
