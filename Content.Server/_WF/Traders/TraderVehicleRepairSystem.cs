using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Traders;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Database;

namespace Content.Server._WF.Traders;

/// <summary>
/// A mechanic's repair: the customer's vehicle parked nearby is quoted by how badly it is hurt and what it is worth,
/// and made whole once they pay.
/// </summary>
public sealed class TraderVehicleRepairSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TraderSystem _trader = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderVehicleRepairComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderVehicleRepairComponent, TraderConfirmedEvent>(OnConfirmed);
    }

    private void OnAction(Entity<TraderVehicleRepairComponent> ent, ref TraderActionEvent args)
    {
        // Any other service taking over means an older quote is dead.
        ent.Comp.QuotedVehicle = null;

        if (args.Action != TraderAction.RepairVehicle || args.Handled || !TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;
        var traderEnt = (ent.Owner, trader);

        if (FindVehicle(ent) is not { } vehicle)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-repair-no-vehicle"));
            return;
        }

        if (GetQuote(ent, vehicle) is not { } price)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-repair-fine", ("vehicle", Name(vehicle))));
            return;
        }

        ent.Comp.QuotedVehicle = vehicle;
        _trader.AskConfirmation(traderEnt, Loc.GetString("trader-repair-quote",
            ("vehicle", Name(vehicle)),
            ("cost", BankSystemExtensions.ToSpesoString(price))));
    }

    private void OnConfirmed(Entity<TraderVehicleRepairComponent> ent, ref TraderConfirmedEvent args)
    {
        if (ent.Comp.QuotedVehicle is not { } vehicle)
            return;

        ent.Comp.QuotedVehicle = null;
        if (!args.Accepted || !TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);

        // The vehicle can have driven off, or taken more damage, while the customer was thinking.
        if (TerminatingOrDeleted(vehicle) || !InRange(ent, vehicle))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-repair-no-vehicle"));
            return;
        }

        if (GetQuote(ent, vehicle) is not { } price)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-repair-fine", ("vehicle", Name(vehicle))));
            return;
        }

        if (!_trader.TryTakePayment(traderEnt, args.Customer, price))
            return;

        _hardpoints.RestoreIntegrity(vehicle);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Customer):actor} paid {price} to have {ToPrettyString(vehicle)} repaired by {ToPrettyString(ent)}");
        _trader.SayAndShow(traderEnt, Loc.GetString("trader-repair-done", ("vehicle", Name(vehicle))));
    }

    /// <summary>The nearest vehicle in reach that needs work, or the nearest at all when none does.</summary>
    public EntityUid? FindVehicle(Entity<TraderVehicleRepairComponent> ent)
    {
        var origin = _transform.GetMapCoordinates(ent);
        EntityUid? best = null, bestDamaged = null;
        var bestDistance = float.MaxValue;
        var bestDamagedDistance = float.MaxValue;

        var query = EntityQueryEnumerator<RMCVehicleComponent, HardpointIntegrityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapID != origin.MapId)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).Length();
            if (distance > ent.Comp.Range)
                continue;

            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }

            if (distance < bestDamagedDistance && _hardpoints.GetMissingIntegrity(uid, out _) > 0f)
            {
                bestDamaged = uid;
                bestDamagedDistance = distance;
            }
        }

        return bestDamaged ?? best;
    }

    /// <summary>What making the vehicle whole costs, or null when it needs nothing.</summary>
    public int? GetQuote(Entity<TraderVehicleRepairComponent> ent, EntityUid vehicle)
    {
        var missing = _hardpoints.GetMissingIntegrity(vehicle, out var max);
        if (missing <= 0f || max <= 0f)
            return null;

        var value = _pricing.GetPrice(vehicle, includeContents: false);
        var price = (int) Math.Ceiling(value * ent.Comp.ValueShare * (missing / max));
        return Math.Max(ent.Comp.MinimumPrice, price);
    }

    private bool InRange(Entity<TraderVehicleRepairComponent> ent, EntityUid vehicle)
    {
        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(vehicle);
        return here.MapId == there.MapId && (here.Position - there.Position).Length() <= ent.Comp.Range;
    }
}
