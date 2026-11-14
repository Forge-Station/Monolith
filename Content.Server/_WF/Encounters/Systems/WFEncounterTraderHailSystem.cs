using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Hailing;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Traders answer hails: a ship carrying passengers picks up a hail ringing at its helm and offers to hold position,
/// so the caller can come alongside and dock while it is under way. A trader whose crew are alerted takes no calls.
/// </summary>
public sealed class WFEncounterTraderHailSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;

    /// <summary>The ids of the answers it offers.</summary>
    public const string Stop = "trader-stop";
    public const string Leave = "trader-leave";

    /// <summary>How long it holds position for a ship that asked it to.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromMinutes(5);

    /// <summary>How long a hail rings before the trader picks it up.</summary>
    public static readonly TimeSpan AnswerDelay = TimeSpan.FromSeconds(2);

    /// <summary>How long an answered hail waits for a pick before it is closed.</summary>
    private static readonly TimeSpan PickTime = TimeSpan.FromSeconds(60);

    /// <summary>How long a channel stays open after its last line, so the line is read.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _next;

    /// <summary>The calls traders are on, by their console.</summary>
    private readonly Dictionary<EntityUid, Call> _calls = new();

    private readonly List<EntityUid> _done = new();
    private readonly List<EntityUid> _rings = new();

    private sealed class Call
    {
        public WFEncounterShipState Ship = default!;
        public EntityUid Operator;
        public TimeSpan Opened;
        public TimeSpan? CloseAt;
    }

    public override void Initialize()
    {
        base.Initialize();
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
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution != null || !encounter.Begun)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Passengers && ship.Extort == null && !ship.Virtual && !WFEncounterSystem.IsStranded(ship) && !TerminatingOrDeleted(ship.Grid))
                    Answer(ship, now);
            }
        }
    }

    /// <summary>Whether a ship is holding position for a caller, for tests and the admin.</summary>
    public bool IsHolding(WFEncounterShipState ship)
    {
        return ship.HeldUntil is { } until && _timing.CurTime < until;
    }

    /// <summary>Picks up the hails that have rung a moment at the trader's helm, or turns them down while it is alerted.</summary>
    private void Answer(WFEncounterShipState ship, TimeSpan now)
    {
        _rings.Clear();
        var ringing = EntityQueryEnumerator<HailConsoleComponent, TransformComponent>();
        while (ringing.MoveNext(out var uid, out var hail, out var xform))
        {
            if (xform.GridUid == ship.Grid && hail.State == HailState.Ringing && now >= hail.StateTime + AnswerDelay)
                _rings.Add(uid);
        }

        foreach (var console in _rings)
        {
            if (_alerts.IsAlerted(ship.Grid, ship.Group))
            {
                _hailing.AnswerAutomated(console, console, false);
                continue;
            }

            // Nobody left alive to take it: it rings out.
            if (!_encounters.TryFindHelm(ship, out _, out var speaker))
                continue;

            _hailing.AnswerAutomated(console, speaker, true);
            if (_hailing.GetState(console) != HailState.Connected)
                continue;

            _calls[console] = new Call { Ship = ship, Operator = speaker, Opened = now };
            var holding = IsHolding(ship);
            _hailing.Say(console, speaker, Loc.GetString(holding ? "wf-encounter-trader-hail-holding" : "wf-encounter-trader-hail-greet"));
            if (!_hailing.Offer(console, speaker, new List<HailOption>
                {
                    new(Stop, Loc.GetString("wf-encounter-trader-hail-stop")),
                    new(Leave, Loc.GetString("wf-encounter-trader-hail-leave")),
                }))
                _calls[console].CloseAt = now + CloseDelay;
        }

        _rings.Clear();
    }

    private void OnChosen(ref HailOptionChosenEvent args)
    {
        if (!_calls.TryGetValue(args.Offerer, out var call) || call.CloseAt != null)
            return;

        var line = args.Id == Stop && _encounters.HoldPosition(call.Ship, HoldTime)
            ? Loc.GetString("wf-encounter-trader-hail-stopping", ("minutes", (int) HoldTime.TotalMinutes))
            : args.Id == Stop
                ? Loc.GetString("wf-encounter-trader-hail-cannot")
                : Loc.GetString("wf-encounter-trader-hail-farewell");

        if (args.Id == Stop)
            Log.Info($"Trader {ToPrettyString(call.Ship.Grid)} was hailed to hold position by {ToPrettyString(args.ChooserGrid)}.");

        _hailing.Say(args.Offerer, call.Operator, line);
        call.CloseAt = _timing.CurTime + CloseDelay;
    }

    private void OnCallEnded(ref HailCallEndedEvent args)
    {
        _calls.Remove(args.Console);
    }

    /// <summary>Closes channels whose answer is in or that waited too long for one.</summary>
    private void TendCalls(TimeSpan now)
    {
        _done.Clear();
        foreach (var (console, call) in _calls)
        {
            if (TerminatingOrDeleted(console) || now >= (call.CloseAt ?? call.Opened + PickTime))
                _done.Add(console);
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
}
