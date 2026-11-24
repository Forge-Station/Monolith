using Content.Server._Mono.Radar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// A ship that carries a stockpile shows it. Every <see cref="Interval"/> the narcotics aboard each deeded ship are
/// added up by street value, and while a ship holds <c>wf.narcotics.stock_signature</c> or more, its largest lot shows
/// on every radar in reach (<c>WFNarcoticSignature</c>'s blip), the ship's own included.
/// </summary>
public sealed class WFNarcoticStockSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private WFNarcoticGoodsSystem _goods = default!;

    public static readonly EntProtoId Signature = "WFNarcoticSignature";
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<EntityUid, (double Total, EntityUid Lot, double LotValue)> _ships = new();
    private readonly HashSet<EntityUid> _shown = new();
    private readonly List<EntityUid> _stale = new();
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        Scan();
    }

    /// <summary>The street value of the narcotics aboard a ship as of the last scan.</summary>
    public double Stock(EntityUid ship)
    {
        return _ships.TryGetValue(ship, out var stock) ? stock.Total : 0;
    }

    /// <summary>Adds up the narcotics aboard each deeded ship and moves the signatures to match.</summary>
    public void Scan()
    {
        _ships.Clear();
        _shown.Clear();
        _stale.Clear();

        var threshold = _cfg.GetCVar(WFNarcoticsCVars.StockSignature);
        var query = EntityQueryEnumerator<WFNarcoticStockComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } ship || !HasComp<ShuttleDeedComponent>(ship))
                continue;

            var value = _goods.StreetValue(uid);
            if (value <= 0)
                continue;

            // The lot already showing keeps the signature, so it doesn't hop about the ship; else the largest takes it.
            var weight = HasComp<WFNarcoticSignatureComponent>(uid) ? double.MaxValue : value;
            var stock = _ships.GetValueOrDefault(ship);
            _ships[ship] = weight > stock.LotValue
                ? (stock.Total + value, uid, weight)
                : (stock.Total + value, stock.Lot, stock.LotValue);
        }

        if (threshold > 0)
        {
            foreach (var stock in _ships.Values)
            {
                if (stock.Total >= threshold)
                    _shown.Add(stock.Lot);
            }
        }

        var signatures = EntityQueryEnumerator<WFNarcoticSignatureComponent>();
        while (signatures.MoveNext(out var uid, out _))
        {
            if (!_shown.Remove(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            Hide(uid);
        }

        foreach (var uid in _shown)
        {
            Show(uid);
        }
    }

    private void Show(EntityUid uid)
    {
        var signature = EnsureComp<WFNarcoticSignatureComponent>(uid);
        if (HasComp<RadarBlipComponent>(uid)
            || !_prototype.TryIndex<EntityPrototype>(Signature, out var proto)
            || !proto.Components.TryGetValue("RadarBlip", out var blip))
            return;

        EntityManager.AddComponents(uid, new ComponentRegistry { { "RadarBlip", blip } }, removeExisting: false);
        signature.OwnBlip = true;
    }

    private void Hide(EntityUid uid)
    {
        if (TryComp<WFNarcoticSignatureComponent>(uid, out var signature) && signature.OwnBlip)
            RemComp<RadarBlipComponent>(uid);

        RemComp<WFNarcoticSignatureComponent>(uid);
    }
}
