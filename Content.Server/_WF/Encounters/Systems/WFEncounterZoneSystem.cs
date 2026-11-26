using System.Linq;
using System.Numerics;
using Content.Server._Mono.AlertLevel;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// The warning and attack zones around encounter ships. A player ship that is not of the ship's company is told
/// over the radio to turn away inside the warning zone, and is fired on inside the attack zone for as long as it
/// stays there.
/// </summary>
public sealed partial class WFEncounterZoneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WarLevelSystem _warLevel = default!;
    [Dependency] private WFCrewStealthSystem _stealth = default!;
    [Dependency] private WFCrewSparedSystem _spared = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromSeconds(45);

    /// <summary>How long after a side calls its guns onto a vessel it may call them again, if it never let the vessel go.</summary>
    private static readonly TimeSpan CallCooldown = TimeSpan.FromSeconds(60);
    private static readonly ProtoId<RadioChannelPrototype> Channel = "Traffic";
    private const int WarnLines = 3;

    private TimeSpan _next;
    private readonly Dictionary<EntityUid, Crewed> _crewed = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<(EntityUid Encounter, WFEncounterShipState Ship)> _fleet = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Other)> _rivals = new();
    private readonly List<(EntityUid Shot, Vector2 Position, MapId Map, EntityUid Shooter)> _shots = new();
    private readonly HashSet<EntityUid> _liveShots = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Shot)> _answeredShots = new();

    /// <summary>A ship with a living player aboard.</summary>
    private sealed class Crewed
    {
        /// <summary>The companies it flies for: its own, or failing that its players'.</summary>
        public readonly List<string> Flags = new();

        /// <summary>The companies of the living players aboard.</summary>
        public readonly HashSet<string> Aboard = new();

        /// <summary>The company the ship itself flies, or empty for none.</summary>
        public string GridFlag = string.Empty;

        /// <summary>Whether a living player aboard is of no company.</summary>
        public bool Unflagged;

        /// <summary>The flags it flies as exact company ids, so a civilian arm is not taken for its navy.</summary>
        public readonly HashSet<string> RawFlags = new();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        WatchFleet();
        var crewedKnown = false;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (Stood(encounter))
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                // A ship lying in wait gives itself away to nobody.
                if (ship.WarnRange <= 0f && ship.AttackRange <= 0f || ship.Lurking || ship.Virtual || TerminatingOrDeleted(ship.Grid)
                    || Disabled(ship))
                    continue;

                if (!crewedKnown)
                {
                    FindCrewedShips();
                    GatherShots();
                    crewedKnown = true;
                }

                Watch(encounter, ship);
            }
        }
    }

    /// <summary>
    /// Whether an encounter's zones have stood down. A resolved encounter's have, except a battle's: whoever won a
    /// decided fight holds the field, and its zones stand until its ships are cleaned up.
    /// </summary>
    private static bool Stood(WFEncounterComponent encounter)
    {
        return encounter.Resolution is { } resolution && resolution != WFEncounterResolution.Decided;
    }

    /// <summary>
    /// Encounter ships answer each other's zones too: one that comes inside another's zone, and is a ship that zone
    /// minds, is taken for an enemy, and takes the zone's owner for one in turn.
    /// </summary>
    private void WatchFleet()
    {
        _fleet.Clear();
        var encounters = EntityQueryEnumerator<WFEncounterComponent>();
        while (encounters.MoveNext(out var uid, out var encounter))
        {
            if (Stood(encounter))
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (!ship.Lurking && !ship.Virtual && !TerminatingOrDeleted(ship.Grid) && !Disabled(ship))
                    _fleet.Add((uid, ship));
            }
        }

        _rivals.RemoveWhere(pair => TerminatingOrDeleted(pair.Ship) || TerminatingOrDeleted(pair.Other));
        foreach (var (uid, ship) in _fleet)
        {
            // An interrogating ship turns on nobody by itself: it answers fire and rivals that turn on it.
            var range = MathF.Max(ship.WarnRange, ship.AttackRange);
            if (range <= 0f || ship.ZoneTargets == WFEncounterZoneTargets.Interrogate || IsDocked(ship.Grid))
                continue;

            var here = CentreOfMass(ship.Grid);
            var company = Company(ship.Grid);
            foreach (var (other, rival) in _fleet)
            {
                // Its own side and its formation are not intruders, nor are ships of its own company.
                if (rival.Grid == ship.Grid || other == uid && rival.Side == ship.Side
                    || _escorts.AreInFormation(ship.Grid, rival.Grid))
                    continue;

                var theirs = Company(rival.Grid);
                if (WFCompanyFamily.Same(company, theirs) || IsNeutral(ship, theirs))
                    continue;
                // The sides of one encounter were set on each other when it was written, ceasefire or no.
                var enemies = other == uid ? Opposed(company, theirs, true) : AtWar(company, theirs);
                if (ship.ZoneTargets == WFEncounterZoneTargets.AtWar && !enemies)
                    continue;

                var key = (ship.Grid, rival.Grid);
                var there = CentreOfMass(rival.Grid);
                if (there.MapId != here.MapId || (there.Position - here.Position).Length() > range)
                {
                    _rivals.Remove(key);
                    continue;
                }

                // No warning between crews: each takes the other for an enemy, and the side says so once.
                _alerts.ReportZoneThreat(ship.Grid, ship.Group, rival.Grid);
                _alerts.ReportZoneThreat(rival.Grid, rival.Group, ship.Grid);
                if (_rivals.Add(key))
                    CallOut(Comp<WFEncounterComponent>(uid), ship, rival.Grid, Loc.GetString($"{ship.ZoneLines}-attack", ("intruder", Name(rival.Grid))));
            }
        }
    }

    /// <summary>Every ship with a living player aboard. Zones answer to ships people are flying, not to stations or debris.</summary>
    private void FindCrewedShips()
    {
        _crewed.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            // A pilot's mech flying free of any grid is a vessel of its own.
            if ((xform.GridUid ?? _alerts.FreeMechOf(uid)) is not { } grid || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            if (!_crewed.TryGetValue(grid, out var crewed))
            {
                if (!IsPlayerShip(grid) || _encounters.DockedAtStation(grid) || _stealth.ShipUnseen(grid))
                    continue;

                _crewed[grid] = crewed = new Crewed { GridFlag = WFCompanyFamily.Of(Company(grid)) };
            }

            // Companies are kept by faction: a Federation civilian aboard flies a Federation flag.
            var own = WFCompanyFamily.Of(Company(uid));
            var company = WFCompanyFamily.Of(Company(grid));
            if (company.Length == 0)
                company = own;
            if (company.Length > 0 && !crewed.Flags.Contains(company))
                crewed.Flags.Add(company);
            var raw = Company(grid);
            if (raw.Length == 0)
                raw = Company(uid);
            if (raw.Length > 0)
                crewed.RawFlags.Add(raw);
            if (own.Length > 0)
                crewed.Aboard.Add(own);
            else
                crewed.Unflagged = true;
        }
    }

    /// <summary>Whether a grid is a ship people fly: not a station, outpost or asteroid, and not one of an encounter's own.</summary>
    private bool IsPlayerShip(EntityUid grid)
    {
        if (HasComp<WFEncounterGridComponent>(grid))
            return false;

        // Stations, outposts and asteroids are station members; a deed makes one a ship somebody owns.
        return HasComp<ShuttleDeedComponent>(grid) || !HasComp<StationMemberComponent>(grid);
    }

    /// <summary>Whether a ship is out of the fight, a derelict aside: a disabled one no longer hails, fires or answers rivals.</summary>
    private bool Disabled(WFEncounterShipState ship)
    {
        return !ship.Derelict && _status.IsDisabled(ship.Grid);
    }

    /// <summary>Whether any port of a grid is docked to another, a ship of its own company and encounter fleet aside: its garrison is not a berth.</summary>
    private bool IsDocked(EntityUid grid)
    {
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != grid || dock.DockedWith is not { } other)
                continue;

            if (Transform(other).GridUid is { } alongside && HasComp<WFEncounterGridComponent>(alongside)
                && WFCompanyFamily.Same(Company(grid), Company(alongside)))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>Forgets intruders that left the map or the crewed ships, so a ship that comes back is told again.</summary>
    private void Forget(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        if (ship.Engaged.Count == 0 && ship.Warned.Count == 0)
            return;

        var map = Transform(ship.Grid).MapID;
        _stale.Clear();
        foreach (var uid in ship.Engaged)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            Release(encounter, ship, uid);
        }

        _stale.Clear();
        foreach (var uid in ship.Warned.Keys)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            ship.Warned.Remove(uid);
        }
    }

    private bool IsGone(EntityUid intruder, MapId map)
    {
        return TerminatingOrDeleted(intruder) || !_crewed.ContainsKey(intruder) || Transform(intruder).MapID != map;
    }

    /// <summary>An entity's company id, or empty for none.</summary>
    private string Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id is { } id && id != "None" ? id : string.Empty;
    }

    /// <summary>
    /// Whether two companies are at war: either one's standing lists the other, outright or as a war that has since
    /// been declared.
    /// </summary>
    public bool AtWar(string first, string second)
    {
        return Opposed(first, second, _warLevel.GetWarLevel(EntityUid.Invalid));
    }

    /// <summary>Whether only the ceasefire keeps two companies from war: they are at war once it is declared.</summary>
    public bool UnderCeasefire(string first, string second)
    {
        return !_warLevel.GetWarLevel(EntityUid.Invalid) && !Opposed(first, second, false) && Opposed(first, second, true);
    }

    private bool UnderCeasefire(string company, List<string> flags)
    {
        foreach (var flag in flags)
        {
            if (UnderCeasefire(company, flag))
                return true;
        }

        return false;
    }

    /// <summary>Whether either company's standing lists the other, counting declared wars if one has been.</summary>
    private bool Opposed(string first, string second, bool declared)
    {
        if (first.Length == 0 || second.Length == 0 || first == second)
            return false;

        return Lists(first, second, declared) || Lists(second, first, declared);
    }

    private bool Lists(string company, string other, bool declared)
    {
        return _prototypes.TryIndex<WFStandingPrototype>(company, out var standing)
               && (standing.AtWar.Contains(other) || declared && standing.DeclaredWar.Contains(other));
    }

    private bool AtWar(string company, List<string> flags)
    {
        foreach (var flag in flags)
        {
            if (AtWar(company, flag))
                return true;
        }

        return false;
    }

    /// <summary>Whether a company is one the ship's zones leave alone.</summary>
    private static bool IsNeutral(WFEncounterShipState ship, string company)
    {
        var family = WFCompanyFamily.Of(company);
        if (family.Length == 0)
            return false;

        foreach (var neutral in ship.NeutralCompanies)
        {
            if (WFCompanyFamily.Of(neutral) == family)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an intruder is left alone: it flies a neutral flag or, with none, has none but neutral people aboard,
    /// or it is docked to a neutral company's station.
    /// </summary>
    private bool IsNeutral(WFEncounterShipState ship, EntityUid intruder, Crewed crewed)
    {
        if (ship.NeutralCompanies.Count == 0)
            return false;

        if (crewed.GridFlag.Length > 0)
        {
            if (IsNeutral(ship, crewed.GridFlag))
                return true;
        }
        else if (!crewed.Unflagged && crewed.Aboard.Count > 0 && crewed.Aboard.All(company => IsNeutral(ship, company)))
            return true;

        return _encounters.IsBerthedAtNeutral(intruder, ship.NeutralCompanies);
    }

    private void Watch(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        Forget(encounter, ship);
        ship.Strangers.Clear();
        var here = CentreOfMass(ship.Grid);
        AnswerFire(encounter, ship, here);

        // A ship in port is not on guard: whatever is berthed beside it is none of its business.
        if (IsDocked(ship.Grid))
            return;

        var company = WFCompanyFamily.Of(Company(ship.Grid));
        var now = _timing.CurTime;
        foreach (var (intruder, crewed) in _crewed)
        {
            // Formation partners and the ships a side has taken as allies are not intruders.
            if (intruder == ship.Grid || _escorts.AreInFormation(ship.Grid, intruder))
                continue;

            // Its own side comes and goes as it likes: a hull showing its colours, or one with its people free aboard.
            // A ship that minds only NPCs lets every player vessel by. Their fire is still answered.
            if (ship.NpcTargetsOnly || _spared.Spared(ship.Grid, intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // Neutral companies, and ships berthed with one, are left alone whatever their IFF says.
            if (IsNeutral(ship, intruder, crewed))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // An interrogating zone minds only a declared threat or an attacker, IFF or none; anyone else in the
            // warning zone is left to be asked.
            if (ship.ZoneTargets == WFEncounterZoneTargets.Interrogate && !IsThreat(ship, intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                var at = CentreOfMass(intruder);
                if (ship.WarnRange > 0f && at.MapId == here.MapId && (at.Position - here.Position).Length() <= ship.WarnRange)
                    ship.Strangers.Add(intruder);
                continue;
            }

            // Anyone else is only known for what he is by his IFF. With that switched off he is nobody's friend: he is
            // told to show himself and shot across the bows in the warning zone, and fired on in the attack zone. With
            // it on, a patrol only minds its faction's enemies.
            // A navy patrol knows the station's own people and the judges by their codes, IFF or none: their ships, the
            // Roswell among them, fly dark by design. They are judged by their company like anyone showing an IFF.
            var masked = IffMasked(intruder) && !(ship.ZoneTargets == WFEncounterZoneTargets.AtWar && StationAuthority(crewed));
            // A ship minding only known threats leaves a player hull running dark alone; its fire is still answered.
            if (masked && ship.KnownThreatsOnly)
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // A civilian ship showing its IFF is never a navy patrol's target, whichever navy its faction is at war with.
            // A zone that warns everyone off, like the USSP remnant's, still does.
            if (!masked && ship.ZoneTargets == WFEncounterZoneTargets.AtWar && IsCivilian(crewed))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // Under the ceasefire the other navy's ships are warned off like any enemy, and fired on only once the
            // ship has had to answer them.
            var ceasefire = !masked && ship.ZoneTargets == WFEncounterZoneTargets.AtWar && !AtWar(company, crewed.Flags)
                            && UnderCeasefire(company, crewed.Flags);
            if (!masked && ship.ZoneTargets == WFEncounterZoneTargets.AtWar && !ceasefire && !AtWar(company, crewed.Flags))
                continue;

            var there = CentreOfMass(intruder);
            if (there.MapId != here.MapId)
                continue;

            // An unknown vessel berthed somewhere is in port, not trespassing: the patrol came to it, and lets it be.
            if (masked && IsDocked(intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            var challenged = masked && ship.ZoneTargets == WFEncounterZoneTargets.AtWar;
            var name = masked ? Loc.GetString("wf-encounter-zone-unknown") : Name(intruder);
            var distance = (there.Position - here.Position).Length();
            // One lying still is hailed to show itself and no more, wherever the zone finds it. Under way, or once
            // fired on, it is treated as any unknown.
            var idle = masked && ship.ZoneTargets != WFEncounterZoneTargets.Interrogate && !ship.Engaged.Contains(intruder)
                       && (!TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(intruder, out var body)
                           || body.LinearVelocity.LengthSquared() < IdleSpeed * IdleSpeed);
            if (idle)
                _alerts.EndZoneWarning(ship.Grid, intruder);

            if (ship.AttackRange > 0f && distance <= ship.AttackRange && !idle
                && (!ceasefire || ship.Engaged.Contains(intruder) || _alerts.IsAttacker(ship.Grid, intruder)))
            {
                _alerts.EndZoneWarning(ship.Grid, intruder);
                if (ship.Engaged.Add(intruder))
                {
                    // The ship turns to fight what is inside its attack zone; its side says so once.
                    _alerts.ReportZoneEngagement(ship.Grid, ship.Group, intruder);
                    CallOut(encounter, ship, intruder, Loc.GetString($"{ship.ZoneLines}-attack", ("intruder", name)));
                }
                else
                    _alerts.ReportZoneThreat(ship.Grid, ship.Group, intruder);
            }
            else if (ship.WarnRange > 0f && distance <= ship.WarnRange)
            {
                // Already fired on, it gets no second warning on its way out: the guns stay on it until it is clear.
                if (ship.Engaged.Contains(intruder))
                {
                    _alerts.ReportZoneThreat(ship.Grid, ship.Group, intruder);
                    continue;
                }

                if (challenged && !idle)
                    _alerts.ReportZoneWarning(ship.Grid, ship.Group, intruder);
                if (ship.Warned.TryGetValue(intruder, out var until) && now < until)
                    continue;

                ship.Warned[intruder] = now + WarnCooldown;
                // One hail for the side: a dozen ships warning in chorus is noise, not a warning.
                HailOut(encounter, ship, intruder, challenged
                    ? Loc.GetString("wf-encounter-zone-identify", ("distance", (int) distance))
                    : Loc.GetString($"{ship.ZoneLines}-warn-{_random.Next(1, WarnLines + 1)}",
                        ("intruder", name), ("distance", (int) distance)));
            }
            else if (distance > ship.WarnRange)
            {
                // Out and back in is a fresh trespass.
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
            }
        }
    }

    /// <summary>Whether an interrogating ship has cause to fire on a vessel: it declared itself a threat, fired on the ship, or is already engaged.</summary>
    public bool IsThreat(WFEncounterShipState ship, EntityUid intruder)
    {
        return ship.Declared.Contains(intruder) || ship.Engaged.Contains(intruder) || _alerts.IsAttacker(ship.Grid, intruder);
    }

    /// <summary>Below this speed, in metres a second, a vessel is lying still.</summary>
    private const float IdleSpeed = 2f;

    /// <summary>Every ship-weapon or free-flying mech round in flight, with the grid or mech it was fired from.</summary>
    private void GatherShots()
    {
        _shots.Clear();
        _liveShots.Clear();
        var query = EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var projectile, out var xform))
        {
            _liveShots.Add(uid);
            var shooter = _alerts.IsShipWeapon(projectile.Weapon)
                ? Transform(projectile.Weapon!.Value).GridUid
                : _alerts.FreeMechOf(projectile.Weapon ?? projectile.Shooter);
            if (shooter is not { } source)
                continue;

            _shots.Add((uid, _transform.GetWorldPosition(xform), xform.MapID, source));
        }

        _answeredShots.RemoveWhere(key => !_liveShots.Contains(key.Shot));
    }

    /// <summary>
    /// Ship-weapon rounds from another vessel that come inside the attack zone are an attack, hit or miss: the ship
    /// answers them as it would a hit, wherever the vessel that fired them is, and says so when it is new to it.
    /// Rounds from its own side, its formation and its own guns are not; a few strays from its own faction are let pass.
    /// </summary>
    private void AnswerFire(WFEncounterComponent encounter, WFEncounterShipState ship, MapCoordinates here)
    {
        if (ship.AttackRange <= 0f)
            return;

        var reach = ship.AttackRange * ship.AttackRange;
        foreach (var shot in _shots)
        {
            if (shot.Map != here.MapId || shot.Shooter == ship.Grid || (shot.Position - here.Position).LengthSquared() > reach
                || !_answeredShots.Add((ship.Grid, shot.Shot)))
                continue;

            if (_escorts.AreInFormation(ship.Grid, shot.Shooter) || SameSide(encounter, ship, shot.Shooter))
                continue;

            var fresh = !_alerts.IsAttacker(ship.Grid, shot.Shooter);
            if (!_alerts.ReportIncomingFire(ship.Grid, ship.Group, shot.Shooter))
                continue;

            if (fresh)
            {
                var name = IffMasked(shot.Shooter) ? Loc.GetString("wf-encounter-zone-unknown") : Name(shot.Shooter);
                CallOut(encounter, ship, shot.Shooter, Loc.GetString($"{ship.ZoneLines}-attack", ("intruder", name)));
            }
        }
    }

    /// <summary>
    /// Hails a vessel for the side, once per cooldown whichever of its ships speaks. A ship with nobody to speak gives
    /// the side's turn back, so the next ship takes it.
    /// </summary>
    private void HailOut(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder, string line)
    {
        Speak(encounter.Hailed, ship, intruder, WarnCooldown, line);
    }

    /// <summary>
    /// Calls the side's guns onto a vessel, once for the side while any of its ships holds the vessel in its attack zone
    /// and once per cooldown otherwise, since a shooter answered or a rival crew may never enter a zone to be let go of.
    /// </summary>
    private void CallOut(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder, string line)
    {
        Speak(encounter.Called, ship, intruder, CallCooldown, line);
    }

    /// <summary>Takes the side's turn to speak of a vessel if it is free, and gives it back if nobody aboard could.</summary>
    private void Speak(Dictionary<(string Side, EntityUid Intruder), TimeSpan> turns, WFEncounterShipState ship, EntityUid intruder,
        TimeSpan cooldown, string line)
    {
        var key = (ship.Side, intruder);
        var now = _timing.CurTime;
        if (turns.TryGetValue(key, out var until) && now < until)
            return;

        turns[key] = now + cooldown;
        if (!_encounters.TrySay(ship, Channel, line))
            turns.Remove(key);
    }

    /// <summary>A ship lets a vessel out of its attack zone; once no ship of its side holds it, the side may call it again.</summary>
    private static void Release(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid intruder)
    {
        if (!ship.Engaged.Remove(intruder))
            return;

        foreach (var other in encounter.Ships.Values)
        {
            if (other.Side == ship.Side && other.Engaged.Contains(intruder))
                return;
        }

        encounter.Called.Remove((ship.Side, intruder));
    }

    /// <summary>Whether a grid is another ship of the same encounter on the same side.</summary>
    private bool SameSide(WFEncounterComponent encounter, WFEncounterShipState ship, EntityUid grid)
    {
        return TryComp<WFEncounterGridComponent>(grid, out var marker) && marker.Encounter == encounter.Owner
            && encounter.Ships.TryGetValue(marker.Key, out var other) && other.Side == ship.Side;
    }

    /// <summary>Whether a ship flies only civilian flags: a faction's civilian arm, or no company at all.</summary>
    private static bool IsCivilian(Crewed crewed) =>
        crewed.RawFlags.All(company => company.EndsWith("Civilian", StringComparison.Ordinal));

    /// <summary>Whether a ship has its IFF switched off, so nobody can tell whose it is.</summary>
    /// <summary>Companies a navy patrol knows whatever their IFF says: Caelestinus staff and the judges.</summary>
    private static readonly string[] StationCompanies = { "Colonial", "WFJudges" };

    /// <summary>Whether a ship flies for the station or carries its staff: its flag, or any player aboard.</summary>
    private static bool StationAuthority(Crewed crewed)
    {
        foreach (var company in StationCompanies)
        {
            if (crewed.GridFlag == company || crewed.Aboard.Contains(company))
                return true;
        }

        return false;
    }

    private bool IffMasked(EntityUid grid)
    {
        return TryComp<Content.Shared.Shuttles.Components.IFFComponent>(grid, out var iff)
               && (iff.Flags & (Content.Shared.Shuttles.Components.IFFFlags.HideLabel | Content.Shared.Shuttles.Components.IFFFlags.Hide)) != 0;
    }

    /// <summary>Where a hull's centre of mass is: the point the radar draws it and its zones around.</summary>
    private MapCoordinates CentreOfMass(EntityUid grid)
    {
        var centre = CompOrNull<Robust.Shared.Physics.Components.PhysicsComponent>(grid)?.LocalCenter ?? System.Numerics.Vector2.Zero;
        return _transform.ToMapCoordinates(new EntityCoordinates(grid, centre));
    }
}
