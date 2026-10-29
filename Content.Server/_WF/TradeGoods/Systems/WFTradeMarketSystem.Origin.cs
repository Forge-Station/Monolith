using Content.Server._WF.Traders;
using Content.Server.Shuttles.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.TradeGoods;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Stamps every trade crate with where it first appears on a market map. A chute refuses a crate stamped on its own
/// grid or within the haul distance of it. An unknown origin always counts as local.
/// </summary>
public sealed partial class WFTradeMarketSystem
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>Crates not yet stamped, checked every sweep.</summary>
    private readonly HashSet<EntityUid> _pending = new();

    private TimeSpan _nextSweep;

    private void InitializeOrigin()
    {
        SubscribeLocalEvent<WFTradeOriginComponent, ComponentStartup>(OnOriginStartup);
        SubscribeLocalEvent<WFTradeOriginComponent, MapInitEvent>(OnOriginMapInit);
        SubscribeLocalEvent<WFTradeOriginComponent, ComponentShutdown>(OnOriginShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
    }

    private void OnOriginStartup(Entity<WFTradeOriginComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Pending)
            _pending.Add(ent);
    }

    private void OnOriginMapInit(Entity<WFTradeOriginComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Pending)
            TryStamp(ent);
    }

    private void OnOriginShutdown(Entity<WFTradeOriginComponent> ent, ref ComponentShutdown args)
    {
        _pending.Remove(ent);
    }

    /// <summary>Re-stamps a jumping ship's crates where it arrives when they were pending or stamped on another map.</summary>
    private void OnFtlCompleted(ref FTLCompletedEvent ev)
    {
        if (!TryComp(ev.MapUid, out TransformComponent? mapXform) || !IsMarketMap(mapXform.MapID))
            return;

        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var origin, out var xform))
        {
            if (xform.GridUid == ev.Entity && (origin.Pending || origin.Point.MapId != xform.MapID))
                Stamp((uid, origin));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateVendors();
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        // Ships placed by teleport (shipyard docking, z-level moves) raise no FTL event; the sweep catches their crates.
        foreach (var uid in new List<EntityUid>(_pending))
        {
            if (TryComp<WFTradeOriginComponent>(uid, out var origin))
                TryStamp((uid, origin));
            else
                _pending.Remove(uid);
        }

        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out _, out var origin, out var xform))
        {
            if (xform.GridUid is { } grid && HasComp<ShuttleDeedComponent>(grid))
                origin.Carrier = grid;
        }
    }

    /// <summary>Stamps a pending crate if it is on a market map now.</summary>
    private void TryStamp(Entity<WFTradeOriginComponent> crate)
    {
        var xform = Transform(crate);
        if (!crate.Comp.Pending || xform.MapUid is not { } map || Paused(map) || !IsMarketMap(xform.MapID))
            return;

        Stamp(crate);
    }

    /// <summary>Records where a crate is now as its origin, and what its grid is docked to.</summary>
    public void Stamp(Entity<WFTradeOriginComponent> crate)
    {
        var grid = Transform(crate).GridUid;
        crate.Comp.Point = _transform.GetMapCoordinates(crate);
        crate.Comp.Grid = grid;
        crate.Comp.Docked = null;
        if (grid is { } own)
        {
            var docked = _cargo.GetCargoSet(own);
            docked.Remove(own);
            if (docked.Count > 0)
                crate.Comp.Docked = docked;
        }

        crate.Comp.Pending = false;
        _pending.Remove(crate);
    }

    /// <summary>Whether a crate was stamped on a chute's grid or on a ship docked to it.</summary>
    private static bool MadeAt(WFTradeOriginComponent origin, EntityUid? chuteGrid)
    {
        return chuteGrid is { } grid && (origin.Grid == grid || origin.Docked?.Contains(grid) == true);
    }

    /// <summary>
    /// Why a chute refuses a crate for its origin, or null if it was hauled far enough. A crate whose origin is unknown
    /// or on another map is stamped at the chute and refused. A sealed-only chute refuses a crate without a seal.
    /// </summary>
    public LocId? CheckHaul(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFTradeOriginComponent>(crate, out var origin))
            return null;

        if (origin.Run == null && IsSealedOnly(chute))
            return "wf-trade-refuse-unsealed";

        var chuteXform = Transform(chute);
        if (origin.Pending || origin.Point.MapId != chuteXform.MapID)
        {
            Stamp((crate, origin));
            return "wf-trade-refuse-unknown";
        }

        if (MadeAt(origin, chuteXform.GridUid))
            return "wf-trade-refuse-made-here";

        var distance = (origin.Point.Position - _transform.GetWorldPosition(chuteXform)).Length();
        return distance < Profile(chuteXform.GridUid).MinHaulDistance ? "wf-trade-refuse-too-close" : null;
    }

    /// <summary>Whether a chute takes only freight with a manifest seal, such as the Underground fence.</summary>
    public bool IsSealedOnly(EntityUid chute)
    {
        return MetaData(chute).EntityPrototype is { } proto
               && Profile(Transform(chute).GridUid).SealedOnlyChutes.Contains(proto.ID);
    }

    /// <summary>Whether a crate passed the haul test at a chute, for rewards that require a real haul.</summary>
    public bool WasHauled(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFTradeOriginComponent>(crate, out var origin) || origin.Pending)
            return false;

        var chuteXform = Transform(chute);
        return origin.Point.MapId == chuteXform.MapID
               && !MadeAt(origin, chuteXform.GridUid)
               && (origin.Point.Position - _transform.GetWorldPosition(chuteXform)).Length()
               >= Profile(chuteXform.GridUid).MinHaulDistance;
    }

    /// <summary>
    /// Whether a chute's haul test would pass a crate, without stamping it as <see cref="CheckHaul"/> does. A crate whose
    /// origin isn't known yet passes.
    /// </summary>
    public bool WouldPassHaul(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFTradeOriginComponent>(crate, out var origin))
            return true;

        if (origin.Run == null && IsSealedOnly(chute))
            return false;

        return origin.Pending || origin.Point.MapId != Transform(chute).MapID || WasHauled(chute, crate);
    }

    /// <summary>Whether a trader aboard the chute's grid refuses to deal with someone who hurt them.</summary>
    public bool Shunned(EntityUid chute, EntityUid user)
    {
        var grid = Transform(chute).GridUid;
        var query = EntityQueryEnumerator<TraderFleeComponent, TransformComponent>();
        while (query.MoveNext(out _, out var flee, out var xform))
        {
            if (xform.GridUid == grid && flee.Attackers.Contains(user))
                return true;
        }

        return false;
    }

    /// <summary>The haul distance players are told, in kilometres.</summary>
    public float HaulKilometres(EntityUid chute)
    {
        return Profile(Transform(chute).GridUid).MinHaulDistance / 1000f;
    }
}
