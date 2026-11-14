using System.Linq;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.ArmorSpecialization;

/// <summary>
/// Ties a Wolfmed tourniquet onto a covered limb with an untreated arterial bleed, for a worn
/// <see cref="WFAutoTourniquetComponent"/> suit, spending a charge per strap and earning charges back over time.
/// </summary>
public sealed class WFAutoTourniquetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private TourniquetSystem _tourniquets = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAutoTourniquetComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WFAutoTourniquetComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-spec-tourniquet-examine",
            ("charges", ent.Comp.Charges), ("max", ent.Comp.MaxCharges)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;
        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WFAutoTourniquetComponent>();
        while (query.MoveNext(out var uid, out var tourniquet))
        {
            Recharge((uid, tourniquet), now);

            // Worn in a clothing slot only; a suit carried in hand does nothing.
            if (tourniquet.Charges <= 0 || !_inventory.TryGetContainingSlot(uid, out _) ||
                !_inventory.TryGetContainingEntity(uid, out var wearer))
                continue;

            foreach (var (part, _) in _body.GetBodyChildren(wearer.Value).ToList())
            {
                if (tourniquet.Charges <= 0)
                    break;

                if (!WolfmedArmorCoverage.Covers(EntityManager, uid, part) || !HasUntreatedArtery(part))
                    continue;

                // A real Wolfmed tourniquet: limbs only, ties off the parts below, and starts the necrosis clock.
                if (!_tourniquets.Apply(wearer.Value, part))
                    continue;

                tourniquet.Charges--;
                if (tourniquet.NextCharge <= now)
                    tourniquet.NextCharge = now + tourniquet.RechargeTime;
                Dirty(uid, tourniquet);
                _popup.PopupEntity(Loc.GetString("wf-armor-spec-tourniquet-popup"), wearer.Value, wearer.Value,
                    PopupType.Medium);
            }
        }
    }

    /// <summary>An untreated, still-pumping artery on this part that a tourniquet can tie off.</summary>
    private bool HasUntreatedArtery(EntityUid part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp<WoundBleedingComponent>(wound, out var bleeding) &&
                bleeding.Treatment == BleedingTreatment.None && bleeding.BleedingSeverity > FixedPoint2.Zero &&
                _traits.TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior _) &&
                _traits.CanTourniquet(wound.AsNullable(), part))
                return true;
        }

        return false;
    }

    private void Recharge(Entity<WFAutoTourniquetComponent> ent, TimeSpan now)
    {
        if (ent.Comp.Charges >= ent.Comp.MaxCharges || now < ent.Comp.NextCharge)
            return;

        ent.Comp.Charges++;
        ent.Comp.NextCharge = ent.Comp.Charges < ent.Comp.MaxCharges ? now + ent.Comp.RechargeTime : TimeSpan.Zero;
        Dirty(ent);
    }
}
