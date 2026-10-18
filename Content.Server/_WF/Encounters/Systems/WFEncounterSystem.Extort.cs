using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Administration.Logs;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Database;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Extorters: a ship that roams, now and then closes on a player ship, hails it and demands a toll. A ship that pays is
/// left alone by every extorter for a long while and its toll goes on the encounter's warrant. One that refuses,
/// cannot pay, or lets three hails go by is boarded as a hunting ship boards its prey, and the extorter goes back to
/// roaming after. A ship alongside whose line cannot ring has let a hail go by all the same.
/// </summary>
public sealed partial class WFEncounterSystem
{
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WFEncounterBountySystem _bounties = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    /// <summary>Hails that may go unanswered before it boards.</summary>
    public const int ExtortHails = 3;

    /// <summary>The ids of the answers it offers.</summary>
    public const string ExtortPay = "pay";
    public const string ExtortRefuse = "refuse";

    /// <summary>Variants of each line.</summary>
    private const int ExtortLines = 3;

    /// <summary>How far past its standoff it may be and still hail.</summary>
    private const float ExtortHailSlack = 400f;

    /// <summary>How long a mark has to pick an answer.</summary>
    private static readonly TimeSpan ExtortAnswerTime = TimeSpan.FromSeconds(45);

    /// <summary>How long a channel stays open after its last line, so the line is read.</summary>
    private static readonly TimeSpan ExtortCloseDelay = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan ExtortRetry = TimeSpan.FromSeconds(5);

    /// <summary>How soon it looks for a mark again after finding none or losing one.</summary>
    private static readonly TimeSpan ExtortLookAgain = TimeSpan.FromSeconds(90);

    /// <summary>How long it chases a mark it cannot close on, and how long it waits on its own console.</summary>
    private static readonly TimeSpan ExtortCloseTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExtortHailPatience = TimeSpan.FromMinutes(2);

    /// <summary>How long it chases a ship it means to board without getting a port on it.</summary>
    public static readonly TimeSpan ExtortChaseTime = TimeSpan.FromMinutes(10);

    /// <summary>How long it leaves alone a ship that paid, one it raided and one it could not catch.</summary>
    public static readonly TimeSpan ExtortPaidSpare = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ExtortRaidSpare = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan ExtortLostSpare = TimeSpan.FromMinutes(15);

    private readonly List<EntityUid> _rings = new();

    private void InitializeExtort()
    {
        SubscribeLocalEvent<HailAnsweredEvent>(OnExtortAnswered);
        SubscribeLocalEvent<HailOptionChosenEvent>(OnExtortChosen);
        SubscribeLocalEvent<HailCallEndedEvent>(OnExtortCallEnded);
    }

    private WFExtortState NewExtort(WFEncounterExtort config)
    {
        // The first run comes sooner than the ones after it.
        return new WFExtortState
        {
            Config = config,
            PhaseSince = _timing.CurTime,
            NextMark = _timing.CurTime + ExtortInterval(config) * 0.5,
        };
    }

    private TimeSpan ExtortInterval(WFEncounterExtort config)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(config.MarkMin, MathF.Max(config.MarkMin, config.MarkMax)));
    }

    /// <summary>One look at an extorter: its roaming, its mark, its hails and its raid.</summary>
    private void Extort(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        var now = _timing.CurTime;
        if (extort.CloseAt is { } close && now >= close)
            EndExtortCall(extort);

        if (extort.Phase == WFExtortPhase.Board)
        {
            // Never alongside in all this time: the ship has outrun it.
            var outrun = ship.RaidEnds == null && now - extort.PhaseSince > ExtortChaseTime;
            if (outrun || !Hunt(ship, false))
                EndExtortRaid(encounter, key, ship, extort);
            return;
        }

        // Nobody aboard to fly it or to talk.
        if (!HasLivingCrew(ship))
            return;

        DeclineRings(ship);
        switch (extort.Phase)
        {
            case WFExtortPhase.Roam:
                ExtortRoam(encounter, key, ship, extort);
                break;
            case WFExtortPhase.Close:
                ExtortClose(encounter, key, ship, extort);
                break;
            case WFExtortPhase.Hail:
                ExtortHail(encounter, key, ship, extort);
                break;
            case WFExtortPhase.Calling:
                if (!ExtortMark(ship, extort, out _))
                {
                    DropMark(encounter, key, ship, extort, null);
                    break;
                }

                // The hail's end comes as an event; this catches one that never came, such as its console being destroyed.
                if (extort.Console is not { } calling || _hailing.GetState(calling) != HailState.Calling)
                    RetryHail(extort, ExtortRetry, false);
                break;
            case WFExtortPhase.Demand:
                ExtortDemand(encounter, key, ship, extort);
                break;
        }
    }

    private void ExtortRoam(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        var now = _timing.CurTime;
        if (_objectives.QueueStatus(ship.Grid, ship.Group) is null or "complete" or "target-lost" or "dock-failed")
            Roam(encounter, key, ship, extort);

        // A ship that turns on its attackers is left to its captain while it is alerted.
        if (now < extort.NextMark || ship.Evades && _alerts.IsAlerted(ship.Grid, ship.Group))
            return;

        PruneSpared(extort);
        if (NearestCrewedShip(ship.Grid, extort.Config.Reach, ExtortOffLimits(extort), ship.NeutralCompanies) is not { } mark || !Shadow(ship, extort, mark))
        {
            extort.NextMark = now + ExtortLookAgain;
            return;
        }

        extort.Mark = mark;
        extort.Attempts = 0;
        extort.DeadSince = null;
        SetExtortPhase(extort, WFExtortPhase.Close);
        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} goes after {ToPrettyString(mark)} for its toll.");
        // Already alongside: it hails at once.
        ExtortClose(encounter, key, ship, extort);
    }

    private void ExtortClose(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        if (!ExtortMark(ship, extort, out var mark))
        {
            DropMark(encounter, key, ship, extort, null);
            return;
        }

        var distance = (_transform.GetWorldPosition(mark) - _transform.GetWorldPosition(ship.Grid)).Length();
        if (distance <= extort.Config.Standoff + ExtortHailSlack)
        {
            SetExtortPhase(extort, WFExtortPhase.Hail);
            extort.NextHail = _timing.CurTime;
            ExtortHail(encounter, key, ship, extort);
            return;
        }

        // It cannot catch the ship: there are others.
        if (_timing.CurTime - extort.PhaseSince > ExtortCloseTime)
        {
            DropMark(encounter, key, ship, extort, ExtortLostSpare);
            return;
        }

        if (_objectives.QueueStatus(ship.Grid, ship.Group) is null or "complete" or "target-lost")
            Shadow(ship, extort, mark);
    }

    private void ExtortHail(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        var now = _timing.CurTime;
        if (!ExtortMark(ship, extort, out var mark))
        {
            DropMark(encounter, key, ship, extort, null);
            return;
        }

        if (now < extort.NextHail)
            return;

        // A channel left open for its last line to be read is closed before it calls anyone else.
        if (extort.CloseAt != null)
            EndExtortCall(extort);

        // No working console of its own, or nobody to make the call: it has no way to ask.
        if (!TryFindHelm(ship, out var console, out var speaker))
        {
            DropMark(encounter, key, ship, extort, null);
            return;
        }

        extort.Console = console;
        extort.Operator = speaker;
        if (_hailing.TryHailAutomated(console, speaker, mark, out var refused))
        {
            extort.Attempts++;
            extort.DeadSince = null;
            SetExtortPhase(extort, WFExtortPhase.Calling);
            return;
        }

        if (refused is HailEndReason.Unavailable or HailEndReason.Busy)
            ExtortDeadLine(encounter, key, ship, extort, mark);
        // Too soon after its last hail, or its own console is not free: it tries again shortly, for a while.
        else if (now - extort.PhaseSince > ExtortHailPatience)
            DropMark(encounter, key, ship, extort, null);
        else
            extort.NextHail = now + ExtortRetry;
    }

    /// <summary>
    /// The mark's line cannot ring: no powered console, running dark, on another call or out of hail range. A mark
    /// that has got away from it is chased again with its count kept. Alongside, a line dead for as long as a hail
    /// rings is a hail gone by, so cutting the power or holding a call open buys nothing.
    /// </summary>
    private void ExtortDeadLine(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort,
        EntityUid mark)
    {
        var now = _timing.CurTime;
        var distance = (_transform.GetWorldPosition(mark) - _transform.GetWorldPosition(ship.Grid)).Length();
        if (distance > extort.Config.Standoff + ExtortHailSlack)
        {
            extort.DeadSince = null;
            SetExtortPhase(extort, WFExtortPhase.Close);
            return;
        }

        extort.DeadSince ??= now;
        if (now - extort.DeadSince < TimeSpan.FromSeconds(_config.GetCVar(HailingCVars.RingTimeout)))
        {
            extort.NextHail = now + ExtortRetry;
            return;
        }

        extort.DeadSince = null;
        extort.Attempts++;
        // Nothing rang aboard, so the ship is told on the radio what its silence is costing it.
        if (extort.Attempts < ExtortHails)
            TrySay(ship, AnnounceChannel, ExtortLine(extort, "dead", ("ship", ExtortMarkName(extort))));

        MissedHail(encounter, key, ship, extort, ExtortRetry);
    }

    /// <summary>One of its hails has gone by: it waits to hail again, and after the last it boards.</summary>
    private void MissedHail(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort,
        TimeSpan delay)
    {
        if (extort.Attempts < ExtortHails)
        {
            RetryHail(extort, delay, true);
            return;
        }

        // There is no channel to say it on, so the ship hears it on the radio.
        TrySay(ship, AnnounceChannel, ExtortLine(extort, "silent", ("ship", ExtortMarkName(extort))));
        Board(encounter, key, ship, extort);
    }

    private void ExtortDemand(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        if (!ExtortMark(ship, extort, out _))
        {
            DropMark(encounter, key, ship, extort, null);
            return;
        }

        var open = extort.Console is { } console && _hailing.GetState(console) == HailState.Connected;
        if (open && _timing.CurTime < extort.DemandEnds)
            return;

        // No answer picked in time is a refusal.
        if (open)
        {
            _hailing.ClearOptions(extort.Console!.Value);
            ExtortSay(extort, "timeout");
        }

        Board(encounter, key, ship, extort);
    }

    /// <summary>The mark answered: the toll is named and the answers put in front of whoever is on the call.</summary>
    private void OnExtortAnswered(ref HailAnsweredEvent args)
    {
        if (!TryExtorter(args.Grid, args.Console, out _, out _, out _, out var extort) || extort.Phase != WFExtortPhase.Calling
            || extort.Operator is not { } speaker)
            return;

        var toll = extort.Config.Toll;
        SetExtortPhase(extort, WFExtortPhase.Demand);
        extort.DemandEnds = _timing.CurTime + ExtortAnswerTime;
        ExtortSay(extort, "demand", ("ship", ExtortMarkName(extort)), ("toll", toll));
        if (!_hailing.Offer(args.Console, speaker, new List<HailOption>
            {
                new(ExtortPay, Loc.GetString($"{extort.Config.Lines}-option-pay", ("toll", toll))),
                new(ExtortRefuse, Loc.GetString($"{extort.Config.Lines}-option-refuse")),
            }))
            Log.Warning($"Extorter console {ToPrettyString(args.Console)} could not offer its answers.");
    }

    /// <summary>An answer was picked: the toll is taken from whoever picked it, or the ship is boarded.</summary>
    private void OnExtortChosen(ref HailOptionChosenEvent args)
    {
        if (!TryExtorter(args.OffererGrid, args.Offerer, out var encounter, out var key, out var ship, out var extort)
            || extort.Phase != WFExtortPhase.Demand)
            return;

        var toll = extort.Config.Toll;
        if (args.Id != ExtortPay)
        {
            ExtortSay(extort, "refused");
            Board(encounter, key, ship, extort);
            return;
        }

        // The whole toll or none of it: a ship that cannot cover it has refused.
        if (!_bank.TryBankWithdraw(args.ChooserEntity, toll))
        {
            ExtortSay(extort, "short", ("toll", toll));
            Board(encounter, key, ship, extort);
            return;
        }

        var now = _timing.CurTime;
        var mark = extort.Mark;
        extort.Tolls++;
        extort.Collected += toll;
        ExtortSay(extort, "paid");
        // Every toll goes on the price of the ship that took it.
        if (!_bounties.AddToBounty(encounter, toll))
            Log.Warning($"Encounter {ToPrettyString(encounter)} has no warrant to add a toll of {toll} to.");
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.ChooserEntity):player} paid a toll of {toll} spesos to encounter ship {ToPrettyString(ship.Grid):entity}");

        if (mark is { } paid)
            extort.Spared[paid] = now + ExtortPaidSpare;
        extort.Mark = null;
        extort.Attempts = 0;
        SetExtortPhase(extort, WFExtortPhase.Roam);
        extort.NextMark = now + ExtortInterval(extort.Config);
        extort.CloseAt = now + ExtortCloseDelay;

        // Paid, it flies clear of the ship before it roams again.
        Vector2? clear = null;
        if (mark is { } left && !TerminatingOrDeleted(left))
        {
            var here = _transform.GetWorldPosition(ship.Grid);
            var away = here - _transform.GetWorldPosition(left);
            clear = here + (away.LengthSquared() > 1f ? Vector2.Normalize(away) : _random.NextAngle().ToVec()) * RaidExit;
        }

        Roam(encounter, key, ship, extort, clear);
    }

    /// <summary>
    /// A hail that rang out, was declined or was cut off from the mark's end is one of its three. A channel closed
    /// with the toll unanswered is a refusal.
    /// </summary>
    private void OnExtortCallEnded(ref HailCallEndedEvent args)
    {
        if (!TryExtorter(args.Grid, args.Console, out var encounter, out var key, out var ship, out var extort))
            return;

        if (extort.Phase == WFExtortPhase.Demand)
        {
            // Its own speaker fell or left the ship: the call is made again by whoever is left.
            if (args.Reason == HailEndReason.Closed)
                RetryHail(extort, ExtortRetry, false);
            else
                Board(encounter, key, ship, extort);
            return;
        }

        if (extort.Phase != WFExtortPhase.Calling)
            return;

        switch (args.Reason)
        {
            case HailEndReason.NoAnswer:
            case HailEndReason.Declined:
            case HailEndReason.PeerClosed:
            // The mark went dark, lost its console or left hail range while it rang: it rang all the same.
            case HailEndReason.Unavailable:
                MissedHail(encounter, key, ship, extort, TimeSpan.FromSeconds(_config.GetCVar(HailingCVars.RetryDelay) + 0.5f));
                break;
            default:
                // Its own end failed: power, or its speaker. Not a hail the mark ignored.
                RetryHail(extort, ExtortRetry, false);
                break;
        }
    }

    /// <summary>Back to waiting beside the mark for the next hail. A hail that was no fault of the mark's is not counted.</summary>
    private void RetryHail(WFExtortState extort, TimeSpan delay, bool counted)
    {
        if (!counted)
            extort.Attempts = Math.Max(0, extort.Attempts - 1);

        SetExtortPhase(extort, WFExtortPhase.Hail);
        extort.NextHail = _timing.CurTime + delay;
    }

    /// <summary>Hands the mark to the hunt: the ship is boarded as a hunting ship boards its prey.</summary>
    private void Board(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        if (!ExtortMark(ship, extort, out var mark) || !IssueRaid(ship, mark))
        {
            DropMark(encounter, key, ship, extort, null);
            return;
        }

        if (extort.Console is { } console && _hailing.GetState(console) != null)
        {
            _hailing.ClearOptions(console);
            extort.CloseAt = _timing.CurTime + ExtortCloseDelay;
        }

        SetExtortPhase(extort, WFExtortPhase.Board);
        extort.Raids++;
        ship.Prey = mark;
        HuntRecordFor(ship.Grid).Misses = 0;
        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} goes to board {ToPrettyString(mark)}.");
    }

    /// <summary>
    /// The raid is flown, given up or lost: whoever is still off the ship is left, the ship it went for is left alone a
    /// while, and the extorter roams again.
    /// </summary>
    private void EndExtortRaid(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort)
    {
        var now = _timing.CurTime;
        RecallBoardingParty(ship);
        LeaveBehind(encounter, ship);
        if (extort.Mark is { } raided)
            extort.Spared[raided] = now + ExtortRaidSpare;

        ship.Prey = null;
        ship.Boarded = null;
        ship.RaidEnds = null;
        _huntRecords.Remove(ship.Grid);
        extort.Mark = null;
        extort.Attempts = 0;
        extort.DeadSince = null;
        SetExtortPhase(extort, WFExtortPhase.Roam);
        extort.NextMark = now + ExtortInterval(extort.Config);
        Roam(encounter, key, ship, extort);
        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} is done with its raid and roams again.");
    }

    /// <summary>
    /// Hands still off the ship when its raid ends are left where they are, as a stranded party is. While anyone
    /// lives aboard the ship they are no longer among those who must fall for the warrant to be paid; with nobody
    /// left aboard they are all the crew it has, and stay on it. The hands aboard go looking for trouble again.
    /// </summary>
    private void LeaveBehind(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship)
    {
        var crewed = HasLivingCrew(ship);
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            // A hand outside in a suit is not left: only those standing on some other ship.
            if (member.Group != ship.Group || xform.GridUid is not { } deck)
                continue;

            if (deck == ship.Grid)
            {
                member.Pursues = true;
                continue;
            }

            if (crewed)
                _bounties.Release(encounter, uid);
            // Already left, by this raid or an earlier one.
            if (member.Post is { } post && post.EntityId != ship.Grid)
                continue;

            if (_mobs.IsAlive(uid))
            {
                member.Pursues = true;
                member.KeepActive = false;
                member.NextPatrol = TimeSpan.MaxValue;
            }

            _crew.SetPost((uid, member), xform.Coordinates);
        }
    }

    /// <summary>Gives up the mark and roams. With <paramref name="spare"/>, the ship is left alone that long.</summary>
    private void DropMark(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort,
        TimeSpan? spare)
    {
        var now = _timing.CurTime;
        if (extort.Mark is { } mark && spare is { } time)
            extort.Spared[mark] = now + time;

        extort.Mark = null;
        extort.Attempts = 0;
        extort.DeadSince = null;
        SetExtortPhase(extort, WFExtortPhase.Roam);
        extort.NextMark = now + ExtortLookAgain;
        EndExtortCall(extort);
        Roam(encounter, key, ship, extort);
    }

    /// <summary>Closes whatever hail or call its console is on.</summary>
    private void EndExtortCall(WFExtortState extort)
    {
        extort.CloseAt = null;
        if (extort.Console is not { } console || _hailing.GetState(console) == null)
            return;

        _hailing.ClearOptions(console);
        _hailing.End(console);
    }

    /// <summary>A resolved encounter's extorters hang up.</summary>
    private void EndExtortCalls(WFEncounterComponent encounter)
    {
        foreach (var ship in encounter.Ships.Values)
        {
            if (ship.Extort is { } extort)
                EndExtortCall(extort);
        }
    }

    /// <summary>
    /// Sends it wandering: its prototype's wander legs, flown about the nearest player ship it has no business with
    /// yet, or about where the encounter began. <paramref name="first"/> is flown before them.
    /// </summary>
    private void Roam(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, WFExtortState extort,
        Vector2? first = null)
    {
        var queue = new List<WFCrewObjective>();
        if (first is { } point)
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = point, Range = 200f });

        if (_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            && prototype.Ships.FirstOrDefault(entry => entry.Key == key) is { } entry)
        {
            PruneSpared(extort);
            Vector2? around = NearestCrewedShip(ship.Grid, except: ExtortOffLimits(extort)) is { } traffic
                ? _transform.GetWorldPosition(traffic)
                : null;
            queue.AddRange(BuildQueue(encounter.Comp, entry, ship.Grid, around));
        }

        // No legs to fly: it lies where it is until its next run.
        if (queue.Count == 0)
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold });

        _objectives.SetQueue(ship.Grid, ship.Group, queue);
    }

    /// <summary>Keeps station on a ship at the standoff.</summary>
    private bool Shadow(WFEncounterShipState ship, WFExtortState extort, EntityUid mark)
    {
        return _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Follow, Target = GetNetEntity(mark), Range = Math.Clamp(extort.Config.Standoff, 1f, 5000f) },
        });
    }

    /// <summary>Its mark, if it is still there to go for and not berthed at a station.</summary>
    private bool ExtortMark(WFEncounterShipState ship, WFExtortState extort, out EntityUid mark)
    {
        mark = extort.Mark ?? default;
        return extort.Mark != null && !TerminatingOrDeleted(mark) && Transform(mark).MapID == Transform(ship.Grid).MapID
               && !DockedAtStation(mark);
    }

    private string ExtortMarkName(WFExtortState extort)
    {
        return extort.Mark is { } mark && !TerminatingOrDeleted(mark) ? Name(mark) : Loc.GetString("wf-encounter-extort-status-nobody");
    }

    private void SetExtortPhase(WFExtortState extort, WFExtortPhase phase)
    {
        extort.Phase = phase;
        extort.PhaseSince = _timing.CurTime;
    }

    private void PruneSpared(WFExtortState extort)
    {
        var now = _timing.CurTime;
        foreach (var (spared, until) in extort.Spared.ToArray())
        {
            if (now >= until || TerminatingOrDeleted(spared))
                extort.Spared.Remove(spared);
        }
    }

    /// <summary>
    /// Ships it does not go for: those any extorter is leaving alone, and those another extorter has as its mark or
    /// is boarding. A toll paid to one buys peace from them all.
    /// </summary>
    private HashSet<EntityUid> ExtortOffLimits(WFExtortState extort)
    {
        var now = _timing.CurTime;
        var ships = new HashSet<EntityUid>();
        var encounters = EntityQueryEnumerator<WFEncounterComponent>();
        while (encounters.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Extort is not { } other)
                    continue;

                foreach (var (spared, until) in other.Spared)
                {
                    if (now < until)
                        ships.Add(spared);
                }

                if (other == extort)
                    continue;

                if (other.Mark is { } mark)
                    ships.Add(mark);
                if (ship.Prey is { } prey)
                    ships.Add(prey);
            }
        }

        return ships;
    }

    private string ExtortLine(WFExtortState extort, string step, params (string, object)[] args)
    {
        return Loc.GetString($"{extort.Config.Lines}-{step}-{_random.Next(1, ExtortLines + 1)}", args);
    }

    /// <summary>Says one of a step's lines down its open channel.</summary>
    private void ExtortSay(WFExtortState extort, string step, params (string, object)[] args)
    {
        if (extort.Console is { } console && extort.Operator is { } speaker)
            _hailing.Say(console, speaker, ExtortLine(extort, step, args));
    }

    /// <summary>An extorter takes no calls: a hail ringing at its helm is declined.</summary>
    private void DeclineRings(WFEncounterShipState ship)
    {
        _rings.Clear();
        var ringing = EntityQueryEnumerator<HailConsoleComponent, TransformComponent>();
        while (ringing.MoveNext(out var uid, out var hail, out var xform))
        {
            if (xform.GridUid == ship.Grid && hail.State == HailState.Ringing)
                _rings.Add(uid);
        }

        foreach (var uid in _rings)
        {
            _hailing.AnswerAutomated(uid, uid, false);
        }

        _rings.Clear();
    }

    /// <summary>
    /// A powered helm console aboard that is free, and the crewman who makes the ship's calls from it: its captain,
    /// else its radio officer, else its pilot, else a hand, and a guard only if nobody else is left.
    /// </summary>
    public bool TryFindHelm(WFEncounterShipState ship, out EntityUid console, out EntityUid speaker)
    {
        console = default;
        speaker = default;
        var found = false;
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ship.Grid || !this.IsPowered(uid, EntityManager))
                continue;

            console = uid;
            found = true;
            if (_hailing.GetState(uid) == null)
                break;
        }

        if (!found)
            return false;

        var rank = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group != ship.Group || xform.GridUid != ship.Grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;

            var standing = member.Role == WFCrewRoles.Captain ? 5
                : member.Role == WFCrewRoles.RadioOperator ? 4
                : member.Duty == WFCrewDuties.Pilot ? 3
                : member.Role == WFCrewRoles.Marine ? 1
                : 2;
            if (standing <= rank)
                continue;

            rank = standing;
            speaker = uid;
        }

        return rank > 0;
    }

    /// <summary>The extorter whose hails go through a console, while its encounter runs.</summary>
    private bool TryExtorter(EntityUid? grid, EntityUid console, out Entity<WFEncounterComponent> encounter, out string key,
        [NotNullWhen(true)] out WFEncounterShipState? ship, [NotNullWhen(true)] out WFExtortState? extort)
    {
        encounter = default;
        key = string.Empty;
        ship = null;
        extort = null;
        if (grid is not { } uid || !TryComp<WFEncounterGridComponent>(uid, out var marker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var comp) || comp.Resolution != null
            || !comp.Ships.TryGetValue(marker.Key, out ship) || ship.Extort is not { } state || state.Console != console)
            return false;

        encounter = (marker.Encounter, comp);
        key = marker.Key;
        extort = state;
        return true;
    }

    /// <summary>
    /// An encounter's state for the admin's lists, with what its warrant pays now and, with <paramref name="ships"/>,
    /// what each of its extorters is doing.
    /// </summary>
    public string StateDetail(EntityUid uid, WFEncounterComponent encounter, string state, bool ships)
    {
        foreach (var ship in encounter.Ships.Values)
        {
            if (ships && encounter.Resolution == null && ExtortStatus(ship) is { } status)
                state = Loc.GetString("wf-encounter-admin-state-detail", ("state", state), ("detail", status));
        }

        return _bounties.Pool(uid) is { } pool
            ? Loc.GetString("wf-encounter-admin-state-pool", ("state", state), ("pool", pool))
            : state;
    }

    /// <summary>What an extorter is doing, for the admin's lists; null for any other ship.</summary>
    public string? ExtortStatus(WFEncounterShipState ship)
    {
        if (ship.Extort is not { } extort)
            return null;

        var mark = ExtortMarkName(extort);
        return extort.Phase switch
        {
            WFExtortPhase.Close => Loc.GetString("wf-encounter-extort-status-close", ("ship", mark)),
            WFExtortPhase.Hail => Loc.GetString("wf-encounter-extort-status-hail", ("ship", mark), ("attempt", extort.Attempts + 1)),
            WFExtortPhase.Calling => Loc.GetString("wf-encounter-extort-status-hail", ("ship", mark), ("attempt", extort.Attempts)),
            WFExtortPhase.Demand => Loc.GetString("wf-encounter-extort-status-demand", ("ship", mark)),
            WFExtortPhase.Board => Loc.GetString("wf-encounter-extort-status-board", ("ship", mark)),
            _ => Loc.GetString("wf-encounter-extort-status-roam"),
        };
    }
}
