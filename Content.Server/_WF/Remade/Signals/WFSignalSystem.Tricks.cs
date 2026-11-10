using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Dread;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade.Signals;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.Remade.Signals;

/// <summary>The psychic tricks. Each costs Psi and has a cooldown; the work is the Director's own tricks, or a door.</summary>
public sealed partial class WFSignalSystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private WFDreadSystem _dread = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    private static readonly WFSignalTrick[] AllTricks = Enum.GetValues<WFSignalTrick>();

    private void InitializeTricks()
    {
        SubscribeLocalEvent<WFSignalComponent, WFSignalTrickEvent>(OnTrick);
    }

    private void OnTrick(EntityUid uid, WFSignalComponent signal, WFSignalTrickEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTrick((uid, signal), args.Trick, args.Target);
    }

    /// <summary>Gives the Signal an action for each trick, with the cooldown on it.</summary>
    private void GrantTricks(Entity<WFSignalComponent> ent)
    {
        foreach (var trick in AllTricks)
        {
            EntityUid? action = null;
            if (!_actions.AddAction(ent, ref action, $"WFSignalAction{trick}"))
                continue;

            _actions.SetUseDelay(action, TimeSpan.FromSeconds(WFSignalMath.Info(trick).Cooldown));
        }

        EntityUid? possess = null;
        if (_actions.AddAction(ent, ref possess, PossessAction))
            _actions.SetUseDelay(possess, TimeSpan.FromSeconds(WFSignalMath.PossessCooldown));
    }

    /// <summary>
    /// Plays a trick on a target if the Signal has the Psi, the trick is off cooldown and the stone has reached it. Nothing
    /// is spent when the trick finds nothing to do.
    /// </summary>
    public bool TryTrick(Entity<WFSignalComponent> ent, WFSignalTrick trick, EntityUid target)
    {
        var info = WFSignalMath.Info(trick);
        var now = _timing.CurTime;
        if (!Bound(ent, out var stone) || TerminatingOrDeleted(target) || target == ent.Owner || HasComp<WFSignalComponent>(target))
            return false;

        if (ent.Comp.Possessing != null)
            return Fail(ent, "wf-signal-busy");

        if (stone.Comp.Stage < info.Min)
            return Fail(ent, "wf-signal-too-quiet");

        if (ent.Comp.Ready.TryGetValue(trick, out var ready) && now < ready)
            return Fail(ent, "wf-signal-cooldown");

        if (ent.Comp.Psi < info.Cost)
            return Fail(ent, "wf-signal-no-psi");

        if (_corruption.IsSanctuaryAt(ent) || _corruption.IsSanctuaryAt(target))
            return Fail(ent, "wf-signal-sanctuary");

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        if (here.MapId != there.MapId || (here.Position - there.Position).Length() > WFSignalMath.TrickRange)
            return Fail(ent, "wf-signal-far");

        if (!Play(ent, stone, trick, target, now))
            return Fail(ent, "wf-signal-nothing");

        ent.Comp.Psi -= info.Cost;
        ent.Comp.Ready[trick] = now + TimeSpan.FromSeconds(info.Cooldown);
        ent.Comp.Tricks++;
        Dirty(ent);

        _adminLog.Add(LogType.Action, trick == WFSignalTrick.Bolt ? LogImpact.Medium : LogImpact.Low,
            $"{ToPrettyString(ent):player} played the {trick} trick on {ToPrettyString(target):target} for {info.Cost} psi");
        return true;
    }

    private bool Fail(EntityUid signal, string key)
    {
        Refuse(signal, key);
        return false;
    }

    private bool Play(Entity<WFSignalComponent> ent, Entity<WFMonolithComponent> stone, WFSignalTrick trick, EntityUid target, TimeSpan now)
    {
        switch (trick)
        {
            case WFSignalTrick.Flicker:
                _director.SignalFlicker(target);
                return true;
            case WFSignalTrick.Whisper:
                if (!IsCrewman(target))
                    return false;

                _dread.Whisper(target, stone.Comp.Stage);
                return true;
            case WFSignalTrick.FalseSound:
                return IsCrewman(target) && _director.SignalFalseSound(target, stone);
            case WFSignalTrick.Scrawl:
                return _director.Scrawl(target);
            case WFSignalTrick.Door:
                return ToggleDoor(target);
            default:
                return BoltDoor(target, now);
        }
    }

    /// <summary>A living player that isn't Remade or a Signal.</summary>
    private bool IsCrewman(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid) && !HasComp<WFRemadeComponent>(uid) && _mobState.IsAlive(uid);
    }

    private bool ToggleDoor(EntityUid uid)
    {
        if (!TryComp<DoorComponent>(uid, out var door) || TryComp<DoorBoltComponent>(uid, out var bolt) && bolt.BoltsDown)
            return false;

        return door.State switch
        {
            DoorState.Open => _doors.TryClose(uid, door),
            DoorState.Closed => _doors.TryOpen(uid, door),
            _ => false,
        };
    }

    private bool BoltDoor(EntityUid uid, TimeSpan now)
    {
        if (!TryComp<DoorComponent>(uid, out var door) || !TryComp<DoorBoltComponent>(uid, out var bolt) || bolt.BoltsDown)
            return false;

        if (door.State == DoorState.Open)
            _doors.TryClose(uid, door);

        if (!_doors.TrySetBoltDown((uid, bolt), true))
            return false;

        EnsureComp<WFSignalBoltComponent>(uid).ReleaseAt = now + TimeSpan.FromSeconds(WFSignalMath.BoltSeconds);
        return true;
    }

    private TimeSpan _nextBolt;

    private void UpdateBolts(TimeSpan now)
    {
        if (now < _nextBolt)
            return;

        _nextBolt = now + TickRate;
        var query = EntityQueryEnumerator<WFSignalBoltComponent, DoorBoltComponent>();
        while (query.MoveNext(out var uid, out var held, out var bolt))
        {
            if (now < held.ReleaseAt)
                continue;

            // An unpowered door can't unbolt; keep trying until it can.
            if (!bolt.BoltsDown || _doors.TrySetBoltDown((uid, bolt), false))
                RemCompDeferred<WFSignalBoltComponent>(uid);
        }
    }
}
