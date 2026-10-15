using System.Linq;
using System.Numerics;
using Content.Server._WF.Hailing;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._WF.Drydock;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Drydock operator: books a berth and its work for the caller's ship, then starts the work once it settles in the berth.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>Earliest time bookings are checked and booking windows pushed again.</summary>
    private TimeSpan _nextBookingCheck;

    /// <summary>A berth console with its berth.</summary>
    private readonly record struct BerthSlot(EntityUid Console, WFDrydockConsoleComponent Comp, WFDrydockBerth Berth);

    /// <summary>The berth a booking is priced at, the berth it would take, and the refusal notice.</summary>
    private readonly record struct Evaluation(BerthSlot? Pricing, BerthSlot? Chosen, string? Notice);

    private void InitializeBooking()
    {
        SubscribeLocalEvent<WFDrydockOperatorComponent, ComponentStartup>(OnOperatorStartup);
        SubscribeLocalEvent<WFDrydockOperatorComponent, HailServiceOpenedEvent>(OnOperatorServiceOpened);
        SubscribeLocalEvent<WFDrydockOperatorComponent, BoundUIClosedEvent>(OnOperatorClosed);
        SubscribeLocalEvent<WFDrydockOperatorComponent, WFDrydockRepairMessage>(OnOperatorRepair);
        SubscribeLocalEvent<WFDrydockOperatorComponent, WFDrydockUpgradeMessage>(OnOperatorUpgrade);
        SubscribeLocalEvent<WFDrydockOperatorComponent, WFDrydockCancelBookingMessage>(OnOperatorCancel);
        SubscribeLocalEvent<WFDrydockOperatorComponent, HailServiceGreetEvent>(OnOperatorHailAnswered);
    }

    /// <summary>Gives the operator the drydock window, so a hail relay can copy it onto the helm.</summary>
    private void OnOperatorStartup(EntityUid uid, WFDrydockOperatorComponent comp, ComponentStartup args)
    {
        if (!_ui.HasUi(uid, WFDrydockConsoleUiKey.Key))
            _ui.SetUi(uid, WFDrydockConsoleUiKey.Key, new InterfaceData("WFDrydockConsoleBoundUserInterface"));
    }

    private void OnOperatorServiceOpened(EntityUid uid, WFDrydockOperatorComponent comp, ref HailServiceOpenedEvent args)
    {
        comp.Viewers[args.Actor] = args.Helm;
        SendOperatorState(uid, ValidShip(args.Ship), args.Helm);
    }

    private void OnOperatorClosed(EntityUid uid, WFDrydockOperatorComponent comp, BoundUIClosedEvent args)
    {
        // The operator also has a trader window; only drydock viewers are dropped here.
        if (!Equals(args.UiKey, WFDrydockConsoleUiKey.Key))
            return;

        comp.Viewers.Remove(args.Actor);
    }

    private void OnOperatorRepair(EntityUid uid, WFDrydockOperatorComponent comp, WFDrydockRepairMessage args)
    {
        HandleBookingRequest(uid, comp, args.Actor, null);
    }

    private void OnOperatorUpgrade(EntityUid uid, WFDrydockOperatorComponent comp, WFDrydockUpgradeMessage args)
    {
        HandleBookingRequest(uid, comp, args.Actor, args.Upgrade);
    }

    private void OnOperatorCancel(EntityUid uid, WFDrydockOperatorComponent comp, WFDrydockCancelBookingMessage args)
    {
        var (ship, helm) = ResolveCaller(comp, args.Actor);
        if (ship is { } caller)
        {
            CancelBookings(caller);
            Tell(args.Actor, helm, Loc.GetString("drydock-booking-cancelled"));
        }

        SendOperatorState(uid, ship, helm);
    }

    /// <summary>Books a repair or refit for the caller's ship, then refreshes the caller's window.</summary>
    private void HandleBookingRequest(EntityUid uid, WFDrydockOperatorComponent comp, EntityUid actor, string? upgrade)
    {
        var (ship, helm) = ResolveCaller(comp, actor);
        if (ship is { } caller)
            Book(uid, comp, caller, helm, actor, upgrade);

        SendOperatorState(uid, ship, helm);
    }

    private void OnOperatorHailAnswered(EntityUid uid, WFDrydockOperatorComponent comp, ref HailServiceGreetEvent args)
    {
        if (ValidShip(args.Ship) is not { } ship)
        {
            args.Lines.Add(Loc.GetString("drydock-hail-offer"));
            return;
        }

        if (TryFindJob(ship, out var jobConsole) && TryComp<WFDrydockConsoleComponent>(jobConsole, out var jobComp) &&
            TryComp<WFDrydockJobComponent>(jobConsole, out var job))
        {
            var minutes = (int) Math.Max(1d, Math.Ceiling((job.EstimatedEnd - _timing.CurTime).TotalMinutes));
            args.Lines.Add(Loc.GetString("drydock-hail-working", ("berth", BerthLabel(jobConsole, jobComp)), ("minutes", minutes)));
            return;
        }

        if (TryFindBooking(ship, out var bookedConsole) && TryComp<WFDrydockConsoleComponent>(bookedConsole, out var bookedComp))
        {
            args.Lines.Add(Loc.GetString("drydock-hail-booked", ("berth", BerthLabel(bookedConsole, bookedComp))));
            return;
        }

        args.Lines.Add(Loc.GetString("drydock-hail-offer"));
    }

    /// <summary>Finds the caller's ship: the helm's grid for a relayed window.</summary>
    private (EntityUid? Ship, EntityUid? Helm) ResolveCaller(WFDrydockOperatorComponent comp, EntityUid actor)
    {
        if (!comp.Viewers.TryGetValue(actor, out var helm))
            return (null, null);

        if (IsViewing(helm))
            return (ValidShip(Transform(helm).GridUid), helm);

        comp.Viewers.Remove(actor);
        return (null, null);
    }

    /// <summary>Whether a helm still has the drydock window open.</summary>
    private bool IsViewing(EntityUid helm)
    {
        return !TerminatingOrDeleted(helm) && _ui.IsUiOpen(helm, WFDrydockConsoleUiKey.Key);
    }

    /// <summary>Returns the ship if it is a shuttle grid, else null.</summary>
    private EntityUid? ValidShip(EntityUid? ship)
    {
        if (ship is not { } uid || TerminatingOrDeleted(uid))
            return null;

        return HasComp<ShuttleComponent>(uid) && HasComp<MapGridComponent>(uid) ? uid : null;
    }

    /// <summary>The ship's footprint as (short, long) in tiles, or zero when it has no grid.</summary>
    private Vector2 ShipSize(EntityUid ship)
    {
        return TryComp<MapGridComponent>(ship, out var grid)
            ? SortedSize(grid.LocalAABB.Width, grid.LocalAABB.Height)
            : Vector2.Zero;
    }

    /// <summary>Lists every berth console on the operator's grid that has cranes to work from.</summary>
    private List<BerthSlot> GetBerths(EntityUid op)
    {
        var berths = new List<BerthSlot>();
        var grid = Transform(op).GridUid;
        if (grid == null)
            return berths;

        var query = EntityQueryEnumerator<WFDrydockConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var console, out var comp, out var xform))
        {
            if (xform.GridUid != grid || !TryGetBerth(console, comp, out var berth))
                continue;

            berths.Add(new BerthSlot(console, comp, berth));
        }

        return berths;
    }

    /// <summary>Berths whose bay fits the ship, smallest first, then nearest.</summary>
    private List<BerthSlot> FittingBerths(EntityUid ship, List<BerthSlot> berths)
    {
        var size = ShipSize(ship);
        var at = _transform.GetWorldPosition(ship);
        return berths
            .Where(b => SortedSize(b.Berth.Area.Width, b.Berth.Area.Height) is var bay && size.X <= bay.X && size.Y <= bay.Y)
            .OrderBy(b => b.Berth.Area.Width * b.Berth.Area.Height)
            .ThenBy(b => (Vector2.Transform(b.Berth.Area.Center, _transform.GetWorldMatrix(b.Berth.Grid)) - at).LengthSquared())
            .ToList();
    }

    /// <summary>
    /// Picks the berth a ship is booked into: a free fitting berth it already sits in, else the first free fitting one.
    /// A berth is free when nothing works in it, nothing else is booked there, and no other ship sits in it.
    /// </summary>
    private BerthSlot? PickFreeBerth(EntityUid ship, List<BerthSlot> fitting)
    {
        BerthSlot? free = null;
        foreach (var slot in fitting)
        {
            if (HasComp<WFDrydockJobComponent>(slot.Console))
                continue;

            if (TryComp<WFDrydockBookingComponent>(slot.Console, out var booking) && booking.Ship != ship)
                continue;

            var berthed = GetBerthedShip(slot.Console, slot.Comp);
            if (berthed == ship)
                return slot;

            if (berthed == null)
                free ??= slot;
        }

        return free;
    }

    /// <summary>Chooses the pricing berth, the berth a booking would take, and the notice explaining a refusal.</summary>
    private Evaluation Evaluate(EntityUid? ship, List<BerthSlot> berths)
    {
        if (berths.Count == 0)
            return new(null, null, Loc.GetString("drydock-booking-full"));

        if (ship is { } owned && berths.Any(b => b.Berth.Grid == owned))
            return new(berths[0], null, Loc.GetString("drydock-console-own-grid"));

        if (ship is not { } caller)
            return new(berths[0], null, Loc.GetString("drydock-booking-no-ship"));

        var fitting = FittingBerths(caller, berths);
        if (fitting.Count == 0)
            return new(berths[0], null, Loc.GetString("drydock-booking-too-big"));

        if (PickFreeBerth(caller, fitting) is not { } chosen)
            return new(fitting[0], null, Loc.GetString("drydock-booking-full"));

        return new(chosen, chosen, null);
    }

    /// <summary>Builds the booking window state for a caller's ship, with its booking and any running job.</summary>
    private WFDrydockConsoleState BuildOperatorState(EntityUid op, EntityUid? ship)
    {
        var eval = Evaluate(ship, GetBerths(op));
        if (eval.Pricing is not { } slot)
            return new WFDrydockConsoleState { Booking = true, Notice = eval.Notice };

        // Prefer the berth working on or booked for the ship over the one a booking would take.
        var console = slot.Console;
        var comp = slot.Comp;
        WFDrydockJobComponent? job = null;
        if (ship is { } caller &&
            (TryFindJob(caller, out var shown) || TryFindBooking(caller, out shown)) &&
            TryComp<WFDrydockConsoleComponent>(shown, out var shownComp))
        {
            console = shown;
            comp = shownComp;
            job = CompOrNull<WFDrydockJobComponent>(shown);
        }

        var state = BuildState(console, ship, comp, job);
        state.Booking = true;
        state.Notice = eval.Notice;

        if (ship is { } booked && TryFindBooking(booked, out var bookedConsole) &&
            TryComp<WFDrydockBookingComponent>(bookedConsole, out var booking) &&
            TryComp<WFDrydockConsoleComponent>(bookedConsole, out var bookedComp))
        {
            state.BookedBerth = BerthLabel(bookedConsole, bookedComp);
            state.BookingSeconds = (float) Math.Max(0d, (booking.Expires - _timing.CurTime).TotalSeconds);
        }

        return state;
    }

    /// <summary>Sends the booking state to the helm's window when relayed, else to the operator window.</summary>
    private void SendOperatorState(EntityUid op, EntityUid? ship, EntityUid? helm)
    {
        _ui.SetUiState(helm ?? op, WFDrydockConsoleUiKey.Key, BuildOperatorState(op, ship));
    }

    /// <summary>Books the work on the berth the ship would take, if it can pay and the work is still due.</summary>
    public void Book(EntityUid op, WFDrydockOperatorComponent comp, EntityUid ship, EntityUid? helm, EntityUid actor, string? upgrade)
    {
        var eval = Evaluate(ship, GetBerths(op));
        if (eval.Chosen is not { } slot)
        {
            Tell(actor, helm, eval.Notice ?? Loc.GetString("drydock-booking-full"));
            return;
        }

        if (TryFindJob(ship, out _))
        {
            Tell(actor, helm, Loc.GetString("drydock-console-busy"));
            return;
        }

        if (BuildWork(ship, slot.Comp, upgrade, out var items) is { } denial)
        {
            Tell(actor, helm, denial);
            return;
        }

        var price = WorkPrice(ship, slot.Comp, upgrade, items);
        if (!TryComp<BankAccountComponent>(actor, out var bank))
        {
            Tell(actor, helm, Loc.GetString("drydock-console-no-bank-account"));
            return;
        }

        if (bank.Balance < price)
        {
            Tell(actor, helm, Loc.GetString("drydock-console-insufficient-funds", ("price", price.ToString("N0"))));
            return;
        }

        CancelBookings(ship);

        var booking = EnsureComp<WFDrydockBookingComponent>(slot.Console);
        booking.Ship = ship;
        booking.Payer = actor;
        booking.Operator = op;
        booking.Helm = helm;
        booking.Upgrade = upgrade;
        booking.Expires = _timing.CurTime + TimeSpan.FromSeconds(comp.BookingTime);
        booking.StillSince = null;

        var guide = EnsureComp<WFDrydockGuideComponent>(ship);
        FillGuide(guide, slot.Berth, slot.Comp.ApproachLength, ShipFrame(ship, slot.Berth.Area, comp.FrameMargin));
        Dirty(ship, guide);

        Tell(actor, helm, Loc.GetString("drydock-booking-head-to", ("berth", slot.Berth.Label), ("ship", Name(ship))));
    }

    /// <summary>The parking frame for a ship: its size plus margin, turned to the bay's long side and clamped to the bay.</summary>
    private Vector2 ShipFrame(EntityUid ship, Box2 area, float margin)
    {
        var shipSize = ShipSize(ship);
        var frameLong = shipSize.Y + 2 * margin;
        var frameShort = shipSize.X + 2 * margin;
        var frame = area.Width >= area.Height ? new Vector2(frameLong, frameShort) : new Vector2(frameShort, frameLong);
        return Vector2.Min(frame, new Vector2(area.Width, area.Height));
    }

    /// <summary>Lays the parking frame and approach lights for a ship in a berth, in the station's local space.</summary>
    private static void FillGuide(WFDrydockGuideComponent guide, WFDrydockBerth berth, float approachLength, Vector2 frame)
    {
        var area = berth.Area;
        var approach = berth.Approach.LengthSquared() > 0f ? Vector2.Normalize(berth.Approach) : new Vector2(0, -1);
        var center = area.Center;
        var frameExtent = Math.Abs(approach.X) * frame.X + Math.Abs(approach.Y) * frame.Y;
        var areaExtent = Math.Abs(approach.X) * area.Width + Math.Abs(approach.Y) * area.Height;

        guide.Station = berth.Grid;
        guide.Center = center;
        guide.Size = frame;
        guide.PathEnd = center + approach * (frameExtent / 2f);
        guide.PathStart = center + approach * (areaExtent / 2f + approachLength);
    }

    /// <summary>Removes every booking held for a ship, and its guide.</summary>
    private void CancelBookings(EntityUid ship)
    {
        foreach (var (console, booking) in ListBookings())
        {
            if (booking.Ship == ship)
                RemoveBooking(console, booking);
        }

        RemComp<WFDrydockGuideComponent>(ship);
    }

    /// <summary>Drops a booking from its berth console, and the guide from its ship.</summary>
    private void RemoveBooking(EntityUid console, WFDrydockBookingComponent booking)
    {
        RemComp<WFDrydockBookingComponent>(console);
        if (!TerminatingOrDeleted(booking.Ship))
            RemComp<WFDrydockGuideComponent>(booking.Ship);
    }

    /// <summary>Snapshot of every booking and its berth console, so bookings can be removed while iterating.</summary>
    private List<(EntityUid Console, WFDrydockBookingComponent Booking)> ListBookings()
    {
        var bookings = new List<(EntityUid Console, WFDrydockBookingComponent Booking)>();
        var query = EntityQueryEnumerator<WFDrydockBookingComponent>();
        while (query.MoveNext(out var console, out var booking))
        {
            bookings.Add((console, booking));
        }

        return bookings;
    }

    /// <summary>Finds the berth console whose job is working on a ship.</summary>
    private bool TryFindJob(EntityUid ship, out EntityUid console)
    {
        var query = EntityQueryEnumerator<WFDrydockJobComponent>();
        while (query.MoveNext(out console, out var job))
        {
            if (job.Ship == ship)
                return true;
        }

        console = EntityUid.Invalid;
        return false;
    }

    /// <summary>Finds the berth console holding a booking for a ship.</summary>
    private bool TryFindBooking(EntityUid ship, out EntityUid console)
    {
        var query = EntityQueryEnumerator<WFDrydockBookingComponent>();
        while (query.MoveNext(out console, out var booking))
        {
            if (booking.Ship == ship)
                return true;
        }

        console = EntityUid.Invalid;
        return false;
    }

    /// <summary>Shows a message to the actor at the cursor, and speaks it on the helm's call when relayed.</summary>
    private void Tell(EntityUid actor, EntityUid? helm, string message)
    {
        _popup.PopupCursor(message, actor);
        SayToHelm(helm, message);
    }

    /// <summary>Speaks a message on the helm's call, if the caller is relayed through a live helm.</summary>
    private void SayToHelm(EntityUid? helm, string message)
    {
        if (helm is { } relay && !TerminatingOrDeleted(relay))
            _hailing.SayToCaller(relay, message);
    }

    /// <summary>Runs bookings: lapses expired ones, and starts the work once the ship has settled in its berth.</summary>
    private void UpdateBookings(TimeSpan curTime)
    {
        if (curTime < _nextBookingCheck)
            return;

        _nextBookingCheck = curTime + BerthCheckInterval;

        foreach (var (console, booking) in ListBookings())
        {
            if (!TryComp<WFDrydockConsoleComponent>(console, out var comp) || TerminatingOrDeleted(booking.Ship) || TerminatingOrDeleted(booking.Payer))
            {
                RemoveBooking(console, booking);
                continue;
            }

            // A lapsed booking, or one whose operator is gone or dead, tells the payer like an expiry.
            if (curTime >= booking.Expires || !OperatorAlive(booking.Operator))
            {
                RemoveBooking(console, booking);
                Tell(booking.Payer, booking.Helm, Loc.GetString("drydock-booking-expired", ("berth", BerthLabel(console, comp))));
                continue;
            }

            // A walk-in job on this console replaces the booking.
            if (HasComp<WFDrydockJobComponent>(console))
            {
                RemoveBooking(console, booking);
                continue;
            }

            // A booked ship has to come all the way in, not just reach into the berth.
            var settled = GetBerthedShip(console, comp) == booking.Ship && CentredInBerth(console, comp, booking.Ship) &&
                          IsHeldStill(console, comp, booking.Ship);
            if (!settled)
            {
                booking.StillSince = null;
                continue;
            }

            booking.StillSince ??= curTime;
            var settleTime = TimeSpan.FromSeconds(Comp<WFDrydockOperatorComponent>(booking.Operator).SettleTime);
            if (curTime - booking.StillSince.Value < settleTime)
                continue;

            var ship = booking.Ship;
            var payer = booking.Payer;
            var helm = booking.Helm;
            var upgrade = booking.Upgrade;
            RemoveBooking(console, booking);

            if (TryStartWork(console, comp, ship, payer, upgrade, booked: true))
                SayToHelm(helm, Loc.GetString("drydock-booking-starting", ("berth", BerthLabel(console, comp)), ("ship", Name(ship))));
        }

        PushOperators();
    }

    /// <summary>True while the operator still exists, is a drydock operator, and is alive if it can die.</summary>
    private bool OperatorAlive(EntityUid op)
    {
        return !TerminatingOrDeleted(op) && HasComp<WFDrydockOperatorComponent>(op) &&
               (!TryComp<MobStateComponent>(op, out var mob) || _mobState.IsAlive(op, mob));
    }

    /// <summary>Refreshes every booking window, pruning viewers whose helm window has closed.</summary>
    private void PushOperators()
    {
        var query = EntityQueryEnumerator<WFDrydockOperatorComponent>();
        while (query.MoveNext(out var op, out var comp))
        {
            foreach (var (actor, helm) in comp.Viewers.ToList())
            {
                if (!IsViewing(helm))
                    comp.Viewers.Remove(actor);
                else
                    SendOperatorState(op, ValidShip(Transform(helm).GridUid), helm);
            }
        }
    }

    /// <summary>Lists the work for a repair (null upgrade) or one refit tier, or the reason it can't be sold.</summary>
    private string? BuildWork(EntityUid ship, WFDrydockConsoleComponent comp, string? upgrade, out List<WFDrydockWorkItem> items)
    {
        if (upgrade == null)
        {
            items = BuildRepairItems(ship, comp);
            return items.Count == 0 ? Loc.GetString("drydock-console-nothing-to-repair") : null;
        }

        return BuildRefitWork(ship, comp, upgrade, out items);
    }

    /// <summary>Price of a repair (with its fee) or a refit.</summary>
    private int WorkPrice(EntityUid ship, WFDrydockConsoleComponent comp, string? upgrade, List<WFDrydockWorkItem> items)
    {
        var cost = items.Sum(i => i.Cost);
        return upgrade == null ? RepairFee(ship, comp) + cost : cost;
    }

    /// <summary>Orders two extents as (short, long).</summary>
    private static Vector2 SortedSize(float a, float b)
    {
        return a <= b ? new Vector2(a, b) : new Vector2(b, a);
    }
}
