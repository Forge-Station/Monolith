using Content.Server.Administration.Logs;
using Content.Server.Storage.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// Owns every edit to <see cref="WFShipAccessComponent"/>: sets a ship up at purchase, keeps the airlock access
/// reader of each door and locker on the grid in step with the lock, list and rules, and applies the console's
/// access tab and verbs. The allow list holds the record keys of ID cards; ownership is the deed, on a card or a
/// voucher, or for a ship an admin tool spawned, the players it is registered to.
/// </summary>
public sealed partial class WFShipAccessServerSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnShipPurchased);
        SubscribeLocalEvent<DoorComponent, EntParentChangedMessage>(OnDoorParentChanged);
        SubscribeLocalEvent<DoorComponent, AnchorStateChangedEvent>(OnDoorAnchorChanged);
        SubscribeLocalEvent<EntityStorageComponent, EntParentChangedMessage>(OnStorageParentChanged);
        SubscribeLocalEvent<EntityStorageComponent, AnchorStateChangedEvent>(OnStorageAnchorChanged);
        InitializeConsole();
        InitializeDoors();
        InitializeCodes();
        InitializeStations();
    }

    private void OnShipPurchased(ShipyardShuttlePurchaseEvent args)
    {
        SetupShip(args.Shuttle, args.Purchaser);
    }

    /// <summary>
    /// Sets a bought ship up: the buyer's name for display, the mode from the grid's company and the lock from
    /// the cvar. Ownership itself is the deed card the shipyard hands out.
    /// </summary>
    public Entity<WFShipAccessComponent> SetupShip(EntityUid grid, EntityUid? purchaser)
    {
        var comp = EnsureComp<WFShipAccessComponent>(grid);
        var ship = new Entity<WFShipAccessComponent>(grid, comp);

        if (purchaser is { } buyer)
        {
            comp.OwnerName = Name(buyer);

            // A deed on a voucher, or on a card without a crew record, can't key a door; the buyer's card stands in.
            if (_access.TryGetCard(buyer, out var card) && _access.TryGetKey(card, out var buyerKey))
                comp.BuyerKey = buyerKey;
        }
        else if (TryComp<ShuttleDeedComponent>(grid, out var deed) && !string.IsNullOrEmpty(deed.ShuttleOwner))
            comp.OwnerName = deed.ShuttleOwner;

        FinishSetup(ship);
        return ship;
    }

    /// <summary>
    /// Sets up a ship an admin tool spawned, registered to players' accounts rather than to a deed card, so it stays
    /// theirs whatever body they play, a ghost included. It locks only once one of them wears a card with a crew record.
    /// </summary>
    public Entity<WFShipAccessComponent> SetupRegisteredShip(EntityUid grid, string ownerName, IEnumerable<NetUserId> users)
    {
        var comp = EnsureComp<WFShipAccessComponent>(grid);
        var ship = new Entity<WFShipAccessComponent>(grid, comp);
        comp.OwnerName = ownerName;
        foreach (var user in users)
        {
            if (!comp.OwnerUsers.Contains(user))
                comp.OwnerUsers.Add(user);
        }

        FinishSetup(ship);
        return ship;
    }

    private void FinishSetup(Entity<WFShipAccessComponent> ship)
    {
        var comp = ship.Comp;
        comp.Mode = _access.IsFactionGrid(ship, out _) ? WFShipAccessMode.Faction : WFShipAccessMode.Private;
        MarkShipStorage(ship);

        // Locking with no owner key would shut the owner out of their own ship.
        SetLocked(ship, _cfg.GetCVar(ShipAccessCVars.LockNewShips) && CanLock(ship));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Ship access set up on {ToPrettyString(ship.Owner):grid}: registered to {comp.OwnerName}, mode {comp.Mode}, locked {comp.Locked}");
    }

    /// <summary>Registers the ship to one more player's account; false when the grid has no ship access or they already own it.</summary>
    public bool AddOwnerUser(EntityUid grid, NetUserId user)
    {
        if (!TryComp<WFShipAccessComponent>(grid, out var comp) || comp.OwnerUsers.Contains(user))
            return false;

        comp.OwnerUsers.Add(user);
        Dirty(grid, comp);
        RefreshShip((grid, comp));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(grid):grid} was registered to player {user}");
        return true;
    }

    /// <summary>Console verb bridge: whether the user holds the ship's deed, a voucher with it included, or the ship is registered to them.</summary>
    public bool IsOwner(EntityUid user, EntityUid grid)
    {
        return _access.IsOwner(user, grid);
    }

    /// <summary>Sets the lock and rewrites every door and locker reader on the grid to match.</summary>
    public void SetLocked(Entity<WFShipAccessComponent> ship, bool locked)
    {
        ship.Comp.Locked = locked;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>
    /// Console verb bridge: false when the grid has no ship access, so the caller keeps its old sweep. A lock that
    /// would shut the owner out (no owner key) is refused, which counts as handled and sets <paramref name="refused"/>.
    /// </summary>
    public bool TrySetLocked(EntityUid? grid, bool locked, out bool refused)
    {
        refused = false;
        if (grid is not { } uid || !TryComp<WFShipAccessComponent>(uid, out var comp))
            return false;

        refused = locked && !CanLock((uid, comp));
        if (!refused)
            SetLocked((uid, comp), locked);

        return true;
    }

    /// <summary>Console verb bridge: the lock state when the grid has ship access.</summary>
    public bool TryGetLocked(EntityUid? grid, out bool locked)
    {
        locked = TryComp<WFShipAccessComponent>(grid, out var comp) && comp.Locked;
        return comp != null;
    }

    /// <summary>Adds the card a person carries to the allow list. Fails when they carry none, it has no record, it holds the deed, or it is listed.</summary>
    public bool TryAddPerson(Entity<WFShipAccessComponent> ship, EntityUid person, string label = "")
    {
        return _access.TryGetCard(person, out var card) && TryAddCard(ship, card, Name(person), label);
    }

    /// <summary>Adds an ID card's record key to the allow list, under the name on the card or the holder's when it is blank.</summary>
    public bool TryAddCard(Entity<WFShipAccessComponent> ship, EntityUid card, string holderName, string label = "")
    {
        if (!_access.TryGetKey(card, out var key) || _access.IsDeedFor(card, ship.Owner) || _access.TryGetEntry(ship.Comp, key, out _))
            return false;

        var name = TryComp<IdCardComponent>(card, out var idCard) && !string.IsNullOrEmpty(idCard.FullName) ? idCard.FullName : holderName;
        ship.Comp.AllowList.Add(new WFShipAccessEntry { Key = key, Name = name, Label = label });
        Dirty(ship);
        RefreshShip(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Card {ToPrettyString(card):card} ({name}) was added to the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a key off the allow list and every door list; false when it was not on it.</summary>
    public bool RemoveEntry(Entity<WFShipAccessComponent> ship, WFShipAccessKey key)
    {
        if (!_access.TryGetEntry(ship.Comp, key, out var entry))
            return false;

        ship.Comp.AllowList.Remove(entry);
        Dirty(ship);
        RemoveDoorPlayer(ship.Owner, key);
        RefreshShip(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{entry.Name} was removed from the allow list of {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Sets a listed card's builder flag; false when it is not listed.</summary>
    public bool SetBuilder(Entity<WFShipAccessComponent> ship, WFShipAccessKey key, bool builder)
    {
        if (!_access.TryGetEntry(ship.Comp, key, out var entry))
            return false;

        entry.Builder = builder;
        Dirty(ship);
        return true;
    }

    /// <summary>Empties the allow list and returns how many cards were on it.</summary>
    public int ClearAllowList(Entity<WFShipAccessComponent> ship)
    {
        var count = ship.Comp.AllowList.Count;
        if (count == 0)
            return 0;

        ship.Comp.AllowList.Clear();
        Dirty(ship);
        ClearDoorPlayers(ship.Owner);
        RefreshShip(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"The allow list of {ToPrettyString(ship.Owner):grid} was cleared ({count} entries)");
        return count;
    }

    /// <summary>Console verb bridge: 0 when the grid has no ship access.</summary>
    public int ClearAllowList(EntityUid grid)
    {
        return TryComp<WFShipAccessComponent>(grid, out var comp) ? ClearAllowList((grid, comp)) : 0;
    }

    /// <summary>Console verb bridge: a guest swiped in at the console also joins the allow list.</summary>
    public void OnGuestAccessGranted(EntityUid grid, EntityUid user)
    {
        if (TryComp<WFShipAccessComponent>(grid, out var comp))
            TryAddPerson((grid, comp), user, Loc.GetString("ship-access-label-guest"));
    }

    private void OnDoorParentChanged(Entity<DoorComponent> ent, ref EntParentChangedMessage args)
    {
        QueueReader(ent);
    }

    private void OnDoorAnchorChanged(Entity<DoorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Detaching)
            QueueReader(ent);
    }

    private void OnStorageParentChanged(Entity<EntityStorageComponent> ent, ref EntParentChangedMessage args)
    {
        QueueReader(ent);
    }

    private void OnStorageAnchorChanged(Entity<EntityStorageComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Detaching)
            QueueReader(ent);
    }
}
