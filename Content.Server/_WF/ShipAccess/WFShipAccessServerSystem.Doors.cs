using Content.Server.Shuttles.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    [Dependency] private SharedDoorSystem _door = default!;

    /// <summary>Per-door rules: set from the console, sealed doors bolted, door lists trimmed with the allow list.</summary>
    private void InitializeDoors()
    {
        SubscribeLocalEvent<WFDoorAccessRuleComponent, MapInitEvent>(OnRuleMapInit);
        SubscribeLocalEvent<WFDoorAccessRuleComponent, PowerChangedEvent>(OnRulePowerChanged);
        SubscribeLocalEvent<WFDoorAccessRuleComponent, DoorStateChangedEvent>(OnRuleDoorStateChanged);
        SubscribeLocalEvent<WFDoorAccessRuleComponent, DoorBoltsChangedEvent>(OnRuleBoltsChanged);
    }

    /// <summary>
    /// How long a door sealed while open stays open before it is closed: the airlock's own auto-close pace, so
    /// the close never lands on someone stepping through, and the door's usual one-second retries follow.
    /// </summary>
    private static readonly TimeSpan SealCloseDelay = TimeSpan.FromSeconds(2);

    /// <summary>A loaded door re-asserts its reader and bolts, since neither follows from the rule on its own.</summary>
    private void OnRuleMapInit(Entity<WFDoorAccessRuleComponent> door, ref MapInitEvent args)
    {
        QueueReader(door);
        if (door.Comp.Rule == WFDoorAccessRule.Sealed)
            Seal(door);
    }

    /// <summary>Bolts need power, so a sealed door bolts, and an unsealed one unbolts, as soon as it is powered again.</summary>
    private void OnRulePowerChanged(Entity<WFDoorAccessRuleComponent> door, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            return;

        if (door.Comp.Rule == WFDoorAccessRule.Sealed)
            Seal(door);
        else if (door.Comp.UnboltWhenPowered)
            Unseal(door);
    }

    /// <summary>
    /// A door sealed while not shut is bolted once it shuts: bolts dropped mid-close cancel the close and leave it
    /// bolted open. Until then it is scheduled to close whenever it comes to rest open. Only the seal itself waits
    /// like this; afterwards the door's bolts can be hacked, emagged and remoted like any airlock's.
    /// </summary>
    private void OnRuleDoorStateChanged(Entity<WFDoorAccessRuleComponent> door, ref DoorStateChangedEvent args)
    {
        if (!door.Comp.SealPending || door.Comp.Rule != WFDoorAccessRule.Sealed)
            return;

        if (args.State == DoorState.Closed)
            Seal(door);
        else if (args.State == DoorState.Open)
            _door.SetNextStateChange(door, SealCloseDelay);
    }

    /// <summary>
    /// Undocking and FTL arrivals lift a dock airlock's bolts on their own, and the next dock would open it, so a
    /// sealed dock airlock is bolted again. Other doors keep whatever a wire or remote does to their bolts.
    /// </summary>
    private void OnRuleBoltsChanged(Entity<WFDoorAccessRuleComponent> door, ref DoorBoltsChangedEvent args)
    {
        if (!args.BoltsDown && door.Comp.Rule == WFDoorAccessRule.Sealed && HasComp<DockingComponent>(door))
            Seal(door);
    }

    /// <summary>Sets a door's rule. Sealing bolts and closes it; leaving Sealed unbolts it. False when nothing changed.</summary>
    public bool SetDoorRule(Entity<WFShipAccessComponent> ship, EntityUid door, WFDoorAccessRule rule)
    {
        var comp = EnsureComp<WFDoorAccessRuleComponent>(door);
        var old = comp.Rule;
        if (old == rule)
            return false;

        comp.Rule = rule;
        Dirty(door, comp);

        if (rule == WFDoorAccessRule.Sealed)
            Seal((door, comp));
        else if (old == WFDoorAccessRule.Sealed)
            Unseal((door, comp));

        RefreshReader(door);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid} was set to rule {rule} (was {old})");
        return true;
    }

    /// <summary>Adds or removes an allow-listed card's key on a door's own list; false when it is not on the ship's list or nothing changed.</summary>
    public bool SetDoorPlayer(Entity<WFShipAccessComponent> ship, EntityUid door, WFShipAccessKey key, bool listed)
    {
        if (!_access.TryGetEntry(ship.Comp, key, out var entry) && listed)
            return false;

        var comp = EnsureComp<WFDoorAccessRuleComponent>(door);
        if (listed == comp.Players.Contains(key))
            return false;

        if (listed)
            comp.Players.Add(key);
        else
            comp.Players.Remove(key);

        Dirty(door, comp);
        RefreshReader(door);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{entry?.Name ?? "A card"} was {(listed ? "added to" : "removed from")} door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a key off every door list on the ship, after it left the allow list.</summary>
    private void RemoveDoorPlayer(EntityUid grid, WFShipAccessKey key)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WFDoorAccessRuleComponent>(child, out var rule) && rule.Players.Remove(key))
                Dirty(child, rule);
        }
    }

    /// <summary>Empties every door list on the ship, after the allow list was cleared.</summary>
    private void ClearDoorPlayers(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<WFDoorAccessRuleComponent>(child, out var rule) || rule.Players.Count == 0)
                continue;

            rule.Players.Clear();
            Dirty(child, rule);
        }
    }

    /// <summary>
    /// Closes and bolts a sealed door, adding bolts to a door that has none. A door that is not shut yet is asked
    /// to close and is bolted when it does; an unpowered one is bolted when power returns. True when bolted now.
    /// </summary>
    private bool Seal(Entity<WFDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
        {
            bolt = AddComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = true;
        }

        if (TryComp<DoorComponent>(door, out var doorComp) && doorComp.State is not (DoorState.Closed or DoorState.Welded))
        {
            door.Comp.SealPending = true;
            if (doorComp.State == DoorState.Open)
                _door.SetNextStateChange(door, SealCloseDelay, doorComp);

            return false;
        }

        door.Comp.SealPending = false;
        return _door.TrySetBoltDown((door, bolt), true) || bolt.BoltsDown;
    }

    /// <summary>
    /// Unbolts a door that leaves Sealed and takes the bolts away again if sealing added them. Bolts the door
    /// already had need power to come up, so without it the door is flagged to unbolt when power returns.
    /// </summary>
    private void Unseal(Entity<WFDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        door.Comp.SealPending = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
            return;

        _door.SetBoltsDown((door, bolt), false);
        if (door.Comp.AddedBolt)
        {
            RemComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = false;
            return;
        }

        door.Comp.UnboltWhenPowered = _door.IsBolted(door, bolt);
    }

    /// <summary>
    /// Wipes what a seller set before a used ship goes to its next buyer: the access record, the ship code, every
    /// door's code, rule and seal, and the readers' own access comes back. A seal lifted without power keeps a
    /// bare rule until the bolts come up.
    /// </summary>
    public void ClearForResale(EntityUid grid)
    {
        RemComp<WFShipAccessComponent>(grid);
        RemComp<WFShipAccessCodeComponent>(grid);

        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_accessReader.GetMainAccessReader(child, out var reader))
                RestoreReader(reader.Value);

            RemComp<WFDoorCodeComponent>(child);
            if (!TryComp<WFDoorAccessRuleComponent>(child, out var rule))
                continue;

            // The rule goes first, so nothing still reads the door as sealed while its bolts come up.
            if (rule.Rule == WFDoorAccessRule.Sealed)
            {
                rule.Rule = WFDoorAccessRule.Default;
                Unseal((child, rule));
            }

            if (!rule.UnboltWhenPowered)
            {
                RemComp<WFDoorAccessRuleComponent>(child);
                continue;
            }

            rule.Rule = WFDoorAccessRule.Default;
            rule.Players.Clear();
            rule.HasOwnCode = false;
            Dirty(child, rule);
        }
    }

    private void OnSetDoorRule(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetDoorRuleMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship) || !TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            return;

        // Sealing an open or unpowered door is deferred, so say so rather than leave the owner guessing.
        if (SetDoorRule(ship, door, args.Rule) && args.Rule == WFDoorAccessRule.Sealed
            && (!_door.IsBolted(door) || Comp<WFDoorAccessRuleComponent>(door).SealPending))
            Popup(console, args.Actor, "ship-access-seal-pending");
    }

    private void OnSetDoorPlayer(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetDoorPlayerMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship) && TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            SetDoorPlayer(ship, door, args.Key, args.Listed);
    }

    /// <summary>Resolves a console message's door and checks it is a door on the console's own grid. Firelocks take no rule.</summary>
    private bool TryGetShipDoor(Entity<ShuttleConsoleComponent> console, Entity<WFShipAccessComponent> ship, NetEntity netDoor, EntityUid actor, out EntityUid door)
    {
        if (TryGetEntity(netDoor, out var uid) && HasComp<DoorComponent>(uid) && !HasComp<FirelockComponent>(uid) && Transform(uid.Value).GridUid == ship.Owner)
        {
            door = uid.Value;
            return true;
        }

        door = default;
        Popup(console, actor, "ship-access-door-not-on-ship");
        return false;
    }
}
