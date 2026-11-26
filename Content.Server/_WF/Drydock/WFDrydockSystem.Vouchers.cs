using Content.Server.Chat.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;
using Content.Shared._WF.Drydock;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IChatManager _chat = default!;

    private const string VoucherSound = "/Audio/Machines/chime.ogg";

    private void InitializeVouchers()
    {
        SubscribeLocalEvent<WFDrydockRepairVoucherComponent, ExaminedEvent>(OnVoucherExamined);
    }

    private void OnVoucherExamined(Entity<WFDrydockRepairVoucherComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.ShipName is { } ship)
            args.PushMarkup(Loc.GetString("drydock-voucher-examine-ship", ("ship", ship)));
    }

    /// <summary>
    /// Gives one voucher to each player ship of <paramref name="mobs"/>, bound to that ship. A mob's ship is the deeded
    /// ship their player owns, or else the one they are aboard. The ship's owner holds it if among them, else the first
    /// of them. A ship that still holds an unused voucher gets none. Returns how many.
    /// </summary>
    public int IssueShipVouchers(EntProtoId? voucher, IEnumerable<EntityUid> mobs)
    {
        if (voucher == null)
            return 0;

        var holders = new Dictionary<EntityUid, EntityUid>();
        foreach (var mob in mobs)
        {
            if (TerminatingOrDeleted(mob) || VoucherShip(mob) is not { } ship)
                continue;

            if (!holders.ContainsKey(ship) || OwnsShip(mob, ship))
                holders[ship] = mob;
        }

        var issued = 0;
        foreach (var (ship, holder) in holders)
        {
            if (HasVoucher(ship))
                continue;

            var item = Spawn(voucher.Value, Transform(holder).Coordinates);
            Bind(item, ship);
            _hands.PickupOrDrop(holder, item);
            AnnounceVoucher(ship, holder);
            issued++;
        }

        return issued;
    }

    /// <summary>
    /// The deeded ship a mob is aboard if their player owns it, else their only other owned deeded ship, else the deeded
    /// ship they are aboard. A player with several ships elsewhere gets the aboard ship, so the pick never rests on
    /// query order.
    /// </summary>
    private EntityUid? VoucherShip(EntityUid mob)
    {
        var aboard = Transform(mob).GridUid is { } grid && HasComp<ShuttleDeedComponent>(grid) ? grid : (EntityUid?) null;
        if (aboard is { } here && OwnsShip(mob, here) || !TryComp<ActorComponent>(mob, out var actor))
            return aboard;

        EntityUid? owned = null;
        var query = EntityQueryEnumerator<ShipOwnershipComponent, ShuttleDeedComponent>();
        while (query.MoveNext(out var ship, out var ownership, out _))
        {
            if (ownership.OwnerUserId != actor.PlayerSession.UserId)
                continue;

            if (owned != null)
                return aboard;

            owned = ship;
        }

        return owned ?? aboard;
    }

    private bool OwnsShip(EntityUid mob, EntityUid ship)
    {
        return TryComp<ActorComponent>(mob, out var actor) && TryComp<ShipOwnershipComponent>(ship, out var ownership)
            && ownership.OwnerUserId == actor.PlayerSession.UserId;
    }

    /// <summary>Tells everyone aboard a ship, loudly, that it earned a free repair and who holds the voucher.</summary>
    private void AnnounceVoucher(EntityUid ship, EntityUid holder)
    {
        var shipName = Name(ship);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid != ship)
                continue;

            var message = uid == holder
                ? Loc.GetString("drydock-voucher-issued-holder", ("ship", shipName))
                : Loc.GetString("drydock-voucher-issued-crew", ("ship", shipName), ("holder", Identity.Name(holder, EntityManager)));
            _popup.PopupEntity(Loc.GetString("drydock-voucher-issued-popup"), uid, uid, PopupType.Large);
            _chat.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, false, actor.PlayerSession.Channel,
                Color.Gold, audioPath: VoucherSound, audioVolume: 2f);
        }

        // An owner away from their ship still holds its voucher, and may have had it dropped at their feet.
        if (Transform(holder).GridUid != ship && TryComp<ActorComponent>(holder, out var away))
        {
            var message = Loc.GetString("drydock-voucher-issued-holder", ("ship", shipName));
            _popup.PopupEntity(Loc.GetString("drydock-voucher-issued-popup"), holder, holder, PopupType.Large);
            _chat.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, false, away.PlayerSession.Channel,
                Color.Gold, audioPath: VoucherSound, audioVolume: 2f);
        }
    }

    /// <summary>Whether an unused voucher is bound to a ship.</summary>
    public bool HasVoucher(EntityUid ship)
    {
        var query = EntityQueryEnumerator<WFDrydockRepairVoucherComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Ship == ship && !TerminatingOrDeleted(uid))
                return true;
        }

        return false;
    }

    private void Bind(EntityUid voucher, EntityUid ship)
    {
        var comp = EnsureComp<WFDrydockRepairVoucherComponent>(voucher);
        comp.Ship = ship;
        comp.ShipName = Name(ship);
    }

    /// <summary>A voucher in the actor's hands or worn slots that repairs this ship, if any.</summary>
    private EntityUid? FindRepairVoucher(EntityUid actor, EntityUid ship)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(actor))
        {
            if (TryComp<WFDrydockRepairVoucherComponent>(item, out var comp) && (comp.Ship == null || comp.Ship == ship))
                return item;
        }

        return null;
    }

    /// <summary>Starts a repair paid by the actor's voucher for this ship, which is used up; false if the actor has none.</summary>
    private bool TryStartVoucherRepair(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor,
        List<WFDrydockWorkItem> items)
    {
        if (FindRepairVoucher(actor, ship) is not { } voucher)
            return false;

        var returned = Comp<WFDrydockRepairVoucherComponent>(voucher).Returned?.Id ?? MetaData(voucher).EntityPrototype?.ID;
        QueueDel(voucher);
        StartJob(console, comp, ship, actor, items, 0, voucher: returned ?? string.Empty);
        return true;
    }

    /// <summary>Hands an aborted voucher job's voucher back to its payer, bound to the ship it was for.</summary>
    private void ReturnVoucher(WFDrydockJobComponent job)
    {
        if (job.Voucher is not { Length: > 0 } voucher || job.Payer is not { } payer || TerminatingOrDeleted(payer))
            return;

        var item = Spawn(voucher, Transform(payer).Coordinates);
        if (!TerminatingOrDeleted(job.Ship))
            Bind(item, job.Ship);
        _hands.PickupOrDrop(payer, item);
    }
}
