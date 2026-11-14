using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Hailing;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Interrogating zones: a ship whose zone targets <see cref="WFEncounterZoneTargets.Interrogate"/> hails each player
/// ship in its warning zone and asks whether it is a threat. YES puts the ship on every ship of the encounter's
/// declared list, and their zones fire on it; NO, or no answer, leaves it alone for a while before it is asked again.
/// The lines come from the ship's zone lines: <c>{ZoneLines}-hail-question</c>, <c>-hail-yes</c>, <c>-hail-no</c>,
/// <c>-hail-declared</c> and <c>-hail-cleared</c>.
/// </summary>
public sealed class WFEncounterInterrogationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFEncounterZoneSystem _zones = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The ids of the answers it offers.</summary>
    public const string Yes = "interrogate-yes";
    public const string No = "interrogate-no";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>How long a ship that answered NO is left alone before it is asked again.</summary>
    public static readonly TimeSpan ClearedCooldown = TimeSpan.FromMinutes(15);

    /// <summary>How long a ship that let the hail go by, declined it or could not take it is left before it is asked again.</summary>
    public static readonly TimeSpan NoAnswerCooldown = TimeSpan.FromMinutes(3);

    /// <summary>How long an answered hail waits for YES or NO before it counts as no answer.</summary>
    private static readonly TimeSpan AnswerTime = TimeSpan.FromSeconds(60);

    /// <summary>How long a channel stays open after its last line, so the line is read.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(4);

    private TimeSpan _next;

    /// <summary>The interrogations under way, by the console making them.</summary>
    private readonly Dictionary<EntityUid, Call> _calls = new();

    private readonly List<EntityUid> _done = new();

    /// <summary>One interrogation hail.</summary>
    private sealed class Call
    {
        public EntityUid Encounter;
        public WFEncounterShipState Ship = default!;
        public EntityUid Target;
        public EntityUid Operator;

        /// <summary>When the question was put, once the hail was answered.</summary>
        public TimeSpan? Asked;

        /// <summary>When the channel is closed, once the answer is in.</summary>
        public TimeSpan? CloseAt;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HailAnsweredEvent>(OnAnswered);
        SubscribeLocalEvent<HailOptionChosenEvent>(OnChosen);
        SubscribeLocalEvent<HailCallEndedEvent>(OnCallEnded);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        TendCalls(now);

        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            Prune(encounter, now);
            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.ZoneTargets == WFEncounterZoneTargets.Interrogate && ship.Strangers.Count > 0)
                    Ask(uid, encounter, ship, now);
            }
        }
    }

    /// <summary>Whether a ship's interrogation is waiting on a vessel's answer, for tests and the admin.</summary>
    public bool IsAsking(WFEncounterShipState ship, EntityUid target)
    {
        return _calls.Values.Any(call => call.Ship == ship && call.Target == target);
    }

    /// <summary>Hails the nearest stranger in a ship's warning zone that is not being asked and is not on cooldown.</summary>
    private void Ask(EntityUid uid, WFEncounterComponent encounter, WFEncounterShipState ship, TimeSpan now)
    {
        if (ship.Lurking || ship.Extort != null || TerminatingOrDeleted(ship.Grid) || _status.IsDisabled(ship.Grid)
            || _calls.Values.Any(call => call.Ship == ship))
            return;

        var here = _transform.GetMapCoordinates(ship.Grid);
        EntityUid? target = null;
        var best = float.MaxValue;
        foreach (var stranger in ship.Strangers)
        {
            if (TerminatingOrDeleted(stranger) || ship.Declared.Contains(stranger) || _zones.IsThreat(ship, stranger)
                || encounter.Interrogated.ContainsKey(stranger)
                || _calls.Values.Any(call => call.Encounter == uid && call.Target == stranger))
                continue;

            var there = _transform.GetMapCoordinates(stranger);
            var distance = (there.Position - here.Position).Length();
            if (there.MapId != here.MapId || distance > ship.WarnRange || distance >= best)
                continue;

            best = distance;
            target = stranger;
        }

        if (target is not { } mark || !_encounters.TryFindHelm(ship, out var console, out var speaker)
            || _hailing.GetState(console) != null)
            return;

        if (_hailing.TryHailAutomated(console, speaker, mark, out var refused))
        {
            _calls[console] = new Call { Encounter = uid, Ship = ship, Target = mark, Operator = speaker };
            Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} hails {ToPrettyString(mark)} to ask whether it is a threat.");
            return;
        }

        // The vessel cannot take a call: it is let be a while, as one that let the hail go by. Its own console's
        // cooldown, or a fault at this end, is tried again on the next look.
        if (refused is HailEndReason.Busy or HailEndReason.Unavailable)
            encounter.Interrogated[mark] = now + NoAnswerCooldown;
    }

    /// <summary>Closes channels whose answer is in, gives up on unanswered questions and drops calls whose encounter is over.</summary>
    private void TendCalls(TimeSpan now)
    {
        _done.Clear();
        foreach (var (console, call) in _calls)
        {
            if (TerminatingOrDeleted(console) || !TryComp<WFEncounterComponent>(call.Encounter, out var encounter)
                || encounter.Resolution != null)
            {
                _done.Add(console);
                continue;
            }

            if (call.CloseAt is { } close)
            {
                if (now >= close)
                    _done.Add(console);
                continue;
            }

            if (call.Asked is { } asked && now >= asked + AnswerTime)
            {
                encounter.Interrogated[call.Target] = now + NoAnswerCooldown;
                Log.Info($"{ToPrettyString(call.Target)} gave encounter ship {ToPrettyString(call.Ship.Grid)} no answer.");
                _done.Add(console);
            }
        }

        foreach (var console in _done)
        {
            _calls.Remove(console);
            if (!TerminatingOrDeleted(console) && _hailing.GetState(console) != null)
            {
                _hailing.ClearOptions(console);
                _hailing.End(console);
            }
        }

        _done.Clear();
    }

    /// <summary>Forgets cooldowns that are over and vessels that are gone.</summary>
    private void Prune(WFEncounterComponent encounter, TimeSpan now)
    {
        foreach (var (vessel, until) in encounter.Interrogated.ToList())
        {
            if (now >= until || TerminatingOrDeleted(vessel))
                encounter.Interrogated.Remove(vessel);
        }

        foreach (var ship in encounter.Ships.Values)
        {
            ship.Declared.RemoveWhere(vessel => TerminatingOrDeleted(vessel));
        }
    }

    /// <summary>The hail was answered: the question is put and YES and NO offered.</summary>
    private void OnAnswered(ref HailAnsweredEvent args)
    {
        if (!_calls.TryGetValue(args.Console, out var call) || call.Asked != null)
            return;

        var lines = call.Ship.ZoneLines;
        call.Asked = _timing.CurTime;
        _hailing.Say(args.Console, call.Operator, Loc.GetString($"{lines}-hail-question"));
        if (!_hailing.Offer(args.Console, call.Operator, new List<HailOption>
            {
                new(Yes, Loc.GetString($"{lines}-hail-yes")),
                new(No, Loc.GetString($"{lines}-hail-no")),
            }))
            Log.Warning($"Interrogating console {ToPrettyString(args.Console)} could not offer its answers.");

        Log.Info($"{ToPrettyString(call.Target)} answered the hail of encounter ship {ToPrettyString(call.Ship.Grid)}.");
    }

    /// <summary>An answer was picked: YES declares the vessel a threat to the whole encounter, NO clears it for a while.</summary>
    private void OnChosen(ref HailOptionChosenEvent args)
    {
        if (args.Id is not (Yes or No) || !_calls.TryGetValue(args.Offerer, out var call) || call.CloseAt != null
            || !TryComp<WFEncounterComponent>(call.Encounter, out var encounter))
            return;

        var vessel = args.ChooserGrid ?? call.Target;
        var now = _timing.CurTime;
        var lines = call.Ship.ZoneLines;
        call.CloseAt = now + CloseDelay;
        if (args.Id == Yes)
        {
            foreach (var ship in encounter.Ships.Values)
            {
                ship.Declared.Add(vessel);
            }

            encounter.Interrogated.Remove(vessel);
            _hailing.Say(args.Offerer, call.Operator, Loc.GetString($"{lines}-hail-declared"));
            Log.Info($"{ToPrettyString(args.ChooserEntity)} on {ToPrettyString(vessel)} declared it a threat to encounter ship {ToPrettyString(call.Ship.Grid)}.");
            return;
        }

        encounter.Interrogated[vessel] = now + ClearedCooldown;
        _hailing.Say(args.Offerer, call.Operator, Loc.GetString($"{lines}-hail-cleared"));
        Log.Info($"{ToPrettyString(args.ChooserEntity)} on {ToPrettyString(vessel)} told encounter ship {ToPrettyString(call.Ship.Grid)} it is no threat.");
    }

    /// <summary>A hail that ended before an answer was picked counts as no answer.</summary>
    private void OnCallEnded(ref HailCallEndedEvent args)
    {
        if (!_calls.Remove(args.Console, out var call) || call.CloseAt != null)
            return;

        if (TryComp<WFEncounterComponent>(call.Encounter, out var encounter))
            encounter.Interrogated[call.Target] = _timing.CurTime + NoAnswerCooldown;

        Log.Info($"{ToPrettyString(call.Target)} gave encounter ship {ToPrettyString(call.Ship.Grid)} no answer ({args.Reason}).");
    }
}
