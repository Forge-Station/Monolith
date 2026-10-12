using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>How far clear of its prey a raider flies before it jumps out.</summary>
    private const float RaidExit = 2500f;

    /// <summary>How long a raider holds once the loot is in, and how long before casting off its guards are called back.</summary>
    private static readonly TimeSpan RaidTime = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan RecallLead = TimeSpan.FromSeconds(20);

    /// <summary>The raid's end while its hands are still looting, before the hold is timed.</summary>
    private static readonly TimeSpan RaidLooting = TimeSpan.MaxValue;

    /// <summary>Failed runs at one ship before a hunter leaves it alone for another.</summary>
    private const int HuntDockMisses = 3;

    /// <summary>
    /// The boarding party's tiles, as tiles in from the prey's port and to its side: two and three in, straight in
    /// first, then beside the tile behind the port. Never the port's own tile or the one straight behind it, where a
    /// guard would stop on the seam between the ships.
    /// </summary>
    private static readonly (int Depth, int Side)[] BreachTiles =
    {
        (2, 0), (3, 0), (2, -1), (2, 1), (3, -1), (3, 1), (1, -1), (1, 1),
    };

    /// <summary>Per hunting ship: its failed runs at its prey, and the ships it gave up on.</summary>
    private readonly Dictionary<EntityUid, HuntRecord> _huntRecords = new();

    /// <summary>
    /// A hunting ship runs down the nearest ship with a player aboard and docks with it, trying again each time
    /// the prey shakes it off. Docked, its hands go aboard, fight whoever they see, carry a few things back,
    /// and the ship casts off and leaves.
    /// </summary>
    private void Hunt(WFEncounterShipState ship)
    {
        if (ship.Raided)
            return;

        var status = _objectives.QueueStatus(ship.Grid, ship.Group);
        if (ship.Prey is { } docked && !TerminatingOrDeleted(docked) && _docking.AreGridsDocked(ship.Grid, docked))
        {
            Raid(ship, docked);
            // The raid's own orders run until it casts off.
            if (status != null)
                return;
        }
        else if (ship.RaidEnds != null)
        {
            // Off the prey with the raid under way: it cast off, or the prey broke away.
            var looting = CurrentOrder(ship) == WFCrewObjectiveKind.Loot;
            ship.RaidEnds = null;
            // Shaken off with the boarding party still aboard the prey: the ship goes straight back for them and takes
            // the raid up again where it docks. They are only given up once it can't get a port on the prey again, or
            // the prey has jumped out of reach with them.
            if (PartyAboard(ship))
            {
                if (!PreyReachable(ship) && AbandonParty(ship))
                    return;
                status = null;
            }
            // Broken off before the loot was in and with nobody left aboard: the raid starts over and the ship goes
            // after its prey again. The new orders cancel the old raid's work.
            else if (looting)
            {
                ship.Boarded = null;
                status = null;
            }
        }

        // Only the whole queue flown counts: the loot finishing reads "complete" for a moment too.
        if (status == "complete" && CurrentOrder(ship) == null)
        {
            // The raid's orders are flown; the encounter completes and the ship jumps out.
            ship.Raided = true;
            ship.HasOrders = true;
            ship.Flown = true;
            _huntRecords.Remove(ship.Grid);
            return;
        }

        // The way out can't be flown: the raid is over all the same, and the encounter skips the leg and completes.
        if (status == "target-lost" && CurrentOrder(ship) is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock)
        {
            ship.Raided = true;
            ship.HasOrders = true;
            ship.Flown = false;
            _huntRecords.Remove(ship.Grid);
            return;
        }

        var hunt = HuntRecordFor(ship.Grid);
        // A ship it cannot get a port on is left alone; the hunt moves on to the next nearest.
        if (status == "dock-failed" && ship.Prey is { } missed && ++hunt.Misses >= HuntDockMisses)
        {
            // Whoever is still aboard the prey fights on there, and the ship gives them up and runs.
            if (missed == ship.Boarded && AbandonParty(ship))
                return;

            Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} gives up on docking with {ToPrettyString(missed)}.");
            hunt.Shunned.Add(missed);
            ship.Prey = null;
        }

        if (ship.Prey is not { } prey || TerminatingOrDeleted(prey) || Transform(prey).MapID != Transform(ship.Grid).MapID)
        {
            // A party left aboard a prey that is gone is given up before the hunt turns to another ship.
            if (!PreyReachable(ship) && PartyAboard(ship) && AbandonParty(ship))
                return;

            hunt.Misses = 0;
            ship.Prey = NearestCrewedShip(ship.Grid, neutral: ship.NeutralCompanies);
            if (ship.Prey is not { } found)
                return;

            prey = found;
            status = null;
        }

        // No orders yet, or the last run at the prey failed or lost it: go again.
        if (status is not (null or "dock-failed" or "target-lost"))
            return;

        var away = _transform.GetMapCoordinates(prey).Position + _random.NextAngle().ToVec() * RaidExit;
        _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Loot, Target = GetNetEntity(prey) },
            // Even with nothing worth taking they stay a while: the boarding party is aboard looking for a fight.
            new() { Kind = WFCrewObjectiveKind.Hold, Duration = (float) RaidTime.TotalSeconds },
            new() { Kind = WFCrewObjectiveKind.Undock },
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = away, Range = 200f },
        });
    }

    /// <summary>
    /// Runs a raid while docked. On the first look the crew are told to steal, not clear the ship, and the guards go
    /// aboard; once the loot is in the hold is timed, and the guards are called back before it ends.
    /// </summary>
    private void Raid(WFEncounterShipState ship, EntityUid prey)
    {
        if (ship.RaidEnds == null)
        {
            ship.RaidEnds = RaidLooting;
            ship.Boarded = prey;
            HuntRecordFor(ship.Grid).Misses = 0;
            // They are here to steal, not to clear the ship: nobody goes looking for a fight.
            var raiders = EntityQueryEnumerator<WFCrewComponent>();
            while (raiders.MoveNext(out var raider, out var member))
            {
                if (member.Group != ship.Group)
                    continue;

                member.Pursues = false;
                // Nobody is left behind lightly: the pilot waits a good while for people still aboard the prey.
                if (TryComp<WFPilotDutyComponent>(raider, out var pilot))
                    pilot.AbsentCrewWait = 180f;
            }

            SendBoardingParty(ship, prey);
            return;
        }

        if (ship.RaidEnds == RaidLooting)
        {
            if (CurrentOrder(ship) != WFCrewObjectiveKind.Loot)
                ship.RaidEnds = _timing.CurTime + RaidTime;
            return;
        }

        if (_timing.CurTime >= ship.RaidEnds - RecallLead)
            RecallBoardingParty(ship);
    }

    /// <summary>The order a ship's crew is on; null once its queue is flown or with nobody aboard to report one.</summary>
    private WFCrewObjectiveKind? CurrentOrder(WFEncounterShipState ship)
    {
        var net = GetNetEntity(ship.Grid);
        foreach (var crew in _objectives.Snapshot())
        {
            if (crew.Grid == net && crew.Group == ship.Group)
                return crew.Objectives.Count > 0 ? crew.Objectives[0].Kind : (WFCrewObjectiveKind?) null;
        }

        return null;
    }

    /// <summary>
    /// Leaves the raiders still aboard the prey to it: each holds where he stands, hunts whoever is aboard, and is
    /// no longer waited for. The fallen stay where they fell, no longer the ship's crew. The rest get their own posts
    /// back. True if anyone living was left.
    /// </summary>
    /// <summary>Whether the ship it boarded is still there, on the same map.</summary>
    private bool PreyReachable(WFEncounterShipState ship)
    {
        return ship.Boarded is { } prey && !TerminatingOrDeleted(prey) && Transform(prey).MapID == Transform(ship.Grid).MapID;
    }

    /// <summary>
    /// Leaves the boarding party to the prey and runs: the raid is over, the encounter completes and the ship jumps out.
    /// False, and nothing changed, when nobody living was aboard to leave.
    /// </summary>
    private bool AbandonParty(WFEncounterShipState ship)
    {
        if (!Strand(ship))
            return false;

        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} cannot get back aboard {ToPrettyString(ship.Boarded)} and leaves its party.");
        ship.Raided = true;
        ship.HasOrders = true;
        ship.Flown = false;
        _huntRecords.Remove(ship.Grid);
        var clear = _transform.GetMapCoordinates(ship.Grid).Position + _random.NextAngle().ToVec() * RaidExit;
        _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = clear, Range = 200f },
        });
        return true;
    }

    /// <summary>Whether any of the ship's crew are alive aboard the ship it boarded.</summary>
    private bool PartyAboard(WFEncounterShipState ship)
    {
        if (ship.Boarded is not { } prey || TerminatingOrDeleted(prey))
            return false;

        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == prey && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    private bool Strand(WFEncounterShipState ship)
    {
        var left = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group != ship.Group || xform.GridUid is not { } grid || grid == ship.Grid || grid != ship.Boarded)
                continue;

            ship.BoardingParty.Remove(uid);
            // A post on the ship would count a body as its crew, to go when the ship is removed; the fallen stay put.
            if (!_mobs.IsAlive(uid))
            {
                _crew.SetPost((uid, member), xform.Coordinates);
                continue;
            }

            member.Pursues = true;
            // Left behind, they hunt while someone is near but don't keep their AI awake for the rest of the round.
            member.KeepActive = false;
            member.NextPatrol = TimeSpan.MaxValue;
            _crew.SetPost((uid, member), xform.Coordinates);
            left++;
        }

        RecallBoardingParty(ship);
        return left > 0;
    }

    /// <summary>
    /// Tiles for the boarding party on the prey, two or three in from the port the raider came in by: safe deck, one
    /// each, clear of the seam between the ships and short of the rest of the prey. Fewer than asked when there is
    /// no room, none when the raider is not docked with it.
    /// </summary>
    private List<EntityCoordinates> BreachPosts(EntityUid raider, EntityUid prey, int count)
    {
        var posts = new List<EntityCoordinates>();
        if (count <= 0 || !TryComp<MapGridComponent>(prey, out var gridComp))
            return posts;

        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != raider || dock.DockedWith is not { } other)
                continue;

            var port = Transform(other);
            if (port.ParentUid != prey)
                continue;

            // A dock mates along its local -Y, so inward is the other way.
            var outward = port.LocalRotation.RotateVec(new Vector2(0f, -1f));
            var inward = new Vector2i((int) MathF.Round(-outward.X), (int) MathF.Round(-outward.Y));
            var side = new Vector2i(-inward.Y, inward.X);
            var tile = new Vector2i((int) MathF.Floor(port.LocalPosition.X), (int) MathF.Floor(port.LocalPosition.Y));
            foreach (var (depth, lateral) in BreachTiles)
            {
                var straight = tile + inward * depth;
                var candidate = straight + side * lateral;
                // Only across open deck: never past a wall into the next room, straight in or to the side.
                if (depth == 3 && !_planner.IsSafePost(prey, tile + inward * 2, gridComp)
                    || lateral != 0 && !_planner.IsSafePost(prey, straight, gridComp)
                    || !_planner.IsSafePost(prey, candidate, gridComp))
                    continue;

                posts.Add(new EntityCoordinates(prey, new Vector2(candidate.X + 0.5f, candidate.Y + 0.5f)));
                if (posts.Count >= count)
                    break;
            }

            break;
        }

        return posts;
    }

    /// <summary>
    /// Posts the ship's guards just inside the prey's port, one to a tile. A crewman's post decides which ship is his
    /// to hold, so there they treat its people as intruders and fight the ones they see, while the hands loot. A
    /// guard with no room aboard holds his own ship.
    /// </summary>
    private void SendBoardingParty(WFEncounterShipState ship, EntityUid prey)
    {
        var guards = new List<Entity<WFCrewComponent>>();
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == ship.Grid && member.Role == WFCrewRoles.Marine && member.Duty != WFCrewDuties.Pilot && _mobs.IsAlive(uid))
                guards.Add((uid, member));
        }

        var posts = BreachPosts(ship.Grid, prey, guards.Count);
        for (var i = 0; i < posts.Count; i++)
        {
            var (uid, member) = guards[i];
            ship.BoardingParty[uid] = member.Post;
            // They hold the breach instead of walking the prey's patrol round.
            member.NextPatrol = TimeSpan.MaxValue;
            _crew.SetPost((uid, member), posts[i]);
        }

        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} sends {posts.Count} of {guards.Count} guards aboard {ToPrettyString(prey)}.");
    }

    /// <summary>
    /// Gives the boarding party their own posts and patrols back, so they return and the ship waits for them. The
    /// fallen keep the post where they fell.
    /// </summary>
    private void RecallBoardingParty(WFEncounterShipState ship)
    {
        foreach (var (uid, post) in ship.BoardingParty)
        {
            if (!TryComp<WFCrewComponent>(uid, out var member) || !_mobs.IsAlive(uid))
                continue;

            member.NextPatrol = TimeSpan.Zero;
            if (post is { } home && !TerminatingOrDeleted(home.EntityId))
                _crew.SetPost((uid, member), home);
        }

        ship.BoardingParty.Clear();
    }

    /// <summary>
    /// The nearest ship on the same map with a living player aboard, within <paramref name="range"/>: not part of an
    /// encounter, not a station or outpost, and not one this hunter has given up on.
    /// </summary>
    private EntityUid? NearestCrewedShip(EntityUid hunter, float range = float.MaxValue, List<string>? neutral = null)
    {
        var shunned = _huntRecords.TryGetValue(hunter, out var hunt) ? hunt.Shunned : null;
        var here = _transform.GetMapCoordinates(hunter);
        EntityUid? nearest = null;
        var best = range * range;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || grid == hunter || xform.MapID != here.MapId
                || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid) || HasComp<WFEncounterGridComponent>(grid)
                || HasComp<Content.Shared.Station.Components.StationMemberComponent>(grid)
                && !HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(grid)
                || shunned != null && shunned.Contains(grid) || neutral is { Count: > 0 } && IsNeutralPlayer(uid, grid, neutral))
                continue;

            var distance = (_transform.GetWorldPosition(grid) - here.Position).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = grid;
        }

        return nearest;
    }

    /// <summary>Whether a player's ship flies a neutral company's flag or, with none, the player is of one, or it is berthed at a neutral station.</summary>
    private bool IsNeutralPlayer(EntityUid player, EntityUid grid, List<string> neutral)
    {
        var company = CompOrNull<CompanyComponent>(grid)?.CompanyName.Id;
        if (company is null or "None")
            company = CompOrNull<CompanyComponent>(player)?.CompanyName.Id;

        return company != null && neutral.Contains(company) || IsBerthedAtNeutral(grid, neutral);
    }

    /// <summary>Whether a grid is docked to a station that flies one of the neutral companies' flags.</summary>
    public bool IsBerthedAtNeutral(EntityUid grid, List<string> neutral)
    {
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != grid || dock.DockedWith is not { } other || Transform(other).GridUid is not { } alongside)
                continue;

            // A station is a station member without a deed, or a grid that becomes one; a ship flying the flag is not.
            var station = HasComp<Content.Shared.Station.Components.StationMemberComponent>(alongside)
                          && !HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(alongside)
                          || HasComp<Content.Server.Station.Components.BecomesStationComponent>(alongside);
            var flag = WFCompanyFamily.Of(CompOrNull<CompanyComponent>(alongside)?.CompanyName.Id);
            if (station && flag.Length > 0 && neutral.Any(company => WFCompanyFamily.Of(company) == flag))
                return true;
        }

        return false;
    }

    /// <summary>A hunter's record, made on first use; records of hunters that are gone are dropped then.</summary>
    private HuntRecord HuntRecordFor(EntityUid hunter)
    {
        if (_huntRecords.TryGetValue(hunter, out var hunt))
            return hunt;

        var gone = new List<EntityUid>();
        foreach (var other in _huntRecords.Keys)
        {
            if (TerminatingOrDeleted(other))
                gone.Add(other);
        }

        foreach (var other in gone)
        {
            _huntRecords.Remove(other);
        }

        hunt = new HuntRecord();
        _huntRecords[hunter] = hunt;
        return hunt;
    }

    /// <summary>A hunter's failed runs at its current prey, and the ships it gave up on.</summary>
    private sealed class HuntRecord
    {
        public int Misses;
        public readonly HashSet<EntityUid> Shunned = new();
    }
}
