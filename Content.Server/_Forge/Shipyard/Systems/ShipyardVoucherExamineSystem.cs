using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Shipyard.Systems;

/// <summary>
/// Examine text for shipyard vouchers: redemptions, reclaim hint, and sector vessel limits.
/// </summary>
public sealed class ShipyardVoucherExamineSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ShipyardVesselLimitSystem _vesselLimit = default!;
    [Dependency] private ShipyardVoucherReclaimSystem _reclaim = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipyardVoucherComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, ShipyardVoucherComponent voucher, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        PushRedemptionInfo(voucher, args);
        PushReclaimInfo(uid, voucher, args);
        PushSectorLimitInfo(voucher, args);
    }

    private void PushRedemptionInfo(ShipyardVoucherComponent voucher, ExaminedEvent args)
    {
        if (voucher.DestroyOnEmpty)
            args.PushMarkup(Loc.GetString("voucher-current-redemptions", ("count", voucher.RedemptionsLeft)));
        else
            args.PushMarkup(Loc.GetString("voucher-infinite-redemptions"));

        var remainingTime = voucher.NextBuyAt - _timing.CurTime;

        if (remainingTime >= TimeSpan.FromSeconds(60))
            args.PushMarkup(Loc.GetString("voucher-current-cooldown-minutes", ("cooldown", remainingTime.TotalMinutes)));
        else if (remainingTime >= TimeSpan.FromSeconds(0))
            args.PushMarkup(Loc.GetString("voucher-current-cooldown-seconds", ("cooldown", remainingTime.TotalSeconds)));
    }

    private void PushReclaimInfo(EntityUid uid, ShipyardVoucherComponent voucher, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("shipyard-voucher-examine-recyclable"));

        if (_reclaim.QualifiesForFullMaterialReclaim(uid, voucher))
            args.PushMarkup(Loc.GetString("shipyard-voucher-examine-reclaimable"));
        else
            args.PushMarkup(Loc.GetString("shipyard-voucher-examine-reclaim-empty"));
    }

    private void PushSectorLimitInfo(ShipyardVoucherComponent voucher, ExaminedEvent args)
    {
        if (voucher.Vessels.Count == 0)
            return;

        foreach (var vesselId in voucher.Vessels)
        {
            if (!_prototypes.TryIndex(vesselId, out VesselPrototype? vessel))
                continue;

            var limit = _vesselLimit.GetActiveLimit(vessel);

            if (limit <= 0)
            {
                args.PushMarkup(Loc.GetString("shipyard-voucher-sector-limit-unlimited",
                    ("vessel", vessel.Name)));
                continue;
            }

            var remaining = _vesselLimit.GetRemainingSlots(vessel);

            if (remaining <= 0)
            {
                args.PushMarkup(Loc.GetString("shipyard-voucher-sector-limit-full",
                    ("vessel", vessel.Name),
                    ("limit", limit)));
            }
            else
            {
                args.PushMarkup(Loc.GetString("shipyard-voucher-sector-limit-remaining",
                    ("vessel", vessel.Name),
                    ("remaining", remaining),
                    ("limit", limit)));
            }
        }
    }
}
