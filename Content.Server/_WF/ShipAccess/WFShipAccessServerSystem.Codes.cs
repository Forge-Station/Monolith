using Content.Server.Shuttles.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    /// <summary>Wrong codes inside <see cref="MissWindow"/> that lock a person out of the ship's keypads.</summary>
    public const int MaxMisses = 5;

    /// <summary>Tiles from the door within which a keypad code is taken.</summary>
    public const float KeypadRange = 2.5f;

    public static readonly TimeSpan MissWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Codes: the Enter Code verb, keypad submissions, and the owner's code edits from the console.</summary>
    private void InitializeCodes()
    {
        SubscribeLocalEvent<WFDoorAccessRuleComponent, GetVerbsEvent<AlternativeVerb>>(OnGetDoorVerbs);
        SubscribeNetworkEvent<WFShipAccessSubmitCodeMessage>(OnSubmitCode);
    }

    /// <summary>Sets or clears (null) the ship code. False when the code is not four digits or nothing changed.</summary>
    public bool SetShipCode(Entity<WFShipAccessComponent> ship, string? code)
    {
        if (code != null && !WFShipAccessSystem.IsValidCode(code))
            return false;

        var codes = EnsureComp<WFShipAccessCodeComponent>(ship);
        if (codes.ShipCode == code)
            return false;

        codes.ShipCode = code;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"The ship code of {ToPrettyString(ship.Owner):grid} was {(code == null ? "cleared" : "changed")}");
        return true;
    }

    /// <summary>Sets or clears (null) a door's own code and its rule's HasOwnCode flag. False when invalid or unchanged.</summary>
    public bool SetDoorCode(Entity<WFShipAccessComponent> ship, EntityUid door, string? code)
    {
        if (code != null && !WFShipAccessSystem.IsValidCode(code))
            return false;

        if (code == null)
        {
            if (!RemComp<WFDoorCodeComponent>(door))
                return false;
        }
        else
        {
            var doorCode = EnsureComp<WFDoorCodeComponent>(door);
            if (doorCode.Code == code)
                return false;

            doorCode.Code = code;
        }

        var rule = EnsureComp<WFDoorAccessRuleComponent>(door);
        rule.HasOwnCode = code != null;
        Dirty(door, rule);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"The code of door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid} was {(code == null ? "cleared" : "changed")}");
        return true;
    }

    /// <summary>
    /// Takes a keypad code at a door: the door's own code or the ship code stands in for the card the door's
    /// reader wants, and every other door check (power, welding, bolts) still applies. A wrong code counts a miss
    /// for the person; a locked-out person is refused before the code is looked at.
    /// </summary>
    public WFShipAccessCodeResult TrySubmitCode(EntityUid user, EntityUid door, string code)
    {
        if (!TryComp<WFDoorAccessRuleComponent>(door, out var rule) || !WFShipAccessSystem.TakesCode(rule.Rule)
            || Transform(door).GridUid is not { } grid || !TryComp<WFShipAccessComponent>(grid, out var access))
            return WFShipAccessCodeResult.NoKeypad;

        // A client can send a code without ever being offered the verb, so its checks are repeated here.
        if (!_actionBlocker.CanInteract(user, door))
            return WFShipAccessCodeResult.CannotInteract;

        if (!_interaction.InRangeUnobstructed(user, door, KeypadRange))
            return WFShipAccessCodeResult.OutOfRange;

        if (_door.IsBolted(door))
            return WFShipAccessCodeResult.Bolted;

        if (!TryComp<DoorComponent>(door, out var doorComp) || doorComp.State != DoorState.Closed)
            return WFShipAccessCodeResult.NotClosed;

        var ship = new Entity<WFShipAccessComponent>(grid, access);
        var codes = EnsureComp<WFShipAccessCodeComponent>(grid);
        // Misses are counted per character name, so a nameless thing never locks out.
        var name = MetaData(user).EntityName is { Length: > 0 } known ? known : null;
        if (name != null && IsLockedOut(codes, name))
            return WFShipAccessCodeResult.LockedOut;

        var ownCode = TryComp<WFDoorCodeComponent>(door, out var doorCode) ? doorCode.Code : null;
        if (code != ownCode && (codes.ShipCode == null || code != codes.ShipCode))
        {
            RecordMiss(ship, codes, name, user, door);
            return WFShipAccessCodeResult.Wrong;
        }

        // A null user skips the access reader and nothing else: power, welding and bolts still decide.
        if (!_door.CanOpen(door, doorComp, user: null))
            return WFShipAccessCodeResult.NoResponse;

        _door.StartOpening(door, doorComp, user);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} opened {ToPrettyString(door):door} on {ToPrettyString(grid):grid} with a code");
        return WFShipAccessCodeResult.Opened;
    }

    /// <summary>Whether the character's keypad lockout on this ship is still running.</summary>
    public bool IsLockedOut(WFShipAccessCodeComponent codes, string name)
    {
        return codes.Lockouts.TryGetValue(name, out var lockout) && lockout.LockedUntil > _timing.CurTime;
    }

    private void RecordMiss(Entity<WFShipAccessComponent> ship, WFShipAccessCodeComponent codes, string? name, EntityUid user, EntityUid door)
    {
        codes.Misses++;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} entered a wrong code at {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid}");

        // Misses are per character, so something without a name never locks out.
        if (name != null)
        {
            var now = _timing.CurTime;
            if (!codes.Lockouts.TryGetValue(name, out var lockout))
            {
                lockout = new WFShipAccessLockout { WindowStart = now };
                codes.Lockouts[name] = lockout;
            }

            if (now - lockout.WindowStart > MissWindow)
            {
                lockout.WindowStart = now;
                lockout.Misses = 0;
            }

            lockout.Misses++;
            if (lockout.Misses >= MaxMisses)
            {
                lockout.LockedUntil = now + LockoutDuration;
                lockout.Misses = 0;
                _adminLog.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(user):user} is locked out of the keypads of {ToPrettyString(ship.Owner):grid} for {LockoutDuration.TotalMinutes} minutes after {MaxMisses} wrong codes");
            }
        }

        NotifyOwner(ship, new WFShipAccessCodeAlertEvent(GetNetEntity(ship.Owner), codes.Misses, CountLockedOut(codes)));
    }

    /// <summary>Sends an event to the ship's owners in the game right now: whoever carries its deed and the players it is registered to.</summary>
    private void NotifyOwner(Entity<WFShipAccessComponent> ship, EntityEventArgs ev)
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } body && (_access.HasDeedFor(body, ship.Owner) || ship.Comp.OwnerUsers.Contains(session.UserId)))
                RaiseNetworkEvent(ev, session.Channel);
        }
    }

    private int CountLockedOut(WFShipAccessCodeComponent codes)
    {
        var now = _timing.CurTime;
        var count = 0;
        foreach (var lockout in codes.Lockouts.Values)
        {
            if (lockout.LockedUntil > now)
                count++;
        }

        return count;
    }

    /// <summary>Answers an owner's console with the codes. Callers have checked the session's character owns the ship.</summary>
    private void SendCodes(Entity<WFShipAccessComponent> ship, ICommonSession session)
    {
        var codes = EnsureComp<WFShipAccessCodeComponent>(ship);
        var doorCodes = new Dictionary<NetEntity, string>();
        var children = Transform(ship.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WFDoorCodeComponent>(child, out var doorCode))
                doorCodes[GetNetEntity(child)] = doorCode.Code;
        }

        RaiseNetworkEvent(new WFShipAccessCodesEvent(GetNetEntity(ship.Owner), codes.ShipCode, doorCodes, codes.Misses, CountLockedOut(codes)), session.Channel);
    }

    /// <summary>Enter Code on a code door, for people the rule does not already admit; bolted doors have no keypad.</summary>
    private void OnGetDoorVerbs(Entity<WFDoorAccessRuleComponent> door, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !WFShipAccessSystem.TakesCode(door.Comp.Rule))
            return;

        if (Transform(door).GridUid is not { } grid || !HasComp<WFShipAccessComponent>(grid))
            return;

        if (ReaderAdmits(args.User, door) || _door.IsBolted(door))
            return;

        if (!_player.TryGetSessionByEntity(args.User, out var session))
            return;

        var channel = session.Channel;
        var netDoor = GetNetEntity(door);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ship-access-keypad-verb"),
            Act = () => RaiseNetworkEvent(new WFShipAccessOpenKeypadEvent(netDoor), channel),
        });
    }

    /// <summary>Whether the door's reader already lets the user through, checked without writing its access log.</summary>
    private bool ReaderAdmits(EntityUid user, EntityUid door)
    {
        if (!TryComp<AccessReaderComponent>(door, out var reader))
            return true;

        var items = _accessReader.FindPotentialAccessItems(user);
        var tags = _accessReader.FindAccessTags(user, items);
        _accessReader.FindStationRecordKeys(user, out var keys, items);
        return _accessReader.IsAllowed(tags, keys, door, reader);
    }

    private void OnSubmitCode(WFShipAccessSubmitCodeMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryGetEntity(msg.Door, out var door))
            return;

        var key = TrySubmitCode(user, door.Value, msg.Code) switch
        {
            WFShipAccessCodeResult.Opened => null,
            WFShipAccessCodeResult.CannotInteract => null,
            WFShipAccessCodeResult.NoResponse => "ship-access-code-no-response",
            WFShipAccessCodeResult.Wrong => "ship-access-code-wrong",
            WFShipAccessCodeResult.LockedOut => "ship-access-code-locked-out",
            WFShipAccessCodeResult.OutOfRange => "ship-access-code-out-of-range",
            WFShipAccessCodeResult.Bolted => "ship-access-code-bolted",
            WFShipAccessCodeResult.NotClosed => "ship-access-code-not-closed",
            _ => "ship-access-code-no-keypad",
        };

        if (key != null)
            _popup.PopupEntity(Loc.GetString(key), door.Value, user);
    }

    private void OnRequestCodes(Entity<ShuttleConsoleComponent> console, ref WFShipAccessRequestCodesMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship) && _player.TryGetSessionByEntity(args.Actor, out var session))
            SendCodes(ship, session);
    }

    private void OnSetShipCode(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetShipCodeMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship))
            return;

        if (args.Code != null && !WFShipAccessSystem.IsValidCode(args.Code))
        {
            Popup(console, args.Actor, "ship-access-code-invalid");
            return;
        }

        SetShipCode(ship, args.Code);
        if (_player.TryGetSessionByEntity(args.Actor, out var session))
            SendCodes(ship, session);
    }

    private void OnSetDoorCode(Entity<ShuttleConsoleComponent> console, ref WFShipAccessSetDoorCodeMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship) || !TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            return;

        if (args.Code != null && !WFShipAccessSystem.IsValidCode(args.Code))
        {
            Popup(console, args.Actor, "ship-access-code-invalid");
            return;
        }

        SetDoorCode(ship, door, args.Code);
        if (_player.TryGetSessionByEntity(args.Actor, out var session))
            SendCodes(ship, session);
    }
}

/// <summary>What a keypad submission did.</summary>
public enum WFShipAccessCodeResult : byte
{
    Opened,
    Wrong,
    LockedOut,
    OutOfRange,
    Bolted,
    NotClosed,
    NoKeypad,

    /// <summary>The person can't act right now: crit, cuffed, a ghost and the like.</summary>
    CannotInteract,

    /// <summary>The code was right but the door wouldn't open, for example without power.</summary>
    NoResponse,
}
